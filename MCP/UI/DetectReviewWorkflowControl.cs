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
            input.Children.Add(Button("重新整理模型來源", "RefreshSourcesCommand"));
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
            var group = new GroupBox { Header = "專案設定", Content = settings }; VisibleWhen(group, "NeedsClearance"); input.Children.Add(group);
            input.Children.Add(new Expander { Header = "進階設定", IsExpanded = false, Content = Edit("顯示上限（1–1000）", "MaxResults") });
            input.Children.Add(Button("開始協調掃描", "ScanCommand"));
            input.Children.Add(Text("ScanDisabledReason")); input.Children.Add(Text("StatusMessage"));
            input.Children.Add(Text("Summary")); input.Children.Add(Text("Warnings"));
            input.Children.Add(Choice("結果分類", "Filters", "SelectedFilter"));
            input.Children.Add(Edit("結果搜尋", "Search"));
            var scrolling = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = input };
            root.Children.Add(scrolling);
            var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
                EnableRowVirtualization = true, EnableColumnVirtualization = true, SelectionMode = DataGridSelectionMode.Single, MinHeight = 150 };
            table.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("RowsView"));
            table.SetBinding(DataGrid.SelectedItemProperty, Bind("SelectedRow"));
            var rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(ToolTipProperty, new Binding("Detail"))); table.RowStyle = rowStyle;
            foreach (var column in new[] { ("類型", "KindDisplay"), ("MEP", "MepLabel"), ("系統", "System"), ("主體", "HostLabel"), ("樓層", "Level"), ("MEP尺寸", "NominalSizeDisplay"), ("建議孔尺寸", "SizeDisplay"), ("穿透 mm", "IntersectionLengthMm"), ("狀態", "Status"), ("說明", "Explanation") })
                table.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2) });
            Grid.SetRow(table, 1); root.Children.Add(table);
            var bottom = new StackPanel();
            var detail = Text("SelectedRow.Detail"); bottom.Children.Add(detail);
            var actions = new WrapPanel();
            foreach (var pair in new[] { ("亮顯 MEP", "HighlightMepCommand"), ("亮顯主體", "HighlightHostCommand"), ("亮顯兩者", "HighlightBothCommand"), ("上一筆", "PreviousCommand"), ("下一筆", "NextCommand"), ("匯出篩選結果 CSV", "ExportCommand") }) actions.Children.Add(Button(pair.Item1, pair.Item2));
            bottom.Children.Add(actions); Grid.SetRow(bottom, 2); root.Children.Add(bottom); Content = root;
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
