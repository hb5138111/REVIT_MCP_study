#if REVIT2026
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Core;
using RevitMCP.Core.Drawing;

namespace RevitMCP.UI
{
    internal sealed class RevitDrawingHost : IDrawingHost,IExternalEventHandler
    {
        private readonly ExternalEvent externalEvent;
        private Action<UIApplication>? pending;
        private Action<string>? failure;
        private long revision;
        public RevitDrawingHost(){externalEvent=ExternalEvent.Create(this);}
        public void ModelChanged()=>revision++;
        public bool Submit(string identity,Action<IDrawingContext> action,Action<string> failed)
        {
            if(pending!=null)return false;
            long expected=revision;failure=failed;
            pending=app=>
            {
                var ui=app.ActiveUIDocument??throw new InvalidOperationException("請先開啟專案模型。");var doc=ui.Document;
                if(doc.IsFamilyDocument||doc.IsReadOnly)throw new InvalidOperationException("請使用可編輯專案。");
                if(expected!=revision||identity!=""&&DocumentSessionIdentity.GetDocumentIdentity(doc)!=identity)throw new InvalidOperationException("模型已變更，請重新讀取與預覽。");
                action(new Context(ui));
            };
            try{if(externalEvent.Raise()==ExternalEventRequest.Accepted)return true;pending=null;failure=null;return false;}
            catch{pending=null;failure=null;throw;}
        }
        public void Execute(UIApplication app){try{pending?.Invoke(app);}catch(Exception e){failure?.Invoke(e.Message);}finally{pending=null;failure=null;}}
        public string GetName()=>"BIM Drawing Production Confirmed Dispatcher";
        internal sealed class Context : IDrawingContext
        {
            private readonly UIDocument ui;
            private readonly RevitDrawingService service;
            public Context(UIDocument ui){this.ui=ui;service=new(ui.Document);}
            public string Identity=>service.Identity;
            public DrawingChoice[] Sheets()=>service.Sheets();
            public DrawingChoice[] Levels()=>service.Levels();
            public DrawingChoice[] Sources(long level)=>service.Sources(level);
            public System.Collections.Generic.Dictionary<long,DrawingChoice[]> SourcesByLevel()=>service.SourcesByLevel();
            public DrawingZone[] Zones()=>service.Zones();
            public DrawingChoice[] Grids()=>service.Grids();
            public DrawingZone GridZone(string name,long[] grids,double paddingMm)=>service.GridZone(name,grids,paddingMm);
            public DrawingChoice[] ViewTemplates()=>service.ViewTemplates();
            public SheetTemplateBlueprint ConfigureViewRule(SheetTemplateBlueprint blueprint,long templateId,int? scale)=>service.ConfigureViewRule(blueprint,templateId,scale);
            public DrawingProjectData Load()=>service.ProductionData();
            public SheetTemplateBlueprint Extract(long sheet)=>service.Extract(sheet);
            public ExternalTitleBlockAnalysis AnalyzeExternal(string path,string unit,string rft)=>new ExternalTitleBlockService(ui.Document).Analyze(path,unit,rft);
            public SheetTemplateBlueprint LoadExternal(ExternalTitleBlockAnalysis analysis,string type,bool useExisting,bool confirmed)=>new ExternalTitleBlockService(ui.Document).Load(analysis,type,useExisting,confirmed);
            public DrawingTemplateProfile SaveProfile(DrawingTemplateProfile profile)=>service.SaveProfile(profile);
            public DrawingPlan Preview(DrawingPackageDefinition package)=>service.Preview(package);
            public long[] Apply(DrawingPlan plan,bool confirmed)=>service.Apply(plan,confirmed);
            public DrawingQaIssue[] Qa(Guid package)=>service.Qa(package);
            public DrawingSheetStatus[] SheetStatuses(Guid package,DrawingQaIssue[] issues)=>service.SheetStatuses(package,issues);
            public void Open(long id){var sheet=ui.Document.GetElement(new ElementId(id)) as ViewSheet??throw new InvalidOperationException("圖紙已遺失。");ui.RequestViewChange(sheet);}
        }
    }
}
#endif
