using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitMCP.Core
{
    /// <summary>API-context-only native document equality; no path/title or managed wrapper identity.</summary>
    internal static class DocumentSessionIdentity
    {
        private static readonly List<Tuple<Document, string>> Sessions = new List<Tuple<Document, string>>();
        public static string GetDocumentIdentity(Document document)
        {
            if (document == null || !document.IsValidObject) throw new InvalidOperationException("目前沒有可用模型。");
            Sessions.RemoveAll(s => !s.Item1.IsValidObject);
            foreach (var entry in Sessions)
                if (entry.Item1.Equals(document)) return entry.Item2;
            var identity = Guid.NewGuid().ToString("N");
            Sessions.Add(Tuple.Create(document, identity));
            return identity;
        }
    }
}
