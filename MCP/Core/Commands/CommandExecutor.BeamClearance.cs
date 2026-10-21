using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCP.Core.Review;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// check_beam_clear_height — 梁下淨高檢討（方法見 domain/beam-clear-height-check.md）
    ///
    /// 對每支梁（主模型 + 已載入連結）：
    ///   1. 沿梁的定位線取樣數個點；每點以垂直線穿過梁實體，得到該處「梁底」Z（可處理斜梁、變斷面）。
    ///   2. 從梁底往下打射線（ReferenceIntersector，含連結），找第一片「樓板頂面」→ 淨高 = 梁底 − 樓板頂。
    ///      若射線起點落在樓板內（例如梁底埋在加厚版內），以「只出現一次的樓板」判定並略過。
    ///   3. 選擇性：同一條射線找天花板 → 回報「梁下到天花板頂」的管線可用空間，以及天花高度。
    ///   4. 每支梁取所有取樣點中的最小淨高，與 requiredClearHeightMm 比較。
    /// 樓層篩選以「空間所在樓層」為準：梁底落在 [該層, 上一層] 之間的梁，也就是該層天花上方的梁。
    /// </summary>
    public partial class CommandExecutor
    {
        private object CheckBeamClearHeight(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument?.Document ?? throw new Exception("目前沒有開啟的專案");

            double requiredMm = parameters["requiredClearHeightMm"]?.Value<double>() ?? throw new Exception("請提供 requiredClearHeightMm（法規或設計要求的梁下淨高，mm）");
            bool includeLinks = parameters["includeLinks"]?.Value<bool>() ?? true;
            bool includeCeilings = parameters["includeCeilings"]?.Value<bool>() ?? true;
            int samples = Math.Max(1, Math.Min(15, parameters["samplesPerBeam"]?.Value<int>() ?? 5));
            int maxResults = Math.Max(1, Math.Min(2000, parameters["maxResults"]?.Value<int>() ?? 300));
            bool includePassed = parameters["includePassed"]?.Value<bool>() ?? false;
            bool createMarkers = parameters["createMarkers"]?.Value<bool>() ?? false;
            bool highlight = parameters["highlightInView"]?.Value<bool>() ?? false;
            double minCeilingSpaceMm = parameters["minCeilingSpaceMm"]?.Value<double>() ?? 0;
            // 時間預算：MCP 端對本命令的逾時是 300 秒，逾時後 Revit 仍會跑完、結果卻被丟棄。
            // 大型專案（數千支梁）全模型可能要 20 分鐘以上 → 到預算就停，回傳已完成部分並標示 truncated。
            int timeBudgetSec = Math.Max(10, Math.Min(270, parameters["timeBudgetSec"]?.Value<int>() ?? 240));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool truncated = false;
            var levelNames = (parameters["levels"] as JArray)?.Select(t => t.Value<string>()).ToList();
            var categoryNames = (parameters["categories"] as JArray)?.Select(t => t.Value<string>()).ToList() ?? new List<string> { "beams" };
            var cats = categoryNames.Select(ReviewUtil.ParseCategory).Distinct().ToList();

            var bands = ReviewUtil.LevelBands(doc, levelNames);
            var hostLevels = ReviewUtil.SortedLevels(doc);

            View3D rayView = ReviewUtil.EnsureReviewView(doc, ReviewUtil.RayViewName,
                new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_RvtLinks });

            var floorRi = new ReferenceIntersector(new ElementCategoryFilter(BuiltInCategory.OST_Floors), FindReferenceTarget.Face, rayView)
            { FindReferencesInRevitLinks = true };
            var ceilRi = includeCeilings
                ? new ReferenceIntersector(new ElementCategoryFilter(BuiltInCategory.OST_Ceilings), FindReferenceTarget.Face, rayView) { FindReferencesInRevitLinks = true }
                : null;

            var results = new List<BeamClearResult>();
            int checkedCount = 0, skippedNoGeometry = 0;

            foreach (var src in ReviewUtil.GetSourceDocs(doc, includeLinks))
            {
                if (truncated) break;
                var filter = new ElementMulticategoryFilter(cats);
                foreach (Element e in new FilteredElementCollector(src.Doc).WherePasses(filter).WhereElementIsNotElementType())
                {
                    if (clock.Elapsed.TotalSeconds > timeBudgetSec) { truncated = true; break; }
                    var box = ReviewUtil.HostBox(e, src.ToHost);
                    if (box == null) continue;
                    if (!ReviewUtil.InBands(bands, box.Value.min.Z)) continue;

                    var solids = ReviewUtil.GetSolids(e, src.ToHost);
                    if (solids.Count == 0) { skippedNoGeometry++; continue; }
                    checkedCount++;

                    var r = MeasureUnderElement(e, src, solids, box.Value, samples, floorRi, ceilRi);
                    r.Required = ReviewUtil.Ft(requiredMm);
                    if (r.Point != null)
                    {
                        // 樓層歸屬以梁底為準（domain §3）：梁底所在 (本層, 上一層] 的「本層」，與 LevelBands/InBands 的下界一致。
                        // 不可用打到的樓板高度決定，否則射線穿過開口打到下下層時，梁會被歸到錯的樓層。
                        var lv = ReviewUtil.LevelAtZ(hostLevels, r.BottomZ - 1e-6, 0);
                        r.SpaceLevel = lv?.Name;
                        r.SpaceLevelElev = lv?.ProjectElevation;
                        int li = lv != null ? hostLevels.FindIndex(l => l.Id == lv.Id) : -1;
                        // 樓板頂低於「本層與下一層的中線」→ 不是本層空間的樓板（多半是穿過開口、挑空打到下下層）
                        if (li > 0 && r.FloorZ != null)
                            r.FloorBelowSpaceLevel = r.FloorZ.Value < (lv.ProjectElevation + hostLevels[li - 1].ProjectElevation) / 2;
                    }
                    results.Add(r);
                }
            }

            double tol = ReviewUtil.Ft(0.5);
            foreach (var r in results)
            {
                if (r.Point == null) r.Status = "NO_GEOMETRY_HIT";
                else if (r.ClearHeight == null) r.Status = "NO_FLOOR_BELOW";
                else if (r.FloorBelowSpaceLevel) r.Status = "FLOOR_BELOW_SPACE_LEVEL"; // 不可當成合格（domain §4）
                else if (r.ClearHeight.Value + tol < r.Required) r.Status = "FAIL";
                else r.Status = "PASS";

                if (r.Status == "PASS" && minCeilingSpaceMm > 0 && r.CeilingSpace != null && r.CeilingSpace.Value + tol < ReviewUtil.Ft(minCeilingSpaceMm))
                    r.Status = "CEILING_SPACE_LOW";
            }

            var fails = results.Where(r => r.Status == "FAIL" || r.Status == "CEILING_SPACE_LOW").OrderBy(r => r.ClearHeight ?? 0).ToList();

            int markers = 0, highlighted = 0;
            if (createMarkers && fails.Count > 0)
                markers = ReviewUtil.PlaceMarkers(doc, "beam_clear_height",
                    fails.Take(maxResults).Select(r => (r.Point, r.Status == "CEILING_SPACE_LOW"
                        ? $"梁底到天花頂 {ReviewUtil.Mm(r.CeilingSpace ?? 0)}mm < {minCeilingSpaceMm}mm｜{r.Source} #{r.Id}"
                        : $"梁下淨高 {ReviewUtil.Mm(r.ClearHeight ?? 0)}mm < {requiredMm}mm｜{r.Source} #{r.Id}")).ToList());
            if (highlight && fails.Count > 0)
                highlighted = ReviewUtil.HighlightInResultView(doc, fails.Where(r => r.IsHost).Select(r => new ElementId(r.Id)), new Color(230, 40, 40));

            // 最小淨高只取有效量測（打到本層樓板者）；需人工確認的另外計數
            bool Measured(BeamClearResult r) => r.Status == "FAIL" || r.Status == "PASS" || r.Status == "CEILING_SPACE_LOW";
            var perLevel = results.Where(r => r.SpaceLevel != null).GroupBy(r => r.SpaceLevel)
                .Select(g => new
                {
                    level = g.Key,
                    elevationMm = ReviewUtil.Mm(g.First().SpaceLevelElev ?? 0),
                    beams = g.Count(),
                    measured = g.Count(Measured),
                    fail = g.Count(r => r.Status == "FAIL"),
                    needsReview = g.Count(r => !Measured(r)),
                    minClearHeightMm = g.Any(Measured) ? ReviewUtil.Mm(g.Where(Measured).Min(r => r.ClearHeight.Value)) : (double?)null,
                    minCeilingSpaceMm = g.Any(r => Measured(r) && r.CeilingSpace != null) ? ReviewUtil.Mm(g.Where(r => Measured(r) && r.CeilingSpace != null).Min(r => r.CeilingSpace.Value)) : (double?)null
                }).OrderBy(x => x.elevationMm).ToList();

            var listedAll = includePassed ? results.OrderBy(r => r.ClearHeight ?? double.MaxValue).ToList() : fails;
            // domain §4：NO_FLOOR_BELOW / FLOOR_BELOW_SPACE_LEVEL / NO_GEOMETRY_HIT 不可當成合格，單獨列出。
            // 依稀有/嚴重度排序（幾何異常 → 打到下層樓板 → 找不到樓板），避免少數重要項目被 maxResults 截掉
            int ReviewRank(string s) => s == "NO_GEOMETRY_HIT" ? 0 : s == "FLOOR_BELOW_SPACE_LEVEL" ? 1 : 2;
            var reviewAll = results.Where(r => !Measured(r)).OrderBy(r => ReviewRank(r.Status)).ThenBy(r => r.SpaceLevelElev ?? 0).ToList();
            bool listCapped = listedAll.Count > maxResults || reviewAll.Count > maxResults;

            return new
            {
                method = "domain/beam-clear-height-check.md",
                requiredClearHeightMm = requiredMm,
                checkedElements = checkedCount,
                skippedNoGeometry,
                truncated,
                elapsedSec = Math.Round(clock.Elapsed.TotalSeconds, 1),
                truncatedNote = truncated
                    ? $"已達時間預算 {timeBudgetSec} 秒，只檢討了前 {checkedCount} 個元素，結果不完整（不合格數與各樓層最小值都可能偏低）。請用 levels 分樓層執行。"
                    : null,
                failCount = results.Count(r => r.Status == "FAIL"),
                ceilingSpaceLowCount = results.Count(r => r.Status == "CEILING_SPACE_LOW"),
                noFloorBelowCount = results.Count(r => r.Status == "NO_FLOOR_BELOW"),
                floorBelowSpaceLevelCount = results.Count(r => r.Status == "FLOOR_BELOW_SPACE_LEVEL"),
                noGeometryHitCount = results.Count(r => r.Status == "NO_GEOMETRY_HIT"),
                perLevel,
                markersPlaced = markers,
                highlightedInView = highlighted,
                resultView = highlighted > 0 || markers > 0 ? ReviewUtil.ResultViewName : null,
                coordinateNote = "座標為主模型內部原點（mm）；淨高 = 梁底 − 下方第一片樓板頂面",
                itemsTotal = listedAll.Count,
                needsReviewTotal = reviewAll.Count,
                listNote = listCapped
                    ? $"清單只列前 {maxResults} 筆（items 共 {listedAll.Count}、needsReviewItems 共 {reviewAll.Count}）；統計數字為完整結果。加大 maxResults 可看全部。"
                    : null,
                items = listedAll.Take(maxResults).Select(r => r.ToJson()).ToList(),
                needsReviewItems = reviewAll.Take(maxResults).Select(r => r.ToJson()).ToList()
            };
        }

        private BeamClearResult MeasureUnderElement(Element e, SourceDoc src, List<Solid> solids, (XYZ min, XYZ max) box,
            int samples, ReferenceIntersector floorRi, ReferenceIntersector ceilRi)
        {
            var res = new BeamClearResult
            {
                Id = e.Id.GetIdValue(),
                Source = src.Label,
                IsHost = src.IsHost,
                Category = e.Category?.Name,
                TypeName = ReviewUtil.TypeName(e),
                Mark = ReviewUtil.Mark(e),
                RefLevel = ReviewUtil.LevelName(e)
            };

            // 取樣點：定位線上均分；定位線偏離實體時，往兩側小幅偏移再試
            var pts = new List<(XYZ p, XYZ side)>();
            if (e.Location is LocationCurve lc && lc.Curve != null)
            {
                Curve c = lc.Curve;
                for (int i = 1; i <= samples; i++)
                {
                    double f = (double)i / (samples + 1);
                    XYZ p = src.ToHost.OfPoint(c.Evaluate(f, true));
                    XYZ tan = src.ToHost.OfVector(c.ComputeDerivatives(f, true).BasisX);
                    XYZ flat = new XYZ(tan.X, tan.Y, 0);
                    XYZ side = flat.GetLength() > 1e-9 ? new XYZ(-flat.Y, flat.X, 0).Normalize() : XYZ.BasisX;
                    pts.Add((p, side));
                }
            }
            else
            {
                XYZ ctr = (box.min + box.max) / 2;
                pts.Add((ctr, XYZ.BasisX));
            }

            double[] sideOffsets = { 0, 0.5, -0.5, 1.0, -1.0 }; // ft
            double eps = ReviewUtil.Ft(2);

            foreach (var (p, side) in pts)
            {
                double? bottom = null;
                XYZ at = null;
                foreach (double off in sideOffsets)
                {
                    XYZ q = p + side * off;
                    bottom = ReviewUtil.BottomZAt(solids, q.X, q.Y, box.min.Z, box.max.Z);
                    if (bottom != null) { at = q; break; }
                }
                if (bottom == null) continue;

                XYZ origin = new XYZ(at.X, at.Y, bottom.Value - eps);
                var floorHit = FirstSurfaceBelow(floorRi, origin, out var floorKey, out _);

                double? clear = floorHit != null ? floorHit.Proximity + eps : (double?)null;
                double? ceilSpace = null, ceilHeight = null;
                if (ceilRi != null)
                {
                    var ceilHit = FirstSurfaceBelow(ceilRi, origin, out _, out double? ceilBottomProx);
                    if (ceilHit != null && (clear == null || ceilHit.Proximity + eps < clear.Value))
                    {
                        ceilSpace = ceilHit.Proximity + eps;
                        if (clear != null && ceilBottomProx != null) ceilHeight = clear.Value - (ceilBottomProx.Value + eps);
                    }
                }

                bool better = res.Point == null
                    || (clear != null && (res.ClearHeight == null || clear.Value < res.ClearHeight.Value));
                if (better)
                {
                    res.Point = new XYZ(at.X, at.Y, bottom.Value);
                    res.BottomZ = bottom.Value;
                    res.ClearHeight = clear;
                    res.FloorZ = clear != null ? bottom.Value - clear.Value : (double?)null;
                    res.FloorRef = floorKey;
                    res.CeilingSpace = ceilSpace;
                    res.CeilingHeight = ceilHeight;
                }
            }
            return res;
        }

        /// <summary>
        /// 往下找第一個「完整」的面：若最近的元素只出現一次（起點在其內部），略過它。
        /// 同時回傳該元素第二次被穿越的距離（= 下表面），供計算天花高度。
        /// </summary>
        private static ReferenceWithContext FirstSurfaceBelow(ReferenceIntersector ri, XYZ origin, out string key, out double? exitProximity)
        {
            key = null; exitProximity = null;
            IList<ReferenceWithContext> hits;
            try { hits = ri.Find(origin, -XYZ.BasisZ); } catch { return null; }
            if (hits == null || hits.Count == 0) return null;

            var ordered = hits.Where(h => h.Proximity > 1e-6 && h.Proximity < 200).OrderBy(h => h.Proximity).ToList();
            if (ordered.Count == 0) return null;

            string K(ReferenceWithContext h)
            {
                var r = h.GetReference();
                return $"{r.ElementId.GetIdValue()}:{r.LinkedElementId.GetIdValue()}";
            }

            var keys = ordered.Select(K).ToList();
            var counts = keys.GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
            int start = 0;
            if (counts[keys[0]] == 1)
            {
                string inside = keys[0];
                while (start < keys.Count && keys[start] == inside) start++;
                if (start >= keys.Count) return null;
            }
            key = keys[start];
            for (int i = start + 1; i < keys.Count; i++)
            {
                if (keys[i] == key) { exitProximity = ordered[i].Proximity; break; }
            }
            return ordered[start];
        }

        private sealed class BeamClearResult
        {
            public IdType Id;
            public string Source, Category, TypeName, Mark, RefLevel, SpaceLevel, Status, FloorRef;
            public bool IsHost, FloorBelowSpaceLevel;
            public XYZ Point;
            public double BottomZ, Required;
            public double? FloorZ, ClearHeight, CeilingSpace, CeilingHeight, SpaceLevelElev;

            private static object FloorRefJson(string key)
            {
                if (string.IsNullOrEmpty(key)) return null;
                var parts = key.Split(':');
                if (parts.Length != 2) return key;
                return parts[1] == "-1"
                    ? (object)new { elementId = parts[0], inLink = false }
                    : new { linkInstanceId = parts[0], linkedElementId = parts[1], inLink = true };
            }

            public object ToJson() => new
            {
                elementId = Id,
                source = Source,
                category = Category,
                type = TypeName,
                mark = Mark,
                referenceLevel = RefLevel,
                spaceLevel = SpaceLevel,
                status = Status,
                clearHeightMm = ClearHeight != null ? ReviewUtil.Mm(ClearHeight.Value) : (double?)null,
                shortageMm = ClearHeight != null && ClearHeight.Value < Required ? ReviewUtil.Mm(Required - ClearHeight.Value) : (double?)null,
                beamBottomAboveLevelMm = SpaceLevelElev != null ? ReviewUtil.Mm(BottomZ - SpaceLevelElev.Value) : (double?)null,
                floorTopAboveLevelMm = SpaceLevelElev != null && FloorZ != null ? ReviewUtil.Mm(FloorZ.Value - SpaceLevelElev.Value) : (double?)null,
                beamToCeilingTopMm = CeilingSpace != null ? ReviewUtil.Mm(CeilingSpace.Value) : (double?)null,
                ceilingHeightMm = CeilingHeight != null ? ReviewUtil.Mm(CeilingHeight.Value) : (double?)null,
                floorHit = FloorRefJson(FloorRef),
                worstPointMm = Point != null ? ReviewUtil.PointMm(Point) : null
            };
        }
    }
}
