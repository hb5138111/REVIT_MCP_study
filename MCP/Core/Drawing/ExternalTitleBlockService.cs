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
        public ExternalTitleBlockAnalysis Analyze(string path,string unit,string rft)
        {
            path=Path.GetFullPath(path);string ext=Path.GetExtension(path).ToLowerInvariant();
            if(!File.Exists(path)||!new[]{".rfa",".dwg",".dxf"}.Contains(ext))throw new ArgumentException("請選擇存在的 RFA、DWG 或 DXF 圖框檔案。");
            string hash=Hash(path);bool cad=ext!=".rfa";
            if(project.Application.Documents.Cast<Document>().Any(d=>string.Equals(d.PathName,path,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("請先關閉圖框來源文件，再重新分析。");
            if(cad)rft=TitleBlockFamilyTemplateResolver.Resolve(project.Application,rft);
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
                    familyDoc=project.Application.NewFamilyDocument(analysis.RftPath);RequireTitleBlock(familyDoc);Import(familyDoc,analysis.FilePath,analysis.Unit);
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
