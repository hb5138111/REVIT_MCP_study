using System;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace RevitMCP.UI
{
    public sealed class BimConstructionPanelPage : Page, IDockablePaneProvider
    {
        public static DockablePaneId PaneId { get; } = new DockablePaneId(new Guid("1A955F5C-60F4-4E02-A2C8-BA4BC7606A31"));
        public BimConstructionPanelPage(BimConstructionPanelViewModel viewModel)
        {
            DataContext = viewModel;
            Background = Brushes.White;
            Content = new DetectReviewWorkflowControl { DataContext = viewModel.Coordination };
            Loaded += (_, __) => viewModel.Initialize();
        }
        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
            data.VisibleByDefault = false;
        }
    }
}
