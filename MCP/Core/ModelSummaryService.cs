using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Models;

namespace RevitMCP.Core
{
    /// <summary>
    /// Read-only Revit query backend for the BIM Construction Panel model summary.
    /// </summary>
    public sealed class ModelSummaryService
    {
        public ModelSummaryResult GetSummary(UIApplication uiApp, ModelSummaryRequest request)
        {
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));
            if (request == null) throw new ArgumentNullException(nameof(request));

            UIDocument uiDocument = uiApp.ActiveUIDocument;
            if (uiDocument == null) throw new InvalidOperationException("目前沒有開啟的 Revit 文件。");

            Document document = uiDocument.Document;
            View activeView = uiDocument.ActiveView;
            int categoryLimit = Math.Max(1, request.CategoryLimit);
            var warnings = new List<string>();
            int totalCategoryGroups;
            IReadOnlyList<CategorySummary> categories = GetActiveViewCategories(
                document, activeView, categoryLimit, warnings, out totalCategoryGroups);

            return new ModelSummaryResult
            {
                CurrentDocument = GetDocumentSummary(document),
                ActiveView = GetActiveViewContext(uiDocument),
                Levels = GetLevels(document),
                Links = GetLinks(uiApp),
                Categories = categories,
                TotalCategoryGroups = totalCategoryGroups,
                CategoryLimit = categoryLimit,
                Scope = new ModelSummaryScope(),
                RefreshedAt = DateTimeOffset.Now,
                Warnings = warnings
            };
        }

        public DocumentSummary GetDocumentSummary(Document document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            ProjectInfo projectInfo = document.ProjectInformation;

            return new DocumentSummary
            {
                ProjectName = document.Title,
                BuildingName = projectInfo?.BuildingName ?? string.Empty,
                OrganizationName = projectInfo?.OrganizationName ?? string.Empty,
                Author = projectInfo?.Author ?? string.Empty,
                Address = projectInfo?.Address ?? string.Empty,
                ClientName = projectInfo?.ClientName ?? string.Empty,
                ProjectNumber = projectInfo?.Number ?? string.Empty,
                ProjectStatus = projectInfo?.Status ?? string.Empty
            };
        }

        public ActiveViewContext GetActiveViewContext(UIDocument uiDocument)
        {
            if (uiDocument == null) throw new ArgumentNullException(nameof(uiDocument));
            View activeView = uiDocument.ActiveView;
            Level level = activeView.GenLevel;

            return new ActiveViewContext
            {
                ElementId = Convert.ToInt64(activeView.Id.GetIdValue()),
                Name = activeView.Name,
                ViewType = activeView.ViewType.ToString(),
                LevelId = level == null ? (long?)null : Convert.ToInt64(level.Id.GetIdValue()),
                LevelName = level?.Name ?? string.Empty,
                Scale = activeView.Scale
            };
        }

        public IReadOnlyList<LevelSummary> GetLevels(Document document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            return new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => level.Elevation)
                .Select(level => new LevelSummary
                {
                    ElementId = Convert.ToInt64(level.Id.GetIdValue()),
                    Name = level.Name,
                    Elevation = Math.Round(level.Elevation * 304.8, 2)
                })
                .ToList();
        }

        public IReadOnlyList<CategorySummary> GetActiveViewCategories(
            Document document,
            View activeView,
            int categoryLimit,
            IList<string> warnings,
            out int totalCategoryGroups)
        {
            totalCategoryGroups = 0;
            try
            {
                var categoryGroups = new FilteredElementCollector(document, activeView.Id)
                    .WhereElementIsNotElementType()
                    .Where(element => element.Category != null)
                    .GroupBy(element => Convert.ToInt64(element.Category.Id.GetIdValue()))
                    .Select(group => CreateCategorySummary(document, group.Key, group.Count()))
                    .OrderByDescending(category => category.Count)
                    .ThenBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                totalCategoryGroups = categoryGroups.Count;
                return categoryGroups.Take(Math.Max(1, categoryLimit)).ToList();
            }
            catch (Exception ex)
            {
                warnings?.Add("Active View Categories 無法讀取：" + ex.Message);
                return new List<CategorySummary>();
            }
        }

        public IReadOnlyList<LinkSummary> GetLinks(UIApplication uiApp)
        {
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));
            return new LinkedModelHelper(uiApp).GetLinkSummaries();
        }

        private static CategorySummary CreateCategorySummary(Document document, long categoryId, int count)
        {
            Category category = Category.GetCategory(document, new ElementId(categoryId));
            string displayName = category?.Name ?? "未知品類";
            string internalName = displayName;

            try
            {
                BuiltInCategory builtInCategory = (BuiltInCategory)unchecked((int)categoryId);
                if (Enum.IsDefined(typeof(BuiltInCategory), builtInCategory))
                    internalName = builtInCategory.ToString().Replace("OST_", string.Empty);
            }
            catch
            {
                internalName = displayName;
            }

            return new CategorySummary
            {
                Name = displayName,
                InternalName = internalName,
                Count = count
            };
        }
    }
}
