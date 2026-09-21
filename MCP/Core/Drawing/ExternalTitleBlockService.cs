#if REVIT2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Autodesk.Revit.DB;

namespace RevitMCP.Core.Drawing
{
    internal static class TitleBlockFamilyTemplateResolver
    {
        private static string SettingsFile=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RevitMCP","drawing-titleblock-rft.txt");
        public static string Resolve(Autodesk.Revit.ApplicationServices.Application app,string selected)
        {
            if(!string.IsNullOrWhiteSpace(selected))return ValidatePath(selected);
            if(File.Exists(SettingsFile)&&File.Exists(File.ReadAllText(SettingsFile)))return ValidatePath(File.ReadAllText(SettingsFile));
            var root=app.FamilyTemplatePath;
            var candidates=Directory.Exists(root)?Directory.EnumerateFiles(root,"*.rft",SearchOption.AllDirectories).Where(p=>p.Contains("Titleblock",StringComparison.OrdinalIgnoreCase)||p.Contains("圖框")||p.Contains("图框")).ToArray():Array.Empty<string>();
            if(candidates.Length!=1)throw new ArgumentException("請選擇 Revit 圖框族樣板 .rft（只需設定一次）。");
            return candidates[0];
        }
        private static string ValidatePath(string path)
        {path=Path.GetFullPath(path);if(!File.Exists(path)||!path.EndsWith(".rft",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("圖框族樣板檔不存在或不是 .rft。");return path;}
        public static void Remember(string path){Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);File.WriteAllText(SettingsFile,ValidatePath(path));}
    }
    internal sealed class ExternalTitleBlockService
    {
        private readonly Document project;
        public ExternalTitleBlockService(Document project){this.project=project;}
        private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        private static void RequireTitleBlock(Document family)
        {if(!family.IsFamilyDocument||family.OwnerFamily.FamilyCategory?.Id.Value!=(long)BuiltInCategory.OST_TitleBlocks)throw new ArgumentException("此檔案不是 Revit 圖框 Family／圖框族樣板。");}
        private static View FamilyView(Document doc)=>new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().First(v=>!v.IsTemplate&&v.CanBePrinted&&v.ViewType!=ViewType.ThreeD);
        private static DrawingBounds Bounds(BoundingBoxXYZ box)=>new(box.Min.X,box.Min.Y,box.Max.X,box.Max.Y);
        private static DrawingBounds FamilyBounds(Document doc,View view)
        {
            var boxes=new FilteredElementCollector(doc,view.Id).WhereElementIsNotElementType().Where(e=>e is CurveElement||e is ImportInstance||e is FamilyInstance).Select(e=>e.get_BoundingBox(view)).Where(b=>b!=null).ToArray();
            if(boxes.Length==0)throw new ArgumentException("圖框沒有可驗證的幾何範圍。");
            var bounds=new DrawingBounds(boxes.Min(b=>b.Min.X),boxes.Min(b=>b.Min.Y),boxes.Max(b=>b.Max.X),boxes.Max(b=>b.Max.Y));
            if(!bounds.Valid)throw new ArgumentException("圖框幾何範圍無效。");return bounds;
        }
        private static ImportUnit Unit(string unit)=>unit switch{"Auto"=>ImportUnit.Default,"mm"=>ImportUnit.Millimeter,"cm"=>ImportUnit.Centimeter,"m"=>ImportUnit.Meter,"inch"=>ImportUnit.Inch,"ft"=>ImportUnit.Foot,_=>throw new ArgumentException("不支援此 CAD 單位。")};
        private static ImportInstance Import(Document family,string path,string unit)
        {
            var view=FamilyView(family);
            using var tx=new Transaction(family,"匯入圖框幾何");tx.Start();
            using var options=new DWGImportOptions{Unit=Unit(unit),Placement=ImportPlacement.Origin,ThisViewOnly=true,OrientToView=true};
            if(!family.Import(path,options,view,out var id))throw new InvalidOperationException("無法匯入 CAD 圖框。");
            family.Regenerate();var import=(ImportInstance)family.GetElement(id);
            if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("CAD 圖框匯入未提交。");return import;
        }
        private static void ConfigureTemplateGraphics(Document family,CadNormalizationAnalysis normalization)
        {
            // Revit protects the four inherited paper-boundary DetailLines. Retain them as
            // invisible paper guides at the confirmed size; never retain arbitrary RFT graphics.
            var elements=new FilteredElementCollector(family).WhereElementIsNotElementType().Where(e=>e is CurveElement or ImportInstance or FamilyInstance or TextElement or FilledRegion).ToArray();
            var predefined=elements.Where(e=>!DocumentValidation.CanDeleteElement(family,e.Id)).ToArray();
            if(predefined.Length!=0&&(predefined.Length!=4||predefined.Any(e=>e is not DetailCurve c||c.GeometryCurve is not Line)))throw new InvalidOperationException("族樣板有非紙張邊界的預定義圖形，不能安全轉換。");
            var guides=predefined.Cast<DetailCurve>().OrderBy(e=>e.Id.Value).ToArray();
            if(guides.Length>0)
            {
                var geometry=guides.Select(e=>{var curve=e.GeometryCurve;var p=curve.GetEndPoint(0);var q=curve.GetEndPoint(1);return new CadGeometry(e.Id.Value.ToString(),"RFT","Line",new(Math.Min(p.X,q.X)*304.8,Math.Min(p.Y,q.Y)*304.8,Math.Max(p.X,q.X)*304.8,Math.Max(p.Y,q.Y)*304.8),new[]{new DrawingPoint(p.X*304.8,p.Y*304.8),new DrawingPoint(q.X*304.8,q.Y*304.8)});}).ToArray();
                if(CadTitleBlockAnalyzer.Analyze(geometry,"mm",1).Candidates.Count!=1)throw new InvalidOperationException("族樣板的预定義邊界不是可驗證矩形。");
            }
            using var tx=new Transaction(family,"設定暫存圖框紙張範圍");tx.Start();
            var ids=elements.Except(predefined).Select(e=>e.Id).ToList();if(ids.Count>0)family.Delete(ids);
            if(guides.Length>0)
            {
                var invisible=guides[0].GetLineStyleIds().Select(id=>family.GetElement(id)).OfType<GraphicsStyle>().FirstOrDefault(style=>style.GraphicsStyleCategory.Id.Value==(long)BuiltInCategory.OST_InvisibleLines)??throw new InvalidOperationException("此圖框族樣板未提供不可見線樣式。");
                double w=normalization.SourceWidth*normalization.UniformScale/304.8,h=normalization.SourceHeight*normalization.UniformScale/304.8;
                var points=new[]{XYZ.Zero,new XYZ(w,0,0),new XYZ(w,h,0),new XYZ(0,h,0)};
                for(int i=0;i<guides.Length;i++){guides[i].SetGeometryCurve(Line.CreateBound(points[i],points[(i+1)%4]),true);guides[i].LineStyle=invisible;}
            }
            family.Regenerate();tx.Commit();
        }
        internal static string NativeCadGeometryHash(Document family)
        {
            var imports=new FilteredElementCollector(family).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToArray();
            if(imports.Length!=1)throw new InvalidOperationException("圖框必須只有一份已確認的 CAD 幾何。");
            var entries=new List<string>();int budget=0;
            string Point(XYZ p)=>FormattableString.Invariant($"{Math.Round(p.X,7):F7},{Math.Round(p.Y,7):F7},{Math.Round(p.Z,7):F7}");
            void Walk(GeometryElement geometry,int depth)
            {
                if(depth>16)throw new InvalidOperationException("圖框幾何深度超過驗證預算。");
                foreach(var g in geometry)
                {
                    if(++budget>100000)throw new InvalidOperationException("圖框幾何量超過驗證預算。");
                    if(g is GeometryInstance instance){Walk(instance.GetInstanceGeometry(),depth+1);continue;}
                    IEnumerable<XYZ>? points=g switch{Curve curve=>curve.Tessellate(),PolyLine poly=>poly.GetCoordinates(),Mesh mesh=>mesh.Vertices,Autodesk.Revit.DB.Point point=>new[]{point.Coord},_=>null};
                    if(points!=null)entries.Add(g.GetType().Name+":"+string.Join(";",points.Select(Point).OrderBy(x=>x,StringComparer.Ordinal)));
                    else if(g is Solid solid)foreach(Edge edge in solid.Edges)entries.Add("Edge:"+string.Join(";",edge.Tessellate().Select(Point).OrderBy(x=>x,StringComparer.Ordinal)));
                    else throw new InvalidOperationException("尚未驗證的 Revit CAD 幾何種類："+g.GetType().Name);
                }
            }
            using var options=new Options{IncludeNonVisibleObjects=true,View=FamilyView(family)};Walk(imports[0].get_Geometry(options)??throw new InvalidOperationException("無法讀取圖框視圖的 CAD 幾何。"),0);
            if(entries.Count==0)throw new InvalidOperationException("圖框沒有可讀回驗證的原生幾何。");
            return CadGeometryClusterService.StableId("N-",entries);
        }
        public ExternalTitleBlockAnalysis Analyze(string path,string unit,string rft)
        {
            path=Path.GetFullPath(path);string ext=Path.GetExtension(path).ToLowerInvariant();
            if(!File.Exists(path)||!new[]{".rfa",".dwg",".dxf"}.Contains(ext))throw new ArgumentException("請選擇存在的 RFA、DWG 或 DXF 圖框檔案。");
            string hash=Hash(path);bool cad=ext!=".rfa";
            if(project.Application.Documents.Cast<Document>().Any(d=>string.Equals(d.PathName,path,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("請先關閉圖框來源文件，再重新分析。");
            if(cad)rft=TitleBlockFamilyTemplateResolver.Resolve(project.Application,rft);
            if(cad)
            {
                // Analyze entities before any Revit import: global coordinates may be hundreds of km.
                var data=CadTitleBlockFileService.Analyze(path,unit);
                var template=project.Application.NewFamilyDocument(rft);try{RequireTitleBlock(template);}finally{template.Close(false);}
                if(Hash(path)!=hash)throw new InvalidOperationException("來源檔案在分析期間變更。");
                TitleBlockFamilyTemplateResolver.Remember(rft);
                var global=data.GlobalBounds;
                return new(){FilePath=path,FileHash=hash,FamilyName=Path.GetFileNameWithoutExtension(path),IsCad=true,Unit=unit,RftPath=rft,RftHash=Hash(rft),Cad=data,Types=new[]{"圖框"},Layers=data.Geometry.Select(g=>g.Layer).Distinct().OrderBy(n=>n,StringComparer.Ordinal).ToArray(),Bounds=new(global.MinX/304.8,global.MinY/304.8,global.MaxX/304.8,global.MaxY/304.8)};
            }
            Document? family=null;
            try
            {
                family=cad?project.Application.NewFamilyDocument(rft):project.Application.OpenDocumentFile(path);RequireTitleBlock(family);
                ImportInstance? import=cad?Import(family,path,unit):null;
                var view=FamilyView(family);
                var bounds=import==null?FamilyBounds(family,view):Bounds(import.get_BoundingBox(view)??throw new ArgumentException("CAD 無有效範圍。"));
                if(!bounds.Valid)throw new ArgumentException("圖框範圍無效。");
                var name=Path.GetFileNameWithoutExtension(path);
                var result=new ExternalTitleBlockAnalysis{FilePath=path,FileHash=hash,FamilyName=name,IsCad=cad,Unit=unit,RftPath=cad?rft:"",RftHash=cad?Hash(rft):"",Bounds=bounds,
                    Types=cad?new[]{"圖框"}:family.FamilyManager.Types.Cast<FamilyType>().Select(t=>t.Name).OrderBy(n=>n,StringComparer.Ordinal).ToArray(),
                    Parameters=family.FamilyManager.Parameters.Cast<FamilyParameter>().Select(p=>p.Definition.Name).OrderBy(n=>n,StringComparer.Ordinal).ToArray(),
                    Layers=import?.Category?.SubCategories?.Cast<Category>().Select(c=>c.Name).OrderBy(n=>n,StringComparer.Ordinal).ToArray()??Array.Empty<string>(),
                    ExistingFamily=new FilteredElementCollector(project).OfClass(typeof(Family)).Cast<Family>().Any(f=>f.Name==name)};
                if(result.Types.Length==0)result.Types=new[]{name};
                if(!cad)
                {
                    using var types=new Transaction(family,"分析圖框類型（不保存來源）");types.Start();
                    foreach(var type in family.FamilyManager.Types.Cast<FamilyType>())
                    {family.FamilyManager.CurrentType=type;family.Regenerate();result.TypeBounds[type.Name]=FamilyBounds(family,view);}
                    types.RollBack();
                    if(result.TypeBounds.TryGetValue(result.Types[0],out var firstBounds))result.Bounds=firstBounds;
                }
                family.Close(false);family=null;
                if(Hash(path)!=hash)throw new InvalidOperationException("來源檔案在分析期間變更。");
                if(cad)TitleBlockFamilyTemplateResolver.Remember(rft);return result;
            }
            finally{if(family!=null&&family.IsValidObject)family.Close(false);}
        }
        public SheetTemplateBlueprint Load(ExternalTitleBlockAnalysis analysis,string typeName,bool useExisting,bool confirmed)
        {
            if(!confirmed)throw new InvalidOperationException("載入圖框需明確確認。");
            if(Hash(analysis.FilePath)!=analysis.FileHash||analysis.IsCad&&Hash(analysis.RftPath)!=analysis.RftHash)throw new InvalidOperationException("來源檔或族樣板已變更，請重新分析。");
            CadNormalizationAnalysis? normalization=null;
            DrawingBounds? normalizedGeometry=null;
            string expectedNativeHash="";
            if(analysis.IsCad)
            {
                if(analysis.Cad==null||analysis.CadSelection==null)throw new InvalidOperationException("CAD 需重新分析並選擇圖框候選。");
                if(analysis.CadPreviewSignature!=CadTitleBlockAnalyzer.PreviewSignature(analysis)||!analysis.CadSelection.Previewed||!analysis.CadSelection.GeometryFilterConfirmed||!analysis.CadSelection.PurposeConfirmed)throw new InvalidOperationException("候選預覽或確認已失效，請重新確認。");
                normalization=CadTitleBlockAnalyzer.Normalize(CadTitleBlockAnalyzer.Selected(analysis.Cad,analysis.CadSelection),analysis.CadSelection);
                if(normalization.ReviewRequired)throw new InvalidOperationException(normalization.Explanation);
                normalizedGeometry=CadTitleBlockAnalyzer.NormalizedGeometryBounds(analysis.Cad,analysis.CadSelection);
                if(!XYZ.IsWithinLengthLimits(new XYZ(normalizedGeometry.MinX/304.8,normalizedGeometry.MinY/304.8,0))||!XYZ.IsWithinLengthLimits(new XYZ(normalizedGeometry.MaxX/304.8,normalizedGeometry.MaxY/304.8,0)))throw new InvalidOperationException("選取保留的幾何仍超出 Revit 座標範圍；請複核追加的範圍外物件或調整等比例目標。");
                if(!XYZ.IsWithinLengthLimits(new XYZ(normalization.SourceWidth*normalization.UniformScale/304.8,normalization.SourceHeight*normalization.UniformScale/304.8,0)))throw new InvalidOperationException("確認後的圖框仍超出 Revit 座標範圍；請先確認等比例正規化目標。");
                analysis.FamilyName="CAD_"+analysis.CadSelection.CandidateId+"_"+analysis.CadPreviewSignature;
            }
            var existing=new FilteredElementCollector(project).OfClass(typeof(Family)).Cast<Family>().SingleOrDefault(f=>f.Name==analysis.FamilyName);
            if(existing!=null&&!useExisting)throw new InvalidOperationException("同名 Family 已存在；請明確選擇使用專案版本或取消。");
            string temp=Path.Combine(Path.GetTempPath(),"RevitMCP-Drawing-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            Document? familyDoc=null;
            using var group=new TransactionGroup(project,"載入施工圖圖框");group.Start();
            try
            {
                string loadPath=analysis.FilePath;
                if(existing==null&&analysis.IsCad)
                {
                    var normalizedPath=Path.Combine(temp,"normalized.dxf");CadTitleBlockFileService.Export(analysis,normalizedPath);
                    familyDoc=project.Application.NewFamilyDocument(analysis.RftPath);RequireTitleBlock(familyDoc);
                    ConfigureTemplateGraphics(familyDoc,normalization!);var imported=Import(familyDoc,normalizedPath,"mm");
                    var importedBounds=Bounds(imported.get_BoundingBox(FamilyView(familyDoc))??throw new InvalidOperationException("CAD 載入後沒有幾何範圍。"));
                    double expectedW=normalizedGeometry!.Width,expectedH=normalizedGeometry.Height;
                    if(Math.Abs(importedBounds.Width*304.8-expectedW)>1||Math.Abs(importedBounds.Height*304.8-expectedH)>1||Math.Abs(importedBounds.MinX*304.8-normalizedGeometry.MinX)>1||Math.Abs(importedBounds.MinY*304.8-normalizedGeometry.MinY)>1)throw new InvalidOperationException($"正規化後實際圖框範圍與候選不符：實際 {importedBounds.Width*304.8:0.###} × {importedBounds.Height*304.8:0.###} mm；目標 {expectedW:0.###} × {expectedH:0.###} mm，請複核文字／字型與預覽。");
                    expectedNativeHash=NativeCadGeometryHash(familyDoc);
                    using(var tx=new Transaction(familyDoc,"圖框類型")){tx.Start();if(!familyDoc.FamilyManager.Types.Cast<FamilyType>().Any(t=>t.Name==typeName))familyDoc.FamilyManager.NewType(typeName);tx.Commit();}
                    loadPath=Path.Combine(temp,analysis.FamilyName+".rfa");familyDoc.SaveAs(loadPath,new SaveAsOptions{OverwriteExistingFile=false});familyDoc.Close(false);familyDoc=null;
                }
                if(existing==null)
                {
                    using var tx=new Transaction(project,"載入圖框 Family");tx.Start();
                    if(!project.LoadFamily(loadPath,new RejectReplacement(),out existing)||existing==null)throw new InvalidOperationException("圖框載入失敗；未替換既有 Family。");
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("圖框載入交易失敗。");
                }
                if(existing.FamilyCategory?.Id.Value!=(long)BuiltInCategory.OST_TitleBlocks)throw new InvalidOperationException("同名 Family 不是圖框。");
                var symbol=existing.GetFamilySymbolIds().Select(id=>(FamilySymbol)project.GetElement(id)).SingleOrDefault(s=>s.Name==typeName)??throw new InvalidOperationException("選定圖框類型不存在於載入版本。");
                SheetTemplateBlueprint blueprint;
                using(var probe=new Transaction(project,"量測圖框（不保留圖紙）"))
                {
                    probe.Start();var sheet=ViewSheet.Create(project,symbol.Id);project.Regenerate();
                    blueprint=new RevitDrawingService(project).Extract(sheet.Id.Value);probe.RollBack();
                }
                blueprint.SourceKind=analysis.IsCad?TemplateSourceKind.Cad:TemplateSourceKind.ExternalRfa;blueprint.SourceSheetId=0;blueprint.SourceSheetUniqueId="";blueprint.SourceSheetNumber="";blueprint.SourceSheetName="";
                blueprint.TitleBlockTypeUniqueId=symbol.UniqueId;
                if(analysis.IsCad)
                {
                    blueprint.CadSourceHash=analysis.FileHash;blueprint.CadCandidateId=analysis.CadSelection!.CandidateId;
                    blueprint.CadGeometryIds=CadTitleBlockAnalyzer.SelectedGeometry(analysis.Cad!,analysis.CadSelection);blueprint.CadNormalization=normalization;
                    var checkFamily=project.EditFamily(existing);try{blueprint.CadNativeGeometryHash=NativeCadGeometryHash(checkFamily);}finally{checkFamily.Close(false);}
                    if(expectedNativeHash!=""&&blueprint.CadNativeGeometryHash!=expectedNativeHash)throw new InvalidOperationException("圖框載入前後原生幾何指紋不一致。");
                    var actualBounds=blueprint.TitleBlockBounds;
                    var paper=normalization!.TargetBounds;var combined=CadGeometryClusterService.Union(new[]{paper,normalizedGeometry!});
                    if(Math.Abs(actualBounds.Width*304.8-combined.Width)>1||Math.Abs(actualBounds.Height*304.8-combined.Height)>1)throw new InvalidOperationException("載入圖框的尺寸 read-back 不符合確認候選與保留集合。");
                    blueprint.CadGeometryBoundsMm=normalizedGeometry;
                    blueprint.TitleBlockBounds=new(paper.MinX/304.8,paper.MinY/304.8,paper.MaxX/304.8,paper.MaxY/304.8);
                    if(!CadGeometryClusterService.Contains(paper,normalizedGeometry!,1))blueprint.Warnings.Add("使用者確認保留的 CAD 幾何超出所選紙張外框；請人工複核列印裁切。");
                }
                var b=blueprint.TitleBlockBounds;
                blueprint.Viewports=new(){new(){Role="MAIN_PLAN",ExpectedViewKind="FloorPlan",ViewportTypeId=project.GetDefaultElementTypeId(ElementTypeGroup.ViewportType).Value,ViewTemplateId=-1,Scale=100,AbsoluteX=(b.MinX+b.MaxX)/2,AbsoluteY=(b.MinY+b.MaxY)/2,Bounds=new((b.MinX+b.MaxX)/2-.001,(b.MinY+b.MaxY)/2-.001,(b.MinX+b.MaxX)/2+.001,(b.MinY+b.MaxY)/2+.001),DetailNumber="1"}};
                blueprint.SheetParameterCopyPolicy.Clear();
                if(analysis.IsCad)blueprint.Warnings.Add("CAD 圖框僅為幾何；文字未自動轉為 Revit 圖號／圖名 Label，亦未建立 Revision 或公司參數。");
                if(project.GetElement(symbol.Id) is not FamilySymbol read||read.Category.Id.Value!=(long)BuiltInCategory.OST_TitleBlocks||!b.Valid)throw new InvalidOperationException("圖框 read-back 失敗。");
                group.Assimilate();return blueprint;
            }
            catch{if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}
            finally
            {
                if(familyDoc!=null&&familyDoc.IsValidObject)familyDoc.Close(false);
                // This directory is created by this operation, never derived from a source file path.
                if(Directory.Exists(temp))Directory.Delete(temp,true);
            }
        }
        private sealed class RejectReplacement : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse,out bool overwriteParameterValues){overwriteParameterValues=false;return false;}
            public bool OnSharedFamilyFound(Family sharedFamily,bool familyInUse,out FamilySource source,out bool overwriteParameterValues){source=FamilySource.Project;overwriteParameterValues=false;return false;}
        }
    }
}
#endif
