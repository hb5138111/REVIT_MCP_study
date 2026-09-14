using System.Collections.Generic;

namespace RevitMCP.Models
{
    public enum SupportedTypeCategory
    {
        Walls,
        Floors,
        Ceilings,
        ArchitecturalColumns,
        StructuralColumns,
        StructuralFraming,
        Mullions
    }

    public enum TypeUsageState
    {
        Placed,
        UnplacedCandidate
    }

    public enum TypeInventoryFilter
    {
        All,
        Normal,
        UnplacedCandidate,
        ReviewRequired,
        DataReminder
    }

    public enum TypeInventoryWarningCode
    {
        TypeMarkMissing,
        TypeCommentsMissing,
        TypeNameMissing,
        DuplicateNameCandidate
    }

    public sealed class TypeInventoryRequest
    {
        public SupportedTypeCategory Category { get; set; }
        public bool IncludeTypeMark { get; set; } = true;
        public bool IncludeTypeComments { get; set; } = true;
    }

    public sealed class TypeInventoryResult
    {
        public SupportedTypeCategory Category { get; set; }
        public string DocumentIdentity { get; set; }
        public IReadOnlyList<TypeInventoryRow> Rows { get; set; }
        public int LoadedTypeCount { get; set; }
        public int PlacedTypeCount { get; set; }
        public int UnplacedCandidateCount { get; set; }
        public int ReviewRequiredCount { get; set; }
        public int DataReminderCount { get; set; }
        public IReadOnlyList<TypeInventoryWarningCode> Warnings { get; set; }
    }

    public sealed class TypeInventoryRow
    {
        public SupportedTypeCategory Category { get; set; }
        public long CategoryId { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public long TypeId { get; set; }
        public int InstanceCount { get; set; }
        public bool IsPlaced { get; set; }
        public TypeUsageState UsageState { get; set; }
        public bool HasReviewRequired { get; set; }
        public bool HasDataReminder { get; set; }
        public string TypeMark { get; set; }
        public string TypeComments { get; set; }
        public IReadOnlyList<TypeInventoryWarningCode> WarningCodes { get; set; }
    }
}
