#if REVIT2026 || CAD_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using Line=ACadSharp.Entities.Line;
using Arc=ACadSharp.Entities.Arc;
using Ellipse=ACadSharp.Entities.Ellipse;
using XYZ=CSMath.XYZ;

namespace RevitMCP.Core.Drawing
{
    /// <summary>Local managed CAD reader. Never writes a source drawing; all exports are new isolated files.</summary>
    public static class CadTitleBlockFileService
    {
        private sealed record ReadResult(CadDocument Document,Dictionary<string,Entity> Entities,double Factor,string Unit,string[] Warnings,IReadOnlyList<CadBlockInstance> Blocks);
        private static ReadResult Read(string path,string unit)
        {
            if(new FileInfo(path).Length>100*1024*1024)throw new ArgumentException("CAD 圖框超過 100 MB，請縮小來源；不截斷分析。");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var warnings=new List<string>();bool incomplete=false;
            void Notification(object sender,NotificationEventArgs e)
            {
                // Raw notification text may contain company text or paths: never persist it.
                if(e.NotificationType is NotificationType.Error or NotificationType.NotSupported or NotificationType.NotImplemented)incomplete=true;
                if(e.NotificationType!=NotificationType.None)warnings.Add("CAD_READER_"+e.NotificationType);
            }
            CadDocument document;
            if(Path.GetExtension(path).Equals(".dwg",StringComparison.OrdinalIgnoreCase)){using var reader=new DwgReader(path);reader.OnNotification+=Notification;document=reader.Read();}
            else {using var reader=new DxfReader(path);reader.OnNotification+=Notification;document=reader.Read();}
            if(incomplete)throw new InvalidOperationException("CAD 含讀取器尚不支援的資料；不能保證完整保留，已停止轉換。");
            double factor=unit switch{"mm"=>1,"cm"=>10,"m"=>1000,"inch"=>25.4,"ft"=>304.8,"Auto"=>(int)document.Header.InsUnits switch{1=>25.4,2=>304.8,4=>1,5=>10,6=>1000,14=>100,_=>throw new ArgumentException("CAD 未定義可靠單位，請明確選擇匯入單位。")},_=>throw new ArgumentException("CAD 單位不支援。")};
            var entities=new Dictionary<string,Entity>(StringComparer.Ordinal);
            var blocks=new List<CadBlockInstance>();
            void Add(Entity e,string id,int depth)
            {
                if(depth>16||entities.Count>=CadGeometryClusterService.MaxGeometry)throw new ArgumentException("CAD 圖塊深度／幾何量超過分析預算，未截斷資料。");
                if(e is Insert insert)
                {if(insert.IsMultiple||insert.HasAttributes||!string.IsNullOrEmpty(insert.Block.BlockEntity.XRefPath))throw new ArgumentException("陣列圖塊／帶實例屬性的圖塊／外部參考需先人工複核，不能忽略其內容。");int index=0,before=entities.Count;foreach(var child in insert.Explode()){if(child is TextEntity text&&insert.Block.Entities.ElementAt(index) is TextEntity original)text.AlignmentPoint=insert.GetTransform().ApplyTransform(original.AlignmentPoint);Add(child,id+"/"+(index++).ToString("D6"),depth+1);}var b=insert.GetBoundingBox();blocks.Add(new(id,new(b.Min.X*factor,b.Min.Y*factor,b.Max.X*factor,b.Max.Y*factor),entities.Count-before));return;}
                if(e is not (Line or LwPolyline or Polyline2D or TextEntity or MText or ACadSharp.Entities.Point or Circle or Arc or Ellipse or Spline or Hatch))throw new ArgumentException("CAD 包含尚未驗證保留的幾何種類："+e.GetType().Name);
                if(e is TextEntity t&&(Math.Abs(t.Normal.X)>1e-8||Math.Abs(t.Normal.Y)>1e-8||Math.Abs(t.Normal.Z-1)>1e-8))throw new ArgumentException("非 XY 平面的 CAD 文字需人工複核，不能忽略 OCS 座標。");
                entities.Add(id,e);
            }
            foreach(var entity in document.Entities.OrderBy(e=>e.Handle))Add(entity,entity.Handle.ToString("X"),0);
            return new(document,entities,factor,unit=="Auto"?$"INSUNITS {(int)document.Header.InsUnits}（每單位 {factor} mm）":unit,warnings.Distinct().ToArray(),blocks);
        }
        private static CadGeometry Geometry(string id,Entity e,double factor)
        {
            var box=e.GetBoundingBox();var bounds=new DrawingBounds(box.Min.X*factor,box.Min.Y*factor,box.Max.X*factor,box.Max.Y*factor);
            DrawingPoint P(double x,double y)=>new(x*factor,y*factor);
            DrawingPoint[] points=e switch{Line l=>new[]{P(l.StartPoint.X,l.StartPoint.Y),P(l.EndPoint.X,l.EndPoint.Y)},LwPolyline p when p.Vertices.All(v=>Math.Abs(v.Bulge)<1e-12)=>p.Vertices.Select(v=>P(v.Location.X,v.Location.Y)).ToArray(),Polyline2D p=>p.Vertices.Select(v=>P(v.Location.X,v.Location.Y)).ToArray(),_=>Array.Empty<DrawingPoint>()};
            string value=e is TextEntity t?t.Value:e is MText mt?mt.Value:"";
            bool construction=value.Contains("施工圖",StringComparison.Ordinal),asbuilt=value.Contains("竣工圖",StringComparison.Ordinal);
            return new(id,e.Layer.Name,e.GetType().Name,bounds,points,e is LwPolyline lw&&lw.IsClosed||e is Polyline2D poly&&poly.IsClosed,construction==asbuilt?TitleBlockPurpose.Custom:construction?TitleBlockPurpose.ConstructionDrawing:TitleBlockPurpose.AsBuiltDrawing);
        }
        public static CadTitleBlockAnalysis Analyze(string path,string unit)
        {var read=Read(path,unit);var result=CadTitleBlockAnalyzer.Analyze(read.Entities.Select(kv=>Geometry(kv.Key,kv.Value,read.Factor)).ToArray(),read.Unit,read.Factor,read.Warnings.Concat(new[]{"CAD 文字按插入點歸屬候選；文字字型與完整外觀仍須以 Revit 匯入後預覽／read-back 複核。"}).ToArray());result.BlockInstances=read.Blocks;return result;}
        public static void Export(ExternalTitleBlockAnalysis a,string destination)
        {
            var s=a.CadSelection??throw new ArgumentException("請先選擇圖框候選。");
            if(!s.Previewed||!s.GeometryFilterConfirmed||!s.PurposeConfirmed||string.IsNullOrWhiteSpace(s.ProfileName)||a.CadPreviewSignature!=CadTitleBlockAnalyzer.PreviewSignature(a))throw new InvalidOperationException("請逐一預覽候選，確認用途、名稱、幾何保留範圍與正規化；設定變更後須重新預覽。");
            if(File.Exists(destination)||Path.GetFullPath(destination).Equals(Path.GetFullPath(a.FilePath),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("CAD 輸出必須是新的隔離檔案。");
            string HashSource()=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.FilePath)));
            if(HashSource()!=a.FileHash)throw new InvalidOperationException("來源 CAD 已變更，請重新分析。");
            var c=CadTitleBlockAnalyzer.Selected(a.Cad!,s);var n=CadTitleBlockAnalyzer.Normalize(c,s);if(n.ReviewRequired)throw new InvalidOperationException(n.Explanation);
            var selected=CadTitleBlockAnalyzer.SelectedGeometry(a.Cad!,s);var source=Read(a.FilePath,a.Unit);var target=new CadDocument();target.Header.InsUnits=ACadSharp.Types.Units.UnitsType.Millimeters;
            foreach(var id in selected)
            {
                if(!source.Entities.TryGetValue(id,out var entity))throw new InvalidOperationException("CAD 幾何已變更，請重新分析。");
                var clone=(Entity)entity.Clone();clone.ApplyTranslation(new XYZ(n.TranslationX/source.Factor,n.TranslationY/source.Factor,0));if(Math.Abs(n.Rotation)>1e-12)clone.ApplyRotation(new XYZ(0,0,1),-n.Rotation);clone.ApplyScaling(new XYZ(source.Factor*n.UniformScale,source.Factor*n.UniformScale,source.Factor*n.UniformScale));// ACadSharp 3.7.16 transforms InsertPoint but leaves TextEntity.AlignmentPoint unchanged.
                // Always derive the second OCS point from the source, including ATTDEF; never double-transform.
                if(entity is TextEntity originalText&&clone is TextEntity transformedText)
                {
                    var point=originalText.AlignmentPoint;double x=point.X*source.Factor+n.TranslationX,y=point.Y*source.Factor+n.TranslationY;
                    transformedText.AlignmentPoint=new XYZ((x*Math.Cos(n.Rotation)+y*Math.Sin(n.Rotation))*n.UniformScale,(-x*Math.Sin(n.Rotation)+y*Math.Cos(n.Rotation))*n.UniformScale,point.Z*source.Factor*n.UniformScale);
                }
                target.Entities.Add(clone);
            }
            DxfWriter.Write(destination,target,false);
            // Verify that the serialized CAD preserves every selected entity and all text values in memory only.
            using var reader=new DxfReader(destination);var check=reader.Read();
            string Text(Entity e)=>e is TextEntity t?t.Value:e is MText mt?mt.Value:"";
            var expected=target.Entities.Select(e=>e.GetType().Name+":"+Text(e)).OrderBy(v=>v,StringComparer.Ordinal).ToArray();
            var actual=check.Entities.Select(e=>e.GetType().Name+":"+Text(e)).OrderBy(v=>v,StringComparer.Ordinal).ToArray();
            if(!expected.SequenceEqual(actual))throw new InvalidOperationException("正規化 CAD 的幾何／文字序列化 read-back 不一致。");
            if(check.Entities.Count!=selected.Length)throw new InvalidOperationException("候選幾何隔離數量 read-back 不一致。");
            var expectedEntities=target.Entities.ToArray();var actualEntities=check.Entities.ToArray();
            for(int index=0;index<expectedEntities.Length;index++)
            {
                var before=expectedEntities[index].GetBoundingBox();var after=actualEntities[index].GetBoundingBox();
                bool Different(XYZ p,XYZ q)=>Math.Abs(p.X-q.X)>1e-5||Math.Abs(p.Y-q.Y)>1e-5||Math.Abs(p.Z-q.Z)>1e-5;
                if(Different(before.Min,after.Min)||Different(before.Max,after.Max))throw new InvalidOperationException("正規化 CAD 幾何座標 read-back 不一致。");
                if(expectedEntities[index] is TextEntity et&&actualEntities[index] is TextEntity at&&(Different(et.AlignmentPoint,at.AlignmentPoint)||Different(et.InsertPoint,at.InsertPoint)||et.HorizontalAlignment!=at.HorizontalAlignment||et.VerticalAlignment!=at.VerticalAlignment||Math.Abs(et.Height-at.Height)>1e-8||Math.Abs(et.WidthFactor-at.WidthFactor)>1e-8||et.Style.Name!=at.Style.Name))throw new InvalidOperationException("CAD 文字樣式／尺度 read-back 不一致。");
            }
            if(HashSource()!=a.FileHash)throw new InvalidOperationException("來源 CAD 在轉換期間變更。");
        }
    }
}
#endif

