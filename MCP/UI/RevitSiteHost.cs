#if REVIT2026
using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitMCP.Core;
using RevitMCP.Core.Site;

namespace RevitMCP.UI
{
    /// <summary>Separate write-capable dispatcher; never widens the read-only coordination dispatcher.</summary>
    internal sealed class RevitSiteHost : ISiteHost,IExternalEventHandler
    {
        private readonly ExternalEvent externalEvent;
        private Action<UIApplication>? pending;
        private Action<string>? failure;
        private long modelRevision;
        public RevitSiteHost(){externalEvent=ExternalEvent.Create(this);}
        public void ModelChanged()=>modelRevision++;
        public bool Submit(string expectedDocument,Action<ISiteContext> work,Action<string> failed)
        {
            if(pending!=null)return false;
            long version=modelRevision;failure=failed;
            pending=app=>
            {
                var d=app.ActiveUIDocument?.Document??throw new InvalidOperationException("請開啟專案模型。");
                if(d.IsFamilyDocument||d.IsReadOnly)throw new InvalidOperationException("需要可編輯的專案文件。");
                if(version!=modelRevision || expectedDocument!="" && expectedDocument!=DocumentSessionIdentity.GetDocumentIdentity(d))throw new InvalidOperationException("文件／模型已變動，舊 Preview 已失效。");
                work(new Context(d,null,app.ActiveUIDocument));
            };
            try{if(externalEvent.Raise()==ExternalEventRequest.Accepted)return true;pending=null;failure=null;return false;}
            catch{pending=null;failure=null;throw;}
        }
        public void Execute(UIApplication app){try{pending?.Invoke(app);}catch(Exception e){failure?.Invoke(e.Message);}finally{pending=null;failure=null;}}
        public string GetName()=>"BIM Site Confirmed Write Dispatcher";
        internal sealed class Context : ISiteContext
        {
            private readonly Document document;
            private readonly string? reportRoot;
            private readonly UIDocument? ui;
            public Context(Document document,string? reportRoot=null,UIDocument? ui=null){this.document=document;this.reportRoot=reportRoot;this.ui=ui;}
            public SiteChoice SelectedElement(bool terrain)
            {
                var ids=ui?.Selection.GetElementIds();
                if(ids==null||ids.Count!=1)throw new InvalidOperationException("請在 Revit 選取一個 host 元素；不接受 Link instance。");
                var element=document.GetElement(ids.Single());
                if(terrain?element is not Toposolid:element is not Floor && element is not RoofBase && element is not Toposolid)throw new ArgumentException("選取類別不符合 Terrain/Cutter。");
                var box=element.get_BoundingBox(null)??throw new InvalidOperationException("選取元素沒有範圍。");
                var min=CoordinateTransformService.Metres(box.Min);var max=CoordinateTransformService.Metres(box.Max);
                return new(element.Id.GetIdValue(),$"{element.Category.Name} / {element.Name} / ID {element.Id.GetIdValue()}\nInternal bounds (m): {min} → {max}");
            }
            public SiteContextSnapshot Snapshot()
            {
                var c=CoordinateTransformService.Read(document);
                return new(DocumentSessionIdentity.GetDocumentIdentity(document),c.SurveyToInternal,JsonConvert.SerializeObject(c,Formatting.Indented),RevitTerrainService.Types(document).Select(t=>new SiteChoice(t.Id,t.Name)).ToArray(),RevitTerrainService.Levels(document).Select(t=>new SiteChoice(t.Id,t.Name)).ToArray());
            }
            public SiteCreateOutcome Create(SiteCreateRequest r,bool confirmed)
            {
                string? receipt=null;
                var result=RevitTerrainService.Create(document,r.Points,r.TypeId,r.LevelId,r.Tolerance,confirmed,r.LargeOverride,value=>receipt=Save("terrain-create",r.Audit,value));
                return new(result.ElementId,$"Element {result.ElementId} / {result.Category}\nArea {result.ProjectUnitArea}；Volume {result.ProjectUnitVolume}\nReport {receipt}");
            }
            public double Excavate(long terrain,long cutter,bool execute,bool confirmed,double? expected,object audit)
                =>RevitTerrainService.Excavate(document,terrain,cutter,execute,confirmed,expected,value=>Save("excavation",audit,new{ExistingTerrainId=terrain,CutterId=cutter,CutVolume=value,FillVolume=0,NetVolume=-value,CalculationMethod="Revit TOTAL_EXCAVATION_VOLUME delta",ProjectUnits=RevitTerrainService.FormatVolume(document,value)}));
            public object Calculate(long terrain,System.Collections.Generic.IReadOnlyList<SitePoint> boundary,double elevation,double tolerance,object audit)
            {
                var r=EarthworkEngine.Calculate(RevitTerrainService.Surface(document,terrain),boundary,elevation,tolerance);r.ExistingTerrainId=terrain;
                var result=new{Quantity=r,ProjectCut=RevitTerrainService.FormatVolume(document,r.CutVolume),ProjectFill=RevitTerrainService.FormatVolume(document,r.FillVolume),ProjectNet=RevitTerrainService.FormatVolume(document,r.NetVolume)};
                Save("earthwork-tin",audit,result);return JsonConvert.SerializeObject(result,Formatting.Indented);
            }
            private string Save(string operation,object audit,object result)
            {
                string root=reportRoot??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"RevitMCP","SiteReports");
                Directory.CreateDirectory(root);string name=Path.Combine(root,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+operation+"-"+Guid.NewGuid().ToString("N"));
                var envelope=new{SchemaVersion=1,Operation=operation,Timestamp=DateTimeOffset.UtcNow,Units="SI m/m²/m³; project-formatted values labelled separately",Audit=audit,Result=result};
                string json=JsonConvert.SerializeObject(envelope,Formatting.Indented);
                File.WriteAllText(name+".json",json,Encoding.UTF8);
                // Two-column lossless audit CSV; escaped JSON retains nested controls, diagnostics and quantities.
                string Escape(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
                File.WriteAllText(name+".csv","Field,Value\r\nOperation,"+Escape(operation)+"\r\nAudit,"+Escape(JsonConvert.SerializeObject(audit))+"\r\nResult,"+Escape(JsonConvert.SerializeObject(result))+"\r\n",Encoding.UTF8);
                File.WriteAllText(name+".md","# 基地／土方稽核\n\n"+operation+"\n\nUnits: m / m² / m³\n\n```json\n"+json+"\n```\n",Encoding.UTF8);return name;
            }
        }
    }
}
#endif
