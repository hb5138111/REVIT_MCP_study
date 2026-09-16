using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitMCP.Models;
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>Typed, read-only native adapter over the existing curve/solid geometry core.</summary>
    public sealed class CoordinationService
    {
        // Computational budget, not a company or engineering threshold.
        private const long PairBudget = 50000;
        public static ElementId Id(long id) => new ElementId(checked((IdType)id));
        private sealed class Source
        {
            public Source(Document document, Transform transform) { Document = document; Transform = transform; }
            public Document Document;
            public Transform Transform;
            public long LinkId;
        }
        private static Source Resolve(Document project, long linkId)
        {
            if (linkId == 0) return new Source(project, Transform.Identity);
            var link = project.GetElement(Id(linkId)) as RevitLinkInstance;
            var doc = link?.GetLinkDocument();
            if (doc == null || link == null) throw new InvalidOperationException("連結模型未載入，請重新讀取來源。");
            return new Source(doc, link.GetTotalTransform()) { LinkId = linkId };
        }
        public IReadOnlyList<CoordinationSource> GetSources(Document project)
        {
            var result = new List<CoordinationSource> { new CoordinationSource { Name = "主模型" } };
            result.AddRange(new FilteredElementCollector(project).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                .Where(l => l.GetLinkDocument() != null).OrderBy(l => l.Id.GetIdValue())
                .Select(l => new CoordinationSource { LinkInstanceId = l.Id.GetIdValue(), Name = "連結：" + l.Name }));
            return result;
        }
        public IReadOnlyList<string> GetLevels(Document project, long linkId) => new FilteredElementCollector(Resolve(project, linkId).Document)
            .OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).Select(l => l.Name).ToArray();
        public IReadOnlyList<CoordinationLevel> GetLevelOptions(Document project, long linkId) => new FilteredElementCollector(Resolve(project, linkId).Document)
            .OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ThenBy(l => l.Id.GetIdValue())
            .Select(l => new CoordinationLevel { Id = l.Id.GetIdValue(), Name = l.Name }).ToArray();
        public IReadOnlyList<string> GetMepCategories(Document project, long linkId)
        {
            var document = Resolve(project, linkId).Document;
            return new[] { "Pipes", "Ducts", "CableTrays", "Conduits" }.Where(category =>
                new FilteredElementCollector(document).OfCategory(LinkedModelHelper.ResolveBuiltInCategory(category))
                    .WhereElementIsNotElementType().FirstElementId() != ElementId.InvalidElementId).ToArray();
        }

        public CoordinationResult Scan(Document project, CoordinationRequest request)
        {
            CoordinationRules.Validate(request);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var result = new CoordinationResult { DocumentIdentity = DocumentSessionIdentity.GetDocumentIdentity(project), Scope = request };
            var mep = Resolve(project, request.MepLinkId);
            var host = Resolve(project, request.HostLinkId);
            if (request.LevelId.HasValue && !(mep.Document.GetElement(Id(request.LevelId.Value)) is Level))
                throw new InvalidOperationException("來源樓層已失效，請重新整理。");
            var pipes = Collect(mep.Document, request.MepCategory).Where(e => request.LevelId.HasValue
                    ? LevelId(e) == request.LevelId.Value : string.IsNullOrEmpty(request.LevelName) || LevelName(e) == request.LevelName)
                .Where(e => string.IsNullOrWhiteSpace(request.SystemContains) || SystemName(e).IndexOf(request.SystemContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var hosts = Collect(host.Document, request.HostCategory);
            if ((long)pipes.Count * hosts.Count > PairBudget)
                throw new InvalidOperationException("掃描範圍過大，請縮小 MEP 樓層、系統或主體分類。");
            if (pipes.Count == 0 || hosts.Count == 0) { result.TotalScanned = pipes.Count; return result; }
            var boxes = hosts.ToDictionary(e => e.Id.GetIdValue(), e =>
            {
                if (timer.Elapsed.TotalSeconds > 15) throw new InvalidOperationException("來源準備超過安全時間，請縮小範圍。");
                return ClashDetector.GetTransformedBBox(e, host.Transform);
            });
            var solids = new Dictionary<long, List<Solid>>();
            try
            {
            foreach (var pipe in pipes)
            {
                result.TotalScanned++;
                using (var curve = ClashDetector.GetElementCurve(pipe, mep.Transform))
                {
                    var box = ClashDetector.GetTransformedBBox(pipe, mep.Transform);
                    if (curve == null || box == null) { result.Warnings.Add("部分管線缺少中心線或範圍，未能檢查。"); continue; }
                    foreach (var structure in hosts)
                    {
                        if (timer.Elapsed.TotalSeconds > 15) throw new InvalidOperationException("掃描超過安全時間，請縮小範圍後重試。");
                        var key = structure.Id.GetIdValue();
                        if (boxes[key] == null) { result.Warnings.Add("部分主體缺少範圍，未能檢查。"); continue; }
                        if (!ClashDetector.BBoxIntersects(box, boxes[key])) continue;
                        if (!solids.TryGetValue(key, out var geometry))
                        {
                            geometry = ClashDetector.GetElementSolids(structure, host.Transform);
                            solids[key] = geometry;
                        }
                        if (geometry.Count == 0) { result.Warnings.Add("部分主體缺少實體幾何，未能檢查。"); continue; }
                        CoordinationRow? issue = null;
                        int segments = 0;
                        foreach (var solid in geometry)
                        {
                            try
                            {
                                using (var intersection = ClashDetector.IntersectCenterline(solid, curve))
                                {
                                    for (int i = 0; i < intersection.SegmentCount; i++)
                                    {
                                        using (var segment = intersection.GetCurveSegment(i))
                                        {
                                            segments++;
                                            if (issue == null) issue = Map(pipe, structure, mep, host, segment, solid, request);
                                        }
                                    }
                                }
                            }
                            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                            { result.Warnings.Add("部分幾何交集無法解析，請人工複核。"); }
                        }
                        if (issue != null)
                        {
                            if (segments > 1) issue.WarningCodes.Add("multiple_intersections");
                            result.TotalMatchedCount++;
                            result.CountsByKind.TryGetValue(issue.ResultKind, out int kindCount);
                            result.CountsByKind[issue.ResultKind] = kindCount + 1;
                            result.CountsByStatus.TryGetValue(issue.Status, out int statusCount);
                            result.CountsByStatus[issue.Status] = statusCount + 1;
                            if (result.Rows.Count < request.MaxResults) result.Rows.Add(issue);
                        }
                    }
                }
            }
            }
            finally { foreach (var solid in solids.Values.SelectMany(value => value)) solid.Dispose(); }
            result.Warnings = result.Warnings.Distinct().ToList();
            result.Warnings.Add("採中心線穿越法，不包含管件、保溫及未穿過中心線的實體擦碰。");
            return result;
        }
        private static List<Element> Collect(Document doc, string category)
        {
            if (!new[] { "Pipes", "Ducts", "CableTrays", "Conduits", "Walls", "Floors", "StructuralFraming", "StructuralColumns" }.Contains(category))
                throw new ArgumentException("不支援的構件分類。");
            var elements = new FilteredElementCollector(doc).OfCategory(LinkedModelHelper.ResolveBuiltInCategory(category))
                .WhereElementIsNotElementType().Take((int)PairBudget + 1).ToList();
            if (elements.Count > PairBudget) throw new InvalidOperationException("單一分類超過安全上限，請以較小模型來源執行。");
            return elements.OrderBy(e => e.Id.GetIdValue()).ToList();
        }
        private static long LevelId(Element e) => ((e as MEPCurve)?.ReferenceLevel?.Id ?? e.LevelId).GetIdValue();
        private static string LevelName(Element e)
        {
            var id = (e as MEPCurve)?.ReferenceLevel?.Id ?? e.LevelId;
            return (e.Document.GetElement(id) as Level)?.Name ?? "—";
        }
        private static string SystemName(Element e) => (e as MEPCurve)?.MEPSystem?.Name ?? "—";
        private static CoordinationLookup Lookup(Element e, Source s) => new CoordinationLookup
        {
            ElementId = e.Id.GetIdValue(), LinkInstanceId = s.LinkId, UniqueId = e.UniqueId,
            SourceDocumentIdentity = DocumentSessionIdentity.GetDocumentIdentity(s.Document)
        };
        private static double? Length(Element e, BuiltInParameter parameter)
        {
            var p = e.get_Parameter(parameter);
            return p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 0 ? p.AsDouble() * 304.8 : (double?)null;
        }
        private static CoordinationRow Map(Element pipe, Element structure, Source mep, Source host, Curve segment, Solid solid, CoordinationRequest request)
        {
            var entry = segment.GetEndPoint(0); var exit = segment.GetEndPoint(1); var center = (entry + exit) / 2;
            var direction = (exit - entry).Normalize();
            double? dot = null;
            foreach (Face face in solid.Faces)
            {
                if (!(face is PlanarFace planar)) continue;
                var projection = face.Project(entry);
                if (projection != null && projection.Distance < 1e-6) { dot = direction.DotProduct(planar.FaceNormal.Normalize()); break; }
            }
            var row = new CoordinationRow { Mep = Lookup(pipe, mep), Host = Lookup(structure, host),
                MepSource = mep.LinkId == 0 ? "主模型" : "Link " + mep.LinkId,
                HostSource = host.LinkId == 0 ? "主模型" : "Link " + host.LinkId,
                MepLabel = pipe.Name, HostLabel = structure.Name,
                ResultKind = request.HostCategory == "StructuralFraming" ? CoordinationKind.BeamPenetration
                    : request.HostCategory == "StructuralColumns" ? CoordinationKind.ReviewRequired
                    : request.OpeningCandidates ? CoordinationKind.OpeningCandidate : CoordinationKind.Clash,
                MepCategory = request.MepCategory, HostCategory = request.HostCategory, Level = LevelName(pipe), System = SystemName(pipe),
                Xmm = center.X * 304.8, Ymm = center.Y * 304.8, Zmm = center.Z * 304.8, IntersectionLengthMm = segment.Length * 304.8 };
            bool round = request.MepCategory == "Pipes" || request.MepCategory == "Conduits";
            row.NominalDiameterMm = round ? Length(pipe, request.MepCategory == "Pipes" ? BuiltInParameter.RBS_PIPE_DIAMETER_PARAM : BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM) : null;
            row.NominalWidthMm = round ? null : Length(pipe, request.MepCategory == "Ducts" ? BuiltInParameter.RBS_CURVE_WIDTH_PARAM : BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
            row.NominalHeightMm = round ? null : Length(pipe, request.MepCategory == "Ducts" ? BuiltInParameter.RBS_CURVE_HEIGHT_PARAM : BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);
            if (request.OpeningCandidates)
            {
                double clearance = request.ClearanceMm ?? throw new ArgumentException("請先設定本專案開孔預留量。");
                if (row.NominalDiameterMm.HasValue) row.DiameterMm = CoordinationRules.OpeningSize(row.NominalDiameterMm.Value, clearance);
                if (row.NominalWidthMm.HasValue) row.WidthMm = CoordinationRules.OpeningSize(row.NominalWidthMm.Value, clearance);
                if (row.NominalHeightMm.HasValue) row.HeightMm = CoordinationRules.OpeningSize(row.NominalHeightMm.Value, clearance);
            }
            row.WarningCodes = CoordinationRules.Classify(request.HostCategory, row.IntersectionLengthMm, dot,
                round ? row.NominalDiameterMm.HasValue : row.NominalWidthMm.HasValue && row.NominalHeightMm.HasValue);
            // No reliable lower-edge projection for all orientations; do not reuse center-height/2 as fact.
            if (request.OpeningCandidates) row.WarningCodes.Add("opening_bottom_unresolved");
            row.WarningCodes.Add("solid_edge_unknown");
            return row;
        }
        public static Element ResolveNavigation(Document project, CoordinationLookup lookup)
        {
            var source = Resolve(project, lookup.LinkInstanceId);
            if (DocumentSessionIdentity.GetDocumentIdentity(source.Document) != lookup.SourceDocumentIdentity)
                throw new InvalidOperationException("來源模型已變更，請重新掃描。");
            var element = source.Document.GetElement(Id(lookup.ElementId));
            if (element == null || element.UniqueId != lookup.UniqueId) throw new InvalidOperationException("元素已失效，請重新掃描。");
            return lookup.LinkInstanceId == 0 ? element : project.GetElement(Id(lookup.LinkInstanceId));
        }
    }
}
