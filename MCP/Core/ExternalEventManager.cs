using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace RevitMCP.Core
{
    /// <summary>
    /// 外部事件管理器
    /// 確保命令在 Revit UI 執行緒中執行
    ///
    /// 並行命令必須排隊：舊版只有單一 _action 欄位，第二個請求在 Revit 執行
    /// ExternalEvent 之前到達時會覆寫掉第一個，而對已 Pending 的事件再 Raise()
    /// 只會回傳 Pending、不會多排一次 —— 第一個請求因此被靜默丟棄，Node 端等到
    /// 「Command timed out」。現在每個請求進 ConcurrentQueue，Execute 一次清空整條佇列。
    /// </summary>
    public class ExternalEventManager
    {
        private static ExternalEventManager _instance;
        private static readonly object _lock = new object();

        private ExternalEvent _externalEvent;
        private CommandEventHandler _eventHandler;

        private ExternalEventManager()
        {
            _eventHandler = new CommandEventHandler();
            _externalEvent = ExternalEvent.Create(_eventHandler);
        }

        public static ExternalEventManager Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new ExternalEventManager();
                    }
                    return _instance;
                }
            }
        }

        /// <summary>
        /// 執行命令（排入佇列，由 UI 執行緒依序執行）
        /// </summary>
        public void ExecuteCommand(Action<UIApplication> action)
        {
            _eventHandler.Enqueue(action);
            RaiseWithRetry();
        }

        /// <summary>
        /// Accepted / Pending 都代表佇列一定會被清到。Denied / TimedOut 可能發生在
        /// handler 剛清完佇列、尚未返回的窄窗內 —— 此時剛排入的命令沒有人會取走，
        /// 所以在背景短暫重試，直到事件被接受或佇列已被其他次 Execute 清空。
        /// </summary>
        private void RaiseWithRetry()
        {
            var result = _externalEvent.Raise();
            if (result == ExternalEventRequest.Accepted || result == ExternalEventRequest.Pending)
            {
                return;
            }

            Logger.Debug($"[ExternalEvent] Raise() 回傳 {result}，背景重試");
            _ = Task.Run(async () =>
            {
                for (int attempt = 0; attempt < 100 && _eventHandler.HasPending; attempt++)
                {
                    await Task.Delay(50);
                    var retry = _externalEvent.Raise();
                    if (retry == ExternalEventRequest.Accepted || retry == ExternalEventRequest.Pending)
                    {
                        return;
                    }
                }
                if (_eventHandler.HasPending)
                {
                    Logger.Error("[ExternalEvent] Raise() 重試失敗，佇列中仍有命令未執行");
                }
            });
        }

        /// <summary>
        /// 命令事件處理器
        /// </summary>
        private class CommandEventHandler : IExternalEventHandler
        {
            private readonly ConcurrentQueue<Action<UIApplication>> _queue = new ConcurrentQueue<Action<UIApplication>>();

            public bool HasPending => !_queue.IsEmpty;

            public void Enqueue(Action<UIApplication> action)
            {
                if (action != null)
                {
                    _queue.Enqueue(action);
                }
            }

            public void Execute(UIApplication app)
            {
                // 清空整條佇列；每個命令獨立 try/catch，一個失敗不影響後面的命令。
                while (_queue.TryDequeue(out var action))
                {
                    try
                    {
                        action(app);
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("命令執行錯誤", ex.Message);
                    }
                }
            }

            public string GetName()
            {
                return "RevitMCP Command Handler";
            }
        }
    }
}
