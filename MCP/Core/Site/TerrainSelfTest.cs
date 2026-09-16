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
    /// <summary>Known mathematical fixtures in a new document only; closes without saving.</summary>
    internal static class TerrainSelfTest
    {
        public static void Run(Autodesk.Revit.ApplicationServices.Application app,string root)
        {
            if(app.Documents.Size!=0||!Path.GetFileName(root).StartsWith("revit-selftest-")||!File.Exists(Path.Combine(root,"request.json")))throw new InvalidOperationException("Terrain fixture requires isolated launcher session.");
            var assertions=new List<object>();int failed=0;Document? doc=null;
            void Check(string name,object expected,object actual,bool pass){assertions.Add(new{TestName=name,Expected=expected,Actual=actual,Passed=pass});if(!pass)failed++;}
            void Near(string name,double expected,double actual,double tolerance=1e-6)=>Check(name,expected,actual,Math.Abs(expected-actual)<=tolerance);
            try
            {
                var request=JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"request.json")))!;
                doc=app.NewProjectDocument(request["BaseProjectTemplate"]);
                if(doc.IsWorkshared||doc.IsLinked)throw new InvalidOperationException("Fixture not standalone.");
                Level level;ToposolidType type;FloorType floorType;
                using(var tx=new Transaction(doc,"Terrain fixture setup"))
                {
                    tx.Start();level=Level.Create(doc,0);
                    type=(ToposolidType)new FilteredElementCollector(doc).OfClass(typeof(ToposolidType)).Cast<ToposolidType>().First().Duplicate("SiteFixture10m");
                    var structure=type.GetCompoundStructure();structure.SetLayers(new[]{new CompoundStructureLayer(10/.3048,MaterialFunctionAssignment.Structure,ElementId.InvalidElementId)});type.SetCompoundStructure(structure);
                    floorType=(FloorType)new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First(f=>!f.IsFoundationSlab).Duplicate("SiteFixtureCutter");
                    var floorStructure=floorType.GetCompoundStructure();floorStructure.SetLayers(new[]{new CompoundStructureLayer(.2/.3048,MaterialFunctionAssignment.Structure,ElementId.InvalidElementId)});floorType.SetCompoundStructure(floorStructure);
                    var position=doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);position.EastWest=100/.3048;position.NorthSouth=-30/.3048;position.Elevation=7/.3048;position.Angle=Math.PI/6;doc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero,position);
                    tx.Commit();
                }
                var coordinates=CoordinateTransformService.Read(doc);
                var reported=CoordinateTransformService.InternalToSurvey(coordinates,new(0,0,0));
                Near("shared_direction_easting",100,reported.X);Near("shared_direction_northing",-30,reported.Y);Near("shared_vertical_datum",7,reported.Z);
                foreach(var p in new[]{new SitePoint(17,3,5),new SitePoint(-4,21,-2)})
                {
                    var expected=doc.ActiveProjectLocation.GetProjectPosition(CoordinateTransformService.Feet(p));var survey=CoordinateTransformService.InternalToSurvey(coordinates,p);
                    Near("independent_direction_x_"+p.X,expected.EastWest*.3048,survey.X);Near("independent_direction_y_"+p.X,expected.NorthSouth*.3048,survey.Y);
                    Near("round_trip_"+p.X,0,CoordinateTransformService.SurveyToInternal(coordinates,survey).Distance(p));
                }
                var points=new SitePoint[]{new(0,0,2),new(10,0,2),new(10,10,2),new(0,10,2),new(5,5,2)};
                bool rejected=false;
                try{RevitTerrainService.Create(doc,points,type.Id.GetIdValue(),level.Id.GetIdValue(),1e-5,false,false,_=>{});}catch(InvalidOperationException){rejected=true;}
                Check("unconfirmed_create_blocked",true,rejected,rejected);
                var context=new RevitSiteHost.Context(doc,Path.Combine(root,"site-reports"));
                var created=context.Create(new(points,type.Id.GetIdValue(),level.Id.GetIdValue(),1e-5,false,new{Fixture="terrain-1"}),true);
                var read=RevitTerrainService.Read(doc,created.ElementId);
                Near("toposolid_area",100,read.Area);Near("toposolid_volume",1000,read.Volume,1e-4);Near("surface_min_z",2,read.SurfaceBounds.Min.Z);Near("surface_max_x",10,read.SurfaceBounds.Max.X);
                Check("toposolid_type_level",true,new{read.TypeId,read.LevelId},read.TypeId==type.Id.GetIdValue()&&read.LevelId==level.Id.GetIdValue());
                var boundary=new SitePoint[]{new(0,0,0),new(5,0,0),new(5,5,0),new(0,5,0)};
                var quantity=EarthworkEngine.Calculate(RevitTerrainService.Surface(doc,created.ElementId),boundary,0,1e-7);Near("runtime_tin_cut",50,quantity.CutVolume);Near("runtime_tin_fill",0,quantity.FillVolume);
                Floor cutter;
                using(var tx=new Transaction(doc,"Disposable cutter"))
                {
                    tx.Start();var loop=new CurveLoop();for(int i=0;i<boundary.Length;i++)loop.Append(Line.CreateBound(CoordinateTransformService.Feet(boundary[i]),CoordinateTransformService.Feet(boundary[(i+1)%boundary.Length])));
                    cutter=Floor.Create(doc,new[]{loop},floorType.Id,level.Id);cutter.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(.2/.3048);tx.Commit();
                }
                double original=read.Volume;
                double preview=context.Excavate(created.ElementId,cutter.Id.GetIdValue(),false,false,null,new{Fixture="terrain-1"});
                Near("revit_excavation_expected",50,preview,1e-4);
                Near("preview_rollback_volume",original,RevitTerrainService.Read(doc,created.ElementId).Volume,1e-4);
                double actual=context.Excavate(created.ElementId,cutter.Id.GetIdValue(),true,true,preview,new{Fixture="terrain-1"});
                Near("excavation_readback",50,actual,1e-4);Near("custom_vs_revit",quantity.CutVolume,actual,1e-4);
                Near("post_excavation_volume",original-actual,RevitTerrainService.Read(doc,created.ElementId).Volume,1e-4);
                // Write failure must restore the complete model transaction, not leave an orphan terrain.
                int before=new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).GetElementCount();bool reportFailure=false;
                try{RevitTerrainService.Create(doc,points,type.Id.GetIdValue(),level.Id.GetIdValue(),1e-5,true,false,_=>throw new IOException("fixture report failure"));}catch(IOException){reportFailure=true;}
                Check("report_failure_rolls_back",before,new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).GetElementCount(),reportFailure&&before==new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).GetElementCount());
                Check("audit_formats",true,Directory.GetFiles(Path.Combine(root,"site-reports")).Length,new[]{"*.json","*.csv","*.md"}.All(pattern=>Directory.GetFiles(Path.Combine(root,"site-reports"),pattern).Length>=2));
                var slopePoints=new SitePoint[]{new(20,0,0),new(30,0,10),new(30,10,10),new(20,10,0)};
                var slope=context.Create(new(slopePoints,type.Id.GetIdValue(),level.Id.GetIdValue(),1e-5,false,new{Fixture="slope"}),true);
                var slopeBoundary=new SitePoint[]{new(20,0,0),new(25,0,0),new(25,5,0),new(20,5,0)};
                var mixed=EarthworkEngine.Calculate(RevitTerrainService.Surface(doc,slope.ElementId),slopeBoundary,2.5,1e-7);
                Near("runtime_mixed_cut",15.625,mixed.CutVolume,1e-4);Near("runtime_mixed_fill",15.625,mixed.FillVolume,1e-4);
                // Production ViewModel + production Revit context; raw import/preview remains off API thread.
                var vm=new SiteTerrainViewModel(new FixtureHost(context));
                string source=Path.Combine(root,"terrain-ui.csv");File.WriteAllText(source,"x,y,z\n40,0,2\n50,0,2\n50,10,2\n40,10,2");
                vm.FilePath=source;Task.Run(async()=>await vm.ImportAsync()).GetAwaiter().GetResult();vm.RefreshContext();vm.CoordinateMode="LocalCoordinates";vm.TypeId=type.Id.GetIdValue();vm.LevelId=level.Id.GetIdValue();
                Task.Run(async()=>await vm.PreviewAsync()).GetAwaiter().GetResult();
                Check("native_vm_can_create",true,vm.CanCreate,vm.CanCreate);
                var control=new SiteTerrainControl(vm);Check("native_wpf_constructed",true,control.Content!=null,control.Content!=null);
                vm.Confirmed=true;vm.Create();Check("native_vm_create_readback",true,vm.Status,vm.TerrainId>0 && doc.GetElement(new ElementId(vm.TerrainId)) is Toposolid);
                vm.Boundary="40,0;45,0;45,5;40,5";vm.TargetElevation=0;vm.CalculateBoundary();Check("native_vm_quantity",true,vm.Status,vm.Result?.ToString()?.Contains("50") == true);
                using(var tx=new Transaction(doc,"Fixture stale terrain")){tx.Start();doc.Delete(new ElementId(vm.TerrainId));tx.Commit();}
                vm.CalculateBoundary();Check("stale_terrain_rejected",true,vm.Status,vm.Status.Contains("已刪除"));
            }
            catch(Exception e){Check("runtime_exception","none",e.ToString(),false);}
            finally
            {
                if(doc!=null&&doc.IsValidObject)doc.Close(false);
                string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)));
                var report=new{FixtureVersion="terrain-1",Status=failed==0?"PASS":"FAIL",BuildSHA256=hash,Passed=assertions.Count-failed,Failed=failed,Assertions=assertions};
                File.WriteAllText(Path.Combine(root,"terrain-runtime.json"),JsonConvert.SerializeObject(report,Formatting.Indented));
                File.WriteAllText(Path.Combine(root,"terrain-runtime.md"),"# Terrain Runtime\n\n"+report.Status+"\n\n```json\n"+JsonConvert.SerializeObject(report,Formatting.Indented)+"\n```\n");
            }
        }
        private sealed class FixtureHost : ISiteHost
        {
            private readonly ISiteContext context;
            public FixtureHost(ISiteContext context){this.context=context;}
            public bool Submit(string expected,Action<ISiteContext> work,Action<string> failed)
            {try{if(expected!=""&&expected!=context.Snapshot().DocumentIdentity)throw new InvalidOperationException("Stale fixture document");work(context);}catch(Exception e){failed(e.Message);}return true;}
        }
    }
}
#endif
