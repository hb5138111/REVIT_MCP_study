#if REVIT2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitMCP.UI;

namespace RevitMCP.Core.Drawing
{
    /// <summary>Extends the existing isolated self-test launcher; never accepts a production document.</summary>
    internal sealed class DrawingProductionFixture : IExternalEventHandler
    {
        private readonly string root;
        private readonly ExternalEvent stepEvent;
        private readonly DispatcherTimer timer;
        private readonly DrawingProductionViewModel vm;
        private readonly List<object> assertions=new();
        private int step=-2,failed;
        private bool queued,complete;
        private DateTime deadline;
        private long referenceSheet;
        private DrawingChoice[] levels=Array.Empty<DrawingChoice>();
        private Dictionary<long,long> sources=new();
        private long[] firstIds=Array.Empty<long>();
        private XYZ? movedCenter;
        public DrawingProductionFixture(UIControlledApplication application,string root,DrawingProductionViewModel vm)
        {
            this.root=Path.GetFullPath(root);this.vm=vm;stepEvent=ExternalEvent.Create(this);
            timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};
            timer.Tick+=(_,__)=>{if(!complete&&!queued&&!vm.Busy){var request=stepEvent.Raise();queued=request==ExternalEventRequest.Accepted||request==ExternalEventRequest.Pending;}};
            application.ControlledApplication.ApplicationInitialized+=(_,__)=>{deadline=DateTime.UtcNow.AddMinutes(4);timer.Start();};
        }
        private void Check(string name,bool pass,object? actual=null){assertions.Add(new{TestName=name,Passed=pass,Actual=actual});if(!pass)failed++;}
        public string GetName()=>"Disposable Drawing Production Workflow Gate C3";
        public void Execute(UIApplication app)
        {
            queued=false;
            try
            {
                if(DateTime.UtcNow>deadline)throw new InvalidOperationException("Drawing fixture timeout.");
                File.WriteAllText(Path.Combine(root,"drawing-progress.txt"),$"Step {step}; {vm.Status}");
                if(step>-2&&(app.ActiveUIDocument==null||!app.ActiveUIDocument.Document.PathName.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Refusing non-fixture document.");
                switch(step)
                {
                    case -2:
                        if(app.Application.Documents.Size!=0||!Path.GetFileName(root).StartsWith("revit-selftest-")||!File.Exists(Path.Combine(root,"request.json")))throw new InvalidOperationException("Isolated launcher required.");
                        Prepare(app);
                        var command=RevitCommandId.LookupPostableCommandId(PostableCommand.Legend);
                        if(!app.CanPostCommand(command))throw new InvalidOperationException("Cannot create disposable Legend through native command.");app.PostCommand(command);break;
                    case -1:
                        var fixture=app.ActiveUIDocument.Document;
                        var legend=new FilteredElementCollector(fixture).OfClass(typeof(View)).Cast<View>().SingleOrDefault(v=>v.ViewType==ViewType.Legend&&!v.IsTemplate);
                        if(legend==null)throw new InvalidOperationException("Native Legend creation was canceled; no safe fixture seed.");
                        using(var tx=new Transaction(fixture,"Fixture legend content"))
                        {
                            tx.Start();legend.Name="Disposable Drawing Legend";
                            var textType=new FilteredElementCollector(fixture).OfClass(typeof(TextNoteType)).FirstElementId();TextNote.Create(fixture,legend.Id,XYZ.Zero,"Fixture legend",textType);
                            fixture.Regenerate();
                            var legendViewport=Viewport.Create(fixture,new ElementId(referenceSheet),legend.Id,new XYZ(.4,.4,0));
                            if(legendViewport==null)throw new InvalidOperationException("Legend viewport creation returned null.");
                            fixture.Regenerate();
                            if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Legend fixture transaction rolled back.");
                            Check("legend_viewport_persisted",fixture.GetElement(legendViewport.Id)!=null,legendViewport.Id.Value);
                            File.WriteAllText(Path.Combine(root,"legend-debug.json"),JsonConvert.SerializeObject(new{Legend=legend.Id.Value,Viewport=legendViewport.Id.Value,Sheet=referenceSheet,ViewportSheet=legendViewport.SheetId.Value,SheetViewports=((ViewSheet)fixture.GetElement(new ElementId(referenceSheet))).GetAllViewports().Select(id=>id.Value)}));
                        }
                        vm.Refresh();break;
                    case 0: Check("legend_seed_created",true);break;
                    case 1:
                        Check("fresh_levels",vm.Levels.Length>=3,vm.Levels.Length);vm.Extract(referenceSheet);break;
                    case 2:
                        var blueprint=vm.Package.Profile.Blueprint;
                        Check("extract_titleblock",blueprint.TitleBlockTypeId>0);Check("extract_main",blueprint.Viewports.Count==1);Check("extract_schedule",blueprint.Schedules.Count==1);Check("extract_legend",blueprint.Legends.Count==1);
                        Check("extract_view_template",blueprint.Viewports.Single().ViewTemplateId>0&&blueprint.Viewports.Single().ScaleControlled);
                        foreach(var parameter in blueprint.SheetParameterCopyPolicy.Where(p=>p.ParameterId==(long)BuiltInParameter.SHEET_DRAWN_BY))parameter.Selected=true;
                        foreach(var parameter in blueprint.SheetParameterCopyPolicy.Where(p=>p.ParameterId==(long)BuiltInParameter.SHEET_CHECKED_BY)){parameter.Selected=true;parameter.SemanticField="Level";}
                        Check("generated_number_policy",blueprint.SheetParameterCopyPolicy.Any(p=>p.ParameterId==(long)BuiltInParameter.SHEET_NUMBER&&p.Policy==DrawingCopyPolicy.Generated));
                        vm.Package.Profile.ProfileName="Drawing Fixture";vm.Package.Profile.NumberingRule="DF-{Sequence:000}";vm.Package.Profile.ViewNamingRule="{Level}-{Zone}-Fixture";
                        vm.Package.PackageName="Disposable Drawing Fixture";vm.Package.DrawingType="施工平面";vm.Package.Levels=levels.ToList();vm.Package.SourceViewsByLevel=sources;
                        vm.Package.Zones=new(){new(){ZoneId="fixture-grid-A",ZoneName="A",ScopeSource="GridRange",Bounds=new(0,0,10,10)},new(){ZoneId="fixture-grid-B",ZoneName="B",ScopeSource="GridRange",Bounds=new(10,0,20,10)}};
                        vm.SaveProfile();break;
                    case 3: vm.GeneratePlan();break;
                    case 4:
                        Check("six_sheet_preview",vm.Plan?.Rows.Count==6&&vm.Plan.CanApply,vm.Status);if(vm.Plan?.CanApply!=true)throw new InvalidOperationException(vm.Status);
                        vm.GoToStep(3);Check("confirm_enabled",vm.CanApply);vm.ConfirmAndApply();break;
                    case 5:
                        Check("create_readback",vm.ResultIds.Length==6,vm.Status);if(vm.ResultIds.Length!=6)throw new InvalidOperationException(vm.Status);
                        firstIds=vm.ResultIds.ToArray();Check("qa_no_errors",vm.Issues.All(i=>i.Severity!="ERROR"),vm.Issues);
                        Check("sheet_directory_ready",vm.SheetList.Length==6&&vm.SheetList.All(s=>s.Readiness=="Ready"));
                        Check("mapped_parameters_readback",vm.SheetList.All(s=>((ViewSheet)app.ActiveUIDocument.Document.GetElement(new ElementId(s.SheetId))).get_Parameter(BuiltInParameter.SHEET_CHECKED_BY).AsString()==s.Level));
                        vm.GeneratePlan();break;
                    case 6:
                        Check("idempotent_plan",vm.Plan?.Rows.All(r=>r.Change==DrawingChange.Unchanged)==true,vm.Status);vm.GoToStep(3);vm.ConfirmAndApply();break;
                    case 7:
                        Check("same_sheet_ids",vm.ResultIds.SequenceEqual(firstIds));
                        var doc=app.ActiveUIDocument.Document;var sheet=(ViewSheet)doc.GetElement(new ElementId(firstIds[0]));var vp=(Viewport)doc.GetElement(sheet.GetAllViewports().First());
                        using(var tx=new Transaction(doc,"Fixture manual move")){tx.Start();movedCenter=vp.GetBoxCenter()+new XYZ(.02,0,0);vp.SetBoxCenter(movedCenter);tx.Commit();}vm.GeneratePlan();break;
                    case 8:
                        Check("manual_override_detected",vm.Plan?.Rows.Count(r=>r.Change==DrawingChange.ManualOverride)==1,vm.Status);vm.GoToStep(3);vm.ConfirmAndApply();break;
                    case 9:
                        var manualSheet=(ViewSheet)app.ActiveUIDocument.Document.GetElement(new ElementId(firstIds[0]));var manualViewport=(Viewport)app.ActiveUIDocument.Document.GetElement(manualSheet.GetAllViewports().First());
                        Check("manual_position_preserved",manualViewport.GetBoxCenter().IsAlmostEqualTo(movedCenter));
                        vm.Package.PreserveManualChanges=false;vm.Invalidate();vm.GeneratePlan();break;
                    case 10:
                        Check("explicit_override_update",vm.Plan?.Rows.Count(r=>r.Change==DrawingChange.Update)==1,vm.Status);vm.GoToStep(3);vm.ConfirmAndApply();break;
                    case 11:
                        Check("override_apply_readback",vm.Step==4,vm.Status);vm.GeneratePlan();break;
                    case 12:
                        Check("override_reapply_idempotent",vm.Plan?.Rows.All(r=>r.Change==DrawingChange.Unchanged)==true,vm.Status);
                        vm.Package.Profile.Blueprint.Viewports[0].AbsoluteX+=.01;vm.SaveProfile();break;
                    case 13: vm.GeneratePlan();break;
                    case 14:
                        Check("version_outdated",vm.Plan?.Rows.All(r=>r.Issues.Any(i=>i.Contains("TEMPLATE_OUTDATED")))==true,vm.Status);
                        vm.GoToStep(3);vm.ConfirmAndApply();break;
                    case 15:
                        Check("version_apply_readback",vm.Step==4,vm.Status);vm.GeneratePlan();break;
                    case 16:
                        Check("version_idempotency",vm.Plan?.Rows.All(r=>r.Change==DrawingChange.Unchanged)==true,vm.Status);
                        vm.Package.Profile.NumberingRule="A-REF";vm.Invalidate();vm.GeneratePlan();break;
                    case 17:
                        Check("number_conflict_blocks",vm.Plan?.CanApply==false,vm.Status);RunNegativeTests(app.ActiveUIDocument.Document);Finish(app);break;
                }
                step++;
            }
            catch(Exception e){Check("runtime_exception",false,e.ToString());Finish(app);}
        }
        private void Prepare(UIApplication app)
        {
            var request=JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"request.json")))!;
            string familyTemplate=Path.Combine(Path.GetDirectoryName(request["FamilyTemplate"])!,"Titleblocks","A1 metric.rft");
            using var family=app.Application.NewFamilyDocument(familyTemplate);
            using(var tx=new Transaction(family,"Fixture titleblock")){tx.Start();if(family.FamilyManager.Types.Size==0)family.FamilyManager.NewType("Fixture A1");tx.Commit();}
            string familyPath=Path.Combine(root,"DrawingFixtureTitleBlock.rfa");family.SaveAs(familyPath,new SaveAsOptions{OverwriteExistingFile=true});family.Close(false);
            var doc=app.Application.NewProjectDocument(request["BaseProjectTemplate"]);
            if(doc.IsWorkshared||doc.IsLinked)throw new InvalidOperationException("Standalone fixture required.");
            using(var tx=new Transaction(doc,"Drawing fixture setup"))
            {
                tx.Start();doc.LoadFamily(familyPath,out Family titleFamily);
                var symbol=(FamilySymbol)doc.GetElement(titleFamily.GetFamilySymbolIds().First());
                var viewType=new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(v=>v.ViewFamily==ViewFamily.FloorPlan);
                var list=new List<DrawingChoice>();
                for(int i=1;i<=3;i++){var level=Level.Create(doc,(i-1)*12);level.Name="DF-FL"+i;var view=ViewPlan.Create(doc,viewType.Id,level.Id);view.Name="DF-Source"+i;view.Scale=100;view.CropBoxActive=true;view.CropBox=new BoundingBoxXYZ{Min=new XYZ(0,0,-1),Max=new XYZ(10,10,1)};list.Add(new(level.Id.Value,level.Name,level.ProjectElevation));sources[level.Id.Value]=view.Id.Value;}
                levels=list.ToArray();
                var firstSource=(View)doc.GetElement(new ElementId(sources[levels[0].Id]));
                var template=firstSource.CreateViewTemplate();template.Name="Drawing Fixture Scale Template";
                template.SetNonControlledTemplateParameterIds(template.GetTemplateParameterIds().Where(id=>id.Value!=(long)BuiltInParameter.VIEW_SCALE).ToArray());
                foreach(long sourceId in sources.Values)((View)doc.GetElement(new ElementId(sourceId))).ViewTemplateId=template.Id;
                foreach(double x in new[]{0d,10d,20d})Grid.Create(doc,Line.CreateBound(new XYZ(x,-5,0),new XYZ(x,15,0))).Name="DX"+x;
                foreach(double y in new[]{0d,10d})Grid.Create(doc,Line.CreateBound(new XYZ(-5,y,0),new XYZ(25,y,0))).Name="DY"+y;
                var sheet=ViewSheet.Create(doc,symbol.Id);sheet.SheetNumber="A-REF";sheet.Name="Drawing Reference";referenceSheet=sheet.Id.Value;
                sheet.get_Parameter(BuiltInParameter.SHEET_DRAWN_BY).Set("Fixture author");sheet.get_Parameter(BuiltInParameter.SHEET_CHECKED_BY).Set("Fixture checker");
                var viewport=Viewport.Create(doc,sheet.Id,new ElementId(sources[levels[0].Id]),new XYZ(1,1,0));viewport.LabelOffset=new XYZ(0,-.02,0);viewport.LabelLineLength=.1;
                var schedule=ViewSchedule.CreateSchedule(doc,new ElementId(BuiltInCategory.OST_Levels));schedule.Name="Fixture Levels";
                var field=schedule.Definition.GetSchedulableFields().First(f=>f.ParameterId.Value==(long)BuiltInParameter.DATUM_TEXT);schedule.Definition.AddField(field);ScheduleSheetInstance.Create(doc,sheet.Id,schedule.Id,new XYZ(1.6,1.3,0));
                doc.Regenerate();tx.Commit();
            }
            string path=Path.Combine(root,"DrawingProductionFixture.rvt");doc.SaveAs(path,new SaveAsOptions{OverwriteExistingFile=true});doc.Close(false);app.OpenAndActivateDocument(path);
        }
        private void RunNegativeTests(Document doc)
        {
            var service=new RevitDrawingService(doc);
            var gridIds=service.Grids().Where(g=>g.Name is "DX0" or "DX10" or "DY0" or "DY10").Select(g=>g.Id).ToArray();
            Check("actual_grid_zone_padding",service.GridZone("Grid A",gridIds,304.8).Bounds==new DrawingBounds(-1,-1,11,11));
            var sheet=(ViewSheet)doc.GetElement(new ElementId(firstIds[0]));
            var viewport=sheet.GetAllViewports().Select(id=>(Viewport)doc.GetElement(id)).Single(v=>((View)doc.GetElement(v.ViewId)).ViewType!=ViewType.Legend);
            var center=viewport.GetBoxCenter();
            using(var group=new TransactionGroup(doc,"Disposable QA negative cases"))
            {
                group.Start();
                using(var tx=new Transaction(doc,"Outside fixture")){tx.Start();viewport.SetBoxCenter(new XYZ(100,100,0));tx.Commit();}
                Check("qa_outside_sheet",service.Qa(vm.Package.PackageGuid).Any(q=>q.Code=="OUTSIDE_SHEET"));
                using(var tx=new Transaction(doc,"Overlap fixture"))
                {tx.Start();var legend=sheet.GetAllViewports().Select(id=>(Viewport)doc.GetElement(id)).Single(v=>((View)doc.GetElement(v.ViewId)).ViewType==ViewType.Legend);viewport.SetBoxCenter(legend.GetBoxCenter());tx.Commit();}
                Check("qa_legend_overlap",service.Qa(vm.Package.PackageGuid).Any(q=>q.Code=="LAYOUT_OVERLAP"));
                using(var tx=new Transaction(doc,"Scale fixture")){tx.Start();var target=(View)doc.GetElement(viewport.ViewId);var primary=target.GetPrimaryViewId();var source=primary==ElementId.InvalidElementId?target:(View)doc.GetElement(primary);source.ViewTemplateId=ElementId.InvalidElementId;source.Scale=50;doc.Regenerate();tx.Commit();}
                var scaleIssues=service.Qa(vm.Package.PackageGuid);
                Check("negative_scale_readback",((View)doc.GetElement(viewport.ViewId)).Scale==50,((View)doc.GetElement(viewport.ViewId)).Scale);
                Check("qa_wrong_scale",scaleIssues.Any(q=>q.Code=="WRONG_VIEW_SCALE"),scaleIssues);
                group.RollBack();
            }
            Check("negative_fixture_restored",viewport.GetBoxCenter().IsAlmostEqualTo(center));
            var failurePackage=RevitDrawingService.Clone(vm.Package);failurePackage.PackageGuid=Guid.NewGuid();failurePackage.Profile.NumberingRule="FAIL-{Sequence:000}";failurePackage.Profile.ViewNamingRule="FAIL-{Level}-{Zone}";
            var preview=service.Preview(failurePackage);Check("rollback_fixture_preview",preview.CanApply,preview.Errors);
            int sheetsBefore=new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();int viewsBefore=new FilteredElementCollector(doc).OfClass(typeof(View)).GetElementCount();int writes=0;bool threw=false;
            try{service.Apply(preview,true,()=>{if(++writes==2)throw new InvalidOperationException("Deliberate second-sheet failure.");});}catch(InvalidOperationException){threw=true;}
            Check("mid_batch_failure_executed",threw&&writes==2,writes);
            Check("rollback_no_orphan_sheets",new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount()==sheetsBefore);
            Check("rollback_no_orphan_views",new FilteredElementCollector(doc).OfClass(typeof(View)).GetElementCount()==viewsBefore);
            Check("rollback_no_package_record",service.Load().Packages.All(p=>p.PackageGuid!=failurePackage.PackageGuid));
            bool unconfirmed=false;try{service.Apply(preview,false);}catch(InvalidOperationException){unconfirmed=true;}Check("unconfirmed_native_blocked",unconfirmed);
            preview.Package.Profile.NumberingRule="TAMPER-{Sequence:000}";bool tampered=false;try{service.Apply(preview,true);}catch(InvalidOperationException){tampered=true;}Check("mutated_preview_blocked",tampered);
            var existing=RevitDrawingService.Clone(vm.Package);existing.PackageGuid=Guid.NewGuid();existing.Profile.ViewStrategy=DrawingViewStrategy.Existing;existing.Profile.NumberingRule="EXIST-{Sequence:000}";existing.Levels=new(){levels[2]};existing.Zones=new(){existing.Zones[0]};
            var original=(View)doc.GetElement(new ElementId(sources[levels[2].Id]));string originalName=original.Name;
            var existingPreview=service.Preview(existing);Check("existing_view_preview",existingPreview.CanApply,existingPreview.Rows.Select(r=>r.IssueText).ToArray());
            if(existingPreview.CanApply)
            {
                long[] existingIds=service.Apply(existingPreview,true);var placed=(ViewSheet)doc.GetElement(new ElementId(existingIds[0]));
                Check("existing_view_not_duplicated",placed.GetAllViewports().Select(id=>(Viewport)doc.GetElement(id)).Any(v=>v.ViewId==original.Id)&&original.Name==originalName);
                Check("existing_view_idempotent",service.Preview(existing).Rows.All(r=>r.Change==DrawingChange.Unchanged));
            }
            var duplicate=RevitDrawingService.Clone(existing);duplicate.PackageGuid=Guid.NewGuid();duplicate.Profile.ViewStrategy=DrawingViewStrategy.Duplicate;duplicate.Profile.NumberingRule="DUP-{Sequence:000}";duplicate.Profile.ViewNamingRule="DUP-{Level}-{Zone}";
            var duplicatePlan=service.Preview(duplicate);Check("duplicate_preview",duplicatePlan.CanApply,duplicatePlan.Rows.Select(r=>r.IssueText).ToArray());
            if(duplicatePlan.CanApply)
            {
                var duplicateIds=service.Apply(duplicatePlan,true);var created=service.Load().Records.Single(r=>r.DrawingPackageGuid==duplicate.PackageGuid);
                Check("duplicate_independent_view",((View)doc.GetElement(new ElementId(created.ViewId))).GetPrimaryViewId()==ElementId.InvalidElementId&&created.ViewId!=original.Id.Value);
                Check("duplicate_idempotent",service.Preview(duplicate).Rows.All(r=>r.Change==DrawingChange.Unchanged));
                var manual=(ViewSheet)doc.GetElement(new ElementId(duplicateIds[0]));using(var tx=new Transaction(doc,"Manual sheet name")){tx.Start();manual.Name="Human edited name";tx.Commit();}
                Check("qa_directory_actual_name",service.SheetStatuses(duplicate.PackageGuid,service.Qa(duplicate.PackageGuid)).Single().SheetName=="Human edited name");
            }
        }
        private void Finish(UIApplication app)
        {
            complete=true;timer.Stop();
            var report=new{GateC3=failed==0?"PASS":"FAIL",Status=failed==0?"PASS":"FAIL",BuildSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))),Passed=assertions.Count-failed,Failed=failed,Assertions=assertions,Timestamp=DateTimeOffset.UtcNow};
            File.WriteAllText(Path.Combine(root,"drawing-runtime.json"),JsonConvert.SerializeObject(report,Formatting.Indented));
            if(app.ActiveUIDocument?.Document.PathName.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)==true)app.ActiveUIDocument.Document.Save();
            var exit=RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);if(app.CanPostCommand(exit))app.PostCommand(exit);
        }
    }
}
#endif
