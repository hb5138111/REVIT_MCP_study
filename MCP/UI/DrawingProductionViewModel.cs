#if REVIT2026 || DRAWING_TESTS
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using RevitMCP.Core.Drawing;

namespace RevitMCP.UI
{
    public interface IDrawingContext
    {
        string Identity { get; }
        DrawingChoice[] Sheets();
        DrawingChoice[] Levels();
        DrawingChoice[] Sources(long level);
        DrawingZone[] Zones();
        DrawingChoice[] Grids();
        DrawingZone GridZone(string name,long[] grids,double paddingMm);
        DrawingChoice[] ViewTemplates();
        SheetTemplateBlueprint ConfigureViewRule(SheetTemplateBlueprint blueprint,long templateId,int? scale);
        DrawingProjectData Load();
        SheetTemplateBlueprint Extract(long sheet);
        DrawingTemplateProfile SaveProfile(DrawingTemplateProfile profile);
        DrawingPlan Preview(DrawingPackageDefinition package);
        long[] Apply(DrawingPlan plan,bool confirmed);
        DrawingQaIssue[] Qa(Guid package);
        DrawingSheetStatus[] SheetStatuses(Guid package,DrawingQaIssue[] issues);
        void Open(long sheetId);
    }
    public interface IDrawingHost { bool Submit(string identity,Action<IDrawingContext> action,Action<string> failure); }
    public sealed class DrawingProductionViewModel : INotifyPropertyChanged
    {
        private readonly IDrawingHost host;
        private string identity="";
        private int revision;
        private int previewRevision=-1;
        public DrawingProductionViewModel(IDrawingHost host){this.host=host;}
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool Busy {get;private set;}
        public int Step {get;private set;}
        public int RenderEpoch {get;private set;}
        public string Status {get;private set;}="選擇樣板圖紙，開始規劃施工圖。";
        public DrawingChoice[] Sheets {get;private set;}=Array.Empty<DrawingChoice>();
        public DrawingChoice[] Levels {get;private set;}=Array.Empty<DrawingChoice>();
        public DrawingZone[] Zones {get;private set;}=Array.Empty<DrawingZone>();
        public DrawingChoice[] Grids {get;private set;}=Array.Empty<DrawingChoice>();
        public DrawingProjectData Data {get;private set;}=new();
        public DrawingPackageDefinition Package {get;private set;}=new();
        public DrawingPlan? Plan {get;private set;}
        public DrawingQaIssue[] Issues {get;private set;}=Array.Empty<DrawingQaIssue>();
        public long[] ResultIds {get;private set;}=Array.Empty<long>();
        public DrawingSheetStatus[] SheetList {get;private set;}=Array.Empty<DrawingSheetStatus>();
        public string QaSummary=>$"總圖紙 {SheetList.Length}；可供複核 {SheetList.Count(s=>s.Readiness=="Ready")}／需檢查 {SheetList.Count(s=>s.Readiness=="NeedsReview")}／錯誤 {SheetList.Count(s=>s.Readiness=="Draft")}";
        public bool CanApply=>!Busy&&Step==3&&Plan?.CanApply==true&&previewRevision==revision;
        public string Summary=>Plan?.Summary??"尚未產生計畫。";
        private void Notify()=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(""));
        public void Invalidate(){revision++;previewRevision=-1;Plan=null;Notify();}
        public void DocumentChanged(string current,bool modelChanged=false)
        {
            if(current!=identity){identity=current;Package=new();Sheets=Array.Empty<DrawingChoice>();Levels=Array.Empty<DrawingChoice>();Zones=Array.Empty<DrawingZone>();Data=new();SheetList=Array.Empty<DrawingSheetStatus>();Grids=Array.Empty<DrawingChoice>();ResultIds=Array.Empty<long>();Issues=Array.Empty<DrawingQaIssue>();Step=0;Invalidate();}
            else if(modelChanged&&!Busy)Invalidate();
        }
        private void Submit(Action<IDrawingContext> action)
        {
            if(Busy)return;Busy=true;Notify();
            try
            {
                if(!host.Submit(identity,c=>{try{action(c);}catch(Exception e){Status=e.Message;}finally{Busy=false;Notify();}},error=>{Busy=false;Status=error;Invalidate();Notify();}))
                {Busy=false;Status="Revit 正忙碌，請稍後重試。";Notify();}
            }catch(Exception e){Busy=false;Status=e.Message;Notify();}
        }
        public void Refresh()=>Submit(c=>{identity=c.Identity;Sheets=c.Sheets();Levels=c.Levels();Zones=c.Zones();Grids=c.Grids();Data=c.Load();Invalidate();Status=$"找到 {Sheets.Length} 張圖紙、{Levels.Length} 個樓層、{Zones.Length} 個 Scope Box。";});
        public void AddGridZone(string name,long[] grids,double paddingMm)=>Submit(c=>{var zone=c.GridZone(name,grids,paddingMm);Package.Zones.RemoveAll(z=>z.ZoneId==zone.ZoneId);Package.Zones.Add(zone);Zones=Zones.Where(z=>z.ZoneId!=zone.ZoneId).Append(zone).ToArray();Invalidate();Status="已加入網格分區："+name;});
        public void Extract(long sheet)=>Submit(c=>{var blueprint=c.Extract(sheet);Package.Profile.Blueprint=blueprint;RenderEpoch++;Invalidate();Status=$"已擷取 {blueprint.SourceSheetNumber}：主視埠 {blueprint.Viewports.Count}／圖例 {blueprint.Legends.Count}／明細表 {blueprint.Schedules.Count}。";});
        public void SaveProfile()=>Submit(c=>{Package.Profile=c.SaveProfile(Package.Profile);Data=c.Load();Invalidate();Status="樣板已保存；既有圖紙不會自動套用。";});
        public void UseProfile(DrawingTemplateProfile profile){Package.Profile=profile;RenderEpoch++;Invalidate();}
        public void UsePackage(DrawingPackageDefinition package){Package=package;RenderEpoch++;Invalidate();Status="已載入出圖包；請重新預覽模型差異。";Notify();}
        public void ReadSources(long level,Action<DrawingChoice[]> result)=>Submit(c=>result(c.Sources(level)));
        public void ReadViewTemplates(Action<DrawingChoice[]> result)=>Submit(c=>result(c.ViewTemplates()));
        public void ConfigureViewRule(long templateId,int? scale)=>Submit(c=>{Package.Profile.Blueprint=c.ConfigureViewRule(Package.Profile.Blueprint,templateId,scale);RenderEpoch++;Invalidate();Status="已更新本次視圖規則；請重新預覽。";});
        public void SelectScope(IEnumerable<DrawingChoice> levels,IEnumerable<DrawingZone> zones)
        {Package.Levels=levels.ToList();Package.Zones=zones.ToList();Invalidate();}
        public void GeneratePlan()=>Submit(c=>{Plan=c.Preview(Package);previewRevision=revision;Step=2;Status=Plan.CanApply?"計畫已產生，請檢查圖號與版面。":string.Join("；",Plan.Errors.Concat(Plan.Rows.SelectMany(r=>r.Issues)));});
        public void GoToStep(int step){if(step<0||step>4)return;if(step==3&&Plan?.CanApply!=true){Status="請先完成無衝突計畫。";Notify();return;}Step=step;Notify();}
        public void ConfirmAndApply()
        {
            if(!CanApply){Status="預覽已失效或尚未確認計畫。";Notify();return;}
            var plan=Plan!;
            int confirmedRevision=revision;
            Submit(c=>{if(confirmedRevision!=revision)throw new InvalidOperationException("確認後設定已修改，請重新預覽。");ResultIds=c.Apply(plan,true);previewRevision=-1;Issues=c.Qa(Package.PackageGuid);Data=c.Load();SheetList=c.SheetStatuses(Package.PackageGuid,Issues);Step=4;Status=$"完成 read-back：{ResultIds.Length} 張；QA {Issues.Count(i=>i.Severity=="ERROR")} 錯誤、{Issues.Count(i=>i.Severity=="WARNING")} 提醒。此狀態不是施工核准。";});
        }
        public void RunQa()=>Submit(c=>{Issues=c.Qa(Package.PackageGuid);Data=c.Load();SheetList=c.SheetStatuses(Package.PackageGuid,Issues);Status=$"QA 共 {Issues.Length} 項；需人工複核。";Step=4;});
        public void Open(long id)=>Submit(c=>c.Open(id));
    }
}
#endif
