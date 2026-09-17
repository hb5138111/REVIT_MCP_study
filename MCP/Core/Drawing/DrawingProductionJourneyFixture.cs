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
    /// <summary>C4 drives the real panel child and ExternalEvent host. Only fixture preparation uses direct model writes.</summary>
    internal sealed class DrawingProductionJourneyFixture : IExternalEventHandler
    {
        private readonly string root;
        private readonly BimConstructionPanelViewModel panel;
        private DrawingProductionViewModel Vm=>panel.Drawing;
        private readonly ExternalEvent next;
        private readonly DispatcherTimer timer;
        private readonly List<object> assertions=new();
        private readonly Dictionary<string,bool> cases=new();
        private int step,failures;
        private bool queued,finished;
        private DateTime deadline;
        private string rfa="",dwg="",rft="";
        private long[] first=Array.Empty<long>();
        private long reference;
        private XYZ? manualCenter;
        private int viewCount,viewportCount,sheetCount;
        private string[] tempBefore=Array.Empty<string>();
        public DrawingProductionJourneyFixture(UIControlledApplication application,string root,BimConstructionPanelViewModel panel)
        {
            this.root=Path.GetFullPath(root);this.panel=panel;next=ExternalEvent.Create(this);
            timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};
            timer.Tick+=(_,__)=>{if(!finished&&!queued&&!Vm.Busy){var request=next.Raise();queued=request is ExternalEventRequest.Accepted or ExternalEventRequest.Pending;}};
            application.ControlledApplication.ApplicationInitialized+=(_,__)=>{deadline=DateTime.UtcNow.AddMinutes(4);timer.Start();};
        }
        public string GetName()=>"Drawing Production User Journey Gate C4";
        private void Check(string name,bool passed,object? actual=null)
        {assertions.Add(new{TestName=name,Passed=passed,Actual=actual});if(!passed)failures++;}
        private void Case(string name,bool passed,object? actual=null){cases[name]=passed;Check("C4_"+name,passed,actual);if(!passed)throw new InvalidOperationException(name+": "+JsonConvert.SerializeObject(actual));}
        private void PlanReady(int count)
        {if(Vm.Plan?.CanApply!=true||Vm.Plan.Rows.Count!=count)throw new InvalidOperationException(Vm.Status+" "+JsonConvert.SerializeObject(Vm.Plan));Vm.GoToStep(3);if(!Vm.CanApply)throw new InvalidOperationException(Vm.ApplyBlockedReason);Vm.ConfirmAndApply();}
        private DrawingChoice[] Select(int count)=>Vm.Levels.Where(l=>l.Name is "FL1" or "FL2" or "FL3").OrderBy(l=>l.Elevation).Take(count).ToArray();
        private static int Count<T>(Document doc) where T:Element=>new FilteredElementCollector(doc).OfClass(typeof(T)).GetElementCount();
        private void Counts(Document doc){viewCount=Count<ViewPlan>(doc);viewportCount=Count<Viewport>(doc);sheetCount=Count<ViewSheet>(doc);}
        public void Execute(UIApplication app)
        {
            queued=false;
            if(Vm.Busy)return;
            try
            {
                File.WriteAllText(Path.Combine(root,"drawing-c4-progress.txt"),$"Step {step}; {Vm.Status}");
                if(DateTime.UtcNow>deadline)throw new InvalidOperationException("C4 timeout");
                if(step>0&&app.ActiveUIDocument?.Document.PathName!=Path.Combine(root,"ProductionJourney.rvt"))throw new InvalidOperationException("Refusing non-fixture document");
                var doc=app.ActiveUIDocument?.Document;
                switch(step)
                {
                    case 0:
                        if(app.Application.Documents.Size!=0||!Path.GetFileName(root).StartsWith("revit-selftest-")||!File.Exists(Path.Combine(root,"request.json")))throw new InvalidOperationException("Isolated launcher required");
                        Prepare(app);panel.Initialize();break;
                    case 1:
                        Check("panel_initialization",Vm.Levels.Any(l=>l.Name=="FL1")&&Vm.LevelSources.Count>0,Vm.Status);
                        var visible=JsonConvert.SerializeObject(new{Vm.Sheets,Vm.Levels,Vm.Data,Vm.LevelSources});
                        Check("production_fixture_text_count_zero",!visible.Contains("Disposable")&&!visible.Contains("Fixture"));
                        Vm.SelectSource(TemplateSourceKind.ExternalRfa);Vm.SelectExternalFile(rfa);Vm.AnalyzeExternal();break;
                    case 2:
                        Check("external_rfa_analyzed_actual_file",Vm.ExternalAnalysis?.FilePath==rfa&&File.Exists(rfa),Vm.Status);
                        Vm.SizeAndUnitConfirmed=true;Vm.LoadExternal(false,true);break;
                    case 3:
                        Check("rfa_loaded_titleblock_only",Vm.Package.Profile.Blueprint.AutoLayout&&Vm.Package.Profile.Blueprint.SourceSheetId==0,Vm.Status);
                        Vm.SelectScope(Select(3),Array.Empty<DrawingZone>());Check("three_auto_resolved_sources",Vm.Package.SourceViewsByLevel.Count==3);
                        Counts(doc!);Vm.GeneratePlan();break;
                    case 4:
                        Check("D_no_zone_three_planned",Vm.Plan?.Rows.Count==3&&Vm.Plan.Rows.All(r=>r.Zone.IsUnzoned),Vm.Status);PlanReady(3);break;
                    case 5:
                        first=Vm.ResultIds.ToArray();
                        Case("A",first.Length==3&&Vm.SheetList.Length==3&&Vm.Step==4&&Vm.Issues.All(i=>i.Severity!="ERROR")&&Count<ViewSheet>(doc!)==sheetCount+3&&Count<ViewPlan>(doc!)==viewCount+3&&Count<Viewport>(doc!)==viewportCount+3,new{Vm.Status,Vm.Issues});
                        Case("D",Vm.Package.Zones.Count==0&&Vm.SheetList.Length==3);
                        Check("A_titleblock_level_ownership_readback",VerifySheets(doc!,3));Counts(doc!);Vm.GeneratePlan();break;
                    case 6:Check("H_unchanged_preview",Vm.Plan?.Rows.All(r=>r.Change==DrawingChange.Unchanged)==true,Vm.Status);PlanReady(3);break;
                    case 7:
                        Case("H",Vm.ResultIds.SequenceEqual(first)&&Count<ViewSheet>(doc!)==sheetCount&&Count<ViewPlan>(doc!)==viewCount&&Count<Viewport>(doc!)==viewportCount);
                        var sheet=(ViewSheet)doc!.GetElement(new ElementId(first[0]));var vp=(Viewport)doc.GetElement(sheet.GetAllViewports().Single());
                        using(var tx=new Transaction(doc,"Manual placement test")){tx.Start();manualCenter=vp.GetBoxCenter()+new XYZ(.005,0,0);vp.SetBoxCenter(manualCenter);tx.Commit();}Vm.GeneratePlan();break;
                    case 8:Check("G_manual_detected",Vm.Plan?.Rows.Any(r=>r.Change==DrawingChange.ManualOverride)==true,Vm.Status);PlanReady(3);break;
                    case 9:
                        var manual=(ViewSheet)doc!.GetElement(new ElementId(first[0]));var moved=(Viewport)doc.GetElement(manual.GetAllViewports().Single());
                        Case("G",moved.GetBoxCenter().IsAlmostEqualTo(manualCenter)&&Vm.Issues.Any(i=>i.Code=="MANUAL_OVERRIDE"));
                        Vm.Package.Profile.NumberingRule="TAKEN";Vm.Invalidate();Vm.GeneratePlan();break;
                    case 10:
                        Vm.GoToStep(3);Case("F",Vm.Plan?.CanApply==false&&!Vm.CanApply&&Vm.ApplyBlockedReason.Length>0&&Vm.Plan.Rows.Any(r=>r.Change==DrawingChange.Conflict),Vm.ApplyBlockedReason);
                        var profile=RevitDrawingService.Clone(Vm.Package.Profile);Vm.StartNewPackage();Vm.UseProfile(profile);Vm.Package.Profile.NumberingRule="E-{Level}";Vm.Package.Profile.ViewNamingRule="E-{Level}";
                        Vm.SelectScope(Vm.Levels.Where(l=>l.Name is "FL1" or "FL2" or "FL4"),Array.Empty<DrawingZone>());Vm.GeneratePlan();break;
                    case 11:
                        Case("E",Vm.Plan?.Rows.Count==3&&Vm.Plan.Rows.Count(r=>r.Change==DrawingChange.Add)==2&&Vm.Plan.Rows.Count(r=>r.IssueText.Contains("來源視圖"))==1,Vm.Status);
                        Vm.StartNewPackage();Vm.SelectSource(TemplateSourceKind.Cad);Vm.SelectExternalFile(dwg);Vm.SetCadUnit("mm");Vm.SetRft(rft);Vm.AnalyzeExternal();break;
                    case 12:
                        Check("B_real_dwg_a3_suggested",Vm.ExternalAnalysis?.SizeSuggestion=="A3"&&File.Exists(dwg),Vm.ExternalAnalysis);
                        tempBefore=Directory.GetDirectories(Path.GetTempPath(),"RevitMCP-Drawing-*");
                        Vm.SizeAndUnitConfirmed=true;Vm.LoadExternal(false,true);break;
                    case 13:
                        Check("B_cad_loaded",Vm.Package.Profile.Blueprint.SourceKind==TemplateSourceKind.Cad,Vm.Status);
                        Check("B_temp_files_cleaned",Directory.GetDirectories(Path.GetTempPath(),"RevitMCP-Drawing-*").OrderBy(p=>p).SequenceEqual(tempBefore.OrderBy(p=>p)));
                        var loadedFamily=(Family)doc!.GetElement(new ElementId(Vm.Package.Profile.Blueprint.TitleBlockFamilyId));
                        var inspect=doc.EditFamily(loadedFamily);
                        try{Check("B_imported_cad_persists_in_loaded_family",new FilteredElementCollector(inspect).OfClass(typeof(ImportInstance)).GetElementCount()==1);}finally{inspect.Close(false);}
                        Vm.Package.Profile.NumberingRule="B-{Level}";Vm.Package.Profile.ViewNamingRule="B-{Level}";Vm.SelectScope(Select(2),Array.Empty<DrawingZone>());Vm.GeneratePlan();break;
                    case 14:PlanReady(2);break;
                    case 15:
                        Case("B",Vm.ResultIds.Length==2&&Vm.SheetList.Length==2&&Vm.Issues.All(i=>i.Severity!="ERROR")&&VerifySheets(doc!,2),new{Vm.Status,Vm.Issues});
                        Check("B_geometry_limit_warning",Vm.Package.Profile.Blueprint.Warnings.Any(w=>w.Contains("Label")));
                        reference=Vm.ResultIds[0];Vm.StartNewPackage();Vm.Extract(reference);break;
                    case 16:
                        Vm.Package.Profile.NumberingRule="C-{Level}";Vm.Package.Profile.ViewNamingRule="C-{Level}";Vm.Package.Profile.ViewStrategy=DrawingViewStrategy.Duplicate;
                        Vm.SelectScope(Select(2),Array.Empty<DrawingZone>());Vm.GeneratePlan();break;
                    case 17:PlanReady(2);break;
                    case 18:
                        var expected=Vm.Package.Profile.Blueprint.Viewports.Single();
                        Case("C",Vm.ResultIds.Length==2&&Vm.SheetList.Length==2&&Vm.Issues.All(i=>i.Severity!="ERROR")&&Vm.ResultIds.All(id=>{var s=(ViewSheet)doc!.GetElement(new ElementId(id));var v=(Viewport)doc.GetElement(s.GetAllViewports().Single());return Math.Abs(v.GetBoxCenter().X-expected.AbsoluteX)<1e-7&&Math.Abs(v.GetBoxCenter().Y-expected.AbsoluteY)<1e-7;}),new{Vm.Status,Vm.Issues});
                        Finish(app);return;
                }
                step++;
            }
            catch(Exception e){Check("runtime_exception",false,e.ToString());Finish(app);}
        }
        private bool VerifySheets(Document doc,int count)
        {
            var data=new RevitDrawingService(doc).Load();var records=data.Records.Where(r=>r.DrawingPackageGuid==Vm.Package.PackageGuid).ToArray();
            return records.Length==count&&records.All(r=>
            {
                var sheet=doc.GetElement(new ElementId(r.SheetId)) as ViewSheet;
                var row=Vm.Plan!.Rows.Single(p=>p.Key==r.Key);var view=doc.GetElement(new ElementId(r.ViewId)) as ViewPlan;
                return sheet?.UniqueId==r.SheetUniqueId&&sheet.SheetNumber==row.SheetNumber&&view?.GenLevel.Id.Value==row.Level.Id&&sheet.GetAllViewports().Count==1&&new FilteredElementCollector(doc,sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().Single().GetTypeId().Value==Vm.Package.Profile.Blueprint.TitleBlockTypeId;
            });
        }
        private void Prepare(UIApplication app)
        {
            var request=JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"request.json")))!;
            var templates=Directory.EnumerateFiles(Path.GetDirectoryName(request["FamilyTemplate"])!,"*.rft",SearchOption.AllDirectories).Where(p=>Path.GetFileName(p).Equals("A3 metric.rft",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(templates.Length!=1)throw new InvalidOperationException("Disposable A3 fixture generator requires one installed A3 metric RFT");rft=templates[0];
            var family=app.Application.NewFamilyDocument(rft);
            try{using(var tx=new Transaction(family,"Fixture type")){tx.Start();if(family.FamilyManager.Types.Size==0)family.FamilyManager.NewType("A3");tx.Commit();}rfa=Path.Combine(root,"external-titleblock-a3.rfa");family.SaveAs(rfa,new SaveAsOptions());}finally{family.Close(false);}
            var doc=app.Application.NewProjectDocument(request["BaseProjectTemplate"]);
            if(doc.IsWorkshared||doc.IsLinked)throw new InvalidOperationException("Standalone fixture required");
            ElementId cadView;
            using(var tx=new Transaction(doc,"Production-like test asset preparation"))
            {
                tx.Start();var type=new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t=>t.ViewFamily==ViewFamily.FloorPlan);
                for(int i=1;i<=4;i++)
                {
                    var level=Level.Create(doc,(i-1)*12);level.Name="FL"+i;if(i==4)continue;
                    var view=ViewPlan.Create(doc,type.Id,level.Id);view.Name="建築平面 "+level.Name;view.Scale=100;view.CropBoxActive=true;view.CropBox=new BoundingBoxXYZ{Min=new XYZ(-1,-1,-1),Max=new XYZ(12,12,1)};
                    doc.Create.NewDetailCurve(view,Line.CreateBound(new XYZ(0,0,0),new XYZ(10,0,0)));doc.Create.NewDetailCurve(view,Line.CreateBound(new XYZ(0,0,0),new XYZ(0,10,0)));
                }
                var hidden=Level.Create(doc,100);hidden.Name="Disposable Level";DrawingFixtureIsolation.Mark(hidden);
                var sheet=ViewSheet.Create(doc,ElementId.InvalidElementId);sheet.SheetNumber="TAKEN";
                var draftType=new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t=>t.ViewFamily==ViewFamily.Drafting);
                var drawing=ViewDrafting.Create(doc,draftType.Id);drawing.Name="CAD asset generator";drawing.Scale=1;cadView=drawing.Id;
                var points=new[]{XYZ.Zero,new XYZ(420/304.8,0,0),new XYZ(420/304.8,297/304.8,0),new XYZ(0,297/304.8,0)};
                for(int i=0;i<4;i++)doc.Create.NewDetailCurve(drawing,Line.CreateBound(points[i],points[(i+1)%4]));
                tx.Commit();
            }
            using(var options=new DWGExportOptions{MergedViews=true,FileVersion=ACADVersion.R2018,TargetUnit=ExportUnit.Millimeter})
            {if(!doc.Export(root,"simple-titleblock-a3",new[]{cadView},options))throw new InvalidOperationException("DWG fixture export failed");}
            dwg=Path.Combine(root,"simple-titleblock-a3.dwg");
            if(!File.Exists(dwg))throw new InvalidOperationException("External DWG asset missing");
            new RevitDrawingService(doc).SaveProfile(new DrawingTemplateProfile{ProfileKind=DrawingProfileKind.Fixture,ProfileName="Fixture hidden profile"});
            string path=Path.Combine(root,"ProductionJourney.rvt");doc.SaveAs(path,new SaveAsOptions());doc.Close(false);app.OpenAndActivateDocument(path);
        }
        private void Finish(UIApplication app)
        {
            finished=true;timer.Stop();bool pass=failures==0&&"ABCDEFGH".All(c=>cases.TryGetValue(c.ToString(),out bool ok)&&ok);
            File.WriteAllText(Path.Combine(root,"drawing-c4-runtime.json"),JsonConvert.SerializeObject(new{Status=pass?"PASS":"FAIL",GateC4=pass?"PASS":"FAIL",Cases=cases,BuildSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))),Passed=assertions.Count-failures,Failed=failures,Assertions=assertions,Timestamp=DateTimeOffset.UtcNow},Formatting.Indented));
            if(app.ActiveUIDocument?.Document.PathName==Path.Combine(root,"ProductionJourney.rvt"))app.ActiveUIDocument.Document.Save();
            var exit=RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);if(app.CanPostCommand(exit))app.PostCommand(exit);
        }
    }
}
#endif
