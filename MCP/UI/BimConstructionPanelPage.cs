using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using RevitMCP.Models;
using Autodesk.Revit.UI;
using WpfBinding = System.Windows.Data.Binding;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

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
            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = "模型摘要", Content = BuildModelSummaryContent() });
            tabs.Items.Add(new TabItem { Header = "族群／類型檢查", Content = BuildTypeInventoryContent() });
            tabs.Items.Add(new TabItem { Header = "樓層／約束檢查", Content = BuildLevelConstraintAuditContent() });
            tabs.Items.Add(new TabItem { Header = "施工協調", Content = BuildCoordinationContent() });
            return tabs;
        }

        private UIElement BuildCoordinationContent()
        {
            var tabs = new TabControl();
            foreach (var workflow in WorkflowRegistry.Definitions)
            {
                if (!workflow.Enabled) continue;
                var panel = new DetectReviewWorkflowControl(workflow);
                panel.SetBinding(DataContextProperty, new WpfBinding(workflow.Id == "clashes" ? "Clashes" : "Openings"));
                tabs.Items.Add(new TabItem { Header = workflow.Title, Content = panel });
            }
            return tabs;
        }

        private UIElement BuildModelSummaryContent()
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

        private UIElement BuildTypeInventoryContent()
        {
            var root = new WpfGrid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "營造 BIM 工具 — 族群／類型檢查",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(title);

            var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            controls.Children.Add(new TextBlock { Text = "構件分類：", VerticalAlignment = VerticalAlignment.Center });
            var category = new WpfComboBox { Width = 150, DisplayMemberPath = "Label", Margin = new Thickness(0, 0, 8, 0) };
            category.SetBinding(ItemsControl.ItemsSourceProperty, new WpfBinding("TypeInventory.Categories"));
            category.SetBinding(WpfComboBox.SelectedItemProperty, new WpfBinding("TypeInventory.SelectedCategory"));
            controls.Children.Add(category);
            var refresh = new Button { Content = "重新整理", MinWidth = 90 };
            refresh.SetBinding(Button.CommandProperty, new WpfBinding("TypeInventory.RefreshCommand"));
            controls.Children.Add(refresh);
            WpfGrid.SetRow(controls, 1);
            root.Children.Add(controls);

            var summary = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            summary.Children.Add(BoundText("載入類型：", "TypeInventory.Result.LoadedTypeCount"));
            summary.Children.Add(BoundText("　已使用類型：", "TypeInventory.Result.PlacedTypeCount"));
            summary.Children.Add(BoundText("　未使用候選：", "TypeInventory.Result.UnplacedCandidateCount"));
            summary.Children.Add(BoundText("　需檢查：", "TypeInventory.Result.ReviewRequiredCount"));
            summary.Children.Add(BoundText("　資料提醒：", "TypeInventory.Result.DataReminderCount"));
            summary.ToolTip = "未使用候選、需檢查與資料提醒可能重疊，不需加總等於載入類型。";
            WpfGrid.SetRow(summary, 2);
            root.Children.Add(summary);

            var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            filters.Children.Add(new TextBlock { Text = "搜尋：", VerticalAlignment = VerticalAlignment.Center });
            var search = new WpfTextBox { Width = 220, Margin = new Thickness(0, 0, 8, 0) };
            search.SetBinding(WpfTextBox.TextProperty, new WpfBinding("TypeInventory.SearchText")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            filters.Children.Add(search);
            filters.Children.Add(new TextBlock { Text = "顯示：", VerticalAlignment = VerticalAlignment.Center });
            var status = new WpfComboBox { Width = 120, DisplayMemberPath = "Label" };
            status.SetBinding(ItemsControl.ItemsSourceProperty, new WpfBinding("TypeInventory.FilterOptions"));
            status.SetBinding(WpfComboBox.SelectedItemProperty, new WpfBinding("TypeInventory.SelectedFilter"));
            filters.Children.Add(status);
            filters.Children.Add(BoundText("　狀態：", "TypeInventory.StatusMessage"));
            WpfGrid.SetRow(filters, 3);
            root.Children.Add(filters);

            var table = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserSortColumns = true,
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true
            };
            table.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
            table.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
            table.Columns.Add(TextColumn("族群", "FamilyName", 150));
            table.Columns.Add(TextColumn("類型", "TypeName", 180));
            table.Columns.Add(TextColumn("類型 ID", "TypeId", 90));
            table.Columns.Add(TextColumn("使用數量", "InstanceCount", 80));
            table.Columns.Add(TextColumn("類型標記", "TypeMark", 110));
            table.Columns.Add(TextColumn("類型備註", "TypeComments", 150));
            table.Columns.Add(new DataGridTextColumn
            {
                Header = "使用狀態",
                Binding = new WpfBinding("UsageState") { Converter = new TypeUsageStateConverter() },
                Width = 100
            });
            table.Columns.Add(new DataGridTextColumn
            {
                Header = "需檢查",
                Binding = new WpfBinding("WarningCodes") { Converter = new ReviewRequiredConverter() },
                Width = 90
            });
            table.Columns.Add(new DataGridTextColumn
            {
                Header = "資料提醒",
                Binding = new WpfBinding("WarningCodes") { Converter = new DataReminderConverter() },
                Width = 120
            });
            table.SetBinding(ItemsControl.ItemsSourceProperty, new WpfBinding("TypeInventory.RowsView"));
            table.SetBinding(DataGrid.SelectedItemProperty, new WpfBinding("TypeInventory.SelectedRow"));
            table.ToolTip = "「未使用候選」表示目前模型中未找到此類型的放置實例，不代表此類型可安全刪除；仍可能存在其他 Revit 相依關係。";
            WpfGrid.SetRow(table, 4);
            root.Children.Add(table);

            var navigation = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var highlight = new Button { Content = "亮顯實例", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
            highlight.SetBinding(Button.CommandProperty, new WpfBinding("TypeInventory.HighlightInstancesCommand"));
            navigation.Children.Add(highlight);
            navigation.Children.Add(new TextBlock
            {
                Text = "實例巡覽：",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0),
                ToolTip = "巡覽順序為穩定技術排序，不代表樓層、施工或空間順序。"
            });
            var previous = new Button { Content = "上一個", MinWidth = 70, Margin = new Thickness(0, 0, 6, 0) };
            previous.SetBinding(Button.CommandProperty, new WpfBinding("TypeInventory.PreviousInstanceCommand"));
            navigation.Children.Add(previous);
            navigation.Children.Add(BoundText(string.Empty, "TypeInventory.NavigationPosition"));
            var next = new Button { Content = "下一個", MinWidth = 70, Margin = new Thickness(6, 0, 0, 0) };
            next.SetBinding(Button.CommandProperty, new WpfBinding("TypeInventory.NextInstanceCommand"));
            navigation.Children.Add(next);
            WpfGrid.SetRow(navigation, 5);
            root.Children.Add(navigation);

            return root;
        }

        private UIElement BuildLevelConstraintAuditContent()
        {
            var root = new WpfGrid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(new TextBlock
            {
                Text = "營造 BIM 工具 — 樓層／約束檢查",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            controls.Children.Add(new TextBlock { Text = "構件分類：", VerticalAlignment = VerticalAlignment.Center });
            var category = new WpfComboBox
            {
                Width = 130,
                DisplayMemberPath = "Label",
                Margin = new Thickness(0, 0, 8, 0)
            };
            category.SetBinding(ItemsControl.ItemsSourceProperty, new WpfBinding("LevelConstraintAudit.Categories"));
            category.SetBinding(WpfComboBox.SelectedItemProperty, new WpfBinding("LevelConstraintAudit.SelectedCategory"));
            controls.Children.Add(category);

            controls.Children.Add(new TextBlock
            {
                Text = "樓層：",
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "樓層篩選以構件基準樓層為準。"
            });
            var level = new WpfComboBox
            {
                Width = 190,
                DisplayMemberPath = "Label",
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "樓層篩選以構件基準樓層為準。"
            };
            level.SetBinding(ItemsControl.ItemsSourceProperty, new WpfBinding("LevelConstraintAudit.Levels"));
            level.SetBinding(WpfComboBox.SelectedItemProperty, new WpfBinding("LevelConstraintAudit.SelectedLevel"));
            controls.Children.Add(level);

            controls.Children.Add(new TextBlock { Text = "顯示：", VerticalAlignment = VerticalAlignment.Center });
            var filter = new WpfComboBox
            {
                Width = 110,
                DisplayMemberPath = "Label",
                Margin = new Thickness(0, 0, 8, 0)
            };
            filter.SetBinding(ItemsControl.ItemsSourceProperty, new WpfBinding("LevelConstraintAudit.FilterOptions"));
            filter.SetBinding(WpfComboBox.SelectedItemProperty, new WpfBinding("LevelConstraintAudit.SelectedFilter"));
            controls.Children.Add(filter);

            var refresh = new Button { Content = "重新整理", MinWidth = 90 };
            refresh.SetBinding(Button.CommandProperty, new WpfBinding("LevelConstraintAudit.RefreshCommand"));
            controls.Children.Add(refresh);
            WpfGrid.SetRow(controls, 1);
            root.Children.Add(controls);

            var summary = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            summary.Children.Add(BoundText("構件數：", "LevelConstraintAudit.Result.TotalMatchedCount"));
            summary.Children.Add(BoundText("　正常：", "LevelConstraintAudit.Result.NormalCount"));
            summary.Children.Add(BoundText("　資料提醒：", "LevelConstraintAudit.Result.DataReminderCount"));
            summary.Children.Add(BoundText("　需檢查：", "LevelConstraintAudit.Result.ReviewRequiredCount"));
            summary.ToolTip = "資料提醒與需檢查可能重疊，不需加總等於構件數。";
            WpfGrid.SetRow(summary, 2);
            root.Children.Add(summary);

            var state = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            state.Children.Add(BoundText("狀態：", "LevelConstraintAudit.StatusMessage"));
            state.Children.Add(BoundText(string.Empty, "LevelConstraintAudit.TruncationMessage"));
            state.Children.Add(BoundText(string.Empty, "LevelConstraintAudit.NavigationScopeMessage"));
            WpfGrid.SetRow(state, 3);
            root.Children.Add(state);

            var scopeNote = new TextBlock
            {
                Text = "範圍：主模型；樓層篩選以構件基準樓層為準。約束標高為參數資料計算，不代表實際幾何高度。",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            WpfGrid.SetRow(scopeNote, 4);
            root.Children.Add(scopeNote);

            var table = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserSortColumns = true,
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true
            };
            table.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
            table.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
            table.Columns.Add(TextColumn("元素 ID", "ElementId", 95));
            table.Columns.Add(TextColumn("族群", "FamilyName", 140));
            table.Columns.Add(TextColumn("類型", "TypeName", 170));
            table.Columns.Add(TextColumn("關聯樓層", "AssociatedLevelName", 110));
            table.Columns.Add(TextColumn("基準樓層", "BaseLevelName", 110));
            table.Columns.Add(TextColumn("頂部樓層", "TopLevelName", 110));
            table.Columns.Add(TextColumn("基準偏移", "BaseOffsetDisplay", 105));
            table.Columns.Add(TextColumn("頂部偏移", "TopOffsetDisplay", 105));
            table.Columns.Add(TextColumn("未約束高度", "UnconnectedHeightDisplay", 115));
            table.Columns.Add(TextColumn("約束方式", "ConstraintModeDisplay", 115));
            table.Columns.Add(TextColumn("檢查結果", "StatusDisplay", 90));
            table.Columns.Add(TextColumn("說明", "Description", 240));
            table.SetBinding(ItemsControl.ItemsSourceProperty, new WpfBinding("LevelConstraintAudit.RowsView"));
            table.SetBinding(DataGrid.SelectedItemProperty, new WpfBinding("LevelConstraintAudit.SelectedRow"));
            WpfGrid.SetRow(table, 5);
            root.Children.Add(table);

            var navigation = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var highlight = new Button { Content = "亮顯元素", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
            highlight.SetBinding(Button.CommandProperty, new WpfBinding("LevelConstraintAudit.HighlightCommand"));
            navigation.Children.Add(highlight);
            navigation.Children.Add(new TextBlock
            {
                Text = "實例巡覽：",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0),
                ToolTip = "巡覽順序為 Element ID 穩定技術排序，不代表施工順序。"
            });
            var previous = new Button { Content = "上一個", MinWidth = 70, Margin = new Thickness(0, 0, 6, 0) };
            previous.SetBinding(Button.CommandProperty, new WpfBinding("LevelConstraintAudit.PreviousCommand"));
            navigation.Children.Add(previous);
            navigation.Children.Add(BoundText(string.Empty, "LevelConstraintAudit.NavigationPosition"));
            var next = new Button { Content = "下一個", MinWidth = 70, Margin = new Thickness(6, 0, 0, 0) };
            next.SetBinding(Button.CommandProperty, new WpfBinding("LevelConstraintAudit.NextCommand"));
            navigation.Children.Add(next);
            WpfGrid.SetRow(navigation, 6);
            root.Children.Add(navigation);

            return root;
        }

        private static DataGridTextColumn TextColumn(string header, string path, double width)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new WpfBinding(path),
                Width = width
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

        private sealed class TypeUsageStateConverter : IValueConverter
        {
            public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
                value is TypeUsageState state && state == TypeUsageState.UnplacedCandidate
                    ? "未使用候選"
                    : string.Empty;

            public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
                throw new NotSupportedException();
        }

        private sealed class ReviewRequiredConverter : IValueConverter
        {
            public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            {
                if (!(value is System.Collections.Generic.IEnumerable<TypeInventoryWarningCode> codes))
                    return string.Empty;
                foreach (TypeInventoryWarningCode code in codes)
                {
                    if (code == TypeInventoryWarningCode.TypeNameMissing)
                        return "類型名稱空白";
                    if (code == TypeInventoryWarningCode.DuplicateNameCandidate)
                        return "疑似重複名稱";
                }
                return string.Empty;
            }

            public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
                throw new NotSupportedException();
        }

        private sealed class DataReminderConverter : IValueConverter
        {
            public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            {
                if (!(value is System.Collections.Generic.IEnumerable<TypeInventoryWarningCode> codes))
                    return string.Empty;
                var labels = new System.Collections.Generic.List<string>();
                foreach (TypeInventoryWarningCode code in codes)
                {
                    if (code == TypeInventoryWarningCode.TypeMarkMissing)
                        labels.Add("類型標記空白");
                    else if (code == TypeInventoryWarningCode.TypeCommentsMissing)
                        labels.Add("類型備註空白");
                }
                return string.Join("、", labels);
            }

            public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
                throw new NotSupportedException();
        }

    }
}
