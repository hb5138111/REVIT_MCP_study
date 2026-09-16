using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Models
{
    public enum WorkflowUiPattern { AuditPattern, DetectReviewPattern, PreviewApplyPattern, BatchCreatePattern, TakeoffPattern, CompliancePattern }
    public enum WorkflowCategory { Coordination, ModelAudit, Takeoff, Drawing, Compliance }
    public enum WorkflowRiskLevel { ReadOnly, Preview, ModelWrite }
    [Flags]
    public enum WorkflowCapability { None = 0, Host = 1, Link = 2, Navigation = 4, Export = 8, Preview = 16, Apply = 32, ReadBack = 64 }

    public sealed class WorkflowDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string DomainPath { get; set; } = string.Empty;
        public WorkflowCategory Category { get; set; }
        public WorkflowUiPattern Pattern { get; set; }
        public WorkflowRiskLevel Risk { get; set; }
        public WorkflowCapability Capabilities { get; set; }
        public string[] RequiredSettings { get; set; } = Array.Empty<string>();
        public bool Enabled { get; set; }
        public string Limitation { get; set; } = string.Empty;
    }

    public sealed class WorkflowPatternDefinition
    {
        public WorkflowUiPattern Pattern { get; set; }
        public string[] Inputs { get; set; } = Array.Empty<string>();
        public string[] Filters { get; set; } = Array.Empty<string>();
        public string[] Summary { get; set; } = Array.Empty<string>();
        public string[] ResultRows { get; set; } = Array.Empty<string>();
        public string[] RowActions { get; set; } = Array.Empty<string>();
        public bool Preview { get; set; }
        public bool Apply { get; set; }
        public bool ReadBack { get; set; }
        public bool Navigation { get; set; }
        public bool Export { get; set; }
        public string[] Warnings { get; set; } = Array.Empty<string>();
    }

    public static class WorkflowRegistry
    {
        public static IReadOnlyList<WorkflowDefinition> Definitions { get; } = new[]
        {
            new WorkflowDefinition { Id = "coordination", Title = "協調掃描", DomainPath = "domain/mep-opening-candidate-scan.md",
                Category = WorkflowCategory.Coordination, Pattern = WorkflowUiPattern.DetectReviewPattern,
                Risk = WorkflowRiskLevel.ReadOnly, Capabilities = WorkflowCapability.Host | WorkflowCapability.Link | WorkflowCapability.Navigation | WorkflowCapability.Export,
                Enabled = true, Limitation = "同次掃描分類碰撞、開孔與穿梁候選；中心線法不含管件、保溫與擦碰，不代表結構核准。" },
            new WorkflowDefinition { Id = "sleeves", Title = "套管分類", DomainPath = "domain/sleeve-classification-protocol.md",
                Category = WorkflowCategory.Coordination, Pattern = WorkflowUiPattern.CompliancePattern,
                Enabled = false, Limitation = "缺少完整套管實體分類與邊距驗證。" },
            new WorkflowDefinition { Id = "beam-compliance", Title = "穿梁規則檢查", DomainPath = "domain/beam-penetration-rc.md",
                Category = WorkflowCategory.Coordination, Pattern = WorkflowUiPattern.CompliancePattern,
                Enabled = false, Limitation = "既有演算法未完整覆蓋 RC／SC／SRC 規則；穿梁候選整合於協調掃描，均需人工複核。" }
        };

        public static IReadOnlyList<WorkflowPatternDefinition> Patterns { get; } =
            Enum.GetValues(typeof(WorkflowUiPattern)).Cast<WorkflowUiPattern>().Select(pattern => new WorkflowPatternDefinition
            {
                Pattern = pattern, Inputs = new[] { "DocumentIdentity", "ExplicitScope", "RequiredSettings" },
                Filters = new[] { "Search", "Status", "Warnings" }, Summary = new[] { "TotalMatchedCount", "ReturnedCount", "IsTruncated" },
                ResultRows = new[] { "ElementLookup", "Result", "Evidence", "Status" }, RowActions = new[] { "Select", "Previous", "Next" },
                Preview = pattern == WorkflowUiPattern.PreviewApplyPattern || pattern == WorkflowUiPattern.BatchCreatePattern,
                Apply = pattern == WorkflowUiPattern.PreviewApplyPattern || pattern == WorkflowUiPattern.BatchCreatePattern,
                ReadBack = pattern == WorkflowUiPattern.PreviewApplyPattern || pattern == WorkflowUiPattern.BatchCreatePattern,
                Navigation = true, Export = true, Warnings = new[] { "IncompleteGeometry", "ReviewRequired", "Truncation", "StaleDocument" }
            }).ToArray();
    }
}
