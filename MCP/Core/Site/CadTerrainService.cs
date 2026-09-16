#if REVIT2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Autodesk.Revit.DB;

namespace RevitMCP.Core.Site
{
    internal static class CadTerrainService
    {
        internal const string SharedError="CAD 無法依共用座標定位，請檢查測量圖座標與 Revit Shared Coordinates。";
        internal const string BudgetError=CadTerrainBudget.Message;
        public static CadTerrainAnalysis Analyze(Document document,CadTerrainRequest request)
        {
            if(!new[]{".dwg",".dxf"}.Contains(Path.GetExtension(request.Path).ToLowerInvariant()))throw new ArgumentException("請選擇 DWG 或 DXF。");
            var file=new FileInfo(request.Path);if(!file.Exists)throw new FileNotFoundException("找不到 CAD 檔案。",request.Path);
            if(file.Length>CadTerrainBudget.MaxFileBytes)throw new InvalidOperationException(BudgetError);
            string Hash(){using var stream=File.OpenRead(request.Path);return Convert.ToHexString(SHA256.HashData(stream));}
            string hash=Hash();
            var view=new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v=>!v.IsTemplate).OrderBy(v=>v.Id.Value).FirstOrDefault()
                ??throw new InvalidOperationException("需要一個既有平面視圖供 CAD 定位；不自動建立 View。");
            long[] Ids(Type type)=>new FilteredElementCollector(document).OfClass(type).ToElementIds().Select(i=>i.Value).OrderBy(i=>i).ToArray();
            var beforeImports=Ids(typeof(ImportInstance));var beforeTypes=Ids(typeof(CADLinkType));var beforeViews=Ids(typeof(View));
            var primitives=new List<CadPrimitive>();string evidence="";var budget=new CadTerrainBudget();Transform sourceFromModel=Transform.Identity;
            var coordinate=CoordinateTransformService.Read(document);
            using(var group=new TransactionGroup(document,"Analyze CAD terrain — rollback only"))
            {
                group.Start();
                try
                {
                    using var transaction=new Transaction(document,"Temporary terrain CAD");transaction.Start();
                    using var options=new DWGImportOptions{ThisViewOnly=false,Placement=request.Placement==CadPlacement.Shared?ImportPlacement.Shared:ImportPlacement.Origin,
                        Unit=request.Units switch{"m"=>ImportUnit.Meter,"mm"=>ImportUnit.Millimeter,"ft"=>ImportUnit.Foot,_=>throw new ArgumentException("請指定 CAD 單位。")},
                        VisibleLayersOnly=false,OrientToView=false,AutoCorrectAlmostVHLines=false};
                    ElementId id;
                    try { bool loaded=request.Placement==CadPlacement.Shared?document.Link(request.Path,options,view,out id):document.Import(request.Path,options,view,out id);if(!loaded)throw new InvalidOperationException("Revit 無法載入 CAD。"); }
                    catch(Exception e) when(request.Placement==CadPlacement.Shared){throw new InvalidOperationException(SharedError,e);}
                    document.Regenerate();
                    var cad=document.GetElement(id) as ImportInstance??throw new InvalidOperationException("CAD 匯入未產生 ImportInstance。");
                    var nativeTotal=cad.GetTotalTransform();var total=nativeTotal;var inherent=cad.GetTransform();double verticalDatumCorrection=0;
                    if(request.Placement==CadPlacement.Shared)
                    {
                        // Native Shared CAD placement can leave elevation at the import reference level.
                        // Normalize only the independently verified vertical datum in the DTO chain.
                        // CAD, ProjectLocation and buildings remain untouched.
                        verticalDatumCorrection=coordinate.SurveyToInternal.DeltaZ-nativeTotal.Origin.Z*.3048;
                        total=Transform.CreateTranslation(new XYZ(0,0,verticalDatumCorrection/.3048)).Multiply(nativeTotal);
                        foreach(var probe in new[]{XYZ.Zero,XYZ.BasisX,XYZ.BasisY,XYZ.BasisZ})
                            if(CoordinateTransformService.Metres(total.OfPoint(probe)).Distance(coordinate.SurveyToInternal.Apply(CoordinateTransformService.Metres(probe)))>1e-6)
                                throw new InvalidOperationException(SharedError+$" CAD transform 與 ProjectPosition 獨立驗證不一致。 Probe={probe}; TotalOrigin={total.Origin}; TotalX={total.BasisX}; TotalY={total.BasisY}; InherentOrigin={inherent.Origin}; InherentX={inherent.BasisX}; Expected={coordinate.SurveyToInternal}; Actual={CoordinateTransformService.Metres(total.OfPoint(probe))}");
                    }
                    sourceFromModel=total.Inverse;
                    var correction=total.Multiply(inherent.Inverse);
                    using var geometryOptions=new Options{IncludeNonVisibleObjects=true,ComputeReferences=false};
                    var geometry=cad.get_Geometry(geometryOptions)??throw new InvalidOperationException("CAD 沒有可讀幾何。");
                    evidence=$"Native GetTotalTransform origin={nativeTotal.Origin}; X={nativeTotal.BasisX}; Y={nativeTotal.BasisY}; verified vertical datum correction={verticalDatumCorrection:R} m; effective origin={total.Origin}; root correction=effectiveTotal*inherent^-1; nested=symbol transforms; placement={request.Placement}; direction={coordinate.VerifiedDirection}";
                    Walk(geometry,correction,"未命名圖層",0);
                    var knownLayers=primitives.Select(p=>p.Layer).ToHashSet(StringComparer.Ordinal);int layerCount=0;
                    if(cad.Category?.SubCategories!=null)
                        foreach(Category layerCategory in cad.Category.SubCategories)
                        {
                            if(++layerCount>CadTerrainBudget.MaxGeometry)throw new InvalidOperationException(BudgetError);
                            if(knownLayers.Add(layerCategory.Name))primitives.Add(new(layerCategory.Name,"NoTerrainGeometry",Array.Empty<SitePoint>()));
                        }
                    transaction.RollBack();
                }
                finally
                {
                    if(group.GetStatus()==TransactionStatus.Started)group.RollBack();
                    if(!beforeImports.SequenceEqual(Ids(typeof(ImportInstance)))||!beforeTypes.SequenceEqual(Ids(typeof(CADLinkType)))||!beforeViews.SequenceEqual(Ids(typeof(View))))
                        throw new InvalidOperationException("CAD_ROLLBACK_FAILURE：暫存 CAD／Type／View 集合不一致。");
                }
            }
            if(!beforeImports.SequenceEqual(Ids(typeof(ImportInstance)))||!beforeTypes.SequenceEqual(Ids(typeof(CADLinkType)))||!beforeViews.SequenceEqual(Ids(typeof(View))))
                throw new InvalidOperationException("CAD_ROLLBACK_FAILURE：暫存 CAD／Type／View 集合不一致。");
            if(hash!=Hash())throw new InvalidOperationException("分析期間 CAD 檔案已變更，請重新分析。");
            return new CadTerrainAnalysis{FileName=file.Name,SHA256=hash,Units=request.Units,Placement=request.Placement,TransformEvidence=evidence,RollbackVerified=true,Geometry=primitives.ToArray()};

            void Walk(GeometryElement geometry,Transform accumulated,string inheritedLayer,int depth)
            {
                foreach(var item in geometry)
                {
                    budget.Geometry(depth);
                    string layer=(document.GetElement(item.GraphicsStyleId) as GraphicsStyle)?.GraphicsStyleCategory?.Name??inheritedLayer;
                    if(item is GeometryInstance instance)
                    {
                        using var symbol=instance.GetSymbolGeometry();Walk(symbol,accumulated.Multiply(instance.Transform),layer,depth+1);continue;
                    }
                    IList<XYZ>? raw=null;string kind=item.GetType().Name;
                    if(item is Autodesk.Revit.DB.Point point)raw=new[]{point.Coord};
                    else if(item is PolyLine polyline)raw=polyline.GetCoordinates();
                    else if(item is Line line&&line.IsBound)raw=new[]{line.GetEndPoint(0),line.GetEndPoint(1)};
                    if(raw==null){primitives.Add(new(layer,"需複核："+kind,Array.Empty<SitePoint>()));continue;}
                    budget.Vertices(raw.Count);
                    var points=raw.Select(p=>CoordinateTransformService.Metres(accumulated.OfPoint(p))).ToArray();
                    if(points.Any(p=>!p.Finite))throw new InvalidOperationException("CAD 座標含非有限值。");
                    // Nested symbol transforms compose with the verified root frame exactly once.
                    bool closed=points.Length>=4&&points[0].Distance(points[^1])<1e-7;
                    primitives.Add(new(layer,kind,points,closed,raw.All(p=>Math.Abs(sourceFromModel.OfPoint(accumulated.OfPoint(p)).Z)<1e-8)));
                }
            }
        }
    }
}
#endif
