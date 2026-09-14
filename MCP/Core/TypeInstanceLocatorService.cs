using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitMCP.Models;

namespace RevitMCP.Core
{
    /// <summary>Fresh, category-scoped lookup for instances of one type.</summary>
    public sealed class TypeInstanceLocatorService
    {
        public IReadOnlyList<long> FindInstanceIds(
            Document document,
            SupportedTypeCategory category,
            long typeId)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            BuiltInCategory builtInCategory = TypeInventoryService.ResolveCategory(category);

            return new FilteredElementCollector(document)
                .OfCategory(builtInCategory)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(element => element.GetTypeId() != ElementId.InvalidElementId &&
                                  element.GetTypeId().GetIdValue() == typeId)
                .Select(element => element.Id.GetIdValue())
                .OrderBy(id => id)
                .ToList();
        }

        public static string GetDocumentIdentity(Document document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            ProjectInfo projectInformation = document.ProjectInformation;
            if (projectInformation == null || string.IsNullOrWhiteSpace(projectInformation.UniqueId))
                throw new InvalidOperationException("無法取得目前模型的穩定識別資訊。");
            return projectInformation.UniqueId;
        }
    }
}
