#if REVIT2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace RevitMCP.Core.Site
{
    public sealed class CoordinateSnapshot
    {
        public SiteTransform SurveyToInternal {get;set;}=new(0,0,0,0);
        public long ActiveProjectLocationId {get;set;}
        public SitePoint InternalOrigin {get;set;}=new(0,0,0);
        public SitePoint ProjectBasePoint {get;set;}=new(0,0,0);
        public SitePoint SurveyPoint {get;set;}=new(0,0,0);
        public SitePoint ProjectPosition {get;set;}=new(0,0,0);
        public double TrueNorthRotation {get;set;}
        public string VerifiedDirection {get;set;}="";
        public string[] PlacementFindings => new[]{Math.Abs(SurveyToInternal.DeltaX)+Math.Abs(SurveyToInternal.DeltaY)>1e-7?"Offset Detected":"Aligned",Math.Abs(SurveyToInternal.Rotation)>1e-9?"Rotation Detected":"No Rotation",Math.Abs(SurveyToInternal.DeltaZ)>1e-7?"Vertical Datum Difference":"No Vertical Offset"};
    }
    public static class CoordinateTransformService
    {
        public static SitePoint Metres(XYZ p)=>new(p.X*.3048,p.Y*.3048,p.Z*.3048);
        public static XYZ Feet(SitePoint p)=>new(p.X/.3048,p.Y/.3048,p.Z/.3048);
        public static SitePoint SurveyToInternal(CoordinateSnapshot c,SitePoint p)=>c.SurveyToInternal.Apply(p);
        public static SitePoint InternalToSurvey(CoordinateSnapshot c,SitePoint p)=>c.SurveyToInternal.Inverse(p);
        public static CoordinateSnapshot Read(Document doc)
        {
            var location=doc.ActiveProjectLocation;
            var api=location.GetTotalTransform();
            SitePoint Position(XYZ p){var q=location.GetProjectPosition(p);return new(q.EastWest*.3048,q.NorthSouth*.3048,q.Elevation*.3048);}
            var samples=new[]{XYZ.Zero,new XYZ(17,3,5),new XYZ(-4,21,-2)};
            bool Matches(Transform t)=>samples.All(p=>Metres(t.OfPoint(p)).Distance(Position(p))<1e-7);
            // Direction is determined against independently reported project coordinates, not assumed.
            Transform toSurvey;string direction;
            if(Matches(api)){toSurvey=api;direction="GetTotalTransform: InternalToSurvey (verified against GetProjectPosition)";}
            else if(Matches(api.Inverse)){toSurvey=api.Inverse;direction="GetTotalTransform inverse: InternalToSurvey (verified against GetProjectPosition)";}
            else throw new InvalidOperationException("ProjectLocation transform 與 ProjectPosition 不一致；停止座標定位。");
            var inverse=toSurvey.Inverse;var origin=Metres(inverse.Origin);
            var transform=new SiteTransform(origin.X,origin.Y,origin.Z,Math.Atan2(inverse.BasisX.Y,inverse.BasisX.X));
            foreach(var p in samples){var m=Metres(p);if(transform.Apply(transform.Inverse(m)).Distance(m)>1e-7 || transform.Inverse(m).Distance(Position(p))>1e-7)throw new InvalidOperationException("座標 round-trip / direction 驗證失敗。");}
            return new(){SurveyToInternal=transform,ActiveProjectLocationId=location.Id.GetIdValue(),ProjectBasePoint=Metres(BasePoint.GetProjectBasePoint(doc).Position),SurveyPoint=Metres(BasePoint.GetSurveyPoint(doc).Position),ProjectPosition=Position(XYZ.Zero),TrueNorthRotation=location.GetProjectPosition(XYZ.Zero).Angle,VerifiedDirection=direction};
        }
    }
    public record SiteElementOption(long Id,string Name);
    public sealed class TerrainReadback
    {
        public long ElementId {get;set;}
        public string Category {get;set;}="";
        public long TypeId {get;set;}
        public long LevelId {get;set;}
        public TerrainBounds Bounds {get;set;}=new(new(0,0,0),new(0,0,0));
        public TerrainBounds SurfaceBounds {get;set;}=new(new(0,0,0),new(0,0,0));
        public double Area {get;set;}
        public double Volume {get;set;}
        public string ProjectUnitArea {get;set;}="";
        public string ProjectUnitVolume {get;set;}="";
    }
    public sealed class RevitTerrainService
    {
        public static List<SiteElementOption> Types(Document d)=>new FilteredElementCollector(d).OfClass(typeof(ToposolidType)).Cast<ToposolidType>().OrderBy(e=>e.Id.GetIdValue()).Select(e=>new SiteElementOption(e.Id.GetIdValue(),e.Name)).ToList();
        public static List<SiteElementOption> Levels(Document d)=>new FilteredElementCollector(d).OfClass(typeof(Level)).Cast<Level>().OrderBy(e=>e.ProjectElevation).Select(e=>new SiteElementOption(e.Id.GetIdValue(),e.Name)).ToList();
        public static List<TerrainTriangle> Surface(Document d,long id)
        {
            var topo=d.GetElement(new ElementId(id)) as Toposolid ?? throw new ArgumentException("Terrain 已刪除或不是 Toposolid。");
            var triangles=new List<TerrainTriangle>();
            foreach(var reference in HostObjectUtils.GetTopFaces(topo))
            {
                var face=topo.GetGeometryObjectFromReference(reference) as Face;
                if(face==null)continue;
                var mesh=face.Triangulate();
                for(int i=0;i<mesh.NumTriangles;i++)
                {
                    var t=mesh.get_Triangle(i);var a=CoordinateTransformService.Metres(t.get_Vertex(0));var b=CoordinateTransformService.Metres(t.get_Vertex(1));var c=CoordinateTransformService.Metres(t.get_Vertex(2));
                    if(Math.Abs((b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X))>1e-12)triangles.Add(new(a,b,c));
                }
            }
            if(triangles.Count==0)throw new InvalidOperationException("地形沒有可用頂面 TIN。");return triangles;
        }
        public static TerrainReadback Read(Document d,long id)
        {
            var t=d.GetElement(new ElementId(id)) as Toposolid??throw new ArgumentException("Terrain 已失效。");
            var box=t.get_BoundingBox(null)??throw new InvalidOperationException("缺少 bounding box。");
            double area=t.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble()??double.NaN;
            double volume=t.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED)?.AsDouble()??double.NaN;
            if(!double.IsFinite(area)||!double.IsFinite(volume))throw new InvalidOperationException("缺少有效 Area/Volume。");
            return new(){ElementId=id,Category=t.Category.Name,TypeId=t.GetTypeId().GetIdValue(),LevelId=t.LevelId.GetIdValue(),Bounds=new(CoordinateTransformService.Metres(box.Min),CoordinateTransformService.Metres(box.Max)),SurfaceBounds=TerrainBounds.Of(Surface(d,id).SelectMany(p=>new[]{p.A,p.B,p.C})),Area=area*.3048*.3048,Volume=volume*Math.Pow(.3048,3),ProjectUnitArea=UnitFormatUtils.Format(d.GetUnits(),SpecTypeId.Area,area,false),ProjectUnitVolume=UnitFormatUtils.Format(d.GetUnits(),SpecTypeId.Volume,volume,false)};
        }
        public static TerrainReadback Create(Document d,IReadOnlyList<SitePoint> points,long type,long level,double tolerance,bool confirmed,bool largeOverride,Action<TerrainReadback> report)
        {
            if(!confirmed)throw new InvalidOperationException("建立地形需要明確確認。");
            if(points.Count>20000&&!largeOverride)throw new InvalidOperationException("超過 tool 20k 點保護門檻，請減點或明確覆核 override。");
            if(points.Count<3||points.Any(p=>!p.Finite||!XYZ.IsWithinLengthLimits(CoordinateTransformService.Feet(p))))throw new ArgumentException("Point count 或座標超過 Revit 範圍。");
            if(d.GetElement(new ElementId(type)) is not ToposolidType || d.GetElement(new ElementId(level)) is not Level)throw new ArgumentException("Type / Level 已失效。");
            using var group=new TransactionGroup(d,"基地：建立地形與回讀");group.Start();
            try
            {
                long id;
                using(var tx=new Transaction(d,"建立 Toposolid")){tx.Start();var t=Toposolid.Create(d,points.Select(CoordinateTransformService.Feet).ToList(),new ElementId(type),new ElementId(level));id=t.Id.GetIdValue();if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("建立交易失敗。");}
                var result=Read(d,id);var expected=TerrainBounds.Of(points);
                if(result.TypeId!=type||result.LevelId!=level||result.SurfaceBounds.Min.Distance(expected.Min)>tolerance||result.SurfaceBounds.Max.Distance(expected.Max)>tolerance)throw new InvalidOperationException("Toposolid read-back Type/Level/頂面 bounds 不一致。");
                report(result);
                if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("地形交易群組未提交。");return result;
            }
            catch{if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}
        }
        public static double Excavate(Document d,long terrainId,long cutterId,bool execute,bool confirmed,double? expected,Action<double>? report=null)
        {
            if(execute&&!confirmed)throw new InvalidOperationException("開挖需要明確確認。");
            var topo=d.GetElement(new ElementId(terrainId)) as Toposolid??throw new ArgumentException("請選 host Toposolid。");
            var cutter=d.GetElement(new ElementId(cutterId));
            if(cutter is not Floor && cutter is not RoofBase && cutter is not Toposolid)throw new ArgumentException("Cutter 僅支援 Floor / Roof / Toposolid。");
            if(!topo.CanBeExcavatedBy(cutter.Id))throw new InvalidOperationException("Revit 不允許此 cutter 開挖；可能已存在或不相交。");
            double Volume(){var p=topo.get_Parameter(BuiltInParameter.TOTAL_EXCAVATION_VOLUME);if(p==null || p.StorageType!=StorageType.Double)throw new InvalidOperationException("缺少 Revit excavation volume 參數。");return p.AsDouble()*Math.Pow(.3048,3);}
            using var group=new TransactionGroup(d,execute?"基地：開挖與回讀":"基地：開挖試算（回復）");group.Start();
            try
            {
                double before=Volume();
                using(var tx=new Transaction(d,"Toposolid cutter")){tx.Start();topo.ExcavateBy(cutter.Id);if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("開挖交易失敗。");}
                double delta=Volume()-before;
                if(!double.IsFinite(delta)||delta<=0)throw new InvalidOperationException("Revit 未回讀到有效新增開挖體積。");
                if(expected.HasValue && Math.Abs(delta-expected.Value)>Math.Max(1e-6,expected.Value*1e-6))throw new InvalidOperationException("開挖量與 Preview 不一致。");
                if(execute){report?.Invoke(delta);if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("開挖群組未提交。");}
                else group.RollBack();return delta;
            }
            catch{if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}
        }
        public static string FormatVolume(Document d,double metres)=>UnitFormatUtils.Format(d.GetUnits(),SpecTypeId.Volume,metres/Math.Pow(.3048,3),false);
    }
}
#endif
