#if REVIT2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using RevitMCP.UI;

namespace RevitMCP.Core.Site
{
    internal static class CadTerrainSelfTest
    {
        public static void Run(Autodesk.Revit.ApplicationServices.Application app,string root)
        {
            if(app.Documents.Size!=0||!Path.GetFileName(root).StartsWith("revit-selftest-")||!File.Exists(Path.Combine(root,"request.json")))throw new InvalidOperationException("Isolated fixture session required.");
            var assertions=new List<object>();int failed=0;Document? doc=null;
            void Check(string name,object expected,object actual,bool pass){assertions.Add(new{TestName=name,Expected=expected,Actual=actual,Passed=pass});if(!pass)failed++;}
            void Near(string name,double expected,double actual,double tolerance=1e-5)=>Check(name,expected,actual,Math.Abs(expected-actual)<=tolerance);
            try
            {
                var request=JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"request.json")))!;
                string fixtures=Path.Combine(Directory.GetParent(root)!.Parent!.FullName,"tests","fixtures","cad-terrain");
                doc=app.NewProjectDocument(request["BaseProjectTemplate"]);
                var type=new FilteredElementCollector(doc).OfClass(typeof(ToposolidType)).Cast<ToposolidType>().First();
                var level=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l=>l.Elevation).First();
                foreach(string name in new[]{"simple-points.dxf","contours-3d.dxf"})
                {
                    var analysis=CadTerrainService.Analyze(doc,new(Path.Combine(fixtures,name),CadPlacement.Origin,"m"));
                    File.WriteAllText(Path.Combine(root,name+".analysis.json"),JsonConvert.SerializeObject(analysis,Formatting.Indented));
                    Check(name+"_rollback",true,analysis.RollbackVerified,analysis.RollbackVerified);
                    Check(name+"_layers",new[]{"SURVEY","BOUNDARY","ZERO_Z"},analysis.Layers.Select(l=>l.Name),new[]{"SURVEY","BOUNDARY","ZERO_Z"}.All(n=>analysis.Layers.Any(l=>l.Name==n)));
                    var data=analysis.Dataset(new[]{"SURVEY"});
                    Check(name+"_point_count",5,data.Points.Count,data.Points.Count==5);
                    Near(name+"_max_x",10,data.Diagnostics.Bounds.Max.X);Near(name+"_min_z",2,data.Diagnostics.Bounds.Min.Z);Near(name+"_max_z",4,data.Diagnostics.Bounds.Max.Z);
                    var block=analysis.Dataset(new[]{"BLOCK_POINTS"});Near(name+"_block_transform",0,block.Points.Single().Point.Distance(new(27,42,9)));
                    Check(name+"_provenance",true,data.SourceKind==TerrainSourceKind.CadFile,data.SourceKind==TerrainSourceKind.CadFile&&data.SourceSHA256.Length==64);
                    Check(name+"_boundary",1,analysis.Boundaries.Count,analysis.Boundaries.Count==1);
                    var zero=analysis.Dataset(new[]{"ZERO_Z"});Check(name+"_zero_z_review",true,zero.Diagnostics.Warnings,zero.Diagnostics.Warnings.Any(w=>w.Code=="CAD_ZERO_Z_REVIEW_REQUIRED"));
                    if(name=="contours-3d.dxf")
                    {
                        var contours=analysis.Dataset(new[]{"CONTOURS"});Check("polyline_vertices",6,contours.Points.Count,contours.Points.Count==6);
                        var context=new RevitSiteHost.Context(doc,Path.Combine(root,"cad-reports"));var vm=new SiteTerrainViewModel(new FixtureHost(context));
                        vm.RefreshContext();vm.FilePath=Path.Combine(fixtures,name);vm.AnalyzeCad();foreach(var layerChoice in vm.CadLayers.Where(l=>l.Name=="SURVEY"))layerChoice.Selected=true;vm.ApplyCadLayers();
                        Task.Run(async()=>await vm.PreviewAsync()).GetAwaiter().GetResult();vm.TypeId=type.Id.Value;vm.LevelId=level.Id.Value;
                        Check("cad_ui_preview",5,vm.PreviewPoints.Count,vm.PreviewPoints.Count==5);
                        var control=new SiteTerrainControl(vm);Check("cad_four_step_wpf",true,control.Content!=null,control.Content!=null);
                        for(int step=0;step<4;step++)
                        {
                            vm.GoToStep(step);control.Width=720;control.Height=1050;control.Measure(new System.Windows.Size(720,1050));control.Arrange(new System.Windows.Rect(0,0,720,1050));control.UpdateLayout();
                            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(720,1050,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(control);
                            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(root,$"site-step-{step+1}.png")))encoder.Save(stream);
                            Check("native_step_"+step,step,vm.Step,vm.Step==step&&control.ActualWidth==720);
                        }
                        vm.ConfirmCreate();Check("cad_ui_create_readback",true,vm.Status,doc.GetElement(new ElementId(vm.TerrainId)) is Toposolid);
                    }
                }
                var millimetres=CadTerrainService.Analyze(doc,new(Path.Combine(fixtures,"simple-points.dxf"),CadPlacement.Origin,"mm")).Dataset(new[]{"SURVEY"});Near("cad_mm_unit",.01,millimetres.Diagnostics.Bounds.Max.X);
                using(var tx=new Transaction(doc,"Fixture shared coordinates"))
                {tx.Start();var p=doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);p.EastWest=100/.3048;p.NorthSouth=-30/.3048;p.Elevation=7/.3048;p.Angle=Math.PI/6;doc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero,p);tx.Commit();}
                var snapshot=CoordinateTransformService.Read(doc);
                var originRotated=CadTerrainService.Analyze(doc,new(Path.Combine(fixtures,"simple-points.dxf"),CadPlacement.Origin,"m")).Dataset(new[]{"SURVEY"});
                Near("origin_with_true_north_x",10,originRotated.Diagnostics.Bounds.Max.X);Near("origin_with_true_north_z",2,originRotated.Diagnostics.Bounds.Min.Z);
                try
                {
                var shared=CadTerrainService.Analyze(doc,new(Path.Combine(fixtures,"simple-points.dxf"),CadPlacement.Shared,"m"));
                var sharedPoints=shared.Dataset(new[]{"SURVEY"}).Points;
                var sharedZero=shared.Dataset(new[]{"ZERO_Z"});Check("shared_zero_source_elevation_review",true,sharedZero.Diagnostics.Warnings,sharedZero.Diagnostics.Warnings.Any(w=>w.Code=="CAD_ZERO_Z_REVIEW_REQUIRED"));
                foreach(var survey in new SitePoint[]{new(0,0,2),new(10,0,2),new(10,10,4),new(0,10,4),new(5,5,3)})
                {
                    var expected=snapshot.SurveyToInternal.Apply(survey);Near("shared_true_north_"+survey,0,sharedPoints.Min(p=>p.Point.Distance(expected)));
                    Near("shared_round_trip_"+survey,0,snapshot.SurveyToInternal.Inverse(expected).Distance(survey));
                }
                }
                catch(Exception e){Check("shared_coordinate_placement","verified placement",e.ToString(),false);}
                // Produce a tiny DWG using Revit's own exporter; no proprietary SDK or production CAD.
                string dwg=Path.Combine(root,"cad-fixture.dwg");var view=new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v=>!v.IsTemplate);
                using(var group=new TransactionGroup(doc,"Disposable DWG fixture"))
                {
                    group.Start();using(var tx=new Transaction(doc,"Import fixture for DWG export")){tx.Start();using var options=new DWGImportOptions{Unit=ImportUnit.Meter,Placement=ImportPlacement.Origin,ThisViewOnly=false};if(!doc.Import(Path.Combine(fixtures,"contours-3d.dxf"),options,view,out var id))throw new InvalidOperationException("DWG fixture source import failed.");tx.Commit();}
                    using var export=new DWGExportOptions{MergedViews=true,FileVersion=ACADVersion.R2018,TargetUnit=ExportUnit.Meter};
                    bool ok=doc.Export(root,"cad-fixture",new List<ElementId>{view.Id},export);Check("dwg_fixture_export",true,ok,ok&&File.Exists(dwg));group.RollBack();
                }
                var dwgAnalysis=CadTerrainService.Analyze(doc,new(dwg,CadPlacement.Origin,"m"));
                File.WriteAllText(Path.Combine(root,"dwg-analysis.json"),JsonConvert.SerializeObject(dwgAnalysis,Formatting.Indented));
                Check("dwg_load_and_rollback",true,dwgAnalysis.Layers.Count,dwgAnalysis.RollbackVerified&&dwgAnalysis.Geometry.Any(g=>g.Points.Count>0));
                var dwgData=dwgAnalysis.Dataset(dwgAnalysis.Layers.Where(l=>l.ValidPointCount>0).Select(l=>l.Name));
                Check("dwg_dataset",true,dwgData.Points.Count,dwgData.Points.Count>=3&&dwgData.SourceName.EndsWith(".dwg"));
                var dwgVm=new SiteTerrainViewModel(new FixtureHost(new RevitSiteHost.Context(doc,Path.Combine(root,"cad-reports"))));
                dwgVm.RefreshContext();dwgVm.FilePath=dwg;dwgVm.AnalyzeCad();foreach(var layerChoice in dwgVm.CadLayers.Where(l=>l.Summary.ValidPointCount>0))layerChoice.Selected=true;dwgVm.ApplyCadLayers();
                Task.Run(async()=>await dwgVm.PreviewAsync()).GetAwaiter().GetResult();Check("dwg_native_coordinate_preview",true,dwgVm.Status,dwgVm.PreviewPoints.Count>=3&&dwgVm.Transform==new SiteTransform(0,0,0,0));
                bool rejected=false;int before=new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).GetElementCount();
                try{CadTerrainService.Analyze(doc,new(Path.Combine(root,"not-cad.rvt"),CadPlacement.Origin,"m"));}catch(ArgumentException){rejected=true;}
                Check("extension_reject_no_import",true,rejected,rejected&&new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).GetElementCount()==before);
            }
            catch(Exception e){Check("cad_runtime_exception","none",e.ToString(),false);}
            finally
            {
                if(doc!=null&&doc.IsValidObject)doc.Close(false);
                string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)));
                var report=new{FixtureVersion="cad-terrain-1",Status=failed==0?"PASS":"FAIL",BuildSHA256=hash,Passed=assertions.Count-failed,Failed=failed,Assertions=assertions};string json=JsonConvert.SerializeObject(report,Formatting.Indented);
                File.WriteAllText(Path.Combine(root,"cad-runtime.json"),json);File.WriteAllText(Path.Combine(root,"cad-runtime.md"),"# CAD Terrain Runtime\n\n```json\n"+json+"\n```\n");
            }
        }
        private sealed class FixtureHost : ISiteHost
        {
            private readonly ISiteContext context;public FixtureHost(ISiteContext context){this.context=context;}
            public bool Submit(string expected,Action<ISiteContext> work,Action<string> failed){try{work(context);}catch(Exception e){failed(e.Message);}return true;}
        }
    }
}
#endif
