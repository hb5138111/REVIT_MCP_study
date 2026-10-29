using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

namespace RevitMCP.Core.Review
{
    /// <summary>
    /// 監聽 DocumentChanged，把每筆交易（使用者手動或 AI）記成精簡紀錄，
    /// 讓 AI 透過 get_recent_changes 知道「上次看過之後模型被改了什麼」，
    /// 不必截螢幕，也避免 AI 拿過期狀態做決策。
    /// 只保留最近 Capacity 筆，記憶體固定。
    /// </summary>
    internal static class ChangeTracker
    {
        private const int Capacity = 500;
        private const int SampleIds = 20;
        private static readonly object Gate = new object();
        private static readonly LinkedList<ChangeEntry> Entries = new LinkedList<ChangeEntry>();
        private static long _seq;

        internal sealed class ChangeEntry
        {
            public long Seq;
            public DateTime Time;
            public string Document;
            public string Operation;
            public List<string> Transactions;
            public bool ByAI;
            public int Added, Modified, Deleted;
            public List<long> AddedIds, ModifiedIds, DeletedIds;
            public Dictionary<string, int> CategoryCounts;
        }

        public static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            try
            {
                Document doc = e.GetDocument();
                if (doc == null || doc.IsFamilyDocument) return;

                var added = e.GetAddedElementIds();
                var modified = e.GetModifiedElementIds();
                var deleted = e.GetDeletedElementIds();
                var names = e.GetTransactionNames()?.ToList() ?? new List<string>();

                var cats = new Dictionary<string, int>();
                foreach (var id in added.Concat(modified).Take(2000))
                {
                    string c;
                    try { c = doc.GetElement(id)?.Category?.Name; } catch { c = null; }
                    if (string.IsNullOrEmpty(c)) continue;
                    cats[c] = cats.TryGetValue(c, out int n) ? n + 1 : 1;
                }

                var entry = new ChangeEntry
                {
                    Time = DateTime.Now,
                    Document = doc.Title,
                    Operation = SafeOperation(e),
                    Transactions = names,
                    ByAI = names.Any(n => n.StartsWith("AI:", StringComparison.Ordinal) || n.StartsWith("MCP", StringComparison.OrdinalIgnoreCase)),
                    Added = added.Count,
                    Modified = modified.Count,
                    Deleted = deleted.Count,
                    AddedIds = added.Take(SampleIds).Select(i => (long)i.GetIdValue()).ToList(),
                    ModifiedIds = modified.Take(SampleIds).Select(i => (long)i.GetIdValue()).ToList(),
                    DeletedIds = deleted.Take(SampleIds).Select(i => (long)i.GetIdValue()).ToList(),
                    CategoryCounts = cats
                };

                lock (Gate)
                {
                    entry.Seq = ++_seq;
                    Entries.AddLast(entry);
                    while (Entries.Count > Capacity) Entries.RemoveFirst();
                }
            }
            catch (Exception ex)
            {
                // 事件處理器絕不能丟例外，否則會干擾使用者在 Revit 的操作
                try { Logger.Error("[ChangeTracker] " + ex.Message); } catch { }
            }
        }

        private static string SafeOperation(DocumentChangedEventArgs e)
        {
            try { return e.Operation.ToString(); } catch { return null; }
        }

        public static long CurrentSeq { get { lock (Gate) return _seq; } }

        public static List<ChangeEntry> Since(long afterSeq, int limit, string documentTitle)
        {
            lock (Gate)
            {
                return Entries.Where(x => x.Seq > afterSeq && (documentTitle == null || x.Document == documentTitle))
                              .Reverse().Take(limit).Reverse().ToList();
            }
        }
    }
}
