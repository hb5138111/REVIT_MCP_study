using System;
using Autodesk.Revit.UI;

namespace RevitMCP.UI
{
    internal enum PanelReadOnlyRequestKind
    {
        ModelSummary,
        TypeInventory,
        HighlightTypeInstances,
        LocateFirstTypeInstance
    }

    internal sealed class PanelReadOnlyDispatcher : IExternalEventHandler
    {
        private readonly ExternalEvent _externalEvent;
        private Action<UIApplication> _pendingAction;
        private Action<string> _pendingFailure;
        private PanelReadOnlyRequestKind? _pendingKind;

        public PanelReadOnlyDispatcher()
        {
            _externalEvent = ExternalEvent.Create(this);
        }

        public bool IsBusy { get; private set; }
        public event EventHandler BusyChanged;

        public bool TrySubmit(
            PanelReadOnlyRequestKind kind,
            Action<UIApplication> action,
            Action<string> failure)
        {
            if (IsBusy) return false;
            _pendingKind = kind;
            _pendingAction = action ?? throw new ArgumentNullException(nameof(action));
            _pendingFailure = failure;
            SetBusy(true);

            try
            {
                ExternalEventRequest request = _externalEvent.Raise();
                if (request == ExternalEventRequest.Accepted) return true;
                Release();
                failure?.Invoke("Revit 未接受更新要求（" + request + "）。");
                return false;
            }
            catch (Exception ex)
            {
                Release();
                failure?.Invoke(ex.Message);
                return false;
            }
        }

        public void Execute(UIApplication app)
        {
            Action<UIApplication> action = _pendingAction;
            Action<string> failure = _pendingFailure;
            try
            {
                action?.Invoke(app);
            }
            catch (Exception ex)
            {
                failure?.Invoke(ex.Message);
            }
            finally
            {
                Release();
            }
        }

        public string GetName() => "BIM Construction Panel Read-Only Dispatcher";

        private void Release()
        {
            _pendingAction = null;
            _pendingFailure = null;
            _pendingKind = null;
            SetBusy(false);
        }

        private void SetBusy(bool value)
        {
            if (IsBusy == value) return;
            IsBusy = value;
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
