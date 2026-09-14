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
                Text = "營造 BIM 工具 — 模型摘要",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var refresh = new Button
            {
                Content = "重新整理",
                MinWidth = 90,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8)
            };
            refresh.SetBinding(Button.CommandProperty, new WpfBinding("RefreshCommand"));
            root.Children.Add(refresh);

            root.Children.Add(BoundText("狀態：", "StatusMessage"));
            root.Children.Add(BoundText("最後更新時間：", "Result.RefreshedAt", "{0:yyyy-MM-dd HH:mm:ss zzz}"));

            root.Children.Add(Group("目前模型 — 主模型",
                BoundText("專案名稱：", "Result.CurrentDocument.ProjectName"),
                BoundText("專案編號：", "Result.CurrentDocument.ProjectNumber"),
                BoundText("專案狀態：", "Result.CurrentDocument.ProjectStatus"),
                BoundText("業主：", "Result.CurrentDocument.ClientName"),
                BoundText("建築名稱：", "Result.CurrentDocument.BuildingName")));

            root.Children.Add(Group("目前視圖 — 目前視圖",
                BoundText("名稱：", "Result.ActiveView.Name"),
                BoundText("視圖類型：", "Result.ActiveView.ViewType", null, new ViewTypeDisplayConverter()),
                BoundText("元素編號：", "Result.ActiveView.ElementId"),
                BoundText("比例：1:", "Result.ActiveView.Scale"),
                BoundText("目前樓層：", "Result.ActiveView.LevelName")));

            root.Children.Add(Group("主模型樓層 — 主模型",
                List("Result.Levels", "Name", "Elevation", "標高：{0:N2} mm")));

            root.Children.Add(Group("Revit 連結模型 — 連結模型",
                EmptyState("Result.Links", "目前無 Revit 連結模型"),
                List("Result.Links", "FileName", "IsLoaded", "已載入：{0}")));

            root.Children.Add(Group("目前視圖構件分類 — 前 10 項 — 目前視圖",
                BoundText("分類總數：", "Result.TotalCategoryGroups"),
                List("Result.Categories", "Name", "Count", "數量：{0}")));

            root.Children.Add(Group("資料範圍",
                BoundText("目前模型：", "Result.Scope.Document", null, new ScopeDisplayConverter()),
                BoundText("目前視圖：", "Result.Scope.ActiveView", null, new ScopeDisplayConverter()),
                BoundText("主模型樓層：", "Result.Scope.Levels", null, new ScopeDisplayConverter()),
                BoundText("Revit 連結模型：", "Result.Scope.Links", null, new ScopeDisplayConverter()),
                BoundText("構件分類：", "Result.Scope.Categories", null, new ScopeDisplayConverter())));

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

        private static TextBlock BoundText(
            string label,
            string path,
            string? valueFormat = null,
            IValueConverter? converter = null)
        {
            var text = new TextBlock { Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap };
            var binding = new WpfBinding(path)
            {
                StringFormat = valueFormat == null ? label + "{0}" : label + valueFormat,
                TargetNullValue = label + "—",
                Converter = converter
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

        private static TextBlock EmptyState(string path, string message)
        {
            var text = new TextBlock
            {
                Text = message,
                Foreground = Brushes.DimGray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 2, 0, 2)
            };
            text.SetBinding(VisibilityProperty, new WpfBinding(path)
            {
                Converter = new EmptyCollectionVisibilityConverter()
            });
            return text;
        }

        private sealed class ViewTypeDisplayConverter : IValueConverter
        {
            public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            {
                switch (value as string)
                {
                    case "FloorPlan": return "樓層平面圖";
                    case "CeilingPlan": return "天花板平面圖";
                    case "ThreeD": return "3D 視圖";
                    case "Section": return "剖面圖";
                    case "Elevation": return "立面圖";
                    case "Sheet": return "圖紙";
                    case "Schedule": return "明細表";
                    case "DraftingView": return "製圖視圖";
                    default: return value ?? string.Empty;
                }
            }

            public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class ScopeDisplayConverter : IValueConverter
        {
            public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            {
                switch (value as string)
                {
                    case "Host Document": return "主模型";
                    case "Active View": return "目前視圖";
                    case "Link": return "連結模型";
                    default: return value ?? string.Empty;
                }
            }

            public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class EmptyCollectionVisibilityConverter : IValueConverter
        {
            public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            {
                var collection = value as System.Collections.ICollection;
                return collection != null && collection.Count == 0
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
            }

            public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}
