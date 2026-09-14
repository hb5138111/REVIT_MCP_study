using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using RevitMCP.Core;
using RevitMCP.Models;

namespace RevitMCP.UI
{
    public sealed class TypeInventoryViewModel : INotifyPropertyChanged
    {
        private readonly PanelReadOnlyDispatcher _dispatcher;
        private readonly TypeInventoryService _service;
        private TypeInventoryResult _result;
        private CategoryOption _selectedCategory;
        private StatusOption _selectedStatus;
        private string _searchText = string.Empty;
        private string _statusMessage = "就緒。請選擇構件分類後按「重新整理」。";

        internal TypeInventoryViewModel(PanelReadOnlyDispatcher dispatcher, TypeInventoryService service)
        {
            _dispatcher = dispatcher;
            _service = service;
            Categories = CreateCategories();
            StatusOptions = CreateStatusOptions();
            _selectedCategory = Categories[0];
            _selectedStatus = StatusOptions[0];
            RefreshCommand = new RelayCommand(RequestRefresh, () => !_dispatcher.IsBusy);
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
        public bool IsBusy => _dispatcher.IsBusy;

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
