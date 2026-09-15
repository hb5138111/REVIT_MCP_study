using System.Collections.Generic;

namespace RevitMCP.Models
{
    public enum LevelConstraintCategory
    {
        Walls,
        ArchitecturalColumns,
        StructuralColumns
    }

    public enum ConstraintMode
    {
        Unknown,
        TopConstrained,
        Unconnected,
        BaseAndTopConstrained
    }

    public enum LevelConstraintStatus
    {
        Normal,
        DataReminder,
        ReviewRequired
    }

    public enum LevelConstraintWarningCode
    {
        BaseLevelUnresolved,
        TopLevelUnresolved,
        TopElevationBelowBaseElevation,
        BaseOffsetNonZero,
        TopOffsetNonZero,
        UnconnectedHeight
    }

    public sealed class LevelConstraintAuditRequest
    {
        public LevelConstraintCategory Category { get; set; }
        public long? LevelId { get; set; }
        public int MaxResults { get; set; } = 500;
    }

    public sealed class LevelConstraintAuditResult
    {
        public string DocumentIdentity { get; set; }
        public LevelConstraintCategory Category { get; set; }
        public string Scope { get; set; } = "Host Document";
        public int TotalMatchedCount { get; set; }
        public int ReturnedCount { get; set; }
        public bool IsTruncated { get; set; }
        public int NormalCount { get; set; }
        public int DataReminderCount { get; set; }
        public int ReviewRequiredCount { get; set; }
        public IReadOnlyList<LevelConstraintAuditRow> Rows { get; set; }
        public IReadOnlyList<LevelConstraintLevelOption> Levels { get; set; }
        public IReadOnlyList<string> Warnings { get; set; }
    }

    public sealed class LevelConstraintAuditRow
    {
        public LevelConstraintCategory Category { get; set; }
        public long ElementId { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public long? AssociatedLevelId { get; set; }
        public string AssociatedLevelName { get; set; }
        public ConstraintMode ConstraintMode { get; set; }
        public bool HasReviewRequired { get; set; }
        public bool HasDataReminder { get; set; }
        public IReadOnlyList<LevelConstraintWarningCode> WarningCodes { get; set; }
        public WallConstraintData Wall { get; set; }
        public ColumnConstraintData Column { get; set; }

        public string ConstraintModeDisplay => ConstraintMode switch
        {
            ConstraintMode.TopConstrained => "頂部約束",
            ConstraintMode.Unconnected => "未約束高度",
            ConstraintMode.BaseAndTopConstrained => "基準／頂部約束",
            _ => "—"
        };

        public string StatusDisplay => HasReviewRequired
            ? "需檢查"
            : HasDataReminder ? "資料提醒" : "正常";

        public string Description => string.Join("、", CreateWarningLabels());
        public string BaseLevelName => Wall?.BaseLevelName ?? Column?.BaseLevelName ?? "—";
        public string TopLevelName => Wall?.TopLevelName ?? Column?.TopLevelName ?? "—";
        public string BaseOffsetDisplay => Wall?.BaseOffsetDisplay ?? Column?.BaseOffsetDisplay ?? "—";
        public string TopOffsetDisplay => Wall?.TopOffsetDisplay ?? Column?.TopOffsetDisplay ?? "—";
        public string UnconnectedHeightDisplay => Wall?.UnconnectedHeightDisplay ?? "—";

        private IEnumerable<string> CreateWarningLabels()
        {
            if (WarningCodes == null) yield break;
            foreach (LevelConstraintWarningCode code in WarningCodes)
            {
                switch (code)
                {
                    case LevelConstraintWarningCode.BaseLevelUnresolved: yield return "基準樓層無法解析"; break;
                    case LevelConstraintWarningCode.TopLevelUnresolved: yield return "頂部樓層無法解析"; break;
                    case LevelConstraintWarningCode.TopElevationBelowBaseElevation: yield return "頂部標高低於基準標高"; break;
                    case LevelConstraintWarningCode.BaseOffsetNonZero: yield return "基準偏移非零"; break;
                    case LevelConstraintWarningCode.TopOffsetNonZero: yield return "頂部偏移非零"; break;
                    case LevelConstraintWarningCode.UnconnectedHeight: yield return "未約束高度"; break;
                }
            }
        }
    }

    public sealed class WallConstraintData
    {
        public long? BaseLevelId { get; set; }
        public string BaseLevelName { get; set; }
        public long? TopLevelId { get; set; }
        public string TopLevelName { get; set; }
        public double? BaseOffsetInternal { get; set; }
        public string BaseOffsetDisplay { get; set; }
        public double? TopOffsetInternal { get; set; }
        public string TopOffsetDisplay { get; set; }
        public double? UnconnectedHeightInternal { get; set; }
        public string UnconnectedHeightDisplay { get; set; }
    }

    public sealed class ColumnConstraintData
    {
        public long? BaseLevelId { get; set; }
        public string BaseLevelName { get; set; }
        public long? TopLevelId { get; set; }
        public string TopLevelName { get; set; }
        public double? BaseOffsetInternal { get; set; }
        public string BaseOffsetDisplay { get; set; }
        public double? TopOffsetInternal { get; set; }
        public string TopOffsetDisplay { get; set; }
    }

    public sealed class LevelConstraintLevelOption
    {
        public long? LevelId { get; set; }
        public string Name { get; set; }
        public double? ProjectElevationInternal { get; set; }
        public string ElevationDisplay { get; set; }
        public string Label => LevelId.HasValue ? Name + "（" + ElevationDisplay + "）" : Name;
    }
}
