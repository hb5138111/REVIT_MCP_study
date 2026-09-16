using System;
using Autodesk.Revit.UI;
using RevitMCP.Core;

namespace RevitMCP.UI
{
    public sealed class BimConstructionPanelViewModel
    {
        public CoordinationViewModel Coordination { get; }
        public BimConstructionPanelViewModel()
        {
            Coordination = new CoordinationViewModel(new RevitCoordinationHost(new PanelReadOnlyDispatcher()));
        }
        public void Initialize() => Coordination.Initialize();
        internal void AttachLifecycle(UIControlledApplication application)
        {
            application.ViewActivated += (_, e) =>
            {
                var doc = e.CurrentActiveView?.Document;
                Coordination.DocumentChanged(doc != null && doc.IsValidObject ? DocumentSessionIdentity.GetDocumentIdentity(doc) : "");
            };
            application.ControlledApplication.DocumentChanged += (_, e) => Coordination.DocumentChanged(Coordination.DocumentIdentity, true);
            application.ControlledApplication.DocumentClosed += (_, e) => Coordination.DocumentChanged("", true);
        }
    }
}
