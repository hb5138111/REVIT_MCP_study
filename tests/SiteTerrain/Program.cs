using RevitMCP.Core.Site;
using System.Diagnostics;
using System.Text.Json;
using System.Globalization;

var checks=new List<object>();int failed=0;
void Check(string name,object expected,object actual,bool pass){checks.Add(new{Name=name,Expected=expected,Actual=actual,Passed=pass});if(!pass){failed++;Console.WriteLine("FAIL "+name+" actual="+actual);}}
void Near(string name,double expected,double actual,double tolerance=1e-8)=>Check(name,expected,actual,Math.Abs(expected-actual)<=tolerance);
void Reject(string name,Action action){try{action();Check(name,"exception","none",false);}catch(ArgumentException){Check(name,"exception","ArgumentException",true);}catch(InvalidOperationException){Check(name,"exception","InvalidOperationException",true);}}
foreach(char delimiter in new[]{',',';','\t',' '})
foreach(bool header in new[]{true,false})
foreach(string unit in new[]{"m","mm","ft"})
{
    var data=TerrainPointParser.Parse((header?string.Join(delimiter,"id","x","y","z","code")+"\n":"")+string.Join(delimiter,"p1","1","2","3","edge"),new(delimiter,header,1,2,3,0,4,unit));
    double f=unit=="m"?1:unit=="mm"?.001:.3048;
    Near($"parser_{(int)delimiter}_{header}_{unit}",3*f,data.Points[0].Point.Z);
}
var qa=TerrainPointParser.Parse("x,y,z\n0,0,0\n0,0,0\n0,0,1\n1,,2\nx,2,3\n2,3,NaN\n1,0,0\n0,1,0\n500,500,500",new(',',true,0,1,2));
Check("qa_counts","9/4/3/2/1",$"{qa.Diagnostics.InputCount}/{qa.Diagnostics.ValidCount}/{qa.Diagnostics.RejectedCount}/{qa.Diagnostics.DuplicateCount}/{qa.Diagnostics.ConflictCount}",qa.Diagnostics.InputCount==9&&qa.Diagnostics.ValidCount==4&&qa.Diagnostics.RejectedCount==3&&qa.Diagnostics.DuplicateCount==2&&qa.Diagnostics.ConflictCount==1);
Check("outlier",true,qa.Diagnostics.Warnings.Any(w=>w.Code=="EXTREME_OUTLIER"),qa.Diagnostics.Warnings.Any(w=>w.Code=="EXTREME_OUTLIER"));
Check("isolated",true,qa.Diagnostics.Warnings.Any(w=>w.Code=="ISOLATED_POINT"),qa.Diagnostics.Warnings.Any(w=>w.Code=="ISOLATED_POINT"));
Reject("bad_mapping",()=>TerrainPointParser.Parse("1,2,3",new(',',false,0,0,2)));
var known=new SiteTransform(100,-30,7,Math.PI/6);
var points=new[]{new SitePoint(0,0,2),new SitePoint(10,0,5),new SitePoint(0,10,8),new SitePoint(10,10,9)};
for(int count=2;count<=4;count++)
{
    var solution=ControlPointAlignment.Solve(points.Take(count).Select(p=>new SiteControl(p,known.Apply(p))).ToArray());
    Near("alignment_x_"+count,100,solution.Transform.DeltaX);Near("alignment_y_"+count,-30,solution.Transform.DeltaY);Near("alignment_z_"+count,7,solution.Transform.DeltaZ);Near("rotation_"+count,Math.PI/6,solution.Transform.Rotation);Near("residual_"+count,0,solution.MaxResidual);
}
Near("single_translation",5,ControlPointAlignment.Solve(new[]{new SiteControl(new(1,2,3),new(6,7,8))}).Transform.DeltaX);
Near("round_trip",0,points[1].Distance(known.Inverse(known.Apply(points[1]))));
Reject("degenerate",()=>ControlPointAlignment.Solve(new[]{new SiteControl(points[0],points[1]),new SiteControl(points[0],points[1])}));
var noisy=ControlPointAlignment.Solve(new[]{new SiteControl(new(0,0,0),new(0,0,0)),new SiteControl(new(10,0,0),new(11,0,0)),new SiteControl(new(0,10,0),new(0,10,0))});
Check("scale_fixed_residual",true,noisy.MaxResidual,noisy.Transform.Scale==1&&noisy.MaxResidual>.1);
TerrainTriangle[] Plane(double z0,double z1)=>new[]{new TerrainTriangle(new(0,0,z0),new(10,0,z1),new(10,10,z1)),new TerrainTriangle(new(0,0,z0),new(10,10,z1),new(0,10,z0))};
SitePoint[] boundary={new(0,0,0),new(5,0,0),new(5,5,0),new(0,5,0)};
var flat=EarthworkEngine.Calculate(Plane(2,2),boundary,0,1e-8);Near("flat_cut",50,flat.CutVolume);Near("flat_area",25,flat.Area);Near("flat_net",-50,flat.NetVolume);
var slope=EarthworkEngine.Calculate(Plane(0,10),boundary,0,1e-8);Near("slope_cut",62.5,slope.CutVolume);Near("average_depth",2.5,slope.AverageCutDepth);
var mixed=EarthworkEngine.Calculate(Plane(-2.5,7.5),boundary,0,1e-8);Near("mixed_cut",15.625,mixed.CutVolume);Near("mixed_fill",15.625,mixed.FillVolume);Near("mixed_net",0,mixed.NetVolume);
Near("winding",62.5,EarthworkEngine.Calculate(Plane(0,10),boundary.Reverse().ToArray(),0,1e-8).CutVolume);
Reject("missing_coverage",()=>EarthworkEngine.Calculate(Plane(0,10).Take(1),boundary,0,1e-8));
Reject("coverage_independent_of_control_tolerance",()=>EarthworkEngine.Calculate(Plane(0,10).Take(1),boundary,0,100));
Reject("concave_boundary",()=>EarthworkEngine.Calculate(Plane(0,10),new SitePoint[]{new(0,0,0),new(5,0,0),new(2,2,0),new(5,5,0),new(0,5,0)},0,1e-8));
var benches=new List<object>();
foreach(int count in new[]{1000,10000,20000,50000,100000})
{
    var text=string.Join('\n',Enumerable.Range(0,count).Select(i=>$"{i%500},{i/500},{(i%500*.001).ToString(CultureInfo.InvariantCulture)}"));
    var clock=Stopwatch.StartNew();var parsed=TerrainPointParser.Parse(text,new(',',false,0,1,2));long parse=clock.ElapsedMilliseconds;
    clock.Restart();var reduced=TerrainSimplifier.Reduce(parsed.Points,10,.02);long simplify=clock.ElapsedMilliseconds;
    Check("benchmark_count_"+count,count,parsed.Points.Count,parsed.Points.Count==count);
    Check("reduction_bound_"+count,"<=.02",reduced.MaxVerticalError,reduced.MaxVerticalError<=.02);
    Check("boundary_"+count,true,reduced.FinalPointCount,reduced.Points.Any(p=>p.Point.X==0)&&reduced.Points.Any(p=>p.Point.X==499));
    benches.Add(new{InputCount=count,ParserQAms=parse,SimplificationMs=simplify,FinalCount=reduced.FinalPointCount});
}
var output=args.Length>0?args[0]:"test-artifacts/v05";Directory.CreateDirectory(output);
var host=new TestHost();var vm=new RevitMCP.UI.SiteTerrainViewModel(host);
string input=Path.GetFullPath(Path.Combine(output,"workflow-points.csv"));File.WriteAllText(input,"x,y,z\n0,0,2\n10,0,2\n10,10,2\n0,10,2");
vm.FilePath=input;vm.Units="m";await vm.ImportAsync();Check("ui_import",4,vm.Dataset?.Points.Count,vm.Dataset?.Points.Count==4);
vm.RefreshContext();Check("ui_context",true,vm.Context!=null,vm.Context!=null);
await vm.PreviewAsync();Check("ui_preview",true,vm.CanCreate,vm.CanCreate);
vm.Create();Check("ui_unconfirmed_block",0,host.Context.Writes,host.Context.Writes==0);
vm.Confirmed=true;vm.ToleranceMetres=.02;Check("ui_setting_invalidates",false,vm.CanExecuteCreate,!vm.CanExecuteCreate);
await vm.PreviewAsync();vm.Confirmed=true;vm.Create();Check("ui_create_readback",1,host.Context.Writes,host.Context.Writes==1&&vm.TerrainId==123);
vm.CutterId=456;vm.PreviewExcavation();Check("ui_excavation_preview",50,vm.ExcavationPreview,vm.ExcavationPreview==50);
vm.ExecuteExcavation();Check("ui_excavation_unconfirmed",1,host.Context.Writes,host.Context.Writes==1);
vm.Confirmed=true;vm.ExecuteExcavation();Check("ui_excavation_confirmed",2,host.Context.Writes,host.Context.Writes==2);
vm.CoordinateMode="ControlPointAlignment";vm.ControlPoints="0,0,0,0,0,0;10,0,0,11,0,0;0,10,0,0,10,0";await vm.PreviewAsync();Check("ui_residual_blocks_create",false,vm.CanCreate,!vm.CanCreate&&vm.PreviewPoints.Count==4);
vm.DocumentChanged("different");Check("ui_document_invalidates",true,vm.Dataset==null&&vm.Context==null,vm.Dataset==null&&vm.Context==null&&!vm.CanExcavate);
vm.FilePath=input;await vm.ImportAsync();vm.RefreshContext();vm.CoordinateMode="LocalCoordinates";await vm.PreviewAsync();vm.Confirmed=true;vm.DocumentChanged("fixture",true);Check("ui_model_change_invalidates",false,vm.CanExecuteCreate,!vm.CanExecuteCreate);
vm.Units="mm";Check("ui_units_require_reimport",true,vm.Dataset==null,vm.Dataset==null);
vm.Units="m";await vm.ImportAsync();vm.RefreshContext();await vm.PreviewAsync();vm.Confirmed=true;host.Delay=true;vm.Create();vm.ToleranceMetres=.03;host.Flush();Check("queued_setting_invalidates",2,host.Context.Writes,host.Context.Writes==2);host.Delay=false;
vm.UseSelection(true);vm.UseSelection(false);Check("ui_revit_selection",true,new{vm.TerrainId,vm.CutterId},vm.TerrainId==123&&vm.CutterId==456);
// v0.5.1 source-independent CAD data and interactive workflow regression.
var cadData=new CadTerrainAnalysis{FileName="fixture.dxf",SHA256=new string('A',64),RollbackVerified=true,Geometry=new CadPrimitive[]{
 new("SURVEY","Point",new SitePoint[]{new(0,0,2),new(10,0,2),new(10,10,4),new(0,10,4),new(5,5,3)}),
 new("BOUNDARY","PolyLine",new SitePoint[]{new(0,0,0),new(10,0,0),new(10,10,0),new(0,10,0),new(0,0,0)},true),
 new("ZERO_Z","Line",new SitePoint[]{new(20,0,0),new(25,0,0)}),new("NOTES","需複核：Text",Array.Empty<SitePoint>())}};
Check("cad_layer_discovery",4,cadData.Layers.Count,cadData.Layers.Count==4);
Check("cad_boundary_candidate",1,cadData.Boundaries.Count,cadData.Boundaries.Count==1);
var selectedData=cadData.Dataset(new[]{"SURVEY"});
Check("cad_layer_filter",5,selectedData.Points.Count,selectedData.Points.Count==5);
Check("cad_provenance",TerrainSourceKind.CadFile,selectedData.SourceKind,selectedData.SourceKind==TerrainSourceKind.CadFile&&selectedData.Provenance!=null);
Check("cad_zero_z_warning",true,cadData.Dataset(new[]{"ZERO_Z"}).Diagnostics.Warnings.Count,cadData.Dataset(new[]{"ZERO_Z"}).Diagnostics.Warnings.Any(w=>w.Code=="CAD_ZERO_Z_REVIEW_REQUIRED"));
Check("cad_unsupported_review",true,cadData.Dataset(new[]{"NOTES"}).Diagnostics.Warnings.Count,cadData.Dataset(new[]{"NOTES"}).Diagnostics.Warnings.Any(w=>w.Code=="CAD_REVIEW_REQUIRED"));
var boundaryProvenance=System.Text.Json.JsonSerializer.SerializeToElement(selectedData.Provenance);
Check("cad_no_auto_boundary",System.Text.Json.JsonValueKind.Null,boundaryProvenance.GetProperty("Boundary").ValueKind,boundaryProvenance.GetProperty("Boundary").ValueKind==System.Text.Json.JsonValueKind.Null);
Check("boundary_reject_concave",false,CadTerrainAnalysis.ValidBoundary(new SitePoint[]{new(0,0,0),new(10,0,0),new(5,5,0),new(10,10,0),new(0,10,0)}),!CadTerrainAnalysis.ValidBoundary(new SitePoint[]{new(0,0,0),new(10,0,0),new(5,5,0),new(10,10,0),new(0,10,0)}));
Check("boundary_reject_nonplanar",false,CadTerrainAnalysis.ValidBoundary(new SitePoint[]{new(0,0,0),new(10,0,1),new(0,10,0)}),!CadTerrainAnalysis.ValidBoundary(new SitePoint[]{new(0,0,0),new(10,0,1),new(0,10,0)}));
Check("boundary_reject_crossing",false,CadTerrainAnalysis.ValidBoundary(new SitePoint[]{new(0,0,0),new(10,10,0),new(0,10,0),new(10,0,0)}),!CadTerrainAnalysis.ValidBoundary(new SitePoint[]{new(0,0,0),new(10,10,0),new(0,10,0),new(10,0,0)}));
var workflow=new RevitMCP.UI.SiteTerrainViewModel(new TestHost());workflow.FilePath=input;workflow.ReadColumns();
Check("visual_mapping_columns",3,workflow.Columns.Count,workflow.Columns.Count==3);
Check("visual_mapping_sample",4,workflow.SampleRows.Count,workflow.SampleRows.Count==4);
workflow.SetColumn(2,1);Check("mapping_selection",1,workflow.GetColumn(2),workflow.GetColumn(2)==1);workflow.SetColumn(2,2);
await workflow.ImportAsync();workflow.RefreshContext();await workflow.PreviewAsync();
Check("default_quality_balanced","平衡",workflow.ReductionMode,workflow.ReductionMode=="平衡");
workflow.GoToStep(2);Check("step_next",2,workflow.Step,workflow.Step==2);workflow.GoToStep(1);Check("step_back",1,workflow.Step,workflow.Step==1);
Check("step_complete_source",RevitMCP.UI.SiteStepState.Complete,workflow.StepState(0),workflow.StepState(0)==RevitMCP.UI.SiteStepState.Complete);
workflow.CutterId=123;Check("cutter_preserves_terrain_preview",4,workflow.PreviewPoints.Count,workflow.PreviewPoints.Count==4);
workflow.ControlRows.Add(new(){Easting=1,Northing=2,Elevation=3,ModelX=4,ModelY=5,ModelZ=6});workflow.ApplyControlRows();
Check("control_grid_invalidates",0,workflow.PreviewPoints.Count,workflow.PreviewPoints.Count==0);
Check("control_grid_serialization",1,RevitMCP.UI.SiteTerrainViewModel.ParseControls(workflow.ControlPoints).Count,RevitMCP.UI.SiteTerrainViewModel.ParseControls(workflow.ControlPoints)[0].Internal==new SitePoint(4,5,6));
workflow.FilePath="fixture.dxf";Check("source_switch_cad",true,workflow.IsCad,workflow.IsCad&&workflow.Dataset==null&&!workflow.Confirmed);
workflow.GoToStep(9);Check("step_bounds",1,workflow.Step,workflow.Step==1);
var cadHost=new TestHost();cadHost.Context.CadData=cadData;cadHost.Context.Shared=new(100,200,300,.3);
var cadVm=new RevitMCP.UI.SiteTerrainViewModel(cadHost);cadVm.RefreshContext();cadVm.FilePath="fixture.dxf";cadVm.AnalyzeCad();
Check("cad_ui_layer_table",4,cadVm.CadLayers.Count,cadVm.CadLayers.Count==4);
cadVm.CadLayers.Single(l=>l.Name=="SURVEY").Selected=true;cadVm.ApplyCadLayers();await cadVm.PreviewAsync();
Check("cad_no_double_transform",10,cadVm.PreviewPoints.Max(p=>p.X),cadVm.PreviewPoints.Max(p=>p.X)==10&&cadVm.Dataset?.CoordinateBasis==TerrainCoordinateBasis.PositionedModelMetres);
cadVm.Confirmed=true;cadVm.CadLayers.Single(l=>l.Name=="ZERO_Z").Selected=true;
Check("cad_layer_invalidates",true,cadVm.Dataset==null,cadVm.Dataset==null&&!cadVm.Confirmed&&cadVm.PreviewPoints.Count==0);
cadVm.ApplyCadLayers();cadVm.Units="mm";Check("cad_units_require_reanalysis",true,cadVm.CadAnalysis==null,cadVm.CadAnalysis==null&&cadVm.Dataset==null);
var budget=new CadTerrainBudget();budget.Vertices(CadTerrainBudget.MaxVertices);bool budgetRejected=false;
try{budget.Vertices(1);}catch(InvalidOperationException e){budgetRejected=e.Message==CadTerrainBudget.Message;}
Check("cad_vertex_budget",true,budgetRejected,budgetRejected);
budget=new CadTerrainBudget();for(int i=0;i<CadTerrainBudget.MaxGeometry;i++)budget.Geometry(0);budgetRejected=false;
try{budget.Geometry(0);}catch(InvalidOperationException){budgetRejected=true;}Check("cad_geometry_budget",true,budgetRejected,budgetRejected);
budget=new CadTerrainBudget();budgetRejected=false;try{budget.Geometry(CadTerrainBudget.MaxDepth+1);}catch(InvalidOperationException){budgetRejected=true;}Check("cad_depth_budget",true,budgetRejected,budgetRejected);
Check("boundary_vertex_budget",false,CadTerrainAnalysis.ValidBoundary(Enumerable.Range(0,2001).Select(i=>new SitePoint(Math.Cos(i),Math.Sin(i),0)).ToArray()),!CadTerrainAnalysis.ValidBoundary(Enumerable.Range(0,2001).Select(i=>new SitePoint(Math.Cos(i),Math.Sin(i),0)).ToArray()));

File.WriteAllText(Path.Combine(output,"terrain-logic.json"),JsonSerializer.Serialize(new{Status=failed==0?"PASS":"FAIL",Passed=checks.Count-failed,Failed=failed,Assertions=checks,Benchmarks=benches},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Terrain logic: {checks.Count-failed} PASS / {failed} FAIL");return failed==0?0:1;

sealed class TestHost:RevitMCP.UI.ISiteHost
{
    public TestContext Context=new();
    public bool Delay;private Action? pending;
    public void Flush(){pending?.Invoke();pending=null;}
    public bool Submit(string expected,Action<RevitMCP.UI.ISiteContext> work,Action<string> failed){void Run(){try{if(expected!=""&&expected!="fixture")throw new InvalidOperationException("Stale document");work(Context);}catch(Exception e){failed(e.Message);}}if(Delay)pending=Run;else Run();return true;}
}
sealed class TestContext:RevitMCP.UI.ISiteContext
{
    public int Writes;
    public CadTerrainAnalysis? CadData;
    public SiteTransform Shared=new(0,0,0,0);
    public CadTerrainAnalysis AnalyzeCad(CadTerrainRequest request)=>CadData??throw new InvalidOperationException("Missing test CAD data");
    public RevitMCP.UI.SiteChoice SelectedElement(bool terrain)=>new(terrain?123:456,"selected host element");
    public RevitMCP.UI.SiteContextSnapshot Snapshot()=>new("fixture",Shared,"Fixture",new[]{new RevitMCP.UI.SiteChoice(1,"type")},new[]{new RevitMCP.UI.SiteChoice(2,"level")});
    public RevitMCP.UI.SiteCreateOutcome Create(RevitMCP.UI.SiteCreateRequest request,bool confirmed){if(!confirmed)throw new Exception("Missing confirmation");Writes++;return new(123,"read-back");}
    public double Excavate(long t,long c,bool execute,bool confirmed,double? expected,object audit){if(execute){if(!confirmed)throw new Exception("Missing confirmation");Writes++;}return 50;}
    public object Calculate(long t,IReadOnlyList<SitePoint>b,double e,double tol,object audit)=>new{Cut=50};
}
