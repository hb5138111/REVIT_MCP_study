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
#if REVIT2026
            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = "施工協調", Content = new DetectReviewWorkflowControl { DataContext = viewModel.Coordination } });
            tabs.Items.Add(new TabItem { Header = "基地／土方", Content = new SiteTerrainControl(viewModel.Site) });
            tabs.Items.Add(new TabItem { Header = "施工圖生產", Content = new DrawingProductionControl(viewModel.Drawing) });
            Content = tabs;
#else
            Content = new DetectReviewWorkflowControl { DataContext = viewModel.Coordination };
#endif
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
