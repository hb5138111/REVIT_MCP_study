#if REVIT2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCP.UI;

namespace RevitMCP.Core.Site
{
    /// <summary>Normal application lifecycle, real selection and queued production ExternalEvents.</summary>
    internal sealed class EarthworkWorkflowSelfTest : IExternalEventHandler
    {
        private readonly string root;
        private readonly SiteTerrainViewModel vm;
        private readonly SiteTerrainControl control;
        private readonly ExternalEvent next;
        private readonly DispatcherTimer timer;
        private readonly List<object> checks=new();
        private int step,failed;
        private bool queued,finished;
        private DateTime deadline;
        private long terrain,cutter;
        private string fixture="";
        private double expectedCutter;
        private Guid zoneA;
        private Dictionary<Guid,long>? originalRecordIds;
        public EarthworkWorkflowSelfTest(UIControlledApplication app,string directory,SiteTerrainViewModel vm,SiteTerrainControl control)
        {
            root=Path.GetFullPath(directory);this.vm=vm;this.control=control;
            vm.EarthworkTestMode=true;
            next=ExternalEvent.Create(this);timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
            timer.Tick+=(_,__)=>{if(!finished&&!queued&&!vm.Busy)queued=next.Raise()==ExternalEventRequest.Accepted;};
            app.ControlledApplication.ApplicationInitialized+=(_,__)=>{deadline=DateTime.UtcNow.AddMinutes(2);timer.Start();};
        }
        public string GetName()=>"Disposable earthwork native workflow";
        private void Check(string name,object expected,object? actual,bool pass)
        {
            checks.Add(new{Name=name,Expected=expected,Actual=actual,Passed=pass,Evidence=$"Normal lifecycle; real UIDocument selection / ExternalEvent / WPF binding; step {step}"});
            if(!pass)failed++;
        }
        private void Near(string name,double expected,double actual)=>Check(name,expected,actual,Math.Abs(expected-actual)<1e-4);
        private void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        public void Execute(UIApplication ui)
        {
            queued=false;
            // WPF Loaded can queue production context work after the timer raised this event.
            // Do not advance the fixture while that work owns the host.
            if(vm.Busy)return;
            try
            {
                Require(DateTime.UtcNow<deadline,"Native workflow timeout");
                if(step>0)Require(ui.ActiveUIDocument?.Document.PathName==fixture,"Disposable fixture is no longer active; stopped without touching another model");
                switch(step++)
                {
                    case 0:
                        Require(ui.Application.Documents.Size==0,"Requires document-free isolated Revit session");
                        var request=JObject.Parse(File.ReadAllText(Path.Combine(root,"request.json")));
                        var template=(string?)request["BaseProjectTemplate"]??throw new InvalidOperationException("Missing test template");
                        Require(Path.GetExtension(template).Equals(".rte",StringComparison.OrdinalIgnoreCase),"Only test template allowed");
                        var d=ui.Application.NewProjectDocument(template);
                        using(var tx=new Transaction(d,"Disposable earthwork native fixture"))
                        {
                            tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FixtureOverlapWarning()));var units=d.GetUnits();units.SetFormatOptions(SpecTypeId.Length,new FormatOptions(UnitTypeId.Centimeters));d.SetUnits(units);
                            var level=new FilteredElementCollector(d).OfClass(typeof(Level)).Cast<Level>().OrderBy(l=>Math.Abs(l.ProjectElevation)).First();
                            var type=new FilteredElementCollector(d).OfClass(typeof(ToposolidType)).FirstElementId();
                            XYZ P(double x,double y,double z)=>CoordinateTransformService.Feet(new SitePoint(x,y,z));
                            var topo=Toposolid.Create(d,new List<XYZ>{P(0,0,2),P(10,0,2),P(10,10,2),P(0,10,2)},type,level.Id);terrain=topo.Id.Value;
                            var ft=new FilteredElementCollector(d).OfClass(typeof(FloorType)).Cast<FloorType>().First(f=>!f.IsFoundationSlab);
                            var corners=new[]{P(0,0,0),P(5,0,0),P(5,5,0),P(0,5,0)};var loop=new CurveLoop();for(int i=0;i<4;i++)loop.Append(Line.CreateBound(corners[i],corners[(i+1)%4]));
                            var floor=Floor.Create(d,new[]{loop},ft.Id,level.Id);cutter=floor.Id.Value;tx.Commit();
                            // Independently derive the cutter prism depth from actual fixture type thickness.
                            expectedCutter=25*(2-Math.Max(CoordinateTransformService.Metres(topo.get_BoundingBox(null).Min).Z,CoordinateTransformService.Metres(floor.get_BoundingBox(null).Min).Z));
                        }
                        fixture=Path.Combine(root,"EarthworkWorkflow.rvt");d.SaveAs(fixture);d.Close(false);ui.OpenAndActivateDocument(fixture);
                        ui.GetDockablePane(BimConstructionPanelPage.PaneId).Show();vm.GoToStep(3);break;
                    case 1: vm.RefreshContext();break;
                    case 2:
                        if(vm.Context==null){step=1;break;}
                        Check("project_units","cm",vm.DisplayLengthUnit,vm.DisplayLengthUnit=="cm");
                        ui.ActiveUIDocument.Selection.SetElementIds(new[]{new ElementId(terrain)});vm.UseSelection(true);break;
                    case 3:
                        Check("actual_terrain_selection",terrain,vm.TerrainId,vm.TerrainId==terrain);
                        ui.ActiveUIDocument.Selection.SetElementIds(new[]{new ElementId(cutter)});vm.UseBoundarySelection();break;
                    case 4:
                        Check("floor_boundary_vertices",4,vm.BoundaryPoints.Count,vm.BoundaryPoints.Count==4);
                        Check("unset_target_blocked",false,vm.CanCalculate,!vm.CanCalculate);
                        control.SetFixtureTargetText("0");
                        Check("can_calculate",true,vm.CanCalculate,vm.CanCalculate);vm.CalculateBoundary();break;
                    case 5:
                        var zero=vm.Result as SiteEarthworkSummary??throw new InvalidOperationException("Missing boundary result: "+vm.Status);
                        Near("area_25",25,zero.Quantity!.Area);Near("cut_50",50,zero.Quantity.CutVolume);Near("fill_zero",0,zero.Quantity.FillVolume);
                        Check("wpf_boundary_result",true,control.FixtureResultText,control.FixtureResultText.Contains(zero.Source));
                        control.SetFixtureTargetText("100");Near("cm_to_m",1,vm.TargetElevation);vm.CalculateBoundary();break;
                    case 6:
                        var elevated=vm.Result as SiteEarthworkSummary??throw new InvalidOperationException("Missing elevated result: "+vm.Status);
                        Near("nonzero_target_cut_25",25,elevated.Quantity!.CutVolume);
                        control.SetFixtureTargetText("invalid");vm.CalculateBoundary();
                        Check("invalid_text_blocks_stale_result",true,vm.Status,!vm.CanCalculate&&vm.Result==null);
                        ui.ActiveUIDocument.Selection.SetElementIds(new[]{new ElementId(cutter)});vm.UseSelection(false);break;
                    case 7:
                        Check("actual_cutter_selection",cutter,vm.CutterId,vm.CutterId==cutter);vm.PreviewExcavation();break;
                    case 8:
                        Near("cutter_preview",expectedCutter,vm.ExcavationPreview??double.NaN);
                        Check("wpf_preview_result",true,control.FixtureResultText,control.FixtureResultText.Contains("模型已回復"));
                        vm.Confirmed=true;vm.ExecuteExcavation();break;
                    case 9:
                        var result=vm.Result as SiteExcavationOutcome??throw new InvalidOperationException("Missing typed cutter result: "+vm.Status);
                        Near("cutter_executed",expectedCutter,result.CutBankVolume);
                        Check("wpf_executed_result",true,control.FixtureResultText,result.Executed&&control.FixtureResultText.Contains(result.ProjectVolume)&&control.FixtureResultText.Contains("read-back"));
                        break;
                    case 10:
                        // Use a fresh unexcavated pair for the multi-zone quantity/schedule workflow.
                        var project=ui.ActiveUIDocument.Document;
                        using(var tx=new Transaction(project,"Disposable multi-zone source"))
                        {
                            tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FixtureOverlapWarning()));
                            var level=new FilteredElementCollector(project).OfClass(typeof(Level)).Cast<Level>().OrderBy(l=>Math.Abs(l.ProjectElevation)).First();
                            var type=new FilteredElementCollector(project).OfClass(typeof(ToposolidType)).FirstElementId();
                            XYZ P(double x,double y,double z)=>CoordinateTransformService.Feet(new SitePoint(x,y,z));
                            terrain=Toposolid.Create(project,new List<XYZ>{P(20,0,2),P(30,0,2),P(30,10,2),P(20,10,2)},type,level.Id).Id.Value;
                            var ft=new FilteredElementCollector(project).OfClass(typeof(FloorType)).Cast<FloorType>().First(f=>!f.IsFoundationSlab);var corners=new[]{P(20,0,0),P(25,0,0),P(25,5,0),P(20,5,0)};var loop=new CurveLoop();for(int i=0;i<4;i++)loop.Append(Line.CreateBound(corners[i],corners[(i+1)%4]));cutter=Floor.Create(project,new[]{loop},ft.Id,level.Id).Id.Value;
                            var unowned=ViewSchedule.CreateSchedule(project,new ElementId(BuiltInCategory.OST_GenericModel));unowned.Name="土方工程明細";tx.Commit();
                        }
                        vm.NewEarthworkZone();vm.ZoneNumber="A";vm.ZoneName="Fixture A";vm.EarthworkMode="BoundaryTin";
                        ui.ActiveUIDocument.Selection.SetElementIds(new[]{new ElementId(terrain)});vm.UseSelection(true);break;
                    case 11: ui.ActiveUIDocument.Selection.SetElementIds(new[]{new ElementId(cutter)});vm.UseBoundarySelection();break;
                    case 12:
                        vm.TargetMode="LevelOffset";vm.EarthworkLevelId=vm.Context!.Levels.OrderBy(l=>Math.Abs(l.ElevationMetres)).First().Id;vm.TargetOffsetText="0";
                        Check("level_offset_can_calculate",true,vm.CanCalculate,vm.CanCalculate);vm.CalculateBoundary();break;
                    case 13:
                        Check("profile_missing_blocks_save",false,vm.CanSaveZone,!vm.CanSaveZone);
                        vm.SaveEarthworkProfile(new EarthworkProjectProfile{ProfileName="Runtime fixture only",ProfileKind=EarthworkProfileKind.TestFixture,Currency="TEST",SwellFactor=1.2,FillLooseFactor=1.1,ReusableRate=0,TruckName="Fixture truck",TruckCapacity=10.1,TruckLoadUtilization=1,ExcavationUnitCost=2,LoadingUnitCost=3,HaulCostPerTrip=4,DisposalCostPerVolume=5,ImportedFillCostPerVolume=6,BackfillPlacementCostPerVolume=7,CompactionCostPerVolume=8,MobilizationCost=9},true);break;
                    case 14:
                        Require(vm.CurrentEarthwork!=null,"Missing estimated record: "+vm.Status+" / "+vm.EstimateStatus);zoneA=vm.CurrentEarthwork.ZoneGuid;
                        Near("native_profile_cut",50,vm.CurrentEarthwork.Quantity.CutBankVolume);Near("native_profile_export",60,vm.CurrentEarthwork.Logistics.ExportLooseVolume);Near("native_profile_trips",6,vm.CurrentEarthwork.Logistics.ExportTruckTrips);Near("native_profile_cost",613,(double)vm.CurrentEarthwork.Cost.TotalEstimatedCost);
                        vm.SaveEarthworkZone(true);break;
                    case 15:
                        Check("saved_zone_A",1,vm.EarthworkRecords.Count,vm.EarthworkRecords.Count==1);
                        vm.NewEarthworkZone();vm.ZoneNumber="B";vm.ZoneName="Fixture B";ui.ActiveUIDocument.Selection.SetElementIds(new[]{new ElementId(cutter)});vm.UseBoundarySelection();break;
                    case 16: vm.TargetMode="LevelOffset";vm.TargetOffsetText="100";vm.CalculateBoundary();break;
                    case 17:
                        Require(vm.CurrentEarthwork!=null,"Missing Zone B estimate: "+vm.EstimateStatus);Near("zone_B_cut",25,vm.CurrentEarthwork.Quantity.CutBankVolume);vm.SaveEarthworkZone(true);break;
                    case 18:
                        Check("two_saved_zones",2,vm.EarthworkRecords.Count,vm.EarthworkRecords.Count==2);vm.PreviewEarthworkSchedule();break;
                    case 19:
                        Require(vm.SchedulePreview!=null,"Missing schedule preview: "+vm.Status);Check("unowned_name_preserved","土方工程明細 (BIM)",vm.SchedulePreview.ScheduleName,vm.SchedulePreview.ScheduleName=="土方工程明細 (BIM)");
                        Check("schedule_preview_two_creates",2,vm.SchedulePreview.CreateZones.Count,vm.SchedulePreview.CreateZones.Count==2);vm.ConfirmEarthworkSchedule(true);break;
                    case 20:
                        Require(vm.ScheduleResult!=null,"Schedule write failed: "+vm.Status);originalRecordIds=vm.ScheduleResult.RecordIds.ToDictionary(x=>x.Key,x=>x.Value);
                        Check("schedule_two_records",2,originalRecordIds.Count,originalRecordIds.Count==2);Check("schedule_required_fields",36,vm.ScheduleResult.FieldCount,vm.ScheduleResult.FieldCount>=36);
                        vm.SelectEarthworkZone(vm.EarthworkRecords.Single(r=>r.ZoneGuid==zoneA),false);vm.TargetElevationText="150";vm.CalculateBoundary();break;
                    case 21:
                        Require(vm.CurrentEarthwork!=null,"Missing recalculated A");Near("zone_A_recalculated_cut",12.5,vm.CurrentEarthwork.Quantity.CutBankVolume);vm.SaveEarthworkZone(true);break;
                    case 22: vm.PreviewEarthworkSchedule();break;
                    case 23: Require(vm.SchedulePreview!=null,"Updated schedule preview missing");vm.ConfirmEarthworkSchedule(true);break;
                    case 24:
                        Require(vm.ScheduleResult!=null,"Updated schedule result missing: "+vm.Status);
                        var read=RevitEarthworkRecords.ReadBack(ui.ActiveUIDocument.Document,vm.EarthworkRecords.ToArray());
                        Check("update_stable_record_ids",true,read.RecordIds.Count,read.RecordIds.Count==2&&read.RecordIds.All(p=>originalRecordIds![p.Key]==p.Value));
                        Near("updated_total_cut",37.5,vm.EarthworkRecords.Sum(r=>r.Quantity.CutBankVolume));Near("updated_total_export",45,vm.EarthworkRecords.Sum(r=>r.Logistics.ExportLooseVolume));Near("updated_total_cost",473,(double)vm.EarthworkRecords.Sum(r=>r.Cost.TotalEstimatedCost));
                        var schedule=(ViewSchedule)ui.ActiveUIDocument.Document.GetElement(new ElementId(read.ScheduleId));Check("schedule_grand_total",true,schedule.Definition.ShowGrandTotal,schedule.Definition.ShowGrandTotal);
                        var rows=vm.EarthworkRecords.ToArray();var preview=RevitEarthworkRecords.Preview(ui.ActiveUIDocument.Document,rows);bool rolledBack=false;
                        try{RevitEarthworkRecords.WriteSchedule(ui.ActiveUIDocument.Document,rows,preview,true,()=>throw new IOException("Injected schedule read-back failure"));}catch(IOException){rolledBack=true;}
                        var after=RevitEarthworkRecords.ReadBack(ui.ActiveUIDocument.Document,rows);Check("schedule_failure_rollback",true,after.ReadBack,rolledBack&&after.RecordIds.All(p=>originalRecordIds![p.Key]==p.Value));
                        EarthworkExport.Write(Path.Combine(root,"earthwork-records.csv"),rows,1);EarthworkExport.Write(Path.Combine(root,"earthwork-records.json"),rows,2);EarthworkExport.Write(Path.Combine(root,"earthwork-records.md"),rows,3);
                        Check("record_exports",true,"CSV / JSON / Markdown",new[]{"csv","json","md"}.All(ext=>new FileInfo(Path.Combine(root,"earthwork-records."+ext)).Length>0));
                        vm.EarthworkTestMode=false;vm.RefreshContext();break;
                    case 25:
                        Check("production_hides_fixture_profiles",0,vm.EarthworkProfiles.Count,vm.EarthworkProfiles.Count==0);
                        Check("production_hides_fixture_results",0,vm.EarthworkRecords.Count,vm.EarthworkRecords.Count==0);
                        Check("production_selector_hides_fixture",0,control.FixtureProfileNames.Length,control.FixtureProfileNames.Length==0); var seed=RevitEarthworkRecords.Load(ui.ActiveUIDocument.Document).Profiles.First();
                        vm.SaveEarthworkProfile(EarthworkProfiles.Clone(seed) with{ProfileKind=EarthworkProfileKind.Production,ProfileName="一般土方",Currency="TWD",TruckName="一般車型"},true);break;
                    case 26:
                        Check("production_profile_visible",1,vm.EarthworkProfiles.Count,vm.EarthworkProfiles.Count==1&&vm.EarthworkProfiles[0].ProfileName=="一般土方");
                        Check("production_ui_no_fixture_data",true,vm.EstimateSummary,!vm.EstimateSummary.Contains("Fixture truck")&&!vm.EstimateSummary.Contains("TEST")&&vm.EarthworkRecords.Count==0);
                        Check("production_selector_actual_items","一般土方",string.Join(",",control.FixtureProfileNames),control.FixtureProfileNames.SequenceEqual(new[]{"一般土方"})); vm.EarthworkTestMode=true;vm.RefreshContext();break;
                    case 27:
                        vm.SelectEarthworkZone(vm.EarthworkRecords.Single(r=>r.ZoneGuid==zoneA),false);
                        var old=vm.EarthworkProfiles.Single(p=>p.ProfileGuid==vm.CurrentEarthwork!.Profile.ProfileGuid);
                        vm.SaveEarthworkProfile(old with{ExcavationUnitCost=old.ExcavationUnitCost+1},true);break;
                    case 28:
                        var historical=vm.EarthworkRecords.Single(r=>r.ZoneGuid==zoneA);
                        Check("profile_version_2",2,vm.EarthworkProfiles.Max(p=>p.ProfileVersion),vm.EarthworkProfiles.Max(p=>p.ProfileVersion)==2);
                        Check("historical_snapshot_v1",1,historical.ProfileSnapshot.ProfileVersion,historical.ProfileSnapshot.ProfileVersion==1&&historical.Profile.ExcavationUnitCost==2);
                        Check("historical_stale_without_recalculation",true,historical.Status,historical.CostProfileOutdated&&historical.CalculationStatus==CalculationStatus.Stale);
                        Near("historical_cost_unchanged",162,(double)historical.Cost.TotalEstimatedCost);
                        vm.PreviewEarthworkSchedule();break;
                    case 29: vm.ConfirmEarthworkSchedule(true);break;
                    case 30:
                        RevitEarthworkRecords.ReadBack(ui.ActiveUIDocument.Document,vm.EarthworkRecords.ToArray());
                        Check("schedule_preserves_snapshot_v1",1,vm.EarthworkRecords.First(r=>r.ZoneGuid==zoneA).ProfileSnapshot.ProfileVersion,vm.EarthworkRecords.First(r=>r.ZoneGuid==zoneA).ProfileSnapshot.ProfileVersion==1);
                        vm.MarkEarthworkReviewed(zoneA,true);break;
                    case 31:
                        Check("stale_review_blocked",ReviewStatus.PendingReview,vm.EarthworkRecords.First(r=>r.ZoneGuid==zoneA).ReviewStatus,vm.EarthworkRecords.First(r=>r.ZoneGuid==zoneA).ReviewStatus==ReviewStatus.PendingReview);
                        vm.RecalculateLatestProfile();break;
                    case 32:
                        Require(vm.CurrentEarthwork!=null,"Latest profile calculation failed: "+vm.Status);
                        Check("recalculation_new_snapshot",2,vm.CurrentEarthwork.ProfileSnapshot.ProfileVersion,vm.CurrentEarthwork.ProfileSnapshot.ProfileVersion==2&&!vm.CurrentEarthwork.CostProfileOutdated);
                        Near("recalculation_geometry_unchanged",12.5,vm.CurrentEarthwork.Quantity.CutBankVolume);vm.SaveEarthworkZone(true);break;
                    case 33: vm.MarkEarthworkReviewed(zoneA,true);break;
                    case 34:
                        Check("review_without_recalculation",ReviewStatus.Reviewed,vm.EarthworkRecords.First(r=>r.ZoneGuid==zoneA).ReviewStatus,vm.EarthworkRecords.First(r=>r.ZoneGuid==zoneA).ReviewStatus==ReviewStatus.Reviewed);
                        using(var tx=new Transaction(ui.ActiveUIDocument.Document,"Disposable foreign summary")){tx.Start();var foreign=ViewSchedule.CreateSchedule(ui.ActiveUIDocument.Document,new ElementId(BuiltInCategory.OST_GenericModel));foreign.Name="土方工程摘要 (BIM)";tx.Commit();}
                        vm.PreviewEarthworkSchedule(EarthworkScheduleKind.Summary);break;
                    case 35:
                        Require(vm.SchedulePreview!=null,"Summary preview missing");Check("summary_foreign_name_protected","土方工程摘要 (BIM) 1",vm.SchedulePreview.ScheduleName,vm.SchedulePreview.ScheduleName=="土方工程摘要 (BIM) 1");vm.ConfirmEarthworkSchedule(true);break;
                    case 36:
                        var currentRows=vm.EarthworkRecords.ToArray();var summaryRead=RevitEarthworkRecords.ReadBack(ui.ActiveUIDocument.Document,currentRows,EarthworkScheduleKind.Summary);
                        var detailRead=RevitEarthworkRecords.ReadBack(ui.ActiveUIDocument.Document,currentRows,EarthworkScheduleKind.Detail);
                        Check("summary_fields_and_two_rows",15,summaryRead.FieldCount,summaryRead.FieldCount==15&&summaryRead.RecordIds.Count==2);
                        Check("summary_detail_distinct_owned_views",true,summaryRead.ScheduleId!=detailRead.ScheduleId,summaryRead.ScheduleId!=detailRead.ScheduleId);
                        Check("v2_update_same_record_ids",true,summaryRead.ReadBack,summaryRead.RecordIds.All(p=>originalRecordIds![p.Key]==p.Value));
                        var summaryView=(ViewSchedule)ui.ActiveUIDocument.Document.GetElement(new ElementId(summaryRead.ScheduleId));var body=summaryView.GetTableData().GetSectionData(SectionType.Body);var cells=new List<string>();
                        for(int row=body.FirstRowNumber;row<=body.LastRowNumber;row++)for(int column=body.FirstColumnNumber;column<=body.LastColumnNumber;column++)cells.Add(summaryView.GetCellText(SectionType.Body,row,column));
                        foreach(var record in currentRows){string amount=record.Cost.TotalEstimatedCost.ToString("F2",System.Globalization.CultureInfo.InvariantCulture);Check("schedule_formatted_cost_"+record.ZoneNumber,amount,string.Join(" | ",cells),cells.Any(cell=>cell.Replace(",","").Contains(amount)));}
                        Check("summary_grand_total",true,summaryView.Definition.ShowGrandTotal,summaryView.Definition.ShowGrandTotal);
                        var summaryPreview=RevitEarthworkRecords.Preview(ui.ActiveUIDocument.Document,currentRows,EarthworkScheduleKind.Summary);bool summaryRollback=false;
                        try{RevitEarthworkRecords.WriteSchedule(ui.ActiveUIDocument.Document,currentRows,summaryPreview,true,()=>throw new IOException("Injected summary failure"));}catch(IOException){summaryRollback=true;}
                        Check("summary_failure_rollback",true,summaryRollback,summaryRollback&&RevitEarthworkRecords.ReadBack(ui.ActiveUIDocument.Document,currentRows,EarthworkScheduleKind.Summary).RecordIds.Count==2);
                        Finish(ui);break;
                }
            }
            catch(Exception ex){Check("runtime_exception","none",ex.ToString(),false);Finish(ui);}
        }
        private sealed class FixtureOverlapWarning:IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach(var failure in accessor.GetFailureMessages())if(failure.GetSeverity()==FailureSeverity.Warning&&failure.GetFailureDefinitionId()==BuiltInFailures.OverlapFailures.ToposolidFloorOverlap)accessor.DeleteWarning(failure);
                return FailureProcessingResult.Continue;
            }
        }
        private void Finish(UIApplication ui)
        {
            finished=true;timer.Stop();
            var report=new{FixtureVersion="earthwork-native-2",Status=failed==0?"PASS":"FAIL",Passed=checks.Count-failed,Failed=failed,BuildSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))),Assertions=checks};
            string json=JsonConvert.SerializeObject(report,Formatting.Indented);File.WriteAllText(Path.Combine(root,"earthwork-workflow.json"),json);File.WriteAllText(Path.Combine(root,"earthwork-workflow.md"),"# Earthwork Native Workflow\n\n```json\n"+json+"\n```\n");
            if(ui.Application.Documents.Size==1&&ui.ActiveUIDocument?.Document.PathName==fixture&&fixture.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            {
                ui.ActiveUIDocument.Document.Save();var exit=RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);if(ui.CanPostCommand(exit))ui.PostCommand(exit);
            }
        }
    }
}
#endif
