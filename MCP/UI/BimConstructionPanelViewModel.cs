using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RevitMCP.Core;
using RevitMCP.Models;

namespace RevitMCP.UI
{
    public sealed class BimConstructionPanelViewModel : INotifyPropertyChanged
    {
        private readonly PanelReadOnlyDispatcher _dispatcher;
        private readonly ModelSummaryService _service;
        private ModelSummaryResult _result;
        private bool _isRefreshing;
        private string _statusMessage = "就緒。請按「重新整理」讀取目前模型資訊。";

        public BimConstructionPanelViewModel()
        {
            _dispatcher = new PanelReadOnlyDispatcher();
            _service = new ModelSummaryService();
            TypeInventory = new TypeInventoryViewModel(_dispatcher, new TypeInventoryService());
            LevelConstraintAudit = new LevelConstraintAuditViewModel(
                _dispatcher, new LevelConstraintAuditService());
            Clashes = new CoordinationViewModel(_dispatcher, false);
            Openings = new CoordinationViewModel(_dispatcher, true);
            _dispatcher.BusyChanged += (_, __) =>
            {
                IsRefreshing = _dispatcher.IsBusy;
                CommandManager.InvalidateRequerySuggested();
            };
            RefreshCommand = new RelayCommand(RequestRefresh, () => !_dispatcher.IsBusy);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand RefreshCommand { get; }
        public TypeInventoryViewModel TypeInventory { get; }
        public LevelConstraintAuditViewModel LevelConstraintAudit { get; }
        public CoordinationViewModel Clashes { get; }
        public CoordinationViewModel Openings { get; }

        public ModelSummaryResult Result
        {
            get => _result;
            private set
            {
                _result = value;
                OnPropertyChanged();
            }
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set
            {
                _isRefreshing = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        private void RequestRefresh()
        {
            if (IsRefreshing) return;

            IsRefreshing = true;
            StatusMessage = "正在更新...";
            try
            {
                bool accepted = _dispatcher.TrySubmit(
                    PanelReadOnlyRequestKind.ModelSummary,
                    app => CompleteSuccess(_service.GetSummary(
                        app,
                        new ModelSummaryRequest { CategoryLimit = 10 })),
                    CompleteFailure);
                if (!accepted && _dispatcher.IsBusy)
                    CompleteFailure("另一項面板更新正在執行。");
            }
            catch (Exception ex)
            {
                CompleteFailure(ex.Message);
            }
        }

        internal void CompleteSuccess(ModelSummaryResult result)
        {
            Result = result;
            StatusMessage = result.Warnings != null && result.Warnings.Count > 0
                ? "更新完成，但有部分資料無法取得。"
                : "更新完成。";
        }

        internal void CompleteFailure(string message)
        {
            StatusMessage = "更新失敗，請稍後再試。";
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Action _execute;
            private readonly Func<bool> _canExecute;

            public RelayCommand(Action execute, Func<bool> canExecute)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

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
