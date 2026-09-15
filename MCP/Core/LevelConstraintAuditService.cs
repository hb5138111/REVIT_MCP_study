using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Models;

namespace RevitMCP.Core
{
    /// <summary>Typed, read-only host-document level and constraint audit.</summary>
    public sealed class LevelConstraintAuditService
    {
        private const double ZeroTolerance = 1e-9;

        public LevelConstraintAuditResult GetAudit(UIApplication uiApp, LevelConstraintAuditRequest request)
        {
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));
            if (request == null) throw new ArgumentNullException(nameof(request));
            Document document = uiApp.ActiveUIDocument?.Document
                ?? throw new InvalidOperationException("目前沒有開啟的 Revit 文件。");

            int maxResults = Math.Max(1, request.MaxResults);
            Units units = document.GetUnits();
            Dictionary<long, LevelMetadata> levels = CollectLevels(document, units);
            BuiltInCategory builtInCategory = ResolveCategory(request.Category);
            var retainedRows = new SortedDictionary<long, LevelConstraintAuditRow>();
            int total = 0;
            int normal = 0;
            int reminders = 0;
            int reviews = 0;

            foreach (Element element in new FilteredElementCollector(document)
                .OfCategory(builtInCategory)
                .WhereElementIsNotElementType())
            {
                LevelConstraintAuditRow row = request.Category == LevelConstraintCategory.Walls
                    ? CreateWallRow(element, levels, units)
                    : CreateColumnRow(element, request.Category, levels, units);

                long? baseLevelId = row.Wall?.BaseLevelId ?? row.Column?.BaseLevelId;
                if (request.LevelId.HasValue && baseLevelId != request.LevelId) continue;

                total++;
                if (row.HasReviewRequired) reviews++;
                if (row.HasDataReminder) reminders++;
                if (!row.HasReviewRequired && !row.HasDataReminder) normal++;

                retainedRows[row.ElementId] = row;
                if (retainedRows.Count > maxResults)
                    retainedRows.Remove(retainedRows.Keys.Last());
            }

            return new LevelConstraintAuditResult
            {
                DocumentIdentity = TypeInstanceLocatorService.GetDocumentIdentity(document),
                Category = request.Category,
                TotalMatchedCount = total,
                ReturnedCount = retainedRows.Count,
                IsTruncated = total > retainedRows.Count,
                NormalCount = normal,
                DataReminderCount = reminders,
                ReviewRequiredCount = reviews,
                Rows = retainedRows.Values.ToList(),
                Levels = CreateLevelOptions(levels),
                Warnings = Array.Empty<string>()
            };
        }

        internal static BuiltInCategory ResolveCategory(LevelConstraintCategory category)
        {
            switch (category)
            {
                case LevelConstraintCategory.Walls: return BuiltInCategory.OST_Walls;
                case LevelConstraintCategory.ArchitecturalColumns: return BuiltInCategory.OST_Columns;
                case LevelConstraintCategory.StructuralColumns: return BuiltInCategory.OST_StructuralColumns;
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        private static Dictionary<long, LevelMetadata> CollectLevels(Document document, Units units) =>
            new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToDictionary(
                    level => level.Id.GetIdValue(),
                    level => new LevelMetadata
                    {
                        Id = level.Id.GetIdValue(),
                        Name = level.Name,
                        ProjectElevation = level.ProjectElevation,
                        ElevationDisplay = FormatLength(units, level.ProjectElevation)
                    });

        private static IReadOnlyList<LevelConstraintLevelOption> CreateLevelOptions(
            IReadOnlyDictionary<long, LevelMetadata> levels)
        {
            var result = new List<LevelConstraintLevelOption>
            {
                new LevelConstraintLevelOption { Name = "全部", ElevationDisplay = string.Empty }
            };
            result.AddRange(levels.Values
                .OrderBy(level => level.ProjectElevation)
                .ThenBy(level => level.Id)
                .Select(level => new LevelConstraintLevelOption
                {
                    LevelId = level.Id,
                    Name = level.Name,
                    ProjectElevationInternal = level.ProjectElevation,
                    ElevationDisplay = level.ElevationDisplay
                }));
            return result;
        }

        private static LevelConstraintAuditRow CreateWallRow(
            Element element,
            IReadOnlyDictionary<long, LevelMetadata> levels,
            Units units)
        {
            var warnings = new List<LevelConstraintWarningCode>();
            LevelMetadata baseLevel = ResolveParameterLevel(element, BuiltInParameter.WALL_BASE_CONSTRAINT, levels);
            Parameter topParameter = element.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
            bool topModeReadable = topParameter != null && topParameter.StorageType == StorageType.ElementId;
            long? topId = ReadElementId(topParameter);
            bool topConstrained = topModeReadable && topId.HasValue;
            LevelMetadata topLevel = topConstrained && levels.TryGetValue(topId.Value, out LevelMetadata foundTop)
                ? foundTop
                : null;
            double? baseOffset = ReadDouble(element, BuiltInParameter.WALL_BASE_OFFSET);
            double? topOffset = ReadDouble(element, BuiltInParameter.WALL_TOP_OFFSET);
            double? unconnectedHeight = ReadDouble(element, BuiltInParameter.WALL_USER_HEIGHT_PARAM);

            if (baseLevel == null) warnings.Add(LevelConstraintWarningCode.BaseLevelUnresolved);
            if (!topModeReadable || topConstrained && topLevel == null)
                warnings.Add(LevelConstraintWarningCode.TopLevelUnresolved);
            if (IsNonZero(baseOffset)) warnings.Add(LevelConstraintWarningCode.BaseOffsetNonZero);
            if (topConstrained && IsNonZero(topOffset)) warnings.Add(LevelConstraintWarningCode.TopOffsetNonZero);
            if (topModeReadable && !topConstrained) warnings.Add(LevelConstraintWarningCode.UnconnectedHeight);

            double? computedBase = baseLevel != null && baseOffset.HasValue
                ? baseLevel.ProjectElevation + baseOffset.Value
                : (double?)null;
            double? computedTop = topConstrained
                ? topLevel != null && topOffset.HasValue
                    ? topLevel.ProjectElevation + topOffset.Value
                    : (double?)null
                : computedBase.HasValue && unconnectedHeight.HasValue
                    ? computedBase.Value + unconnectedHeight.Value
                    : (double?)null;
            if (computedBase.HasValue && computedTop.HasValue && computedTop.Value < computedBase.Value)
                warnings.Add(LevelConstraintWarningCode.TopElevationBelowBaseElevation);

            ElementIdentity identity = GetElementIdentity(element, levels);
            return new LevelConstraintAuditRow
            {
                Category = LevelConstraintCategory.Walls,
                ElementId = element.Id.GetIdValue(),
                FamilyName = identity.FamilyName,
                TypeName = identity.TypeName,
                AssociatedLevelId = identity.AssociatedLevelId,
                AssociatedLevelName = identity.AssociatedLevelName,
                ConstraintMode = !topModeReadable
                    ? ConstraintMode.Unknown
                    : topConstrained ? ConstraintMode.TopConstrained : ConstraintMode.Unconnected,
                HasReviewRequired = warnings.Any(IsReviewRequired),
                HasDataReminder = warnings.Any(IsDataReminder),
                WarningCodes = warnings,
                Wall = new WallConstraintData
                {
                    BaseLevelId = baseLevel?.Id,
                    BaseLevelName = baseLevel?.Name ?? "—",
                    TopLevelId = topLevel?.Id,
                    TopLevelName = topLevel?.Name ?? "—",
                    BaseOffsetInternal = baseOffset,
                    BaseOffsetDisplay = FormatOptionalLength(units, baseOffset),
                    TopOffsetInternal = topConstrained ? topOffset : null,
                    TopOffsetDisplay = topConstrained ? FormatOptionalLength(units, topOffset) : "—",
                    UnconnectedHeightInternal = topModeReadable && !topConstrained ? unconnectedHeight : null,
                    UnconnectedHeightDisplay = topModeReadable && !topConstrained
                        ? FormatOptionalLength(units, unconnectedHeight)
                        : "—"
                }
            };
        }

        private static LevelConstraintAuditRow CreateColumnRow(
            Element element,
            LevelConstraintCategory category,
            IReadOnlyDictionary<long, LevelMetadata> levels,
            Units units)
        {
            var warnings = new List<LevelConstraintWarningCode>();
            LevelMetadata baseLevel = ResolveParameterLevel(element, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, levels);
            LevelMetadata topLevel = ResolveParameterLevel(element, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, levels);
            double? baseOffset = ReadDouble(element, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
            double? topOffset = ReadDouble(element, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);

            if (baseLevel == null) warnings.Add(LevelConstraintWarningCode.BaseLevelUnresolved);
            if (topLevel == null) warnings.Add(LevelConstraintWarningCode.TopLevelUnresolved);
            if (IsNonZero(baseOffset)) warnings.Add(LevelConstraintWarningCode.BaseOffsetNonZero);
            if (IsNonZero(topOffset)) warnings.Add(LevelConstraintWarningCode.TopOffsetNonZero);
            if (baseLevel != null && topLevel != null && baseOffset.HasValue && topOffset.HasValue &&
                topLevel.ProjectElevation + topOffset.Value < baseLevel.ProjectElevation + baseOffset.Value)
                warnings.Add(LevelConstraintWarningCode.TopElevationBelowBaseElevation);

            ElementIdentity identity = GetElementIdentity(element, levels);
            return new LevelConstraintAuditRow
            {
                Category = category,
                ElementId = element.Id.GetIdValue(),
                FamilyName = identity.FamilyName,
                TypeName = identity.TypeName,
                AssociatedLevelId = identity.AssociatedLevelId,
                AssociatedLevelName = identity.AssociatedLevelName,
                ConstraintMode = ConstraintMode.BaseAndTopConstrained,
                HasReviewRequired = warnings.Any(IsReviewRequired),
                HasDataReminder = warnings.Any(IsDataReminder),
                WarningCodes = warnings,
                Column = new ColumnConstraintData
                {
                    BaseLevelId = baseLevel?.Id,
                    BaseLevelName = baseLevel?.Name ?? "—",
                    TopLevelId = topLevel?.Id,
                    TopLevelName = topLevel?.Name ?? "—",
                    BaseOffsetInternal = baseOffset,
                    BaseOffsetDisplay = FormatOptionalLength(units, baseOffset),
                    TopOffsetInternal = topOffset,
                    TopOffsetDisplay = FormatOptionalLength(units, topOffset)
                }
            };
        }

        private static ElementIdentity GetElementIdentity(
            Element element,
            IReadOnlyDictionary<long, LevelMetadata> levels)
        {
            ElementType type = element.Document.GetElement(element.GetTypeId()) as ElementType;
            long? associatedLevelId = element.LevelId != null && element.LevelId != ElementId.InvalidElementId
                ? element.LevelId.GetIdValue()
                : (long?)null;
            string associatedLevelName = associatedLevelId.HasValue &&
                                         levels.TryGetValue(associatedLevelId.Value, out LevelMetadata level)
                ? level.Name
                : "—";
            return new ElementIdentity
            {
                FamilyName = type?.FamilyName ?? "—",
                TypeName = type?.Name ?? "—",
                AssociatedLevelId = associatedLevelId,
                AssociatedLevelName = associatedLevelName
            };
        }

        private static LevelMetadata ResolveParameterLevel(
            Element element,
            BuiltInParameter builtInParameter,
            IReadOnlyDictionary<long, LevelMetadata> levels)
        {
            long? id = ReadElementId(element.get_Parameter(builtInParameter));
            return id.HasValue && levels.TryGetValue(id.Value, out LevelMetadata level) ? level : null;
        }

        private static long? ReadElementId(Parameter parameter)
        {
            if (parameter == null || parameter.StorageType != StorageType.ElementId) return null;
            ElementId id = parameter.AsElementId();
            return id != null && id != ElementId.InvalidElementId && id.GetIdValue() > 0
                ? id.GetIdValue()
                : (long?)null;
        }

        private static double? ReadDouble(Element element, BuiltInParameter builtInParameter)
        {
            Parameter parameter = element.get_Parameter(builtInParameter);
            return parameter != null && parameter.StorageType == StorageType.Double
                ? parameter.AsDouble()
                : (double?)null;
        }

        private static bool IsNonZero(double? value) =>
            value.HasValue && Math.Abs(value.Value) > ZeroTolerance;

        private static bool IsReviewRequired(LevelConstraintWarningCode code) =>
            code == LevelConstraintWarningCode.BaseLevelUnresolved ||
            code == LevelConstraintWarningCode.TopLevelUnresolved ||
            code == LevelConstraintWarningCode.TopElevationBelowBaseElevation;

        private static bool IsDataReminder(LevelConstraintWarningCode code) =>
            code == LevelConstraintWarningCode.BaseOffsetNonZero ||
            code == LevelConstraintWarningCode.TopOffsetNonZero ||
            code == LevelConstraintWarningCode.UnconnectedHeight;

        private static string FormatOptionalLength(Units units, double? value) =>
            value.HasValue ? FormatLength(units, value.Value) : "—";

        private static string FormatLength(Units units, double value) =>
            UnitFormatUtils.Format(units, SpecTypeId.Length, value, false);

        private sealed class LevelMetadata
        {
            public long Id { get; set; }
            public string Name { get; set; }
            public double ProjectElevation { get; set; }
            public string ElevationDisplay { get; set; }
        }

        private sealed class ElementIdentity
        {
            public string FamilyName { get; set; }
            public string TypeName { get; set; }
            public long? AssociatedLevelId { get; set; }
            public string AssociatedLevelName { get; set; }
        }
    }
}
