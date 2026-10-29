using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace RevitMCP.Core
{
    /// <summary>
    /// 外部事件管理器
    /// 確保命令在 Revit UI 執行緒中執行
    ///
    /// 以佇列保存待執行命令：多個命令在 Revit 執行事件前接連到達時，
    /// 舊版單一欄位會被後到的命令覆蓋（且事件已在排隊時 Raise 不會多跑一次），
    /// 被覆蓋的命令永遠不會執行也不會回應，MCP 端只能等到逾時。
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
        /// 執行命令（排入佇列，依到達順序執行）
        /// </summary>
        public void ExecuteCommand(Action<UIApplication> action)
        {
            _eventHandler.Enqueue(action);
            // 事件已在排隊時 Raise 會回傳 Pending，不影響：Execute 會把佇列整個跑完
            _externalEvent.Raise();
        }

        /// <summary>
        /// 命令事件處理器
        /// </summary>
        private class CommandEventHandler : IExternalEventHandler
        {
            private readonly ConcurrentQueue<Action<UIApplication>> _queue = new ConcurrentQueue<Action<UIApplication>>();

            public void Enqueue(Action<UIApplication> action)
            {
                if (action != null) _queue.Enqueue(action);
            }

            public void Execute(UIApplication app)
            {
                // 跑完目前佇列中的所有命令；執行期間新到的命令也會在這一輪被取出，
                // 迴圈結束後才到的命令則由它自己的 Raise 觸發下一輪
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
