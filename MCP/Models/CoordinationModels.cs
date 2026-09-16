using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitMCP.Models
{
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
        public string Status => ReviewRequired ? "需人工複核" : "開孔候選";
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
            if (string.IsNullOrWhiteSpace(request.MepCategory) || string.IsNullOrWhiteSpace(request.HostCategory) || string.IsNullOrWhiteSpace(request.LevelName))
                throw new ArgumentException("請指定來源、分類及 MEP 來源樓層，縮小掃描範圍。");
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
                case "solid_edge_unknown": return "中心線法未檢驗實體邊距";
                default: return "幾何資料不完整，請人工複核";
            }
        }
    }
}
