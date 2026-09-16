using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Core;
using RevitMCP.Models;

namespace RevitMCP.UI
{
    internal sealed class RevitCoordinationHost : ICoordinationHost
    {
        private readonly PanelReadOnlyDispatcher dispatcher;
        public RevitCoordinationHost(PanelReadOnlyDispatcher dispatcher) { this.dispatcher = dispatcher; }
        public bool IsBusy => dispatcher.IsBusy;
        public event EventHandler BusyChanged { add => dispatcher.BusyChanged += value; remove => dispatcher.BusyChanged -= value; }
        public bool Submit(Action<ICoordinationContext> action, Action<string> failure) => dispatcher.TrySubmit(
            PanelReadOnlyRequestKind.CoordinationScan, app => action(new Context(app)), failure);
        private sealed class Context : ICoordinationContext
        {
            private readonly UIDocument ui;
            private readonly Document document;
            private readonly CoordinationService service = new CoordinationService();
            public Context(UIApplication application)
            {
                ui = application.ActiveUIDocument ?? throw new InvalidOperationException("請先開啟模型。");
                document = ui.Document;
                if (document.IsFamilyDocument) throw new InvalidOperationException("請開啟專案模型；不支援 Family 編輯器。");
                DocumentIdentity = DocumentSessionIdentity.GetDocumentIdentity(document);
            }
            public string DocumentIdentity { get; }
            public IReadOnlyList<CoordinationSource> GetSources() => service.GetSources(document);
            public IReadOnlyList<CoordinationLevel> GetLevels(long linkId) => service.GetLevelOptions(document, linkId);
            public IReadOnlyList<string> GetMepCategories(long linkId) => service.GetMepCategories(document, linkId);
            public double ParseClearance(string text)
            {
                if (!UnitFormatUtils.TryParse(document.GetUnits(), SpecTypeId.Length, text, out double feet))
                    throw new ArgumentException("請輸入有效長度，可明確輸入 mm，例如 25 mm。");
                return feet * 304.8;
            }
            public CoordinationResult Scan(CoordinationRequest request) => service.Scan(document, request);
            public void Highlight(CoordinationRow row, bool mep, bool host)
            {
                var ids = new List<ElementId>();
                if (mep) ids.Add(CoordinationService.ResolveNavigation(document, row.Mep).Id);
                if (host) ids.Add(CoordinationService.ResolveNavigation(document, row.Host).Id);
                ids = ids.Distinct().ToList(); ui.Selection.SetElementIds(ids);
                if (!ui.Selection.GetElementIds().OrderBy(id => id.GetIdValue()).SequenceEqual(ids.OrderBy(id => id.GetIdValue())))
                    throw new InvalidOperationException("選取 read-back 不一致，請重新整理後再試。");
                ui.ShowElements(ids);
            }
        }
    }
}
