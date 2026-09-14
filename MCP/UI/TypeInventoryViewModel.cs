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
        private FilterOption _selectedFilter;
        private string _searchText = string.Empty;
        private string _statusMessage = "就緒。請選擇構件分類後按「重新整理」。";
        private TypeInventoryRow _selectedRow;
        private NavigationSession _navigationSession;

        internal TypeInventoryViewModel(PanelReadOnlyDispatcher dispatcher, TypeInventoryService service)
        {
            _dispatcher = dispatcher;
            _service = service;
            _locatorService = new TypeInstanceLocatorService();
            Categories = CreateCategories();
            FilterOptions = CreateFilterOptions();
            _selectedCategory = Categories[0];
            _selectedFilter = FilterOptions[0];
            RefreshCommand = new RelayCommand(RequestRefresh, () => !_dispatcher.IsBusy);
            HighlightInstancesCommand = new RelayCommand(HighlightInstances, CanNavigateToSelectedRow);
            PreviousInstanceCommand = new RelayCommand(NavigatePrevious, CanNavigatePrevious);
            NextInstanceCommand = new RelayCommand(NavigateNext, CanNavigateNext);
            _dispatcher.BusyChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(IsBusy));
                CommandManager.InvalidateRequerySuggested();
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public IReadOnlyList<CategoryOption> Categories { get; }
        public IReadOnlyList<FilterOption> FilterOptions { get; }
        public ICommand RefreshCommand { get; }
        public ICommand HighlightInstancesCommand { get; }
        public ICommand PreviousInstanceCommand { get; }
        public ICommand NextInstanceCommand { get; }
        public bool IsBusy => _dispatcher.IsBusy;

        public TypeInventoryRow SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (ReferenceEquals(_selectedRow, value)) return;
                _selectedRow = value;
                ResetNavigationSession();
                OnPropertyChanged();
                if (value != null && value.InstanceCount == 0)
                    StatusMessage = "此類型目前沒有放置實例可供巡覽。";
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public CategoryOption SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (ReferenceEquals(_selectedCategory, value)) return;
                _selectedCategory = value;
                ResetNavigationSession();
                OnPropertyChanged();
            }
        }

        public FilterOption SelectedFilter
        {
            get => _selectedFilter;
            set { _selectedFilter = value; OnPropertyChanged(); RowsView?.Refresh(); }
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
        public string NavigationPosition => _navigationSession == null
            ? "— / —"
            : (_navigationSession.CurrentIndex + 1) + " / " + _navigationSession.InstanceIds.Count;

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        private void RequestRefresh()
        {
            if (_dispatcher.IsBusy) return;
            ResetNavigationSession();
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
            TypeInventoryRow row = SelectedRow;
            TypeInventoryResult result = Result;
            if (row == null || result == null || row.InstanceCount == 0) return;

            SubmitReadOnlyRequest(
                PanelReadOnlyRequestKind.HighlightTypeInstances,
                row,
                result,
                (uiDocument, instanceIds) =>
                {
                    uiDocument.Selection.SetElementIds(instanceIds.Select(id => id.ToElementId()).ToList());
                    StatusMessage = "已亮顯 " + instanceIds.Count + " 個實例。";
                });
        }

        private bool CanNavigatePrevious() =>
            _navigationSession != null && _navigationSession.CurrentIndex > 0 && !_dispatcher.IsBusy;

        private bool CanNavigateNext() =>
            CanNavigateToSelectedRow() &&
            (_navigationSession == null ||
             _navigationSession.CurrentIndex < _navigationSession.InstanceIds.Count - 1);

        private void NavigatePrevious()
        {
            Navigate(PanelReadOnlyRequestKind.NavigationPrevious, -1);
        }

        private void NavigateNext()
        {
            Navigate(PanelReadOnlyRequestKind.NavigationNext, 1);
        }

        private void Navigate(PanelReadOnlyRequestKind kind, int direction)
        {
            TypeInventoryRow row = SelectedRow;
            TypeInventoryResult result = Result;
            if (row == null || result == null || row.InstanceCount == 0) return;

            bool accepted = _dispatcher.TrySubmit(
                kind,
                app =>
                {
                    if (!ReferenceEquals(SelectedRow, row) || !ReferenceEquals(Result, result))
                    {
                        ResetNavigationSession();
                        return;
                    }
                    var uiDocument = RequireMatchingDocument(app, result);
                    if (uiDocument == null) return;

                    IReadOnlyList<long> freshIds = _locatorService.FindInstanceIds(
                        uiDocument.Document, row.Category, row.TypeId);
                    if (_navigationSession == null)
                    {
                        if (freshIds.Count == 0)
                        {
                            ResetNavigationSession();
                            StatusMessage = "此類型目前沒有放置實例可供巡覽。";
                            return;
                        }
                        if (freshIds.Count != row.InstanceCount)
                        {
                            ResetNavigationSession();
                            StatusMessage = "模型內容已變更，請重新整理族群／類型資料。";
                            return;
                        }
                        _navigationSession = new NavigationSession(
                            result.DocumentIdentity, row.Category, row.TypeId, freshIds, 0);
                    }
                    else
                    {
                        if (!freshIds.SequenceEqual(_navigationSession.InstanceIds))
                        {
                            ResetNavigationSession();
                            StatusMessage = "模型內容已變更，請重新整理族群／類型資料。";
                            return;
                        }
                        _navigationSession.CurrentIndex += direction;
                    }

                    Element element = ValidateCurrentElement(uiDocument.Document, _navigationSession);
                    if (element == null)
                    {
                        ResetNavigationSession();
                        StatusMessage = "模型內容已變更，請重新整理族群／類型資料。";
                        return;
                    }

                    var single = new List<ElementId> { element.Id };
                    uiDocument.Selection.SetElementIds(single);
                    uiDocument.ShowElements(single);
                    OnPropertyChanged(nameof(NavigationPosition));
                    StatusMessage = "目前巡覽：" + (_navigationSession.CurrentIndex + 1) +
                                    " / " + _navigationSession.InstanceIds.Count + "。";
                    CommandManager.InvalidateRequerySuggested();
                },
                message => StatusMessage = "操作失敗：" + message);

            if (!accepted && _dispatcher.IsBusy)
                StatusMessage = "另一項面板更新正在執行。";
        }

        private void SubmitReadOnlyRequest(
            PanelReadOnlyRequestKind kind,
            TypeInventoryRow row,
            TypeInventoryResult result,
            Action<Autodesk.Revit.UI.UIDocument, IReadOnlyList<long>> action)
        {
            bool accepted = _dispatcher.TrySubmit(
                kind,
                app =>
                {
                    if (!ReferenceEquals(SelectedRow, row) || !ReferenceEquals(Result, result)) return;
                    var uiDocument = RequireMatchingDocument(app, result);
                    if (uiDocument == null) return;
                    IReadOnlyList<long> ids = _locatorService.FindInstanceIds(
                        uiDocument.Document, row.Category, row.TypeId);
                    if (ids.Count == 0 || ids.Count != row.InstanceCount)
                    {
                        ResetNavigationSession();
                        StatusMessage = "模型內容已變更，請重新整理族群／類型資料。";
                        return;
                    }
                    action(uiDocument, ids);
                },
                message => StatusMessage = "操作失敗：" + message);

            if (!accepted && _dispatcher.IsBusy)
                StatusMessage = "另一項面板更新正在執行。";
        }

        private Autodesk.Revit.UI.UIDocument RequireMatchingDocument(
            Autodesk.Revit.UI.UIApplication app,
            TypeInventoryResult result)
        {
            var uiDocument = app.ActiveUIDocument;
            if (uiDocument == null)
                throw new InvalidOperationException("目前沒有開啟的 Revit 文件。");
            if (string.Equals(
                TypeInstanceLocatorService.GetDocumentIdentity(uiDocument.Document),
                result.DocumentIdentity,
                StringComparison.Ordinal)) return uiDocument;

            ResetNavigationSession();
            StatusMessage = "目前模型已切換，請重新整理族群／類型資料後再操作。";
            return null;
        }

        private static Element ValidateCurrentElement(Document document, NavigationSession session)
        {
            long id = session.InstanceIds[session.CurrentIndex];
            Element element = document.GetElement(id.ToElementId());
            if (element == null || element.Category == null) return null;
            if (element.Category.Id.GetIdValue() != (long)TypeInventoryService.ResolveCategory(session.Category))
                return null;
            return element.GetTypeId() != ElementId.InvalidElementId &&
                   element.GetTypeId().GetIdValue() == session.TypeId
                ? element
                : null;
        }

        private void ResetNavigationSession()
        {
            if (_navigationSession == null) return;
            _navigationSession = null;
            OnPropertyChanged(nameof(NavigationPosition));
            CommandManager.InvalidateRequerySuggested();
        }

        private bool MatchesFilter(object item)
        {
            if (!(item is TypeInventoryRow row)) return false;
            if (!MatchesSelectedFilter(row)) return false;
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

        private bool MatchesSelectedFilter(TypeInventoryRow row)
        {
            switch (SelectedFilter.Value)
            {
                case TypeInventoryFilter.Normal:
                    return row.InstanceCount > 0 && !row.HasReviewRequired && !row.HasDataReminder;
                case TypeInventoryFilter.UnplacedCandidate: return row.InstanceCount == 0;
                case TypeInventoryFilter.ReviewRequired: return row.HasReviewRequired;
                case TypeInventoryFilter.DataReminder: return row.HasDataReminder;
                default: return true;
            }
        }

        private static IReadOnlyList<FilterOption> CreateFilterOptions() => new[]
        {
            new FilterOption(TypeInventoryFilter.All, "全部"),
            new FilterOption(TypeInventoryFilter.Normal, "正常"),
            new FilterOption(TypeInventoryFilter.UnplacedCandidate, "未使用候選"),
            new FilterOption(TypeInventoryFilter.ReviewRequired, "需檢查"),
            new FilterOption(TypeInventoryFilter.DataReminder, "資料提醒")
        };

        private void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public sealed class CategoryOption
        {
            public CategoryOption(SupportedTypeCategory value, string label) { Value = value; Label = label; }
            public SupportedTypeCategory Value { get; }
            public string Label { get; }
        }

        public sealed class FilterOption
        {
            public FilterOption(TypeInventoryFilter value, string label) { Value = value; Label = label; }
            public TypeInventoryFilter Value { get; }
            public string Label { get; }
        }

        private sealed class NavigationSession
        {
            public NavigationSession(
                string documentIdentity,
                SupportedTypeCategory category,
                long typeId,
                IReadOnlyList<long> instanceIds,
                int currentIndex)
            {
                DocumentIdentity = documentIdentity;
                Category = category;
                TypeId = typeId;
                InstanceIds = instanceIds;
                CurrentIndex = currentIndex;
            }

            public string DocumentIdentity { get; }
            public SupportedTypeCategory Category { get; }
            public long TypeId { get; }
            public IReadOnlyList<long> InstanceIds { get; }
            public int CurrentIndex { get; set; }
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
