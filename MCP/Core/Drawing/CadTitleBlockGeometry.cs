#if REVIT2026 || DRAWING_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RevitMCP.Core.Drawing
{
    public enum TitleBlockPurpose { Custom, ConstructionDrawing, AsBuiltDrawing }
    public enum CadNormalizationMode { Relocate, UniformToPaper, OriginalSize }
    public sealed record CadGeometry(string Id,string Layer,string Kind,DrawingBounds Bounds,DrawingPoint[] Points,bool Closed=false,TitleBlockPurpose SuggestedPurpose=TitleBlockPurpose.Custom);
    public sealed record CadBlockInstance(string Id,DrawingBounds Bounds,int ExpandedGeometryCount);
    public sealed class CadGeometryCluster
    {
        public string ClusterId {get;init;}="";
        public string[] GeometryIds {get;init;}=Array.Empty<string>();
        public int GeometryCount=>GeometryIds.Length;
        public DrawingBounds Bounds {get;init;}=new(0,0,0,0);
        public double Width=>Bounds.Width;
        public double Height=>Bounds.Height;
        public DrawingPoint Centroid=>new((Bounds.MinX+Bounds.MaxX)/2,(Bounds.MinY+Bounds.MaxY)/2);
        public string[] Layers {get;init;}=Array.Empty<string>();
        public double DistanceFromLargestCluster {get;set;}
        public bool RemoteGeometryWarning {get;set;}
    }
    public sealed class CadTitleBlockCandidate
    {
        public string CandidateId {get;init;}="";
        public string ClusterId {get;set;}="";
        public DrawingBounds Bounds {get;init;}=new(0,0,0,0);
        public double PaperWidth {get;init;}
        public double PaperHeight {get;init;}
        public DrawingPoint? Origin {get;init;}
        public double Width=>PaperWidth>0?PaperWidth:Bounds.Width;
        public double Height=>PaperHeight>0?PaperHeight:Bounds.Height;
        public double AspectRatio=>Math.Max(Width,Height)/Math.Min(Width,Height);
        public double Rotation {get;init;}
        public bool Closed {get;init;}
        public string Orientation=>Width>=Height?"Landscape":"Portrait";
        public string DetectedPaperSize {get;init;}="Custom";
        public double SuggestedUniformScale {get;init;}=1;
        public string BorderLayer {get;init;}="";
        public string[] ContainedLayers {get;init;}=Array.Empty<string>();
        public string[] GeometryIds {get;init;}=Array.Empty<string>();
        public int GeometryCount=>GeometryIds.Length;
        public DrawingPoint Centroid=>new((Bounds.MinX+Bounds.MaxX)/2,(Bounds.MinY+Bounds.MaxY)/2);
        public string[] ConfidenceEvidence {get;init;}=Array.Empty<string>();
        public TitleBlockPurpose SuggestedPurpose {get;init;}
        public string OptionalSuggestedName=>SuggestedPurpose switch{TitleBlockPurpose.ConstructionDrawing=>"可能為施工圖",TitleBlockPurpose.AsBuiltDrawing=>"可能為竣工圖",_=>""};
        public override string ToString()=>$"{CandidateId} — {DetectedPaperSize} {(Orientation=="Landscape"?"橫式":"直式")} {Width:0.###} × {Height:0.###} mm；幾何 {GeometryCount}";
    }
    public sealed class CadTitleBlockAnalysis
    {
        public string Unit {get;init;}="";
        public double SourceUnitToMm {get;init;}=1;
        public CadGeometry[] Geometry {get;init;}=Array.Empty<CadGeometry>();
        public IReadOnlyList<CadBlockInstance> BlockInstances {get;set;}=Array.Empty<CadBlockInstance>();
        public DrawingBounds GlobalBounds {get;init;}=new(0,0,0,0);
        public IReadOnlyList<CadGeometryCluster> Clusters {get;init;}=Array.Empty<CadGeometryCluster>();
        public IReadOnlyList<CadTitleBlockCandidate> Candidates {get;init;}=Array.Empty<CadTitleBlockCandidate>();
        public string[] Warnings {get;init;}=Array.Empty<string>();
    }
    public sealed class CadConversionSelection
    {
        public string CandidateId {get;set;}="";
        public string[] SelectedLayers {get;set;}=Array.Empty<string>();
        public string[] AdditionalGeometryIds {get;set;}=Array.Empty<string>();
        public CadNormalizationMode Mode {get;set;}
        public string TargetPaper {get;set;}="Custom";
        public double CustomWidthMm {get;set;}
        public bool Previewed {get;set;}
        public bool GeometryFilterConfirmed {get;set;}
        public bool PurposeConfirmed {get;set;}
        public TitleBlockPurpose Purpose {get;set;}
        public string ProfileName {get;set;}="";
        public DrawingBounds? ManualBounds {get;set;}
    }
    public sealed record CadNormalizationAnalysis(DrawingBounds OriginalBounds,DrawingBounds TargetBounds,double TranslationX,double TranslationY,double UniformScale,double AspectRatioError,bool ReviewRequired,string Explanation,double SourceWidth,double SourceHeight,double Rotation)
    {
        public bool TranslationRequired=>Math.Abs(TranslationX)>1e-9||Math.Abs(TranslationY)>1e-9;
        public bool UniformScaleRequired=>Math.Abs(UniformScale-1)>1e-9;
    }
    public static class CadGeometryClusterService
    {
        public const int MaxGeometry=20000;
        public static DrawingBounds Union(IEnumerable<DrawingBounds> input)
        {var a=input.ToArray();if(a.Length==0)throw new ArgumentException("CAD 沒有可分析的幾何。");return new(a.Min(b=>b.MinX),a.Min(b=>b.MinY),a.Max(b=>b.MaxX),a.Max(b=>b.MaxY));}
        public static double Gap(DrawingBounds a,DrawingBounds b)
        {double x=Math.Max(0,Math.Max(a.MinX-b.MaxX,b.MinX-a.MaxX)),y=Math.Max(0,Math.Max(a.MinY-b.MaxY,b.MinY-a.MaxY));return Math.Sqrt(x*x+y*y);}
        public static bool Contains(DrawingBounds outer,DrawingBounds inner,double tolerance)=>inner.MinX>=outer.MinX-tolerance&&inner.MinY>=outer.MinY-tolerance&&inner.MaxX<=outer.MaxX+tolerance&&inner.MaxY<=outer.MaxY+tolerance;
        public static string StableId(string prefix,IEnumerable<string> ids)
        {var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",ids.OrderBy(s=>s,StringComparer.Ordinal)))));return prefix+(prefix is "P-" or "N-"?hash:hash[..12]);}
        public static IReadOnlyList<CadGeometryCluster> Cluster(IReadOnlyList<CadGeometry> geometry)
        {
            if(geometry.Count==0||geometry.Count>MaxGeometry)throw new ArgumentException($"CAD 幾何需介於 1–{MaxGeometry} 個；請縮小來源，不會截斷資料。");
            var sizes=geometry.Select(g=>Math.Max(g.Bounds.Width,g.Bounds.Height)).Where(d=>d>0).OrderBy(d=>d).ToArray();
            double tolerance=Math.Max(.01,(sizes.Length>0?sizes[sizes.Length/2]:1)*.02);
            var a=geometry.OrderBy(g=>g.Bounds.MinX).ThenBy(g=>g.Id,StringComparer.Ordinal).ToArray();var parent=Enumerable.Range(0,a.Length).ToArray();
            int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
            long comparisons=0;
            for(int i=0;i<a.Length;i++)for(int j=i+1;j<a.Length&&a[j].Bounds.MinX<=a[i].Bounds.MaxX+tolerance;j++)
            {if(++comparisons>10000000)throw new ArgumentException("CAD 重疊幾何過多，請縮小來源；分析未截斷。");if(Gap(a[i].Bounds,a[j].Bounds)<=tolerance)parent[Root(j)]=Root(i);}
            var result=Enumerable.Range(0,a.Length).GroupBy(Root).Select(g=>{var members=g.Select(i=>a[i]).ToArray();return new CadGeometryCluster{ClusterId=StableId("G-",members.Select(m=>m.Id)),GeometryIds=members.Select(m=>m.Id).OrderBy(s=>s,StringComparer.Ordinal).ToArray(),Bounds=Union(members.Select(m=>m.Bounds)),Layers=members.Select(m=>m.Layer).Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToArray()};}).OrderByDescending(c=>c.GeometryCount).ThenBy(c=>c.Bounds.MinX).ThenBy(c=>c.ClusterId,StringComparer.Ordinal).ToArray();
            var largest=result[0];double extent=Math.Max(largest.Width,largest.Height);
            foreach(var c in result){c.DistanceFromLargestCluster=Gap(c.Bounds,largest.Bounds);c.RemoteGeometryWarning=c!=largest&&c.GeometryCount<=Math.Max(2,largest.GeometryCount*.1)&&c.DistanceFromLargestCluster>Math.Max(10000,extent*10);}
            return result;
        }
    }
    public static class CadTitleBlockAnalyzer
    {
        public static string PreviewSignature(ExternalTitleBlockAnalysis a)
        {
            var s=a.CadSelection??throw new ArgumentException("請先選擇候選。");var c=Selected(a.Cad!,s);var n=Normalize(c,s);
            return CadGeometryClusterService.StableId("P-",new[]{a.FileHash,c.CandidateId,n.ToString(),s.Mode.ToString(),s.TargetPaper,s.Purpose.ToString(),s.ProfileName}.Concat(SelectedGeometry(a.Cad!,s)));
        }
        // Recognition tolerances are tool rules documented in the drawing Domain, not company standards.
        public const double PaperToleranceMm=1;
        public const double AspectTolerance=.005;
        public static (string Paper,double Scale) Recognize(double width,double height)
        {
            var factors=new[]{1d,.001,1000,.01,100,.1,10,25.4,1/25.4};
            foreach(double factor in factors)foreach(var paper in new[]{"A0","A1","A2","A3","A4"})
            {var p=AutoSheetLayoutService.PaperSize(paper);if(Math.Abs(Math.Max(width,height)*factor-p.Width)<=PaperToleranceMm&&Math.Abs(Math.Min(width,height)*factor-p.Height)<=PaperToleranceMm)return(paper,factor);}
            return("Custom",1);
        }
        public static CadTitleBlockAnalysis Analyze(CadGeometry[] input,string unit,double unitToMm,string[]? warnings=null)
        {
            var geometry=input.OrderBy(g=>g.Id,StringComparer.Ordinal).ToArray();
            if(geometry.Any(g=>new[]{g.Bounds.MinX,g.Bounds.MinY,g.Bounds.MaxX,g.Bounds.MaxY}.Any(v=>!double.IsFinite(v))||g.Bounds.Width<0||g.Bounds.Height<0)||geometry.Select(g=>g.Id).Distinct().Count()!=geometry.Length)throw new ArgumentException("CAD 幾何座標／識別無效。");
            var clusters=CadGeometryClusterService.Cluster(geometry);var candidates=new List<CadTitleBlockCandidate>();
            foreach(var cluster in clusters)
            {
                var ids=cluster.GeometryIds.ToHashSet(StringComparer.Ordinal);var members=geometry.Where(g=>ids.Contains(g.Id)).ToArray();
                double tolerance=Math.Max(.001,Math.Max(cluster.Width,cluster.Height)*1e-8);
                var segments=members.Where(g=>g.Kind=="Line"||g.Kind=="LwPolyline"||g.Kind=="Polyline2D").SelectMany(g=>Segments(g)).ToArray();
                var horizontal=segments.Where(s=>Math.Abs(s.A.Y-s.B.Y)<=tolerance&&Math.Abs(s.A.X-s.B.X)>tolerance).ToArray();
                var vertical=segments.Where(s=>Math.Abs(s.A.X-s.B.X)<=tolerance&&Math.Abs(s.A.Y-s.B.Y)>tolerance).ToArray();
                if(horizontal.Length>1000||vertical.Length>1000)throw new ArgumentException("CAD 圖框邊線超過分析預算；請縮小來源，不會省略候選。");
                var boxes=new List<(DrawingBounds Bounds,string Layer)>();
                // Paired full horizontal edges plus coverage by collinear vertical segments.
                for(int i=0;i<horizontal.Length;i++)for(int j=i+1;j<horizontal.Length;j++)
                {
                    var a=horizontal[i];var b=horizontal[j];double x0=Math.Min(a.A.X,a.B.X),x1=Math.Max(a.A.X,a.B.X),y0=Math.Min(a.A.Y,b.A.Y),y1=Math.Max(a.A.Y,b.A.Y);
                    if(y1-y0<=tolerance||Math.Abs(x0-Math.Min(b.A.X,b.B.X))>tolerance||Math.Abs(x1-Math.Max(b.A.X,b.B.X))>tolerance)continue;
                    bool Covered(double x){double end=y0;foreach(var s in vertical.Where(v=>Math.Abs(v.A.X-x)<=tolerance).OrderBy(v=>Math.Min(v.A.Y,v.B.Y))){double lo=Math.Min(s.A.Y,s.B.Y),hi=Math.Max(s.A.Y,s.B.Y);if(lo<=end+tolerance&&hi>=end-tolerance)end=Math.Max(end,hi);}return end>=y1-tolerance;}
                    if(Covered(x0)&&Covered(x1))boxes.Add((new(x0,y0,x1,y1),a.Layer));
                }
                // Only exact duplicate locations collapse. Equal dimensions at different locations never merge.
                var unique=new List<(DrawingBounds Bounds,string Layer)>();
                foreach(var box in boxes.OrderByDescending(b=>b.Bounds.Width*b.Bounds.Height).ThenBy(b=>b.Bounds.MinX).ThenBy(b=>b.Bounds.MinY))
                {if(unique.Any(u=>Math.Abs(u.Bounds.MinX-box.Bounds.MinX)<=tolerance&&Math.Abs(u.Bounds.MinY-box.Bounds.MinY)<=tolerance&&Math.Abs(u.Bounds.MaxX-box.Bounds.MaxX)<=tolerance&&Math.Abs(u.Bounds.MaxY-box.Bounds.MaxY)<=tolerance))continue;unique.Add(box);}
                // Nested tables are evidence of the enclosing frame, not additional sheets.
                foreach(var box in unique.Where(b=>!unique.Any(o=>o.Bounds!=b.Bounds&&o.Bounds.Width*o.Bounds.Height>b.Bounds.Width*b.Bounds.Height&&CadGeometryClusterService.Contains(o.Bounds,b.Bounds,tolerance))))
                    candidates.Add(CreateCandidate(geometry,cluster,box.Bounds,box.Layer,tolerance,"四邊閉合矩形；包含圖欄；內部小表格不重複列為圖紙"));
                foreach(var border in members.Where(g=>g.Closed&&g.Points.Length is 4 or 5))
                {
                    var p=border.Points;double dx=p[1].X-p[0].X,dy=p[1].Y-p[0].Y,ex=p[2].X-p[1].X,ey=p[2].Y-p[1].Y;
                    double w=Math.Sqrt(dx*dx+dy*dy),h=Math.Sqrt(ex*ex+ey*ey),angle=Math.Atan2(dy,dx);
                    if(w<=tolerance||h<=tolerance||Math.Abs(dx*ex+dy*ey)>w*h*1e-8||Math.Abs(p[3].X-(p[0].X+ex))>tolerance||Math.Abs(p[3].Y-(p[0].Y+ey))>tolerance)continue;
                    if(Math.Abs(Math.Sin(angle))<1e-8||Math.Abs(Math.Cos(angle))<1e-8)continue; // Axis-aligned loops already handled above.
                    var origin=dx*ey-dy*ex>0?p[0]:p[3];
                    if(candidates.Any(c=>CadGeometryClusterService.Contains(c.Bounds,border.Bounds,tolerance)))continue;
                    candidates.Add(CreateCandidate(geometry,cluster,border.Bounds,border.Layer,tolerance,"閉合旋轉矩形；轉換前還原矩形方向",w,h,angle,origin));
                }
            }
            // A closed frame spatially owns detached text/logo inside it. Proximity to a border
            // alone would drop text whose insertion point does not touch any line.
            var assigned=candidates.SelectMany(c=>c.GeometryIds).ToHashSet(StringComparer.Ordinal);
            var frameClusters=candidates.Select(c=>{c.ClusterId=CadGeometryClusterService.StableId("G-",c.GeometryIds);return new CadGeometryCluster{ClusterId=c.ClusterId,GeometryIds=c.GeometryIds,Bounds=c.Bounds,Layers=c.ContainedLayers};}).ToList();
            var remainder=geometry.Where(g=>!assigned.Contains(g.Id)).ToArray();
            if(remainder.Length>0)frameClusters.AddRange(CadGeometryClusterService.Cluster(remainder));
            if(frameClusters.Count>0)
            {
                clusters=frameClusters.OrderByDescending(c=>c.GeometryCount).ThenBy(c=>c.Bounds.MinX).ToArray();var main=clusters[0];double extent=Math.Max(main.Width,main.Height);
                foreach(var c in clusters){c.DistanceFromLargestCluster=CadGeometryClusterService.Gap(c.Bounds,main.Bounds);c.RemoteGeometryWarning=c!=main&&c.GeometryCount<=Math.Max(2,main.GeometryCount*.1)&&c.DistanceFromLargestCluster>Math.Max(10000,extent*10);}
            }
            return new(){Unit=unit,SourceUnitToMm=unitToMm,Geometry=geometry,GlobalBounds=CadGeometryClusterService.Union(geometry.Select(g=>g.Bounds)),Clusters=clusters,Candidates=candidates.OrderByDescending(c=>c.DetectedPaperSize!="Custom").ThenByDescending(c=>c.Width*c.Height).ThenBy(c=>c.Bounds.MinX).ThenBy(c=>c.CandidateId,StringComparer.Ordinal).ToArray(),Warnings=(warnings??Array.Empty<string>()).Concat(clusters.Where(c=>c.RemoteGeometryWarning).Select(c=>"REMOTE_GEOMETRY："+c.ClusterId+"，不自動用於紙張尺寸；仍可明確選取保留。")).ToArray()};
        }
        private static IEnumerable<(DrawingPoint A,DrawingPoint B,string Layer)> Segments(CadGeometry g)
        {for(int i=1;i<g.Points.Length;i++)yield return(g.Points[i-1],g.Points[i],g.Layer);if(g.Closed&&g.Points.Length>2)yield return(g.Points[^1],g.Points[0],g.Layer);}
        public static CadTitleBlockCandidate Manual(CadTitleBlockAnalysis a,DrawingBounds bounds)
        {if(!bounds.Valid)throw new ArgumentException("請指定有效的兩個對角點。");return CreateCandidate(a.Geometry,new CadGeometryCluster{ClusterId="manual"},bounds,"使用者指定",Math.Max(.001,Math.Max(bounds.Width,bounds.Height)*1e-8),"人工指定紙張範圍；尚未授權刪除範圍外幾何");}
        private static CadTitleBlockCandidate CreateCandidate(CadGeometry[] members,CadGeometryCluster cluster,DrawingBounds bounds,string layer,double tolerance,string evidence,double width=0,double height=0,double rotation=0,DrawingPoint? origin=null)
        {
            if(width==0)width=bounds.Width;if(height==0)height=bounds.Height;
            bool Inside(CadGeometry g)
            {
                if(!CadGeometryClusterService.Contains(bounds,g.Bounds,tolerance))return false;if(origin==null)return true;
                var points=g.Points.Length>0?g.Points:new[]{new DrawingPoint(g.Bounds.MinX,g.Bounds.MinY),new DrawingPoint(g.Bounds.MaxX,g.Bounds.MinY),new DrawingPoint(g.Bounds.MaxX,g.Bounds.MaxY),new DrawingPoint(g.Bounds.MinX,g.Bounds.MaxY)};
                return points.All(p=>{double dx=p.X-origin.X,dy=p.Y-origin.Y,x=dx*Math.Cos(rotation)+dy*Math.Sin(rotation),y=-dx*Math.Sin(rotation)+dy*Math.Cos(rotation);return x>=-tolerance&&y>=-tolerance&&x<=width+tolerance&&y<=height+tolerance;});
            }
            var inside=members.Where(Inside).ToArray();var recognized=Recognize(width,height);
            var purposes=inside.Select(g=>g.SuggestedPurpose).Where(p=>p!=TitleBlockPurpose.Custom).Distinct().ToArray();
            return new(){CandidateId=CadGeometryClusterService.StableId("C-",inside.Select(g=>g.Id).Append(FormattableString.Invariant($"{bounds.MinX:R},{bounds.MinY:R},{bounds.MaxX:R},{bounds.MaxY:R}"))),ClusterId=cluster.ClusterId,Bounds=bounds,PaperWidth=width,PaperHeight=height,Origin=origin,Rotation=rotation,Closed=true,BorderLayer=layer,ContainedLayers=inside.Select(g=>g.Layer).Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToArray(),GeometryIds=inside.Select(g=>g.Id).OrderBy(s=>s,StringComparer.Ordinal).ToArray(),DetectedPaperSize=recognized.Paper,SuggestedUniformScale=recognized.Scale,SuggestedPurpose=purposes.Length==1?purposes[0]:TitleBlockPurpose.Custom,ConfidenceEvidence=new[]{evidence,recognized.Paper=="Custom"?"外框可信；尺寸未符合 ISO 紙張／已知單位倍率，需人工複核":"尺寸符合 "+recognized.Paper+(recognized.Scale==1?"，僅需重新定位":"，需確認單位倍率 "+recognized.Scale)}};
        }
        public static CadTitleBlockCandidate Selected(CadTitleBlockAnalysis a,CadConversionSelection s)=>s.ManualBounds!=null?Manual(a,s.ManualBounds):a.Candidates.SingleOrDefault(c=>c.CandidateId==s.CandidateId)??throw new ArgumentException("請先選擇圖框候選。");
        public static string[] SelectedGeometry(CadTitleBlockAnalysis a,CadConversionSelection s)
        {
            var c=Selected(a,s);var layers=s.SelectedLayers.ToHashSet(StringComparer.Ordinal);var ids=c.GeometryIds.Concat(s.AdditionalGeometryIds).ToHashSet(StringComparer.Ordinal);
            if(s.AdditionalGeometryIds.Except(a.Geometry.Select(g=>g.Id)).Any()||layers.Except(a.Geometry.Select(g=>g.Layer)).Any())throw new ArgumentException("幾何／圖層已失效，請重新分析。");
            if(a.Candidates.Where(other=>other.CandidateId!=c.CandidateId).SelectMany(other=>other.GeometryIds).Intersect(s.AdditionalGeometryIds).Any())throw new ArgumentException("另一候選的幾何必須另建圖框，不可混入此 Family。");
            var selected=a.Geometry.Where(g=>ids.Contains(g.Id)&&layers.Contains(g.Layer)).Select(g=>g.Id).OrderBy(i=>i,StringComparer.Ordinal).ToArray();
            if(selected.Length==0)throw new ArgumentException("未選擇要保留的圖框幾何。");return selected;
        }
        public static CadNormalizationAnalysis Normalize(CadTitleBlockCandidate c,CadConversionSelection s)
        {
            var b=c.Bounds;double scale=1,error=0;bool review=false;double targetW=c.Width,targetH=c.Height;
            if(s.Mode==CadNormalizationMode.UniformToPaper)
            {
                if(s.TargetPaper=="Custom")
                {if(!double.IsFinite(s.CustomWidthMm)||s.CustomWidthMm<=0)throw new ArgumentException("請輸入自訂目標寬度（mm）。");scale=s.CustomWidthMm/c.Width;targetW=s.CustomWidthMm;targetH=c.Height*scale;}
                else
                {var p=AutoSheetLayoutService.PaperSize(s.TargetPaper);targetW=c.Width>=c.Height?p.Width:p.Height;targetH=c.Width>=c.Height?p.Height:p.Width;error=Math.Abs((c.Width/c.Height)/(targetW/targetH)-1);review=error>AspectTolerance;scale=Math.Min(targetW/c.Width,targetH/c.Height);}
            }
            if(!double.IsFinite(scale)||scale<=0)throw new ArgumentException("正規化比例無效。");
            return new(b,new(0,0,targetW,targetH),-(c.Origin?.X??b.MinX),-(c.Origin?.Y??b.MinY),scale,error,review,review?"比例不符，禁止拉伸；請複核外框或選擇保留比例的自訂寬度。":scale==1?"不縮放，只重新定位並還原外框方向。":"只使用單一等比例倍率；不拉伸，不改寫原 CAD。",c.Width,c.Height,c.Rotation);
        }
        public static DrawingBounds NormalizedGeometryBounds(CadTitleBlockAnalysis a,CadConversionSelection s)
        {
            var n=Normalize(Selected(a,s),s);var ids=SelectedGeometry(a,s).ToHashSet(StringComparer.Ordinal);
            var points=a.Geometry.Where(g=>ids.Contains(g.Id)).SelectMany(g=>g.Points.Length>0?g.Points:new[]{new DrawingPoint(g.Bounds.MinX,g.Bounds.MinY),new DrawingPoint(g.Bounds.MaxX,g.Bounds.MinY),new DrawingPoint(g.Bounds.MaxX,g.Bounds.MaxY),new DrawingPoint(g.Bounds.MinX,g.Bounds.MaxY)}).Select(p=>{double x=p.X+n.TranslationX,y=p.Y+n.TranslationY;return new DrawingPoint((x*Math.Cos(n.Rotation)+y*Math.Sin(n.Rotation))*n.UniformScale,(-x*Math.Sin(n.Rotation)+y*Math.Cos(n.Rotation))*n.UniformScale);}).ToArray();
            return new(points.Min(p=>p.X),points.Min(p=>p.Y),points.Max(p=>p.X),points.Max(p=>p.Y));
        }
    }
}
#endif
