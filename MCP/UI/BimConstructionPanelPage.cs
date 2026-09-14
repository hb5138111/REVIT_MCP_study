using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.UI;
using WpfBinding = System.Windows.Data.Binding;

namespace RevitMCP.UI
{
    public sealed class BimConstructionPanelPage : Page, IDockablePaneProvider
    {
        private static readonly Guid PaneGuid = new Guid("1A955F5C-60F4-4E02-A2C8-BA4BC7606A31");

        public static DockablePaneId PaneId { get; } = new DockablePaneId(PaneGuid);

        public BimConstructionPanelPage(BimConstructionPanelViewModel viewModel)
        {
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Background = Brushes.White;
            Content = BuildContent();
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
            data.VisibleByDefault = false;
        }

        private UIElement BuildContent()
        {
            var root = new StackPanel { Margin = new Thickness(10) };
            root.Children.Add(new TextBlock
            {
                Text = "BIM Construction — Model Summary",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var refresh = new Button
            {
                Content = "Refresh",
                MinWidth = 90,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8)
            };
            refresh.SetBinding(Button.CommandProperty, new WpfBinding("RefreshCommand"));
            root.Children.Add(refresh);

            root.Children.Add(BoundText("Status: ", "StatusMessage"));
            root.Children.Add(BoundText("Last Refresh: ", "Result.RefreshedAt", "{0:yyyy-MM-dd HH:mm:ss zzz}"));

            root.Children.Add(Group("Current Document — Host Document",
                BoundText("Project: ", "Result.CurrentDocument.ProjectName"),
                BoundText("Number: ", "Result.CurrentDocument.ProjectNumber"),
                BoundText("Status: ", "Result.CurrentDocument.ProjectStatus"),
                BoundText("Client: ", "Result.CurrentDocument.ClientName"),
                BoundText("Building: ", "Result.CurrentDocument.BuildingName")));

            root.Children.Add(Group("Active View — Active View",
                BoundText("Name: ", "Result.ActiveView.Name"),
                BoundText("Type: ", "Result.ActiveView.ViewType"),
                BoundText("Element ID: ", "Result.ActiveView.ElementId"),
                BoundText("Scale: 1:", "Result.ActiveView.Scale"),
                BoundText("Active View Level: ", "Result.ActiveView.LevelName")));

            root.Children.Add(Group("Main Document Levels — Host Document",
                List("Result.Levels", "Name", "Elevation", "{0:N2} mm")));

            root.Children.Add(Group("Revit Links — Link",
                List("Result.Links", "FileName", "IsLoaded", "Loaded: {0}")));

            root.Children.Add(Group("Active View Categories — Top 10 — Active View",
                BoundText("Total category groups: ", "Result.TotalCategoryGroups"),
                List("Result.Categories", "Name", "Count", "Count: {0}")));

            root.Children.Add(Group("Scope",
                BoundText("Document: ", "Result.Scope.Document"),
                BoundText("Active View: ", "Result.Scope.ActiveView"),
                BoundText("Levels: ", "Result.Scope.Levels"),
                BoundText("Links: ", "Result.Scope.Links"),
                BoundText("Categories: ", "Result.Scope.Categories")));

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = root
            };
        }

        private static GroupBox Group(string header, params UIElement[] children)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };
            foreach (UIElement child in children) panel.Children.Add(child);
            return new GroupBox
            {
                Header = header,
                Content = panel,
                Margin = new Thickness(0, 4, 0, 4),
                Padding = new Thickness(4)
            };
        }

        private static TextBlock BoundText(string label, string path, string valueFormat = null)
        {
            var text = new TextBlock { Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap };
            var binding = new WpfBinding(path)
            {
                StringFormat = valueFormat == null ? label + "{0}" : label + valueFormat,
                TargetNullValue = label + "—"
            };
            text.SetBinding(TextBlock.TextProperty, binding);
            return text;
        }

        private static ItemsControl List(
            string path,
            string primaryProperty,
            string secondaryProperty,
            string secondaryFormat)
        {
            var template = new DataTemplate();
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.MarginProperty, new Thickness(0, 2, 0, 2));

            var primary = new FrameworkElementFactory(typeof(TextBlock));
            primary.SetBinding(TextBlock.TextProperty, new WpfBinding(primaryProperty));
            row.AppendChild(primary);

            var secondary = new FrameworkElementFactory(typeof(TextBlock));
            secondary.SetValue(TextBlock.ForegroundProperty, Brushes.DimGray);
            secondary.SetBinding(TextBlock.TextProperty, new WpfBinding(secondaryProperty)
            {
                StringFormat = secondaryFormat
            });
            row.AppendChild(secondary);

            template.VisualTree = row;
            var items = new ItemsControl { ItemTemplate = template };
            items.SetBinding(ItemsControl.ItemsSourceProperty, new WpfBinding(path));
            return items;
        }
    }
}
