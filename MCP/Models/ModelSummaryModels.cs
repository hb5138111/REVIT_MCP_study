using System;
using System.Collections.Generic;

namespace RevitMCP.Models
{
    public sealed class ModelSummaryRequest
    {
        public int CategoryLimit { get; set; } = 10;
    }

    public sealed class ModelSummaryResult
    {
        public DocumentSummary CurrentDocument { get; set; }
        public ActiveViewContext ActiveView { get; set; }
        public IReadOnlyList<LevelSummary> Levels { get; set; }
        public IReadOnlyList<LinkSummary> Links { get; set; }
        public IReadOnlyList<CategorySummary> Categories { get; set; }
        public int TotalCategoryGroups { get; set; }
        public int CategoryLimit { get; set; }
        public ModelSummaryScope Scope { get; set; }
        public DateTimeOffset RefreshedAt { get; set; }
        public IReadOnlyList<string> Warnings { get; set; }
    }

    public sealed class DocumentSummary
    {
        public string ProjectName { get; set; }
        public string BuildingName { get; set; }
        public string OrganizationName { get; set; }
        public string Author { get; set; }
        public string Address { get; set; }
        public string ClientName { get; set; }
        public string ProjectNumber { get; set; }
        public string ProjectStatus { get; set; }
    }

    public sealed class ActiveViewContext
    {
        public long ElementId { get; set; }
        public string Name { get; set; }
        public string ViewType { get; set; }
        public long? LevelId { get; set; }
        public string LevelName { get; set; }
        public int Scale { get; set; }
    }

    public sealed class LevelSummary
    {
        public long ElementId { get; set; }
        public string Name { get; set; }
        public double Elevation { get; set; }
    }

    public sealed class LinkSummary
    {
        public long LinkInstanceId { get; set; }
        public string LinkTypeName { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public bool IsLoaded { get; set; }
        public LinkTransformSummary Transform { get; set; }
    }

    public sealed class LinkTransformSummary
    {
        public double OriginX { get; set; }
        public double OriginY { get; set; }
        public double OriginZ { get; set; }
        public bool IsIdentity { get; set; }
    }

    public sealed class CategorySummary
    {
        public string Name { get; set; }
        public string InternalName { get; set; }
        public int Count { get; set; }
    }

    public sealed class ModelSummaryScope
    {
        public string Document { get; set; } = "Host Document";
        public string Levels { get; set; } = "Host Document";
        public string ActiveView { get; set; } = "Active View";
        public string Categories { get; set; } = "Active View";
        public string Links { get; set; } = "Link";
    }
}
