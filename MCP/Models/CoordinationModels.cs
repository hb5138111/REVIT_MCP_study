using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitMCP.Models
{
    public sealed class CoordinationNavigationSession
    {
        public long? CoordinationViewId { get; set; }
        public long? PreviousViewId { get; set; }
        public void Clear() { CoordinationViewId = null; PreviousViewId = null; }
    }
    public sealed class CoordinationViewOption
    {
        public long Id { get; set; }
        public bool Usable { get; set; }
        public bool Perspective { get; set; }
    }
    public static class CoordinationViewPolicy
    {
        // Resolve lazily: normal next/previous never enumerate all document views.
        public static long? Resolve(long current, long? session, bool keepSession,
            Func<long, CoordinationViewOption?> lookup, Func<IEnumerable<CoordinationViewOption>> candidates)
        {
            bool Valid(long? id) => id.HasValue && lookup(id.Value)?.Usable == true;
            if (keepSession && Valid(session)) return session;
            if (Valid(current)) return current;
            if (Valid(session)) return session;
            return candidates().Where(v => v.Usable).OrderBy(v => v.Perspective).ThenBy(v => v.Id).Select(v => (long?)v.Id).FirstOrDefault();
        }
    }
    public sealed class CoordinationNavigationResult
    {
        public bool FocusVerified { get; set; }
        public bool ThreeDAvailable { get; set; }
        public string Message { get; set; } = "";
    }
    public enum CoordinationKind { Clash, OpeningCandidate, BeamPenetration, ReviewRequired }
    public sealed class CoordinationLevel
    {
        public long? Id { get; set; }
        public string Name { get; set; } = "全部";
        public override string ToString() => Name;
    }
    public sealed class CoordinationSource
    {
        public long LinkInstanceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public override string ToString() => Name;
    }
    public sealed class CoordinationRequest
    {
        public long MepLinkId { get; set; }
        public long HostLinkId { get; set; }
        public string MepCategory { get; set; } = string.Empty;
        public string HostCategory { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public long? LevelId { get; set; }
        public string SystemContains { get; set; } = string.Empty;
        public int MaxResults { get; set; } = 200;
        public bool OpeningCandidates { get; set; }
        public double? ClearanceMm { get; set; }
    }
    public sealed class CoordinationProjectSettings
    {
        public int Version { get; set; } = 1;
        public double? OpeningClearanceMm { get; set; }
    }
    public sealed class CoordinationLookup
    {
        public long ElementId { get; set; }
        public long LinkInstanceId { get; set; }
        public string UniqueId { get; set; } = string.Empty;
        public string SourceDocumentIdentity { get; set; } = string.Empty;
        public override string ToString() => LinkInstanceId == 0 ? ElementId.ToString() : LinkInstanceId + ":" + ElementId;
    }
    public sealed class CoordinationRow
    {
        public CoordinationKind ResultKind { get; set; }
        public bool HasClash { get; set; } = true;
        public string MepSource { get; set; } = string.Empty;
        public string HostSource { get; set; } = string.Empty;
        public string MepLabel { get; set; } = string.Empty;
        public string HostLabel { get; set; } = string.Empty;
        public string KindDisplay => ResultKind == CoordinationKind.OpeningCandidate ? "開孔候選" : ResultKind == CoordinationKind.BeamPenetration ? "穿梁候選" : ResultKind == CoordinationKind.ReviewRequired ? "需人工複核" : "一般碰撞";
        public string Explanation => KindDisplay + "，仍需人工確認；不代表結構或法規核准。";
        public string Detail => $"MEP：{MepLabel}（{MepCategoryDisplay}）\n來源：{MepSource}；Element ID：{Mep}\n主體：{HostLabel}（{HostCategoryDisplay}）\n來源：{HostSource}；Element ID：{Host}\n系統：{System}；樓層：{Level}\nMEP 尺寸：{NominalSizeDisplay}；建議孔尺寸：{SizeDisplay}\n交點：{PointDisplay}；穿透長度：{IntersectionLengthMm:F1} mm；孔下緣：{OpeningBottomDisplay}\n提醒：{Warnings}\n{Explanation}";
        public string TechnicalDetail => Detail + "\nWarningCodes: " + string.Join(", ", WarningCodes);
        public CoordinationLookup Mep { get; set; } = new CoordinationLookup();
        public CoordinationLookup Host { get; set; } = new CoordinationLookup();
        public string MepCategory { get; set; } = string.Empty;
        public string HostCategory { get; set; } = string.Empty;
        public string System { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string MepCategoryDisplay => CategoryDisplay(MepCategory);
        public string HostCategoryDisplay => CategoryDisplay(HostCategory);
        public double IntersectionLengthMm { get; set; }
        public double Xmm { get; set; }
        public double Ymm { get; set; }
        public double Zmm { get; set; }
        public string PointDisplay => $"{Xmm:F1}, {Ymm:F1}, {Zmm:F1} mm";
        public double? DiameterMm { get; set; }
        public double? WidthMm { get; set; }
        public double? HeightMm { get; set; }
        public double? NominalDiameterMm { get; set; }
        public double? NominalWidthMm { get; set; }
        public double? NominalHeightMm { get; set; }
        public double? OpeningBottomMm { get; set; }
        public string OpeningBottomDisplay => OpeningBottomMm?.ToString("F1", CultureInfo.CurrentCulture) ?? "—";
        public string SizeDisplay => Size(DiameterMm, WidthMm, HeightMm);
        public string NominalSizeDisplay => Size(NominalDiameterMm, NominalWidthMm, NominalHeightMm);
        public List<string> WarningCodes { get; set; } = new List<string>();
        public bool ReviewRequired => WarningCodes.Count > 0;
        public string Status => ReviewRequired ? "需人工複核" : "候選";
        public string Warnings => string.Join("；", WarningCodes.Select(CoordinationRules.WarningText));
        private static string Size(double? d, double? w, double? h) => d.HasValue ? $"Ø {d:F1} mm" : w.HasValue && h.HasValue ? $"{w:F1} × {h:F1} mm" : "—";
        private static string CategoryDisplay(string category)
        {
            switch(category)
            {
                case "Pipes": return "管";
                case "Ducts": return "風管";
                case "CableTrays": return "電纜架";
                case "Conduits": return "電管";
                case "Walls": return "牆";
                case "Floors": return "樓板";
                case "StructuralFraming": return "梁";
                case "StructuralColumns": return "柱";
                default: return "—";
            }
        }
    }
    public sealed class CoordinationResult
    {
        public CoordinationRequest Scope { get; set; } = new CoordinationRequest();
        public int TotalScanned { get; set; }
        public int TotalIssues => TotalMatchedCount;
        public Dictionary<CoordinationKind, int> CountsByKind { get; set; } = new Dictionary<CoordinationKind, int>();
        public Dictionary<string, int> CountsByStatus { get; set; } = new Dictionary<string, int>();
        public string DocumentIdentity { get; set; } = string.Empty;
        public int TotalMatchedCount { get; set; }
        public int ReturnedCount => Rows.Count;
        public bool IsTruncated => TotalMatchedCount > ReturnedCount;
        public List<CoordinationRow> Rows { get; set; } = new List<CoordinationRow>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
    public static class CoordinationRules
    {
        public static double OpeningSize(double nominal, double clearance)
        {
            if (double.IsNaN(nominal) || double.IsInfinity(nominal) || nominal <= 0 ||
                double.IsNaN(clearance) || double.IsInfinity(clearance) || clearance < 0)
                throw new ArgumentOutOfRangeException(nameof(clearance));
            return nominal + 2 * clearance;
        }
        public static void Validate(CoordinationRequest request)
        {
            if (request.MaxResults < 1 || request.MaxResults > 1000) throw new ArgumentException("顯示上限須為 1 至 1000。");
            if (!new[] { "Pipes", "Ducts", "CableTrays", "Conduits" }.Contains(request.MepCategory) ||
                !new[] { "Walls", "Floors", "StructuralFraming", "StructuralColumns" }.Contains(request.HostCategory))
                throw new ArgumentException("請各選一個 MEP 與主體分類。");
            if (request.MepLinkId < 0 || request.HostLinkId < 0 || request.LevelId <= 0)
                throw new ArgumentException("來源或樓層識別碼無效。");
            if (request.OpeningCandidates)
            {
                if (!request.ClearanceMm.HasValue) throw new ArgumentException("請先設定本專案開孔預留量。");
                OpeningSize(1, request.ClearanceMm.Value);
            }
        }
        public static List<string> Classify(string category, double lengthMm, double? normalDot, bool sizeResolved)
        {
            var warnings = new List<string>();
            if (category == "StructuralFraming") warnings.Add("structural_framing_review");
            if (category == "StructuralColumns") warnings.Add("structural_column_review");
            if (!normalDot.HasValue) warnings.Add("host_normal_unresolved");
            else if (Math.Abs(normalDot.Value) < 1 - 1e-9) warnings.Add("oblique_penetration");
            if (lengthMm < 10) warnings.Add("short_intersection");
            if (!sizeResolved) warnings.Add("size_data_missing");
            return warnings;
        }
        public static string WarningText(string code)
        {
            switch (code)
            {
                case "structural_framing_review": return "穿梁：需結構專業複核";
                case "structural_column_review": return "穿柱：需結構專業複核";
                case "host_normal_unresolved": return "主體表面法向無法確認";
                case "oblique_penetration": return "斜向穿越，尺寸未計投影放大";
                case "short_intersection": return "交集小於 10 mm，可能擦邊";
                case "size_data_missing": return "必要標稱尺寸缺失";
                case "opening_bottom_unresolved": return "開孔下緣尚無可靠量測";
                case "solid_edge_unknown": return "此結果依中心線穿越判定，未包含保溫、管件與實體邊緣擦碰。";
                case "multiple_intersections": return "同一對元素有多段交集；表列長度與交點為第一段，需逐段複核";
                default: return "幾何資料不完整，請人工複核";
            }
        }
    }
}
