using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.DB;
using RevitMCP.Core;
using RevitMCP.Models;

namespace RevitMCP.UI
{
    public sealed class TypeInventoryViewModel : INotifyPropertyChanged
    {
        private readonly PanelReadOnlyDispatcher _dispatcher;
        private readonly TypeInventoryService _service;
        private readonly TypeInstanceLocatorService _locatorService;
        private TypeInventoryResult _result;
        private CategoryOption _selectedCategory;
        private StatusOption _selectedStatus;
        private string _searchText = string.Empty;
        private string _statusMessage = "就緒。請選擇構件分類後按「重新整理」。";
        private TypeInventoryRow _selectedRow;

        internal TypeInventoryViewModel(PanelReadOnlyDispatcher dispatcher, TypeInventoryService service)
        {
            _dispatcher = dispatcher;
            _service = service;
            _locatorService = new TypeInstanceLocatorService();
            Categories = CreateCategories();
            StatusOptions = CreateStatusOptions();
            _selectedCategory = Categories[0];
            _selectedStatus = StatusOptions[0];
            RefreshCommand = new RelayCommand(RequestRefresh, () => !_dispatcher.IsBusy);
            HighlightInstancesCommand = new RelayCommand(HighlightInstances, CanNavigateToSelectedRow);
            LocateFirstInstanceCommand = new RelayCommand(LocateFirstInstance, CanNavigateToSelectedRow);
            _dispatcher.BusyChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(IsBusy));
                CommandManager.InvalidateRequerySuggested();
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public IReadOnlyList<CategoryOption> Categories { get; }
        public IReadOnlyList<StatusOption> StatusOptions { get; }
        public ICommand RefreshCommand { get; }
        public ICommand HighlightInstancesCommand { get; }
        public ICommand LocateFirstInstanceCommand { get; }
        public bool IsBusy => _dispatcher.IsBusy;

        public TypeInventoryRow SelectedRow
        {
            get => _selectedRow;
            set
            {
                _selectedRow = value;
                OnPropertyChanged();
                if (value != null && value.InstanceCount == 0)
                    StatusMessage = "此類型目前沒有放置實例可供定位。";
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public CategoryOption SelectedCategory
        {
            get => _selectedCategory;
            set { _selectedCategory = value; OnPropertyChanged(); }
        }

        public StatusOption SelectedStatus
        {
            get => _selectedStatus;
            set { _selectedStatus = value; OnPropertyChanged(); RowsView?.Refresh(); }
        }

        public string SearchText
        {
            get => _searchText;
            set { _searchText = value ?? string.Empty; OnPropertyChanged(); RowsView?.Refresh(); }
        }

        public TypeInventoryResult Result
        {
            get => _result;
            private set
            {
                _result = value;
                SelectedRow = null;
                OnPropertyChanged();
                RowsView = value == null ? null : CollectionViewSource.GetDefaultView(value.Rows);
                if (RowsView != null) RowsView.Filter = MatchesFilter;
                OnPropertyChanged(nameof(RowsView));
            }
        }

        public ICollectionView RowsView { get; private set; }

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        private void RequestRefresh()
        {
            if (_dispatcher.IsBusy) return;
            StatusMessage = "正在更新...";
            var request = new TypeInventoryRequest
            {
                Category = SelectedCategory.Value,
                IncludeTypeMark = true,
                IncludeTypeComments = true
            };

            if (!_dispatcher.TrySubmit(
                PanelReadOnlyRequestKind.TypeInventory,
                app =>
                {
                    Result = _service.GetInventory(app, request);
                    StatusMessage = "更新完成。";
                },
                message => StatusMessage = "更新失敗：" + message))
            {
                if (_dispatcher.IsBusy) StatusMessage = "另一項面板更新正在執行。";
            }
        }

        private bool CanNavigateToSelectedRow() =>
            SelectedRow != null && SelectedRow.InstanceCount > 0 && !_dispatcher.IsBusy;

        private void HighlightInstances()
        {
            SubmitNavigationRequest(PanelReadOnlyRequestKind.HighlightTypeInstances, false);
        }

        private void LocateFirstInstance()
        {
            SubmitNavigationRequest(PanelReadOnlyRequestKind.LocateFirstTypeInstance, true);
        }

        private void SubmitNavigationRequest(PanelReadOnlyRequestKind kind, bool locateFirst)
        {
            TypeInventoryRow row = SelectedRow;
            TypeInventoryResult result = Result;
            if (row == null || result == null || row.InstanceCount == 0) return;

            bool accepted = _dispatcher.TrySubmit(
                kind,
                app =>
                {
                    var uiDocument = app.ActiveUIDocument;
                    if (uiDocument == null)
                        throw new InvalidOperationException("目前沒有開啟的 Revit 文件。");
                    if (!string.Equals(
                        TypeInstanceLocatorService.GetDocumentIdentity(uiDocument.Document),
                        result.DocumentIdentity,
                        StringComparison.Ordinal))
                    {
                        StatusMessage = "目前模型已切換，請重新整理族群／類型資料後再定位。";
                        return;
                    }

                    IReadOnlyList<long> instanceIds = _locatorService.FindInstanceIds(
                        uiDocument.Document,
                        row.Category,
                        row.TypeId);
                    if (instanceIds.Count == 0)
                    {
                        StatusMessage = "模型內容已變更，請重新整理族群／類型資料。";
                        return;
                    }

                    if (locateFirst)
                    {
                        var firstId = new List<ElementId> { instanceIds[0].ToElementId() };
                        uiDocument.Selection.SetElementIds(firstId);
                        uiDocument.ShowElements(firstId);
                        StatusMessage = "已定位至第一個實例。";
                    }
                    else
                    {
                        uiDocument.Selection.SetElementIds(instanceIds.Select(id => id.ToElementId()).ToList());
                        StatusMessage = "已亮顯 " + instanceIds.Count + " 個實例。";
                    }
                },
                message => StatusMessage = "操作失敗：" + message);

            if (!accepted && _dispatcher.IsBusy)
                StatusMessage = "另一項面板更新正在執行。";
        }

        private bool MatchesFilter(object item)
        {
            if (!(item is TypeInventoryRow row)) return false;
            if (SelectedStatus.Value.HasValue && row.Status != SelectedStatus.Value.Value) return false;
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            return Contains(row.FamilyName, SearchText) ||
                   Contains(row.TypeName, SearchText) ||
                   Contains(row.TypeMark, SearchText);
        }

        private static bool Contains(string source, string value) =>
            (source ?? string.Empty).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        private static IReadOnlyList<CategoryOption> CreateCategories() => new[]
        {
            new CategoryOption(SupportedTypeCategory.Walls, "牆"),
            new CategoryOption(SupportedTypeCategory.Floors, "樓板"),
            new CategoryOption(SupportedTypeCategory.Ceilings, "天花板"),
            new CategoryOption(SupportedTypeCategory.ArchitecturalColumns, "建築柱"),
            new CategoryOption(SupportedTypeCategory.StructuralColumns, "結構柱"),
            new CategoryOption(SupportedTypeCategory.StructuralFraming, "結構構架"),
            new CategoryOption(SupportedTypeCategory.Mullions, "帷幕牆豎框")
        };

        private static IReadOnlyList<StatusOption> CreateStatusOptions() => new[]
        {
            new StatusOption(null, "全部"),
            new StatusOption(TypeInventoryStatus.Normal, "正常"),
            new StatusOption(TypeInventoryStatus.UnplacedCandidate, "未使用候選"),
            new StatusOption(TypeInventoryStatus.ReviewRequired, "需檢查")
        };

        private void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public sealed class CategoryOption
        {
            public CategoryOption(SupportedTypeCategory value, string label) { Value = value; Label = label; }
            public SupportedTypeCategory Value { get; }
            public string Label { get; }
        }

        public sealed class StatusOption
        {
            public StatusOption(TypeInventoryStatus? value, string label) { Value = value; Label = label; }
            public TypeInventoryStatus? Value { get; }
            public string Label { get; }
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Action _execute;
            private readonly Func<bool> _canExecute;
            public RelayCommand(Action execute, Func<bool> canExecute) { _execute = execute; _canExecute = canExecute; }
            public event EventHandler CanExecuteChanged
            {
                add => CommandManager.RequerySuggested += value;
                remove => CommandManager.RequerySuggested -= value;
            }
            public bool CanExecute(object parameter) => _canExecute();
            public void Execute(object parameter) => _execute();
        }
    }
}
