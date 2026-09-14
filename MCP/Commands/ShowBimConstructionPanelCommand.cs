using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.UI;

namespace RevitMCP.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ShowBimConstructionPanelCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            DockablePane pane = commandData.Application.GetDockablePane(BimConstructionPanelPage.PaneId);
            if (pane.IsShown())
                pane.Hide();
            else
                pane.Show();

            return Result.Succeeded;
        }
    }
}
