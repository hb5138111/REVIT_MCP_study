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
        private string adversarialDwg="",actualCadPath="",sourceCadHash="";
        private DrawingTemplateProfile? constructionProfile,asBuiltProfile;
        private long[] cadSheets=Array.Empty<long>();
        private object? cadAnalysisEvidence;
        private bool keepOpenForReview;
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
            application.ControlledApplication.ApplicationInitialized+=(_,__)=>{deadline=DateTime.UtcNow.AddMinutes(6);timer.Start();};
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
                        ConfirmCad(Vm.ExternalAnalysis!.Cad!.Candidates.Single(),"Simple A3",TitleBlockPurpose.Custom,false);break;
                    case 13:
                        if(Vm.Package.Profile.Blueprint.TitleBlockTypeId<=0)throw new InvalidOperationException(Vm.DiagnosticFailure+" "+Vm.Status);
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
                        Vm.StartNewPackage();Vm.SelectSource(TemplateSourceKind.Cad);Vm.SelectExternalFile(string.IsNullOrEmpty(actualCadPath)?adversarialDwg:actualCadPath);Vm.SetCadUnit("Auto");Vm.SetRft(rft);Vm.AnalyzeExternal();break;
                    case 19:
                        var cad=Vm.ExternalAnalysis?.Cad??throw new InvalidOperationException(Vm.Status);
                        Case("I_Candidates",cad.Candidates.Count==2,new{Count=cad.Candidates.Count,GeometryCount=cad.Geometry.Length});
                        Check("I_all_geometry_accounted",cad.Candidates.SelectMany(c=>c.GeometryIds).Distinct().Count()+(cad.Clusters.Where(c=>c.RemoteGeometryWarning).Sum(c=>c.GeometryCount))==cad.Geometry.Length);
                        cadAnalysisEvidence=new{cad.Unit,cad.GlobalBounds,Candidates=cad.Candidates.Select(c=>new{c.CandidateId,c.Width,c.Height,c.Centroid,c.GeometryCount,c.SuggestedPurpose,c.DetectedPaperSize}).ToArray()};
                        var construction=cad.Candidates.Single(c=>c.SuggestedPurpose==TitleBlockPurpose.ConstructionDrawing);
                        ConfirmCad(construction,"施工圖測試樣板",TitleBlockPurpose.ConstructionDrawing,true);break;
                    case 20:
                        if(Vm.Package.Profile.Blueprint.TitleBlockTypeId<=0)throw new InvalidOperationException(Vm.Status);
                        constructionProfile=RevitDrawingService.Clone(Vm.Package.Profile);
                        Check("I_construction_normalized_width",Math.Abs(constructionProfile.Blueprint.TitleBlockBounds.Width*304.8-420)<1,constructionProfile.Blueprint.TitleBlockBounds);
                        Vm.SaveProfile();break;
                    case 21:
                        constructionProfile=RevitDrawingService.Clone(Vm.Package.Profile);
                        Vm.Package.Profile.NumberingRule="N-{Level}";Vm.Package.Profile.ViewNamingRule="N-{Level}";Vm.SelectScope(Select(2),Array.Empty<DrawingZone>());Counts(doc!);Vm.GeneratePlan();break;
                    case 22:PlanReady(2);break;
                    case 23:
                        Case("I_ProductionJourney",Vm.ResultIds.Length==2&&Vm.SheetList.Length==2&&Vm.Issues.All(i=>i.Severity!="ERROR")&&VerifySheets(doc!,2),new{Vm.Status,Vm.Issues});
                        cadSheets=Vm.ResultIds.ToArray();Counts(doc!);Vm.GeneratePlan();break;
                    case 24:
                        Check("I_idempotency_preview",Vm.Plan?.Rows.All(r=>r.Change==DrawingChange.Unchanged)==true,Vm.Status);PlanReady(2);break;
                    case 25:
                        Case("I_Idempotency",Vm.ResultIds.SequenceEqual(cadSheets)&&Count<ViewSheet>(doc!)==sheetCount&&Count<ViewPlan>(doc!)==viewCount&&Count<Viewport>(doc!)==viewportCount);
                        var cadSheet=(ViewSheet)doc!.GetElement(new ElementId(cadSheets[0]));var cadViewport=(Viewport)doc.GetElement(cadSheet.GetAllViewports().Single());
                        using(var tx=new Transaction(doc,"CAD manual placement test")){tx.Start();manualCenter=cadViewport.GetBoxCenter()+new XYZ(.005,0,0);cadViewport.SetBoxCenter(manualCenter);tx.Commit();}Vm.GeneratePlan();break;
                    case 26:
                        Check("I_manual_detected",Vm.Plan?.Rows.Any(r=>r.Change==DrawingChange.ManualOverride)==true,Vm.Status);PlanReady(2);break;
                    case 27:
                        var preserved=(ViewSheet)doc!.GetElement(new ElementId(cadSheets[0]));var preservedViewport=(Viewport)doc.GetElement(preserved.GetAllViewports().Single());
                        Case("I_ManualOverride",preservedViewport.GetBoxCenter().IsAlmostEqualTo(manualCenter)&&Vm.Issues.Any(i=>i.Code=="MANUAL_OVERRIDE"));
                        var asbuilt=Vm.ExternalAnalysis!.Cad!.Candidates.Single(c=>c.SuggestedPurpose==TitleBlockPurpose.AsBuiltDrawing);ConfirmCad(asbuilt,"竣工圖測試樣板",TitleBlockPurpose.AsBuiltDrawing,true);break;
                    case 28:
                        if(Vm.Package.Profile.Blueprint.TitleBlockTypeId<=0)throw new InvalidOperationException(Vm.Status);Vm.SaveProfile();break;
                    case 29:
                        asBuiltProfile=RevitDrawingService.Clone(Vm.Package.Profile);
                        Case("J_IndependentProfiles",constructionProfile!.ProfileGuid!=asBuiltProfile.ProfileGuid&&constructionProfile.Blueprint.TitleBlockTypeId!=asBuiltProfile.Blueprint.TitleBlockTypeId&&constructionProfile.TitleBlockPurpose==TitleBlockPurpose.ConstructionDrawing&&asBuiltProfile.TitleBlockPurpose==TitleBlockPurpose.AsBuiltDrawing&&Vm.Data.Profiles.Any(p=>p.ProfileGuid==constructionProfile.ProfileGuid)&&Vm.Data.Profiles.Any(p=>p.ProfileGuid==asBuiltProfile.ProfileGuid));
                        Check("J_disjoint_geometry",!constructionProfile.Blueprint.CadGeometryIds.Intersect(asBuiltProfile.Blueprint.CadGeometryIds).Any());
                        Case("J_FamilyReadback",VerifyCadFamily(doc!,constructionProfile)&&VerifyCadFamily(doc!,asBuiltProfile)&&constructionProfile.Blueprint.CadNativeGeometryHash!=asBuiltProfile.Blueprint.CadNativeGeometryHash);
                        if(!string.IsNullOrEmpty(actualCadPath))Check("actual_source_unchanged",sourceCadHash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(actualCadPath))));
                        Finish(app);return;
                }
                step++;
            }
            catch(Exception e){Check("runtime_exception",false,e.ToString());Finish(app);}
        }
        private void ConfirmCad(CadTitleBlockCandidate candidate,string name,TitleBlockPurpose purpose,bool normalize)
        {
            Vm.GoToStep(0);Vm.SelectCadCandidate(candidate.CandidateId);var s=Vm.ExternalAnalysis!.CadSelection!;
            s.Purpose=purpose;s.ProfileName=name;
            if(normalize){s.Mode=CadNormalizationMode.UniformToPaper;s.TargetPaper=string.IsNullOrEmpty(actualCadPath)?"A3":"Custom";s.CustomWidthMm=420;}
            Vm.PreviewCad();if(!s.Previewed)throw new InvalidOperationException(Vm.Status);
            s.GeometryFilterConfirmed=true;s.PurposeConfirmed=true;Vm.SizeAndUnitConfirmed=true;Vm.LoadExternal(false,true);
        }
        private bool VerifyCadFamily(Document doc,DrawingTemplateProfile profile)
        {
            var family=(Family)doc.GetElement(new ElementId(profile.Blueprint.TitleBlockFamilyId));var opened=doc.EditFamily(family);
            try
            {
                var imports=new FilteredElementCollector(opened).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToArray();
                var residual=new FilteredElementCollector(opened).OfClass(typeof(CurveElement)).Cast<CurveElement>().ToArray();
                if(imports.Length!=1||residual.Length>4||residual.Any(c=>c.LineStyle is not GraphicsStyle style||style.GraphicsStyleCategory.Id.Value!=(long)BuiltInCategory.OST_InvisibleLines)||ExternalTitleBlockService.NativeCadGeometryHash(opened)!=profile.Blueprint.CadNativeGeometryHash)return false;
                var box=imports[0].get_BoundingBox(null);var expected=profile.Blueprint.CadNormalization!;
                return opened.OwnerFamily.FamilyCategory.Id.Value==(long)BuiltInCategory.OST_TitleBlocks&&box!=null&&Math.Abs((box.Max.X-box.Min.X)*304.8-expected.SourceWidth*expected.UniformScale)<1&&Math.Abs((box.Max.Y-box.Min.Y)*304.8-expected.SourceHeight*expected.UniformScale)<1&&profile.Blueprint.CadGeometryIds.Length>0;
            }finally{opened.Close(false);}
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
            if(request.TryGetValue("ActualCadPath",out var supplied)&&!string.IsNullOrWhiteSpace(supplied))
            {actualCadPath=Path.GetFullPath(supplied);sourceCadHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(actualCadPath)));if(!request.TryGetValue("ActualCadCustomWidthMm",out var width)||width!="420")throw new InvalidOperationException("Actual CAD UAT requires the explicit approved custom-width choice");}
            keepOpenForReview=request.TryGetValue("KeepOpenForReview",out var keep)&&string.Equals(keep,"True",StringComparison.OrdinalIgnoreCase);
            var templates=Directory.EnumerateFiles(Path.GetDirectoryName(request["FamilyTemplate"])!,"*.rft",SearchOption.AllDirectories).Where(p=>Path.GetFileName(p).Equals("A3 metric.rft",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(templates.Length!=1)throw new InvalidOperationException("Disposable A3 fixture generator requires one installed A3 metric RFT");rft=templates[0];
            var family=app.Application.NewFamilyDocument(rft);
            File.WriteAllText(Path.Combine(root,"rft-geometry.json"),JsonConvert.SerializeObject(new FilteredElementCollector(family).WhereElementIsNotElementType().Where(e=>e is CurveElement or ImportInstance or FamilyInstance or TextElement or FilledRegion).Select(e=>new{Id=e.Id.Value,Kind=e.GetType().Name,Category=e.Category?.Name,Deletable=DocumentValidation.CanDeleteElement(family,e.Id),Style=e is CurveElement ce?ce.LineStyle?.Name:"",Styles=e is CurveElement curve?curve.GetLineStyleIds().Select(id=>new{Id=id.Value,Name=family.GetElement(id).Name}).ToArray():null}).ToArray(),Formatting.Indented));
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
            var dirty=new ACadSharp.CadDocument();dirty.Header.InsUnits=ACadSharp.Types.Units.UnitsType.Millimeters;
            foreach(var x in new[]{10000000d,11000000d})
            {
                dirty.Entities.Add(new ACadSharp.Entities.LwPolyline(new[]{new CSMath.XY(x,20000000),new CSMath.XY(x+420000,20000000),new CSMath.XY(x+420000,20297000),new CSMath.XY(x,20297000)}){IsClosed=true});
                dirty.Entities.Add(new ACadSharp.Entities.TextEntity(x==10000000?"施工圖":"竣工圖"){InsertPoint=new CSMath.XYZ(x+10000,20010000,0),AlignmentPoint=new CSMath.XYZ(x+20000,20010000,0),HorizontalAlignment=ACadSharp.Entities.TextHorizontalAlignment.Middle,Height=3000});
                dirty.Entities.Add(new ACadSharp.Entities.Line(new CSMath.XYZ(x+10000,20020000,0),new CSMath.XYZ(x+(x==10000000?50000:90000),20020000,0)));
            }
            dirty.Entities.Add(new ACadSharp.Entities.Line(new CSMath.XYZ(500000000,500000000,0),new CSMath.XYZ(500000100,500000000,0)));
            adversarialDwg=Path.Combine(root,"adversarial-multi-titleblock.dwg");ACadSharp.IO.DwgWriter.Write(adversarialDwg,dirty);
            new RevitDrawingService(doc).SaveProfile(new DrawingTemplateProfile{ProfileKind=DrawingProfileKind.Fixture,ProfileName="Fixture hidden profile"});
            string path=Path.Combine(root,"ProductionJourney.rvt");doc.SaveAs(path,new SaveAsOptions());doc.Close(false);app.OpenAndActivateDocument(path);
        }
        private void Finish(UIApplication app)
        {
            finished=true;timer.Stop();bool pass=failures==0&&"ABCDEFGH".All(c=>cases.TryGetValue(c.ToString(),out bool ok)&&ok)&&cases.TryGetValue("I_ProductionJourney",out bool journey)&&journey&&cases.TryGetValue("J_FamilyReadback",out bool readback)&&readback;
            File.WriteAllText(Path.Combine(root,"drawing-c4-runtime.json"),JsonConvert.SerializeObject(new{Status=pass?"PASS":"FAIL",GateC4=pass?"PASS":"FAIL",Cases=cases,BuildSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))),Passed=assertions.Count-failures,Failed=failures,Assertions=assertions,Timestamp=DateTimeOffset.UtcNow},Formatting.Indented));
            if(!string.IsNullOrEmpty(actualCadPath))File.WriteAllText(Path.Combine(root,"actual-cad-uat.json"),JsonConvert.SerializeObject(new{Status=pass?"ACTUAL_DWG_UAT_PASS":"SOURCE_FAILURE",Source="<USER_TITLEBLOCK_DWG>",SourceSHA256=sourceCadHash,BuildSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))),Analysis=cadAnalysisEvidence,Construction=constructionProfile==null?null:new{constructionProfile.ProfileGuid,constructionProfile.ProfileVersion,constructionProfile.TitleBlockPurpose,constructionProfile.Blueprint},AsBuilt=asBuiltProfile==null?null:new{asBuiltProfile.ProfileGuid,asBuiltProfile.ProfileVersion,asBuiltProfile.TitleBlockPurpose,asBuiltProfile.Blueprint},SheetIds=cadSheets,Cases=cases},Formatting.Indented));
            if(app.ActiveUIDocument?.Document.PathName==Path.Combine(root,"ProductionJourney.rvt"))app.ActiveUIDocument.Document.Save();
            if(pass&&keepOpenForReview&&!string.IsNullOrEmpty(actualCadPath))
            {var saved=Vm.Data.Packages.Single(p=>p.Profile.ProfileGuid==constructionProfile!.ProfileGuid);Vm.UsePackage(RevitDrawingService.Clone(saved));Vm.RunQa();return;}
            var exit=RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);if(app.CanPostCommand(exit))app.PostCommand(exit);
        }
    }
}
#endif

