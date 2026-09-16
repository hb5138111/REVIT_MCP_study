#if REVIT2026 || SITE_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RevitMCP.Core.Site
{
    public record SitePoint(double X, double Y, double Z)
    {
        public double Distance(SitePoint p) => Math.Sqrt(Math.Pow(X-p.X,2)+Math.Pow(Y-p.Y,2)+Math.Pow(Z-p.Z,2));
        public bool Finite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    }
    public record TerrainPointRow(int Line, string Id, SitePoint Point, string Code);
    public record TerrainDiagnostic(int Line, string Code, string Message);
    public record TerrainBounds(SitePoint Min, SitePoint Max)
    {
        public static TerrainBounds Of(IEnumerable<SitePoint> points)
        {
            var p=points.ToArray();
            if(p.Length==0) throw new ArgumentException("沒有有效測量點。");
            return new(new(p.Min(a=>a.X),p.Min(a=>a.Y),p.Min(a=>a.Z)),new(p.Max(a=>a.X),p.Max(a=>a.Y),p.Max(a=>a.Z)));
        }
        public double Area => (Max.X-Min.X)*(Max.Y-Min.Y);
    }
    public sealed class TerrainImportDiagnostics
    {
        public int InputCount {get;set;}
        public int ValidCount {get;set;}
        public int RejectedCount {get;set;}
        public int DuplicateCount {get;set;}
        public int ConflictCount {get;set;}
        public int WarningCount => Warnings.Count;
        public TerrainBounds Bounds {get;set;} = new(new(0,0,0),new(0,0,0));
        public double[] ElevationRange => new[]{Bounds.Min.Z,Bounds.Max.Z};
        public double Density => Bounds.Area>0 ? ValidCount/Bounds.Area : 0;
        public List<TerrainDiagnostic> Warnings {get;}=new();
    }
    public sealed class TerrainPointDataset
    {
        public TerrainSourceKind SourceKind {get;set;}=TerrainSourceKind.DelimitedPointFile;
        public TerrainCoordinateBasis CoordinateBasis {get;set;}=TerrainCoordinateBasis.SourceCoordinates;
        public object? Provenance {get;set;}
        public string SourceName {get;set;}="";
        public string SourceSHA256 {get;set;}="";
        public string Units {get;set;}="m";
        public List<TerrainPointRow> Points {get;}=new();
        public TerrainImportDiagnostics Diagnostics {get;}=new();
    }
    public record PointImportOptions(char Delimiter, bool Header, int X, int Y, int Z, int Id=-1, int Code=-1, string Units="m");
    public static class TerrainPointParser
    {
        public static TerrainPointDataset Read(string path, PointImportOptions options)
        {
            if(!new[]{".csv",".txt"}.Contains(Path.GetExtension(path).ToLowerInvariant())) throw new ArgumentException("只支援 CSV / TXT。");
            // Hash and parse the same immutable byte snapshot, not two independent reads.
            var bytes=File.ReadAllBytes(path);
            var result=Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'),options);
            result.SourceName=Path.GetFileName(path);
            result.SourceSHA256=Convert.ToHexString(SHA256.HashData(bytes));
            return result;
        }
        public static TerrainPointDataset Parse(string text, PointImportOptions o)
        {
            double factor=o.Units switch {"m"=>1,"mm"=>0.001,"ft"=>0.3048,_=>throw new ArgumentException("請指定 m/mm/ft。")};
            if(new[]{o.X,o.Y,o.Z}.Distinct().Count()!=3 || Math.Min(o.X,Math.Min(o.Y,o.Z))<0) throw new ArgumentException("X/Y/Z 欄位必須不同且有效。");
            if(!new[]{',',';','\t',' '}.Contains(o.Delimiter)) throw new ArgumentException("不支援的分隔符。");
            var result=new TerrainPointDataset{Units=o.Units,Provenance=o};
            var d=result.Diagnostics;
            var xy=new Dictionary<(double,double),TerrainPointRow>();
            using var reader=new StringReader(text);
            string? line; int n=0; bool header=o.Header;
            while((line=reader.ReadLine())!=null)
            {
                n++;
                if(string.IsNullOrWhiteSpace(line)) continue;
                if(header){header=false;continue;}
                d.InputCount++;
                string[] fields;
                try { fields=Split(line,o.Delimiter); }
                catch(ArgumentException e){Reject("INVALID_QUOTING",e.Message);continue;}
                string Field(int i)=>i>=0 && i<fields.Length?fields[i].Trim():"";
                var raw=new[]{Field(o.X),Field(o.Y),Field(o.Z)};
                if(raw.Any(string.IsNullOrEmpty)){Reject("MISSING_XYZ","缺少 X/Y/Z");continue;}
                var values=new double[3];
                if(Enumerable.Range(0,3).Any(i=>!double.TryParse(raw[i],NumberStyles.Float,CultureInfo.InvariantCulture,out values[i]) || !double.IsFinite(values[i]*factor)))
                {Reject("INVALID_NUMERIC","X/Y/Z 必須為有限數值（小數點 .）。");continue;}
                var p=new SitePoint(values[0]*factor,values[1]*factor,values[2]*factor);
                var row=new TerrainPointRow(n,Field(o.Id),p,Field(o.Code));
                if(xy.TryGetValue((p.X,p.Y),out var previous))
                {
                    d.DuplicateCount++;
                    if(previous.Point.Z!=p.Z){d.ConflictCount++;d.Warnings.Add(new(n,"DUPLICATE_XY_CONFLICT","相同 XY 不同 Z；阻擋建立"));}
                    else d.Warnings.Add(new(n,"DUPLICATE_XYZ_SAME_XY_Z","XYZ 重複；保留第一筆"));
                    continue;
                }
                xy[(p.X,p.Y)]=row;result.Points.Add(row);
                void Reject(string code,string message){d.RejectedCount++;d.Warnings.Add(new(n,code,message));}
            }
            d.ValidCount=result.Points.Count;
            if(d.ValidCount>0)
            {
                d.Bounds=TerrainBounds.Of(result.Points.Select(p=>p.Point));
                var zs=result.Points.Select(p=>p.Point.Z).OrderBy(z=>z).ToArray();
                double median=zs[zs.Length/2],mad=zs.Select(z=>Math.Abs(z-median)).OrderBy(z=>z).ElementAt(zs.Length/2);
                // Statistical flags only; no automatic deletion or company-standard claim.
                foreach(var p in result.Points.Where(p=>Math.Abs(p.Point.Z-median)>Math.Max(10*mad,1)))
                    d.Warnings.Add(new(p.Line,"EXTREME_OUTLIER","高程偏離中位數；tool heuristic，請複核"));
                var steps=new[]{result.Points.Select(p=>p.Point.X),result.Points.Select(p=>p.Point.Y)}
                    .SelectMany(axis=>{var sorted=axis.Distinct().OrderBy(v=>v).ToArray();return sorted.Skip(1).Select((v,i)=>v-sorted[i]);}).Where(v=>v>0).OrderBy(v=>v).ToArray();
                double cell=steps.Length>0?steps[(steps.Length-1)/2]*3:0;
                if(cell>0)
                {
                    var buckets=result.Points.GroupBy(p=>((long)Math.Floor((p.Point.X-d.Bounds.Min.X)/cell),(long)Math.Floor((p.Point.Y-d.Bounds.Min.Y)/cell)))
                        .ToDictionary(g=>g.Key,g=>g.Count());
                    foreach(var p in result.Points)
                    {
                        long x=(long)Math.Floor((p.Point.X-d.Bounds.Min.X)/cell),y=(long)Math.Floor((p.Point.Y-d.Bounds.Min.Y)/cell);int neighbors=0;
                        for(int a=-1;a<=1;a++) for(int b=-1;b<=1;b++) if(buckets.TryGetValue((x+a,y+b),out int count))neighbors+=count;
                        if(neighbors==1)d.Warnings.Add(new(p.Line,"ISOLATED_POINT","鄰近網格無其他點；tool heuristic，請複核"));
                    }
                }
            }
            return result;
        }
        public static string[] Split(string line,char delimiter)
        {
            var fields=new List<string>();var field=new StringBuilder();bool quote=false;
            for(int i=0;i<line.Length;i++)
            {
                char c=line[i];
                if(c=='"'){if(quote && i+1<line.Length && line[i+1]=='"'){field.Append('"');i++;}else quote=!quote;}
                else if(!quote && (c==delimiter || delimiter==' ' && char.IsWhiteSpace(c)))
                {if(delimiter!=' ' || field.Length>0){fields.Add(field.ToString());field.Clear();}}
                else field.Append(c);
            }
            if(quote)throw new ArgumentException("引號未成對。");
            if(delimiter!=' ' || field.Length>0)fields.Add(field.ToString());return fields.ToArray();
        }
    }
    public record SiteTransform(double DeltaX,double DeltaY,double DeltaZ,double Rotation)
    {
        public double Scale=>1;
        public SitePoint Apply(SitePoint p)=>new(Math.Cos(Rotation)*p.X-Math.Sin(Rotation)*p.Y+DeltaX,Math.Sin(Rotation)*p.X+Math.Cos(Rotation)*p.Y+DeltaY,p.Z+DeltaZ);
        public SitePoint Inverse(SitePoint p)
        {double x=p.X-DeltaX,y=p.Y-DeltaY;return new(Math.Cos(Rotation)*x+Math.Sin(Rotation)*y,-Math.Sin(Rotation)*x+Math.Cos(Rotation)*y,p.Z-DeltaZ);}
    }
    public record SiteControl(SitePoint Survey,SitePoint Internal);
    public record AlignmentResult(SiteTransform Transform,double RMSResidual,double MaxResidual,double[] Residuals);
    public static class ControlPointAlignment
    {
        public static AlignmentResult Solve(IReadOnlyList<SiteControl> controls)
        {
            if(controls.Count==0 || controls.Any(c=>!c.Survey.Finite || !c.Internal.Finite))throw new ArgumentException("請提供有效控制點。");
            double sx=controls.Average(c=>c.Survey.X),sy=controls.Average(c=>c.Survey.Y),tx=controls.Average(c=>c.Internal.X),ty=controls.Average(c=>c.Internal.Y);
            double a=0,b=0,spread=0;
            foreach(var c in controls){double x=c.Survey.X-sx,y=c.Survey.Y-sy,u=c.Internal.X-tx,v=c.Internal.Y-ty;a+=x*u+y*v;b+=x*v-y*u;spread+=x*x+y*y;}
            if(controls.Count>1 && (spread<1e-16 || a*a+b*b<1e-24))throw new ArgumentException("控制點退化，不能決定旋轉。");
            double angle=controls.Count==1?0:Math.Atan2(b,a);
            var t=new SiteTransform(tx-Math.Cos(angle)*sx+Math.Sin(angle)*sy,ty-Math.Sin(angle)*sx-Math.Cos(angle)*sy,controls.Average(c=>c.Internal.Z-c.Survey.Z),angle);
            var residuals=controls.Select(c=>t.Apply(c.Survey).Distance(c.Internal)).ToArray();
            return new(t,Math.Sqrt(residuals.Average(r=>r*r)),residuals.Max(),residuals);
        }
    }
    public record SimplificationResult(IReadOnlyList<TerrainPointRow> Points,int OriginalPointCount,double MeanVerticalError,double MaxVerticalError)
    {
        public int FinalPointCount=>Points.Count;
        public double ReductionPercent=>OriginalPointCount==0?0:100.0*(OriginalPointCount-Points.Count)/OriginalPointCount;
        public string ErrorMethod=>"Conservative removed-point cell elevation envelope; not a final-TIN interpolation certification";
    }
    public static class TerrainSimplifier
    {
        public static SimplificationResult Reduce(IReadOnlyList<TerrainPointRow> input,double grid,double tolerance)
        {
            if(input.Count<3)throw new ArgumentException("至少需要三個有效點。");
            if(grid==0)return new(input.ToArray(),input.Count,0,0);
            if(!double.IsFinite(grid)||grid<0||!double.IsFinite(tolerance)||tolerance<0)throw new ArgumentException("減點 tolerance 必須有效。");
            var sorted=input.OrderBy(p=>p.Point.X).ThenBy(p=>p.Point.Y).ToArray();
            double Cross(TerrainPointRow a,TerrainPointRow b,TerrainPointRow c)=>(b.Point.X-a.Point.X)*(c.Point.Y-a.Point.Y)-(b.Point.Y-a.Point.Y)*(c.Point.X-a.Point.X);
            var hull=new List<TerrainPointRow>();
            foreach(var p in sorted){while(hull.Count>=2 && Cross(hull[^2],hull[^1],p)<0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
            int lower=hull.Count;
            foreach(var p in Enumerable.Reverse(sorted)){while(hull.Count>lower && Cross(hull[^2],hull[^1],p)<0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
            var keep=new HashSet<int>(hull.Select(p=>p.Line));double sum=0,max=0;int removed=0;
            var bounds=TerrainBounds.Of(input.Select(p=>p.Point));
            foreach(var group in sorted.GroupBy(p=>(Math.Floor((p.Point.X-bounds.Min.X)/grid),Math.Floor((p.Point.Y-bounds.Min.Y)/grid))))
            {
                var p=group.OrderBy(p=>p.Point.Z).ThenBy(p=>p.Line).ToArray();double span=p[^1].Point.Z-p[0].Point.Z;
                keep.Add(p[0].Line);keep.Add(p[^1].Line);
                foreach(var row in p)
                {
                    if(span>tolerance || !string.IsNullOrWhiteSpace(row.Code))keep.Add(row.Line);
                    if(!keep.Contains(row.Line)){sum+=span;max=Math.Max(max,span);removed++;}
                }
            }
            return new(sorted.Where(p=>keep.Contains(p.Line)).ToArray(),input.Count,removed>0?sum/removed:0,max);
        }
    }
    public record TerrainTriangle(SitePoint A,SitePoint B,SitePoint C);
    public sealed class EarthworkResult
    {
        public double Area {get;set;}
        public double CutVolume {get;set;}
        public double FillVolume {get;set;}
        public double NetVolume=>FillVolume-CutVolume;
        public double CutArea {get;set;}
        public double FillArea {get;set;}
        public double MinDepth {get;set;}=double.PositiveInfinity;
        public double MaxDepth {get;set;}=double.NegativeInfinity;
        public double AverageCutDepth=>CutArea>0?CutVolume/CutArea:0;
        public double AverageFillDepth=>FillArea>0?FillVolume/FillArea:0;
        public string CalculationMethod {get;set;}="TIN clipped triangles; linear depth integration, SI m/m²/m³";
        public string BoundarySource {get;set;}="Explicit convex polygon in internal axes, metres";
        public long ExistingTerrainId {get;set;}
        public string TargetSource {get;set;}="";
        public string CoordinateSystem {get;set;}="Internal axes / metres";
        public double Tolerance {get;set;}
        public double CoverageRelativeTolerance=>1e-7;
        public List<string> Warnings {get;}=new();
    }
    public static class EarthworkEngine
    {
        public static EarthworkResult Calculate(IEnumerable<TerrainTriangle> triangles,IReadOnlyList<SitePoint> boundary,double target,double tolerance)
        {
            if(!double.IsFinite(target)||!double.IsFinite(tolerance)||tolerance<=0||boundary.Count<3||boundary.Any(p=>!p.Finite))throw new ArgumentException("請提供有效 boundary、target 及 tolerance。");
            var b=boundary.ToList();
            double Cross(SitePoint a,SitePoint v,SitePoint p)=>(v.X-a.X)*(p.Y-a.Y)-(v.Y-a.Y)*(p.X-a.X);
            double signed=Enumerable.Range(0,b.Count).Sum(i=>b[i].X*b[(i+1)%b.Count].Y-b[(i+1)%b.Count].X*b[i].Y)/2;
            if(Math.Abs(signed)<1e-12)throw new ArgumentException("Boundary 面積為零。");
            if(signed<0)b.Reverse();
            for(int i=0;i<b.Count;i++)for(int j=0;j<b.Count;j++)if(Cross(b[i],b[(i+1)%b.Count],b[j])< -1e-10)throw new ArgumentException("本版精準模式僅支援凸 boundary，不能近似凹多邊形。");
            var result=new EarthworkResult{TargetSource="Elevation "+target.ToString("R",CultureInfo.InvariantCulture)+" m",Tolerance=tolerance};
            foreach(var triangle in triangles)
            {
                if(!triangle.A.Finite||!triangle.B.Finite||!triangle.C.Finite)throw new ArgumentException("TIN 含無效座標。");
                var p=new List<SitePoint>{triangle.A,triangle.B,triangle.C};
                for(int i=0;i<b.Count && p.Count>0;i++){var a=b[i];var v=b[(i+1)%b.Count];p=Clip(p,q=>Cross(a,v,q));}
                if(p.Count<3)continue;
                foreach(var q in p){result.MinDepth=Math.Min(result.MinDepth,q.Z-target);result.MaxDepth=Math.Max(result.MaxDepth,q.Z-target);}
                result.Area+=Integrate(p,target).area;
                var cut=Integrate(Clip(p,q=>q.Z-target),target);var fill=Integrate(Clip(p,q=>target-q.Z),target);
                if(cut.volume>1e-12){result.CutArea+=cut.area;result.CutVolume+=cut.volume;}
                if(fill.volume< -1e-12){result.FillArea+=fill.area;result.FillVolume-=fill.volume;}
            }
            if(result.Area<=0)throw new ArgumentException("Boundary 與 terrain 沒有覆蓋。");
            if(Math.Abs(result.Area-Math.Abs(signed))>result.CoverageRelativeTolerance*Math.Max(1,Math.Abs(signed)))throw new InvalidOperationException("TIN 未完整覆蓋 boundary 或有重疊；不輸出精準數量。覆蓋檢查不受控制點 tolerance 放寬。");
            return result;
        }
        private static List<SitePoint> Clip(List<SitePoint> polygon,Func<SitePoint,double> distance)
        {
            var result=new List<SitePoint>();if(polygon.Count==0)return result;
            var a=polygon[^1];double da=distance(a);
            foreach(var b in polygon){double db=distance(b);if((da>=0)!=(db>=0)){double t=da/(da-db);result.Add(new(a.X+t*(b.X-a.X),a.Y+t*(b.Y-a.Y),a.Z+t*(b.Z-a.Z)));}if(db>=0)result.Add(b);a=b;da=db;}return result;
        }
        private static (double area,double volume) Integrate(List<SitePoint> p,double target)
        {
            double area=0,volume=0;
            for(int i=1;i+1<p.Count;i++){double a=Math.Abs((p[i].X-p[0].X)*(p[i+1].Y-p[0].Y)-(p[i].Y-p[0].Y)*(p[i+1].X-p[0].X))/2;area+=a;volume+=a*((p[0].Z-target)+(p[i].Z-target)+(p[i+1].Z-target))/3;}return(area,volume);
        }
    }
}
#endif
