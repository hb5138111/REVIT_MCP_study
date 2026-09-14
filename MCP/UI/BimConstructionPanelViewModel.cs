using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Autodesk.Revit.UI;
using RevitMCP.Core;
using RevitMCP.Models;

namespace RevitMCP.UI
{
    public sealed class BimConstructionPanelViewModel : INotifyPropertyChanged
    {
        private readonly ModelSummaryExternalEventHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private ModelSummaryResult _result;
        private bool _isRefreshing;
        private string _statusMessage = "就緒。請按「重新整理」讀取目前模型資訊。";

        public BimConstructionPanelViewModel()
        {
            _handler = new ModelSummaryExternalEventHandler(this, new ModelSummaryService());
            _externalEvent = ExternalEvent.Create(_handler);
            RefreshCommand = new RelayCommand(RequestRefresh, () => !IsRefreshing);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand RefreshCommand { get; }

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
                ExternalEventRequest request = _externalEvent.Raise();
                if (request != ExternalEventRequest.Accepted)
                    CompleteFailure("Revit did not accept the refresh request (" + request + ").");
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
            IsRefreshing = false;
        }

        internal void CompleteFailure(string message)
        {
            StatusMessage = "更新失敗，請稍後再試。";
            IsRefreshing = false;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed class ModelSummaryExternalEventHandler : IExternalEventHandler
        {
            private readonly BimConstructionPanelViewModel _viewModel;
            private readonly ModelSummaryService _service;

            public ModelSummaryExternalEventHandler(
                BimConstructionPanelViewModel viewModel,
                ModelSummaryService service)
            {
                _viewModel = viewModel;
                _service = service;
            }

            public void Execute(UIApplication app)
            {
                try
                {
                    if (app.ActiveUIDocument == null)
                        throw new InvalidOperationException("目前沒有開啟的 Revit 文件。");

                    ModelSummaryResult result = _service.GetSummary(
                        app,
                        new ModelSummaryRequest { CategoryLimit = 10 });
                    _viewModel.CompleteSuccess(result);
                }
                catch (Exception ex)
                {
                    _viewModel.CompleteFailure(ex.Message);
                }
                finally
                {
                    if (_viewModel.IsRefreshing)
                        _viewModel.IsRefreshing = false;
                }
            }

            public string GetName()
            {
                return "BIM Construction Panel Model Summary Refresh";
            }
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
