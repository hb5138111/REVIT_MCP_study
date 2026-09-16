using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RevitMCP.Models;
using Grid = System.Windows.Controls.Grid;
using Binding = System.Windows.Data.Binding;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace RevitMCP.UI
{
    /// <summary>Single DetectReview workbench. Workflow state lives in the typed controller.</summary>
    internal sealed class DetectReviewWorkflowControl : UserControl
    {
        public DetectReviewWorkflowControl()
        {
            var root = new Grid { Margin = new Thickness(8) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MaxHeight = 520 });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var input = new StackPanel();
            input.Children.Add(new TextBlock { Text = "營造 BIM 工具 — 施工協調", FontSize = 17, FontWeight = FontWeights.SemiBold });
            var refresh = Button("重新整理來源", "RefreshSourcesCommand"); refresh.HorizontalAlignment = HorizontalAlignment.Right;
            input.Children.Add(refresh);
            input.Children.Add(new TextBlock { Text = "協調範圍", FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 5, 2, 3) });
            input.Children.Add(Choice("MEP 來源", "Sources", "MepSource"));
            input.Children.Add(Choice("主體來源", "Sources", "HostSource"));
            input.Children.Add(Choice("MEP 分類", "MepCategories", "MepCategory", true));
            input.Children.Add(Choice("穿越對象", "HostCategories", "HostCategory", true));
            input.Children.Add(Choice("樓層", "Levels", "SelectedLevel"));
            input.Children.Add(Text("CostWarning"));
            var system = Edit("系統名稱包含", "SystemContains"); VisibleWhen(system, "HasSystemFilter"); input.Children.Add(system);
            var opening = new CheckBox { Content = "計算建議開孔尺寸（候選，仍需人工複核）", Margin = new Thickness(2) };
            opening.SetBinding(CheckBox.IsCheckedProperty, Bind("OpeningCandidates")); input.Children.Add(opening);
            var settings = new StackPanel();
            settings.Children.Add(Edit("每側預留量（可輸入 mm）", "ClearanceText"));
            settings.Children.Add(Button("保存本模型設定", "SaveSettingsCommand")); settings.Children.Add(Text("SavedSetting"));
            var group = new GroupBox { Header = "開孔設定", Content = settings }; VisibleWhen(group, "NeedsClearance"); input.Children.Add(group);
            input.Children.Add(new Expander { Header = "進階設定", IsExpanded = false, Content = Edit("顯示上限（1–1000）", "MaxResults") });
            input.Children.Add(PrimaryButton("開始協調掃描", "ScanCommand"));
            input.Children.Add(Text("ScanDisabledReason")); input.Children.Add(Text("StatusMessage"));
            var summary = new WrapPanel();
            foreach (var pair in new[] { ("TotalLabel", "FilterAllCommand"), ("OpeningLabel", "FilterOpeningCommand"), ("BeamLabel", "FilterBeamCommand"), ("ClashLabel", "FilterClashCommand"), ("ReviewLabel", "FilterReviewCommand") })
            { var chip = Button("", pair.Item2); chip.SetBinding(ContentControl.ContentProperty, new Binding(pair.Item1)); summary.Children.Add(chip); }
            input.Children.Add(summary); input.Children.Add(Text("Summary"));
            input.Children.Add(new Expander { Header = "方法與限制", IsExpanded = false, Content = Text("Warnings") });
            input.Children.Add(Choice("結果分類", "Filters", "SelectedFilter"));
            input.Children.Add(Edit("結果搜尋", "Search"));
            var scrolling = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = input };
            root.Children.Add(scrolling);
            var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
                EnableRowVirtualization = true, EnableColumnVirtualization = true, SelectionMode = DataGridSelectionMode.Single, MinHeight = 150 };
            table.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("RowsView"));
            table.SetBinding(DataGrid.SelectedItemProperty, Bind("SelectedRow"));
            var rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(ToolTipProperty, new Binding("TechnicalDetail"))); table.RowStyle = rowStyle;
            foreach (var column in new[] { ("類型", "KindDisplay"), ("MEP", "MepLabel"), ("系統", "System"), ("主體", "HostLabel"), ("樓層", "Level"), ("MEP尺寸", "NominalSizeDisplay"), ("建議孔尺寸", "SizeDisplay"), ("狀態", "Status") })
                table.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2) });
            table.MouseDoubleClick += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject origin && ItemsControl.ContainerFromElement(table, origin) is DataGridRow row &&
                    row.DataContext is CoordinationRow issue && DataContext is CoordinationViewModel vm)
                { vm.SelectedRow = issue; vm.Locate3DCommand.Execute(null); e.Handled = true; }
            };
            var results = new Grid();
            results.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            results.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            results.Children.Add(Text("EmptyState")); Grid.SetRow(table, 1); results.Children.Add(table);
            Grid.SetRow(results, 1); root.Children.Add(results);
            var bottom = new StackPanel();
            var navigation = new WrapPanel();
            navigation.Children.Add(Button("上一筆", "PreviousCommand")); navigation.Children.Add(Text("NavigationPosition"));
            navigation.Children.Add(Button("下一筆", "NextCommand"));
            var automatic = new CheckBox { Content = "巡覽時自動 3D 定位", Margin = new Thickness(6, 4, 2, 4) };
            automatic.SetBinding(CheckBox.IsCheckedProperty, Bind("AutoLocateEnabled")); navigation.Children.Add(automatic); bottom.Children.Add(navigation);
            var actions = new WrapPanel();
            actions.Children.Add(PrimaryButton("3D定位", "Locate3DCommand"));
            foreach (var pair in new[] { ("MEP", "HighlightMepCommand"), ("主體", "HighlightHostCommand"), ("返回原視圖", "ReturnPreviousCommand"), ("匯出 CSV", "ExportCommand") }) actions.Children.Add(Button(pair.Item1, pair.Item2));
            bottom.Children.Add(actions);
            bottom.Children.Add(new Expander { Header = "問題明細", IsExpanded = true,
                Content = new ScrollViewer { MaxHeight = 155, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Text("Detail") } });
            Grid.SetRow(bottom, 2); root.Children.Add(bottom); Content = root;
            DataContextChanged += (_, e) => { if (e.OldValue is CoordinationViewModel old) old.ExportRequested -= Export; if (e.NewValue is CoordinationViewModel current) current.ExportRequested += Export; };
        }
        private void Export(object? sender, EventArgs e)
        {
            if (!(sender is CoordinationViewModel vm)) return;
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV|*.csv", FileName = "coordination.csv" };
            if (dialog.ShowDialog() != true) return;
            try { File.WriteAllText(dialog.FileName, vm.ExportCsv(), new UTF8Encoding(true)); vm.ReportExport("已匯出目前顯示結果；若有截斷請縮小範圍後重掃。"); }
            catch (Exception ex) { vm.ReportExport("匯出失敗：" + ex.Message); }
        }
        private static void VisibleWhen(FrameworkElement element, string path) => element.SetBinding(VisibilityProperty, new Binding(path) { Converter = new BooleanToVisibilityConverter() });
        private static Binding Bind(string path) => new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
        private static Button Button(string text, string command) { var b = new Button { Content = text, Margin = new Thickness(2), Padding = new Thickness(6, 3, 6, 3) }; b.SetBinding(System.Windows.Controls.Button.CommandProperty, new Binding(command)); return b; }
        private static Button PrimaryButton(string text, string command)
        { var b = Button(text, command); b.FontWeight = FontWeights.SemiBold; b.Padding = new Thickness(12, 7, 12, 7);
          b.Background = System.Windows.Media.Brushes.SteelBlue; b.Foreground = System.Windows.Media.Brushes.White; return b; }
        private static TextBlock Text(string path) { var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2) }; text.SetBinding(TextBlock.TextProperty, new Binding(path)); return text; }
        private static UIElement Choice(string title, string items, string selected, bool category = false)
        {
            var row = new DockPanel(); row.Children.Add(new TextBlock { Text = title, Width = 110 });
            var combo = new ComboBox { Margin = new Thickness(2) };
            combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(items)); combo.SetBinding(ComboBox.SelectedItemProperty, Bind(selected));
            if (category) { var template = new DataTemplate(); var factory = new FrameworkElementFactory(typeof(TextBlock)); factory.SetBinding(TextBlock.TextProperty, new Binding { Converter = new CategoryLabelConverter() }); template.VisualTree = factory; combo.ItemTemplate = template; }
            row.Children.Add(combo); return row;
        }
        private static FrameworkElement Edit(string title, string path)
        { var row = new DockPanel(); row.Children.Add(new TextBlock { Text = title, Width = 180 }); var box = new TextBox { Margin = new Thickness(2) }; box.SetBinding(TextBox.TextProperty, Bind(path)); row.Children.Add(box); return row; }
        private sealed class CategoryLabelConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => new CoordinationRow { MepCategory = value as string ?? "" }.MepCategoryDisplay;
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
        }
    }
}
