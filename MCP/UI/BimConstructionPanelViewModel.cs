using System;
using Autodesk.Revit.UI;
using RevitMCP.Core;

namespace RevitMCP.UI
{
    public sealed class BimConstructionPanelViewModel
    {
        public CoordinationViewModel Coordination { get; }
#if REVIT2026
        public SiteTerrainViewModel Site { get; }
        public DrawingProductionViewModel Drawing { get; }
        private readonly RevitDrawingHost drawingHost;
        private readonly RevitSiteHost siteHost;
#endif
        public BimConstructionPanelViewModel()
        {
            Coordination = new CoordinationViewModel(new RevitCoordinationHost(new PanelReadOnlyDispatcher()));
#if REVIT2026
            siteHost = new RevitSiteHost();
            Site = new SiteTerrainViewModel(siteHost);
            drawingHost = new RevitDrawingHost();
            Drawing = new DrawingProductionViewModel(drawingHost);
#endif
        }
        public void Initialize()
        {
            Coordination.Initialize();
#if REVIT2026
            Drawing.Refresh();
#endif
        }
        internal void AttachLifecycle(UIControlledApplication application)
        {
            application.ViewActivated += (_, e) =>
            {
                var doc = e.CurrentActiveView?.Document;
                Coordination.DocumentChanged(doc != null && doc.IsValidObject ? DocumentSessionIdentity.GetDocumentIdentity(doc) : "");
#if REVIT2026
                Site.DocumentChanged(doc != null && doc.IsValidObject ? DocumentSessionIdentity.GetDocumentIdentity(doc) : "");
                Drawing.DocumentChanged(doc != null && doc.IsValidObject ? DocumentSessionIdentity.GetDocumentIdentity(doc) : "");
#endif
            };
            application.ControlledApplication.DocumentChanged += (_, e) => Coordination.DocumentChanged(Coordination.DocumentIdentity, true);
            application.ControlledApplication.DocumentClosed += (_, e) => Coordination.DocumentChanged("", true);
#if REVIT2026
            application.ControlledApplication.DocumentChanged += (_, e) => { var identity=DocumentSessionIdentity.GetDocumentIdentity(e.GetDocument()); if(Site.Context?.DocumentIdentity==identity){siteHost.ModelChanged(); Site.DocumentChanged(identity, true);} };
            application.ControlledApplication.DocumentClosed += (_, e) => { siteHost.ModelChanged(); Site.DocumentChanged("", true); };
            application.ControlledApplication.DocumentChanged += (_, e) => { var id=DocumentSessionIdentity.GetDocumentIdentity(e.GetDocument()); if(Drawing.DocumentIdentity==id){drawingHost.ModelChanged(); Drawing.DocumentChanged(id, true);} };
#endif
        }
    }
}
