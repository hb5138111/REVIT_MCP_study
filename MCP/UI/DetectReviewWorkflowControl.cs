using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RevitMCP.Models;
using Binding = System.Windows.Data.Binding;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace RevitMCP.UI
{
    /// <summary>Shared DetectReview pattern renderer; workflow metadata controls settings visibility.</summary>
    internal sealed class DetectReviewWorkflowControl : UserControl
    {
        public DetectReviewWorkflowControl(WorkflowDefinition workflow)
        {
            var root = new DockPanel { Margin = new Thickness(8) };
            var input = new StackPanel();
            DockPanel.SetDock(input, Dock.Top); root.Children.Add(input);
            input.Children.Add(new TextBlock { Text = workflow.Limitation, TextWrapping = TextWrapping.Wrap });
            input.Children.Add(Button("讀取來源／樓層", "RefreshSourcesCommand"));
            input.Children.Add(Choice("MEP 來源", "Sources", "MepSource"));
            input.Children.Add(Choice("主體來源", "Sources", "HostSource"));
            input.Children.Add(Choice("MEP 分類", "MepCategories", "MepCategory"));
            input.Children.Add(Choice("主體分類", "HostCategories", "HostCategory"));
            input.Children.Add(Choice("MEP 來源樓層", "Levels", "LevelName"));
            input.Children.Add(Edit("系統名稱包含", "SystemContains"));
            input.Children.Add(Edit("顯示上限（1–1000）", "MaxResults"));
            if (workflow.RequiredSettings.Length > 0)
            {
                var settings = new StackPanel();
                settings.Children.Add(Text("UnitsDescription"));
                settings.Children.Add(Edit("每側預留量", "ClearanceText"));
                var actions = new WrapPanel(); actions.Children.Add(Button("保存設定", "SaveSettingsCommand")); actions.Children.Add(Button("重設", "ResetSettingsCommand"));
                settings.Children.Add(actions);
                input.Children.Add(new GroupBox { Header = "專案設定", Content = settings });
            }
            input.Children.Add(Button("開始掃描", "ScanCommand"));
            input.Children.Add(Text("StatusMessage")); input.Children.Add(Text("Summary")); input.Children.Add(Text("Warnings"));
            input.Children.Add(Edit("結果搜尋", "Search"));
            var check = new CheckBox { Content = "只顯示需複核／警告" }; check.SetBinding(CheckBox.IsCheckedProperty, Bind("WarningsOnly")); input.Children.Add(check);
            var navigation = new WrapPanel();
            foreach (var pair in new[] { ("亮顯 MEP", "HighlightMepCommand"), ("亮顯主體", "HighlightHostCommand"), ("亮顯兩者", "HighlightBothCommand"), ("上一筆", "PreviousCommand"), ("下一筆", "NextCommand"), ("匯出 CSV", "ExportCommand") })
                navigation.Children.Add(Button(pair.Item1, pair.Item2));
            input.Children.Add(navigation);
            var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
                EnableRowVirtualization = true, EnableColumnVirtualization = true, SelectionMode = DataGridSelectionMode.Single, MinHeight = 160 };
            grid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("RowsView")); grid.SetBinding(DataGrid.SelectedItemProperty, Bind("SelectedRow"));
            foreach (var column in new[] { ("MEP", "Mep"), ("主體", "Host"), ("MEP分類", "MepCategoryDisplay"), ("主體分類", "HostCategoryDisplay"), ("系統", "System"), ("樓層", "Level"), ("MEP尺寸", "NominalSizeDisplay"), ("建議尺寸", "SizeDisplay"), ("穿透長度 mm", "IntersectionLengthMm"), ("交點", "PointDisplay"), ("開孔下緣 mm", "OpeningBottomDisplay"), ("狀態", "Status"), ("警告", "Warnings") })
                grid.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2) });
            root.Children.Add(grid);
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        }
        private static Binding Bind(string path) => new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
        private static Button Button(string text, string command) { var b = new Button { Content = text, Margin = new Thickness(2), Padding = new Thickness(6, 3, 6, 3) }; b.SetBinding(System.Windows.Controls.Button.CommandProperty, new Binding(command)); return b; }
        private static TextBlock Text(string path) { var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2) }; t.SetBinding(TextBlock.TextProperty, new Binding(path)); return t; }
        private static UIElement Choice(string title, string items, string selected)
        { var row = new DockPanel(); var label = new TextBlock { Text = title, Width = 125 }; row.Children.Add(label); var combo = new ComboBox { Margin = new Thickness(2) }; combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(items)); combo.SetBinding(ComboBox.SelectedItemProperty, Bind(selected)); row.Children.Add(combo); return row; }
        private static UIElement Edit(string title, string property)
        { var row = new DockPanel(); row.Children.Add(new TextBlock { Text = title, Width = 150 }); var text = new TextBox { Margin = new Thickness(2) }; text.SetBinding(TextBox.TextProperty, Bind(property)); row.Children.Add(text); return row; }
    }
}
