#if REVIT2026 || SITE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;

namespace RevitMCP.Core.Site
{
    public enum TerrainSourceKind { DelimitedPointFile, CadFile }
    public enum TerrainCoordinateBasis { SourceCoordinates, PositionedModelMetres }
    public enum CadPlacement { Origin, Shared }
    public record CadTerrainRequest(string Path, CadPlacement Placement, string Units);
    public record CadPrimitive(string Layer,string Kind,IReadOnlyList<SitePoint> Points,bool Closed=false,bool? SourceZeroElevation=null);
    public record CadLayerSummary(string Name,int GeometryCount,int PointCount,int PolylineCount,int CurveCount,int ValidPointCount,TerrainBounds? Bounds,string GeometryTypes);
    public record CadBoundary(string Name,IReadOnlyList<SitePoint> Points);
    public sealed class CadTerrainBudget
    {
        public const string Message="CAD 地形資料量過大，請縮小圖層或使用減點。";
        public const long MaxFileBytes=100*1024*1024;
        public const int MaxGeometry=100000,MaxVertices=200000,MaxDepth=16;
        private long geometry,vertices;
        public void Geometry(int depth){if(depth>MaxDepth||++geometry>MaxGeometry)throw new InvalidOperationException(Message);}
        public void Vertices(int count){if(count<0||(vertices+=count)>MaxVertices)throw new InvalidOperationException(Message);}
    }
    /// <summary>Only immutable CLR data may survive the import transaction rollback.</summary>
    public sealed class CadTerrainAnalysis
    {
        public string FileName {get;init;}="";
        public string SHA256 {get;init;}="";
        public string Units {get;init;}="m";
        public CadPlacement Placement {get;init;}
        public string TransformEvidence {get;init;}="";
        public bool RollbackVerified {get;init;}
        public IReadOnlyList<CadPrimitive> Geometry {get;init;}=Array.Empty<CadPrimitive>();
        private IReadOnlyList<CadLayerSummary>? layers;
        private IReadOnlyList<CadBoundary>? boundaries;
        public IReadOnlyList<CadLayerSummary> Layers=>layers??=Geometry.GroupBy(p=>p.Layer).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>
        {
            var points=g.SelectMany(p=>p.Points).Where(p=>p.Finite).ToArray();
            return new CadLayerSummary(g.Key,g.Count(p=>p.Kind!="NoTerrainGeometry"),g.Count(p=>p.Kind=="Point"),g.Count(p=>p.Kind=="PolyLine"),g.Count(p=>p.Kind=="Line"),points.Length,points.Length>0?TerrainBounds.Of(points):null,string.Join(" / ",g.Select(p=>p.Kind=="NoTerrainGeometry"?"無可靠地形幾何":p.Kind).Distinct()));
        }).ToArray();
        public IReadOnlyList<CadBoundary> Boundaries=>boundaries??=Geometry.Where(p=>p.Closed&&ValidBoundary(p.Points)).Select((p,i)=>new CadBoundary($"{p.Layer} — 邊界 {i+1}",OpenLoop(p.Points))).ToArray();
        public TerrainPointDataset Dataset(IEnumerable<string> selectedLayers,string? boundaryName=null)
        {
            if(!RollbackVerified)throw new InvalidOperationException("CAD 暫存回復尚未通過驗證。");
            var selected=selectedLayers.ToHashSet(StringComparer.Ordinal);
            if(selected.Count==0)throw new ArgumentException("請至少選擇一個地形圖層。");
            if(selected.Except(Layers.Select(l=>l.Name)).Any())throw new ArgumentException("圖層已失效，請重新分析。");
            var geometry=Geometry.Where(g=>selected.Contains(g.Layer)).ToArray();
            var text=new StringBuilder("X,Y,Z,ID\n");int n=0;
            foreach(var primitive in geometry)foreach(var p in primitive.Points)
                text.AppendLine(FormattableString.Invariant($"{p.X:R},{p.Y:R},{p.Z:R},{++n}"));
            var data=TerrainPointParser.Parse(text.ToString(),new(',',true,0,1,2,3));
            data.SourceKind=TerrainSourceKind.CadFile;data.CoordinateBasis=TerrainCoordinateBasis.PositionedModelMetres;data.SourceName=FileName;data.SourceSHA256=SHA256;data.Units=Units;
            data.Provenance=new{Placement,Units,SelectedLayers=selected.OrderBy(s=>s,StringComparer.Ordinal).ToArray(),Boundary=boundaryName,GeometryStats=Layers,TransformEvidence,RollbackVerified,CoordinateBasis="Already positioned model metres"};
            foreach(var unsupported in geometry.Where(g=>g.Points.Count==0))data.Diagnostics.Warnings.Add(new(0,"CAD_REVIEW_REQUIRED",$"圖層 {unsupported.Layer}：{unsupported.Kind} 不提供可靠地形點。"));
            if(data.Points.Count>0&&geometry.Where(g=>g.Points.Count>0).All(g=>g.SourceZeroElevation??g.Points.All(p=>Math.Abs(p.Z)<1e-9)))
                data.Diagnostics.Warnings.Add(new(0,"CAD_ZERO_Z_REVIEW_REQUIRED","CAD 圖層存在高程文字，但幾何沒有可靠 Z 值，第一版不自動配對。請確認零高程是否為實際測量值；文字存在與否無法由幾何 API 完整判定。"));
            return data;
        }
        public static IReadOnlyList<SitePoint> OpenLoop(IReadOnlyList<SitePoint> points)=>points.Count>1&&points[0].Distance(points[^1])<1e-7?points.Take(points.Count-1).ToArray():points;
        public static bool ValidBoundary(IReadOnlyList<SitePoint> input)
        {
            var p=OpenLoop(input);if(p.Count<3||p.Count>2000||p.Any(v=>!v.Finite)||p.Max(v=>v.Z)-p.Min(v=>v.Z)>1e-7)return false;
            double sign=0;
            for(int i=0;i<p.Count;i++)
            {
                var a=p[i];var b=p[(i+1)%p.Count];if(a.Distance(b)<1e-7)return false;
                for(int j=0;j<p.Count;j++)
                {
                    double cross=(b.X-a.X)*(p[j].Y-a.Y)-(b.Y-a.Y)*(p[j].X-a.X);
                    if(Math.Abs(cross)<1e-9)continue;
                    if(sign==0)sign=Math.Sign(cross);else if(sign*cross<0)return false;
                }
            }
            return sign!=0;
        }
    }
}
#endif
