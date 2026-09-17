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
            public CadTerrainAnalysis AnalyzeCad(CadTerrainRequest request)=>CadTerrainService.Analyze(document,request);
            public SitePoint PickControlPoint()=>CoordinateTransformService.Metres((ui??throw new InvalidOperationException("需要可操作的模型視窗。")).Selection.PickPoint("選取控制點的模型位置"));
            public string Locate(long id)=>new CoordinationNavigationService().LocateElement(ui??throw new InvalidOperationException("需要可操作的模型視窗。"),new ElementId(id),new RevitMCP.Models.CoordinationNavigationSession()).Message;
            public string FormatVolume(double cubicMetres)=>RevitTerrainService.FormatVolume(document,cubicMetres);
            public long[] SelectedIds()=>ui?.Selection.GetElementIds().Select(id=>id.Value).OrderBy(id=>id).ToArray()??Array.Empty<long>();
            public string EarthworkSignature(EarthworkZone zone)=>RevitEarthworkRecords.Signature(document,zone);
            public EarthworkProjectData LoadEarthwork()=>RevitEarthworkRecords.Load(document);
            public EarthworkProjectData SaveEarthwork(EarthworkProjectData data,bool confirmed)=>RevitEarthworkRecords.Save(document,data,confirmed);
            public EarthworkSchedulePreview PreviewSchedule(System.Collections.Generic.IReadOnlyList<EarthworkRecord> rows)=>RevitEarthworkRecords.Preview(document,rows);
            public EarthworkSchedulePreview PreviewSchedule(System.Collections.Generic.IReadOnlyList<EarthworkRecord> rows,EarthworkScheduleKind kind)=>RevitEarthworkRecords.Preview(document,rows,kind);
            public System.Collections.Generic.IReadOnlyDictionary<EarthworkScheduleKind,string> EarthworkSchedules()=>Enum.GetValues<EarthworkScheduleKind>().ToDictionary(k=>k,k=>RevitEarthworkRecords.Schedule(document,k)==null?"未建立":"已建立；資料變更後請更新同步");
            public string OpenEarthworkSchedule(EarthworkScheduleKind kind)
            {
                var view=RevitEarthworkRecords.Schedule(document,kind)??throw new InvalidOperationException("尚未建立此明細表。");
                if(!RevitEarthworkRecords.IsProductionSchedule(document,view))throw new InvalidOperationException("此明細表包含隔離測試紀錄，正式工作流不可開啟；請先建立正式土方區並更新明細表。");
                (ui??throw new InvalidOperationException("需要模型視窗。")).RequestViewChange(view);return "已要求開啟 "+view.Name;
            }
            public EarthworkScheduleResult WriteSchedule(System.Collections.Generic.IReadOnlyList<EarthworkRecord> rows,EarthworkSchedulePreview preview,bool confirmed)=>RevitEarthworkRecords.WriteSchedule(document,rows,preview,confirmed);
            public EarthworkProjectData DeleteEarthwork(Guid id,bool deleteScheduleRecord,bool confirmed)=>RevitEarthworkRecords.Delete(document,id,deleteScheduleRecord,confirmed);
            public System.Collections.Generic.IReadOnlyList<SitePoint> SelectedBoundary()
            {
                var ids=ui?.Selection.GetElementIds()??throw new InvalidOperationException("請選取樓板、地形或閉合模型線。");
                return BoundaryFromIds(ids.Select(id=>id.Value).ToArray());
            }
            public System.Collections.Generic.IReadOnlyList<SitePoint> BoundaryFromIds(System.Collections.Generic.IReadOnlyList<long> ids)
            {
                var elements=ids.Select(id=>document.GetElement(new ElementId(id))??throw new InvalidOperationException($"邊界 Element {id} 已刪除。")).ToArray();
                System.Collections.Generic.IReadOnlyList<SitePoint> loop;
                if(elements.Length==1&&elements[0] is Floor floor&&document.GetElement(floor.SketchId) is Sketch sketch)
                {
                    if(sketch.Profile.Size!=1)throw new ArgumentException("含孔洞或多重邊界的樓板，第一版不自動選擇輪廓。");
                    loop=ReadLoop(sketch.Profile.get_Item(0).Cast<Curve>().ToArray());
                }
                else if(elements.Length>=3&&elements.All(e=>e is ModelCurve))loop=ReadLoop(elements.Cast<ModelCurve>().Select(e=>e.GeometryCurve).ToArray());
                else throw new ArgumentException("請選取一個無孔洞樓板，或至少三條閉合直線模型線；不使用 bounding box 當邊界。");
                if(!CadTerrainAnalysis.ValidBoundary(loop))throw new ArgumentException("邊界必須平面、閉合且為凸多邊形；不自動近似。");return loop;
            }
            private static System.Collections.Generic.IReadOnlyList<SitePoint> ReadLoop(Curve[] curves)
            {
                if(curves.Any(c=>c is not Line||!c.IsBound))throw new ArgumentException("第一版僅支援直線邊界。");
                var remaining=curves.ToList();var points=new System.Collections.Generic.List<XYZ>{remaining[0].GetEndPoint(0),remaining[0].GetEndPoint(1)};remaining.RemoveAt(0);
                while(remaining.Count>0)
                {
                    var candidates=remaining.Where(c=>c.GetEndPoint(0).IsAlmostEqualTo(points[^1])||c.GetEndPoint(1).IsAlmostEqualTo(points[^1])).ToArray();
                    if(candidates.Length!=1)throw new ArgumentException("邊界斷開或分岔。");var next=candidates[0];points.Add(next.GetEndPoint(next.GetEndPoint(0).IsAlmostEqualTo(points[^1])?1:0));remaining.Remove(next);
                }
                if(!points[0].IsAlmostEqualTo(points[^1]))throw new ArgumentException("邊界未閉合。");return CadTerrainAnalysis.OpenLoop(points.Select(CoordinateTransformService.Metres).ToArray());
            }
            private readonly Document document;
            private readonly string? reportRoot;
            private readonly UIDocument? ui;
            public Context(Document document,string? reportRoot=null,UIDocument? ui=null){this.document=document;this.reportRoot=reportRoot;this.ui=ui;}
            public SiteChoice SelectedElement(bool terrain)
            {
                var ids=ui?.Selection.GetElementIds();
                if(ids==null||ids.Count!=1)throw new InvalidOperationException("請在 Revit 選取一個 host 元素；不接受 Link instance。");
                var element=document.GetElement(ids.Single());
                if(terrain?element is not Toposolid:element is not Floor && element is not RoofBase && element is not Toposolid)throw new ArgumentException(terrain?"請選取本模型的地形實體。":"開挖構件僅接受樓板、屋頂或地形；不接受連結模型。");
                var box=element.get_BoundingBox(null)??throw new InvalidOperationException("選取元素沒有範圍。");
                var min=CoordinateTransformService.Metres(box.Min);var max=CoordinateTransformService.Metres(box.Max);
                return new(element.Id.GetIdValue(),$"{element.Category.Name} / {element.Name}");
            }
            public SiteContextSnapshot Snapshot()
            {
                var c=CoordinateTransformService.Read(document);
                var unit=document.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();double factor=UnitUtils.ConvertToInternalUnits(1,unit)*.3048;
                string Format(SitePoint p)=>string.Join(" / ",new[]{p.X,p.Y,p.Z}.Select(v=>UnitFormatUtils.Format(document.GetUnits(),SpecTypeId.Length,v/.3048,false)));
                string evidence=$"目前位置：{document.ActiveProjectLocation.Name}\n內部原點：{Format(c.InternalOrigin)}\n專案基準點：{Format(c.ProjectBasePoint)}\n測量點：{Format(c.SurveyPoint)}\n真北旋轉：{c.TrueNorthRotation*180/Math.PI:F4}°\n座標正反向已與 ProjectPosition 驗證。";
                string symbol=unit==UnitTypeId.Millimeters?"mm":unit==UnitTypeId.Meters?"m":unit==UnitTypeId.Centimeters?"cm":unit==UnitTypeId.Feet?"ft":LabelUtils.GetLabelForUnit(unit);
                var areaUnit=document.GetUnits().GetFormatOptions(SpecTypeId.Area).GetUnitTypeId();var volumeUnit=document.GetUnits().GetFormatOptions(SpecTypeId.Volume).GetUnitTypeId();
                double areaFactor=UnitUtils.ConvertFromInternalUnits(UnitUtils.ConvertToInternalUnits(1,areaUnit),UnitTypeId.SquareMeters),volumeFactor=UnitUtils.ConvertFromInternalUnits(UnitUtils.ConvertToInternalUnits(1,volumeUnit),UnitTypeId.CubicMeters);
                return new(DocumentSessionIdentity.GetDocumentIdentity(document),c.SurveyToInternal,evidence,RevitTerrainService.Types(document).Select(t=>new SiteChoice(t.Id,t.Name)).ToArray(),RevitTerrainService.Levels(document).Select(t=>new SiteChoice(t.Id,t.Name,UnitUtils.ConvertFromInternalUnits(((Level)document.GetElement(new ElementId(t.Id))).ProjectElevation,UnitTypeId.Meters))).ToArray(),factor,symbol,areaFactor,LabelUtils.GetLabelForUnit(areaUnit),volumeFactor,LabelUtils.GetLabelForUnit(volumeUnit));
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
                var result=new{Quantity=r,ProjectCut=RevitTerrainService.FormatVolume(document,r.CutVolume),ProjectFill=RevitTerrainService.FormatVolume(document,r.FillVolume),ProjectNet=RevitTerrainService.FormatVolume(document,r.CutVolume-r.FillVolume)};
                Save("earthwork-tin",audit,result);
                string Length(double metres)=>UnitFormatUtils.Format(document.GetUnits(),SpecTypeId.Length,metres/.3048,false);
                return new SiteEarthworkSummary(UnitFormatUtils.Format(document.GetUnits(),SpecTypeId.Area,r.Area/Math.Pow(.3048,2),false),result.ProjectCut,result.ProjectFill,result.ProjectNet,Length(r.MaxDepth),"頂面三角網裁切積分",document.GetElement(new ElementId(terrain)).Name,Length(elevation),string.Join("；",r.Warnings),r);
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
