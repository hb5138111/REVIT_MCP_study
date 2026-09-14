using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Models;

namespace RevitMCP.Core
{
    /// <summary>Read-only type inventory shared by native UI and future adapters.</summary>
    public sealed class TypeInventoryService
    {
        public TypeInventoryResult GetInventory(UIApplication uiApp, TypeInventoryRequest request)
        {
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));
            if (request == null) throw new ArgumentNullException(nameof(request));
            Document document = uiApp.ActiveUIDocument?.Document
                ?? throw new InvalidOperationException("目前沒有開啟的 Revit 文件。");

            BuiltInCategory builtInCategory = ResolveCategory(request.Category);
            List<ElementType> types = CollectTypes(document, request.Category, builtInCategory);
            Dictionary<long, int> instanceCounts = new FilteredElementCollector(document)
                .OfCategory(builtInCategory)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(element => element.GetTypeId() != ElementId.InvalidElementId)
                .GroupBy(element => element.GetTypeId().GetIdValue())
                .ToDictionary(group => group.Key, group => group.Count());

            var rows = types.Select(type => CreateRow(type, request, builtInCategory, instanceCounts)).ToList();
            AddDuplicateNameWarnings(rows);
            foreach (TypeInventoryRow row in rows)
                row.Status = ResolveStatus(row);

            return new TypeInventoryResult
            {
                Category = request.Category,
                Rows = rows.OrderBy(row => row.FamilyName).ThenBy(row => row.TypeName).ToList(),
                LoadedTypeCount = rows.Count,
                PlacedTypeCount = rows.Count(row => row.IsPlaced),
                UnplacedCandidateCount = rows.Count(row => !row.IsPlaced),
                ReviewCount = rows.Count(row => row.WarningCodes.Count > 0),
                Warnings = rows.SelectMany(row => row.WarningCodes).Distinct().ToList()
            };
        }

        private static BuiltInCategory ResolveCategory(SupportedTypeCategory category)
        {
            switch (category)
            {
                case SupportedTypeCategory.Walls: return BuiltInCategory.OST_Walls;
                case SupportedTypeCategory.Floors: return BuiltInCategory.OST_Floors;
                case SupportedTypeCategory.Ceilings: return BuiltInCategory.OST_Ceilings;
                case SupportedTypeCategory.ArchitecturalColumns: return BuiltInCategory.OST_Columns;
                case SupportedTypeCategory.StructuralColumns: return BuiltInCategory.OST_StructuralColumns;
                case SupportedTypeCategory.StructuralFraming: return BuiltInCategory.OST_StructuralFraming;
                case SupportedTypeCategory.Mullions: return BuiltInCategory.OST_CurtainWallMullions;
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        private static List<ElementType> CollectTypes(
            Document document,
            SupportedTypeCategory category,
            BuiltInCategory builtInCategory)
        {
            if (category == SupportedTypeCategory.Walls)
                return new FilteredElementCollector(document).OfClass(typeof(WallType)).Cast<ElementType>().ToList();
            if (category == SupportedTypeCategory.Floors)
                return new FilteredElementCollector(document).OfClass(typeof(FloorType)).Cast<ElementType>().ToList();
            if (category == SupportedTypeCategory.Ceilings)
                return new FilteredElementCollector(document).OfClass(typeof(CeilingType)).Cast<ElementType>().ToList();
            if (category == SupportedTypeCategory.Mullions)
                return new FilteredElementCollector(document).OfClass(typeof(MullionType)).Cast<ElementType>().ToList();

            return new FilteredElementCollector(document)
                .OfCategory(builtInCategory)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol))
                .Cast<ElementType>()
                .ToList();
        }

        private static TypeInventoryRow CreateRow(
            ElementType type,
            TypeInventoryRequest request,
            BuiltInCategory builtInCategory,
            IReadOnlyDictionary<long, int> instanceCounts)
        {
            long typeId = type.Id.GetIdValue();
            int instanceCount = instanceCounts.TryGetValue(typeId, out int count) ? count : 0;
            string typeName = type.Name ?? string.Empty;
            string typeMark = request.IncludeTypeMark
                ? ReadText(type, BuiltInParameter.ALL_MODEL_TYPE_MARK, "類型標記", "Type Mark")
                : string.Empty;
            string typeComments = request.IncludeTypeComments
                ? ReadText(type, BuiltInParameter.ALL_MODEL_TYPE_COMMENTS, "類型備註", "Type Comments")
                : string.Empty;

            var warningCodes = new List<TypeInventoryWarningCode>();
            if (request.IncludeTypeMark && string.IsNullOrWhiteSpace(typeMark))
                warningCodes.Add(TypeInventoryWarningCode.TypeMarkMissing);
            if (request.IncludeTypeComments && string.IsNullOrWhiteSpace(typeComments))
                warningCodes.Add(TypeInventoryWarningCode.TypeCommentsMissing);
            if (string.IsNullOrWhiteSpace(typeName))
                warningCodes.Add(TypeInventoryWarningCode.TypeNameMissing);

            return new TypeInventoryRow
            {
                Category = request.Category,
                CategoryId = (long)builtInCategory,
                FamilyName = type.FamilyName ?? string.Empty,
                TypeName = typeName,
                TypeId = typeId,
                InstanceCount = instanceCount,
                IsPlaced = instanceCount > 0,
                TypeMark = typeMark,
                TypeComments = typeComments,
                WarningCodes = warningCodes
            };
        }

        private static string ReadText(
            ElementType type,
            BuiltInParameter builtInParameter,
            params string[] fallbackNames)
        {
            Parameter parameter = type.get_Parameter(builtInParameter);
            if (parameter == null)
            {
                foreach (string fallbackName in fallbackNames)
                {
                    parameter = type.LookupParameter(fallbackName);
                    if (parameter != null) break;
                }
            }

            return parameter?.AsString() ?? parameter?.AsValueString() ?? string.Empty;
        }

        private static void AddDuplicateNameWarnings(IReadOnlyList<TypeInventoryRow> rows)
        {
            var duplicateGroups = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.TypeName))
                .GroupBy(row => row.TypeName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);

            foreach (var group in duplicateGroups)
            foreach (TypeInventoryRow row in group)
                ((List<TypeInventoryWarningCode>)row.WarningCodes).Add(TypeInventoryWarningCode.DuplicateNameCandidate);
        }

        private static TypeInventoryStatus ResolveStatus(TypeInventoryRow row)
        {
            if (!row.IsPlaced) return TypeInventoryStatus.UnplacedCandidate;
            return row.WarningCodes.Count > 0
                ? TypeInventoryStatus.ReviewRequired
                : TypeInventoryStatus.Normal;
        }
    }
}
