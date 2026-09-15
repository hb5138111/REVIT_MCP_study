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
    public sealed class LevelConstraintAuditViewModel : INotifyPropertyChanged
    {
        private readonly PanelReadOnlyDispatcher _dispatcher;
        private readonly LevelConstraintAuditService _service;
        private LevelConstraintAuditResult _result;
        private CategoryOption _selectedCategory;
        private LevelConstraintLevelOption _selectedLevel;
        private FilterOption _selectedFilter;
        private LevelConstraintAuditRow _selectedRow;
        private NavigationSession _navigationSession;
        private string _statusMessage = "就緒。請選擇構件分類後按「重新整理」。";

        internal LevelConstraintAuditViewModel(
            PanelReadOnlyDispatcher dispatcher,
            LevelConstraintAuditService service)
        {
            _dispatcher = dispatcher;
            _service = service;
            Categories = CreateCategories();
            Levels = new[] { CreateAllLevelsOption() };
            FilterOptions = CreateFilterOptions();
            _selectedCategory = Categories[0];
            _selectedLevel = Levels[0];
            _selectedFilter = FilterOptions[0];
            RefreshCommand = new RelayCommand(RequestRefresh, () => !_dispatcher.IsBusy);
            HighlightCommand = new RelayCommand(HighlightSelected, CanHighlight);
            PreviousCommand = new RelayCommand(NavigatePrevious, CanNavigatePrevious);
            NextCommand = new RelayCommand(NavigateNext, CanNavigateNext);
            _dispatcher.BusyChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(IsBusy));
                CommandManager.InvalidateRequerySuggested();
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public IReadOnlyList<CategoryOption> Categories { get; }
        public IReadOnlyList<FilterOption> FilterOptions { get; }
        public IReadOnlyList<LevelConstraintLevelOption> Levels { get; private set; }
        public ICommand RefreshCommand { get; }
        public ICommand HighlightCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand NextCommand { get; }
        public bool IsBusy => _dispatcher.IsBusy;

        public CategoryOption SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (ReferenceEquals(_selectedCategory, value)) return;
                _selectedCategory = value;
                SelectedRow = null;
                ResetNavigationSession();
                OnPropertyChanged();
            }
        }

        public LevelConstraintLevelOption SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                if (ReferenceEquals(_selectedLevel, value)) return;
                _selectedLevel = value ?? CreateAllLevelsOption();
                SelectedRow = null;
                ResetNavigationSession();
                OnPropertyChanged();
            }
        }

        public FilterOption SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (ReferenceEquals(_selectedFilter, value)) return;
                _selectedFilter = value;
                SelectedRow = null;
                OnPropertyChanged();
                RowsView?.Refresh();
            }
        }

        public LevelConstraintAuditRow SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (ReferenceEquals(_selectedRow, value)) return;
                _selectedRow = value;
                ResetNavigationSession();
                if (value != null) CreateNavigationSession(value);
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public LevelConstraintAuditResult Result
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
                OnPropertyChanged(nameof(TruncationMessage));
                OnPropertyChanged(nameof(NavigationScopeMessage));
            }
        }

        public ICollectionView RowsView { get; private set; }
        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string NavigationPosition => _navigationSession == null
            ? "— / —"
            : (_navigationSession.CurrentIndex + 1) + " / " + _navigationSession.ElementIds.Count;

        public string TruncationMessage => Result != null && Result.IsTruncated
            ? "共有 " + Result.TotalMatchedCount + " 個符合結果，目前顯示前 " + Result.ReturnedCount + " 個。"
            : string.Empty;

        public string NavigationScopeMessage => Result != null && Result.IsTruncated
            ? "巡覽範圍僅包含目前顯示結果。"
            : string.Empty;

        private void RequestRefresh()
        {
            if (_dispatcher.IsBusy) return;
            ResetNavigationSession();
            StatusMessage = "正在更新...";
            CategoryOption category = SelectedCategory;
            long? levelId = SelectedLevel?.LevelId;
            var request = new LevelConstraintAuditRequest
            {
                Category = category.Value,
                LevelId = levelId,
                MaxResults = 500
            };

            bool accepted = _dispatcher.TrySubmit(
                PanelReadOnlyRequestKind.LevelConstraintAudit,
                app =>
                {
                    LevelConstraintAuditResult result = _service.GetAudit(app, request);
                    if (!ReferenceEquals(SelectedCategory, category) || SelectedLevel?.LevelId != levelId) return;
                    Result = result;
                    UpdateLevels(result.Levels, levelId);
                    StatusMessage = result.IsTruncated ? "更新完成，結果已依顯示上限截斷。" : "更新完成。";
                },
                message => StatusMessage = "更新失敗：" + message);

            if (!accepted && _dispatcher.IsBusy)
                StatusMessage = "另一項面板更新正在執行。";
        }

        private bool CanHighlight() => SelectedRow != null && Result != null && !_dispatcher.IsBusy;

        private void HighlightSelected()
        {
            LevelConstraintAuditRow row = SelectedRow;
            LevelConstraintAuditResult result = Result;
            if (row == null || result == null) return;

            SubmitElementAction(PanelReadOnlyRequestKind.LevelConstraintHighlight, row, result, (uiDocument, element) =>
            {
                uiDocument.Selection.SetElementIds(new List<ElementId> { element.Id });
                StatusMessage = "已亮顯元素 " + row.ElementId + "。";
            });
        }

        private bool CanNavigatePrevious() =>
            _navigationSession != null && _navigationSession.CurrentIndex > 0 && !_dispatcher.IsBusy;

        private bool CanNavigateNext() =>
            _navigationSession != null &&
            _navigationSession.CurrentIndex < _navigationSession.ElementIds.Count - 1 &&
            !_dispatcher.IsBusy;

        private void NavigatePrevious() => Navigate(PanelReadOnlyRequestKind.LevelConstraintPrevious, -1);
        private void NavigateNext() => Navigate(PanelReadOnlyRequestKind.LevelConstraintNext, 1);

        private void Navigate(PanelReadOnlyRequestKind kind, int direction)
        {
            NavigationSession session = _navigationSession;
            LevelConstraintAuditResult result = Result;
            if (session == null || result == null) return;

            bool accepted = _dispatcher.TrySubmit(
                kind,
                app =>
                {
                    if (!ReferenceEquals(session, _navigationSession) || !ReferenceEquals(result, Result)) return;
                    var uiDocument = RequireMatchingDocument(app, result);
                    if (uiDocument == null) return;
                    int nextIndex = session.CurrentIndex + direction;
                    if (nextIndex < 0 || nextIndex >= session.ElementIds.Count) return;
                    Element element = ValidateElement(uiDocument.Document, session.ElementIds[nextIndex], session.Category);
                    if (element == null)
                    {
                        InvalidateForStaleModel();
                        return;
                    }
                    session.CurrentIndex = nextIndex;
                    SelectedRow = Result.Rows.FirstOrDefault(row => row.ElementId == element.Id.GetIdValue());
                    _navigationSession = session;
                    uiDocument.Selection.SetElementIds(new List<ElementId> { element.Id });
                    uiDocument.ShowElements(new List<ElementId> { element.Id });
                    OnPropertyChanged(nameof(NavigationPosition));
                    StatusMessage = "目前巡覽：" + (session.CurrentIndex + 1) + " / " + session.ElementIds.Count + "。";
                    CommandManager.InvalidateRequerySuggested();
                },
                message => StatusMessage = "操作失敗：" + message);

            if (!accepted && _dispatcher.IsBusy)
                StatusMessage = "另一項面板更新正在執行。";
        }

        private void SubmitElementAction(
            PanelReadOnlyRequestKind kind,
            LevelConstraintAuditRow row,
            LevelConstraintAuditResult result,
            Action<Autodesk.Revit.UI.UIDocument, Element> action)
        {
            bool accepted = _dispatcher.TrySubmit(
                kind,
                app =>
                {
                    if (!ReferenceEquals(row, SelectedRow) || !ReferenceEquals(result, Result)) return;
                    var uiDocument = RequireMatchingDocument(app, result);
                    if (uiDocument == null) return;
                    Element element = ValidateElement(uiDocument.Document, row.ElementId, row.Category);
                    if (element == null)
                    {
                        InvalidateForStaleModel();
                        return;
                    }
                    action(uiDocument, element);
                },
                message => StatusMessage = "操作失敗：" + message);

            if (!accepted && _dispatcher.IsBusy)
                StatusMessage = "另一項面板更新正在執行。";
        }

        private Autodesk.Revit.UI.UIDocument RequireMatchingDocument(
            Autodesk.Revit.UI.UIApplication app,
            LevelConstraintAuditResult result)
        {
            var uiDocument = app.ActiveUIDocument;
            if (uiDocument == null) throw new InvalidOperationException("目前沒有開啟的 Revit 文件。");
            if (string.Equals(TypeInstanceLocatorService.GetDocumentIdentity(uiDocument.Document),
                result.DocumentIdentity, StringComparison.Ordinal)) return uiDocument;

            ResetNavigationSession();
            StatusMessage = "目前模型已切換，請重新整理樓層／約束資料後再操作。";
            return null;
        }

        private static Element ValidateElement(Document document, long elementId, LevelConstraintCategory category)
        {
            Element element = document.GetElement(elementId.ToElementId());
            if (element?.Category == null) return null;
            return element.Category.Id.GetIdValue() == (long)LevelConstraintAuditService.ResolveCategory(category)
                ? element
                : null;
        }

        private void CreateNavigationSession(LevelConstraintAuditRow selectedRow)
        {
            if (Result == null || RowsView == null) return;
            List<long> ids = RowsView.Cast<LevelConstraintAuditRow>().Select(row => row.ElementId).ToList();
            int index = ids.IndexOf(selectedRow.ElementId);
            if (index < 0) return;
            _navigationSession = new NavigationSession(Result.DocumentIdentity, Result.Category, ids, index);
            OnPropertyChanged(nameof(NavigationPosition));
        }

        private void InvalidateForStaleModel()
        {
            ResetNavigationSession();
            StatusMessage = "模型內容已變更，請重新整理樓層／約束資料。";
        }

        private void ResetNavigationSession()
        {
            if (_navigationSession == null) return;
            _navigationSession = null;
            OnPropertyChanged(nameof(NavigationPosition));
            CommandManager.InvalidateRequerySuggested();
        }

        private void UpdateLevels(IReadOnlyList<LevelConstraintLevelOption> levels, long? selectedLevelId)
        {
            Levels = levels ?? new[] { CreateAllLevelsOption() };
            OnPropertyChanged(nameof(Levels));
            _selectedLevel = Levels.FirstOrDefault(level => level.LevelId == selectedLevelId) ?? Levels[0];
            OnPropertyChanged(nameof(SelectedLevel));
        }

        private bool MatchesFilter(object item)
        {
            if (!(item is LevelConstraintAuditRow row)) return false;
            switch (SelectedFilter.Value)
            {
                case LevelConstraintStatus.Normal: return !row.HasReviewRequired && !row.HasDataReminder;
                case LevelConstraintStatus.DataReminder: return row.HasDataReminder;
                case LevelConstraintStatus.ReviewRequired: return row.HasReviewRequired;
                default: return true;
            }
        }

        private static IReadOnlyList<CategoryOption> CreateCategories() => new[]
        {
            new CategoryOption(LevelConstraintCategory.Walls, "牆"),
            new CategoryOption(LevelConstraintCategory.ArchitecturalColumns, "建築柱"),
            new CategoryOption(LevelConstraintCategory.StructuralColumns, "結構柱")
        };

        private static IReadOnlyList<FilterOption> CreateFilterOptions() => new[]
        {
            new FilterOption(null, "全部"),
            new FilterOption(LevelConstraintStatus.Normal, "正常"),
            new FilterOption(LevelConstraintStatus.DataReminder, "資料提醒"),
            new FilterOption(LevelConstraintStatus.ReviewRequired, "需檢查")
        };

        private static LevelConstraintLevelOption CreateAllLevelsOption() =>
            new LevelConstraintLevelOption { Name = "全部", ElevationDisplay = string.Empty };

        private void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public sealed class CategoryOption
        {
            public CategoryOption(LevelConstraintCategory value, string label) { Value = value; Label = label; }
            public LevelConstraintCategory Value { get; }
            public string Label { get; }
        }

        public sealed class FilterOption
        {
            public FilterOption(LevelConstraintStatus? value, string label) { Value = value; Label = label; }
            public LevelConstraintStatus? Value { get; }
            public string Label { get; }
        }

        private sealed class NavigationSession
        {
            public NavigationSession(
                string documentIdentity,
                LevelConstraintCategory category,
                IReadOnlyList<long> elementIds,
                int currentIndex)
            {
                DocumentIdentity = documentIdentity;
                Category = category;
                ElementIds = elementIds;
                CurrentIndex = currentIndex;
            }

            public string DocumentIdentity { get; }
            public LevelConstraintCategory Category { get; }
            public IReadOnlyList<long> ElementIds { get; }
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
