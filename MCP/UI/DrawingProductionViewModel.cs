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
        Dictionary<long,DrawingChoice[]> SourcesByLevel();
        DrawingZone[] Zones();
        DrawingChoice[] Grids();
        DrawingZone GridZone(string name,long[] grids,double paddingMm);
        DrawingChoice[] ViewTemplates();
        SheetTemplateBlueprint ConfigureViewRule(SheetTemplateBlueprint blueprint,long templateId,int? scale);
        DrawingProjectData Load();
        SheetTemplateBlueprint Extract(long sheet);
        ExternalTitleBlockAnalysis AnalyzeExternal(string path,string unit,string rft);
        SheetTemplateBlueprint LoadExternal(ExternalTitleBlockAnalysis analysis,string type,bool useExisting,bool confirmed);
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
        public string DocumentIdentity=>identity;
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
        public Dictionary<long,DrawingChoice[]> LevelSources {get;private set;}=new();
        public DrawingZone[] Zones {get;private set;}=Array.Empty<DrawingZone>();
        public DrawingChoice[] Grids {get;private set;}=Array.Empty<DrawingChoice>();
        public DrawingProjectData Data {get;private set;}=new();
        public DrawingPackageDefinition Package {get;private set;}=new();
        public DrawingPlan? Plan {get;private set;}
        public ExternalTitleBlockAnalysis? ExternalAnalysis {get;private set;}
        public TemplateSourceKind SourceKind {get;private set;}
        public string ExternalPath {get;private set;}="";
        public string CadUnit {get;private set;}="Auto";
        public string RftPath {get;private set;}="";
        public string SelectedExternalType {get;set;}="";
        public void SelectExternalType(string type)
        {if(SelectedExternalType!=type){Package.Profile.Blueprint=new();Invalidate();}SelectedExternalType=type;SizeAndUnitConfirmed=false;if(ExternalAnalysis?.TypeBounds.TryGetValue(type,out var bounds)==true){ExternalAnalysis.Bounds=bounds;ExpectedPaperSize=ExternalAnalysis.SizeSuggestion;}Notify();}
        public string ExpectedPaperSize {get;set;}="A3";
        public string SizeComparison
        {
            get{if(ExternalAnalysis==null)return "";var expected=AutoSheetLayoutService.PaperSize(ExpectedPaperSize);var b=ExternalAnalysis.Bounds;
                return $"預期 {ExpectedPaperSize}：{expected.Width} × {expected.Height} mm；尺度差：寬 {(Math.Max(b.Width,b.Height)*304.8/expected.Width-1)*100:0.##}%／高 {(Math.Min(b.Width,b.Height)*304.8/expected.Height-1)*100:0.##}%（需人工確認，非自動核准）。";}
        }
        private bool sizeAndUnitConfirmed;
        public bool SizeAndUnitConfirmed {get=>sizeAndUnitConfirmed;set{if(sizeAndUnitConfirmed==value)return;sizeAndUnitConfirmed=value;Notify();}}
        public void SelectSource(TemplateSourceKind kind)
        {SourceKind=kind;ExternalAnalysis=null;SizeAndUnitConfirmed=false;Package.Profile=new();RenderEpoch++;Invalidate();}
        public void StartNewPackage()
        {Package=new();Plan=null;Issues=Array.Empty<DrawingQaIssue>();ResultIds=Array.Empty<long>();SheetList=Array.Empty<DrawingSheetStatus>();ExternalAnalysis=null;SourceKind=TemplateSourceKind.CurrentSheet;Step=0;RenderEpoch++;Invalidate();}
        public void SelectExternalFile(string path)
        {ExternalPath=path;ExternalAnalysis=null;Package.Profile.Blueprint=new();SizeAndUnitConfirmed=false;RenderEpoch++;Invalidate();}
        public void SetCadUnit(string unit){CadUnit=unit;ExternalAnalysis=null;Package.Profile.Blueprint=new();SizeAndUnitConfirmed=false;RenderEpoch++;Invalidate();}
        public void SetRft(string path){RftPath=path;ExternalAnalysis=null;Package.Profile.Blueprint=new();SizeAndUnitConfirmed=false;RenderEpoch++;Invalidate();}
        public void AnalyzeExternal()=>Submit(c=>{ExternalAnalysis=c.AnalyzeExternal(ExternalPath,CadUnit,RftPath);ExpectedPaperSize=ExternalAnalysis.SizeSuggestion;SelectedExternalType=ExternalAnalysis.Types.FirstOrDefault()??"";RenderEpoch++;Status="請確認圖框類型、尺寸與單位，再載入圖框。";});
        public void LoadExternal(bool useExisting,bool confirmed)
        {
            if(ExternalAnalysis==null||!confirmed||!SizeAndUnitConfirmed){Status="請先分析並確認圖框尺寸／單位，再明確確認載入。";Notify();return;}
            var analysis=ExternalAnalysis;
            Submit(c=>{Package.Profile.Blueprint=c.LoadExternal(analysis,SelectedExternalType,useExisting,confirmed);Package.Profile.ProfileName=analysis.FamilyName;Package.Profile.ViewStrategy=DrawingViewStrategy.Duplicate;RenderEpoch++;Invalidate();Status="圖框已載入並讀回驗證。此來源只有圖框，工具將使用自動單一主視圖配置。";});
        }
        public DrawingQaIssue[] Issues {get;private set;}=Array.Empty<DrawingQaIssue>();
        public long[] ResultIds {get;private set;}=Array.Empty<long>();
        public DrawingSheetStatus[] SheetList {get;private set;}=Array.Empty<DrawingSheetStatus>();
        public string QaSummary=>$"總圖紙 {SheetList.Length}；可供複核 {SheetList.Count(s=>s.Readiness=="Ready")}／需檢查 {SheetList.Count(s=>s.Readiness=="NeedsReview")}／錯誤 {SheetList.Count(s=>s.Readiness=="Draft")}";
        public bool CanApply=>!Busy&&Step==3&&Plan?.CanApply==true&&previewRevision==revision;
        public string GenerateBlockedReason=>Busy?"Revit 正在處理，請稍候。":Package.Profile.Blueprint.TitleBlockTypeId<=0?"尚未選擇或載入圖框。":Package.Levels.Count==0?"請選擇至少一個樓層。":"";
        public bool CanGenerate=>GenerateBlockedReason=="";
        public string ApplyBlockedReason=>Busy?"Revit 正在處理，請稍候。":Plan==null?"尚未產生圖紙計畫。":previewRevision!=revision?"設定或模型已變更，請重新產生計畫。":Plan.Rows.Count==0?"未產生任何圖紙，請回到出圖範圍。":!Plan.CanApply?string.Join("；",Plan.Errors.Concat(Plan.Rows.SelectMany(r=>r.Issues)).Distinct()):Step!=3?"請先確認圖紙計畫。":"";
        public string Summary=>Plan?.Summary??"尚未產生計畫。";
        public string ConfirmationSummary=>Summary+(Plan==null?"":$"\n新增圖紙：{Plan.Rows.Count(r=>r.Change==DrawingChange.Add)}；主視埠：{Plan.Rows.Count(r=>r.Change==DrawingChange.Add)*Plan.Package.Profile.Blueprint.Viewports.Count}；新建視圖：{(Plan.Package.Profile.ViewStrategy==DrawingViewStrategy.Existing?0:Plan.Rows.Count(r=>r.Change==DrawingChange.Add)*Plan.Package.Profile.Blueprint.Viewports.Count)}");
        private void Notify()=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(""));
        public void Invalidate(){revision++;previewRevision=-1;Plan=null;Notify();}
        public void DocumentChanged(string current,bool modelChanged=false)
        {
            if(current!=identity){identity=current;ExternalAnalysis=null;ExternalPath="";SourceKind=TemplateSourceKind.CurrentSheet;LevelSources=new();Package=new();Sheets=Array.Empty<DrawingChoice>();Levels=Array.Empty<DrawingChoice>();Zones=Array.Empty<DrawingZone>();Data=new();SheetList=Array.Empty<DrawingSheetStatus>();Grids=Array.Empty<DrawingChoice>();ResultIds=Array.Empty<long>();Issues=Array.Empty<DrawingQaIssue>();Step=0;Invalidate();}
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
        public void Refresh()=>Submit(c=>{identity=c.Identity;Sheets=c.Sheets();Levels=c.Levels();LevelSources=c.SourcesByLevel();Zones=c.Zones();Grids=c.Grids();Data=c.Load();ResolveSources();Invalidate();Status=$"找到 {Sheets.Length} 張圖紙、{Levels.Length} 個樓層；未選分區時使用不分區。";});
        private void ResolveSources()
        {
            LevelViewResolver.Resolve(Package.Levels,LevelSources,Package.SourceViewsByLevel);
        }
        public void SelectLevel(DrawingChoice level,bool selected)
        {Package.Levels.RemoveAll(l=>l.Id==level.Id);if(selected)Package.Levels.Add(level);ResolveSources();Invalidate();}
        public void AddGridZone(string name,long[] grids,double paddingMm)=>Submit(c=>{var zone=c.GridZone(name,grids,paddingMm);Package.Zones.RemoveAll(z=>z.ZoneId==zone.ZoneId);Package.Zones.Add(zone);Zones=Zones.Where(z=>z.ZoneId!=zone.ZoneId).Append(zone).ToArray();Invalidate();Status="已加入網格分區："+name;});
        public void Extract(long sheet)=>Submit(c=>{var blueprint=c.Extract(sheet);Package.Profile.Blueprint=blueprint;RenderEpoch++;Invalidate();Status=$"已擷取 {blueprint.SourceSheetNumber}：主視埠 {blueprint.Viewports.Count}／圖例 {blueprint.Legends.Count}／明細表 {blueprint.Schedules.Count}。";});
        public void SaveProfile()=>Submit(c=>{Package.Profile=c.SaveProfile(Package.Profile);Data=c.Load();Invalidate();Status="樣板已保存；既有圖紙不會自動套用。";});
        public void UseProfile(DrawingTemplateProfile profile){Package.Profile=profile;SourceKind=profile.Blueprint.SourceKind;ExternalAnalysis=null;RenderEpoch++;Invalidate();}
        public void UsePackage(DrawingPackageDefinition package){Package=package;SheetList=Array.Empty<DrawingSheetStatus>();ResultIds=Array.Empty<long>();Issues=Array.Empty<DrawingQaIssue>();RenderEpoch++;Invalidate();Status="已載入出圖包；請重新預覽模型差異。";Notify();}
        public void ReadSources(long level,Action<DrawingChoice[]> result)=>Submit(c=>result(c.Sources(level)));
        public void ReadViewTemplates(Action<DrawingChoice[]> result)=>Submit(c=>result(c.ViewTemplates()));
        public void ConfigureViewRule(long templateId,int? scale)=>Submit(c=>{Package.Profile.Blueprint=c.ConfigureViewRule(Package.Profile.Blueprint,templateId,scale);RenderEpoch++;Invalidate();Status="已更新本次視圖規則；請重新預覽。";});
        public void SelectScope(IEnumerable<DrawingChoice> levels,IEnumerable<DrawingZone> zones)
        {Package.Levels=levels.ToList();Package.Zones=zones.ToList();ResolveSources();Invalidate();}
        public void GeneratePlan()
        {
            if(!CanGenerate){Status=GenerateBlockedReason;Notify();return;}
            Submit(c=>{Plan=c.Preview(Package);previewRevision=revision;Step=Plan.Rows.Count>0?2:1;Status=Plan.CanApply?"計畫已產生，請檢查圖號與版面。":(Plan.Rows.Count==0?"未產生任何圖紙：":"")+string.Join("；",Plan.Errors.Concat(Plan.Rows.SelectMany(r=>r.Issues)));});
        }
        public void GoToStep(int step)
        {
            if(step<0||step>4)return;
            if(Busy){Status="Revit 正在處理，請稍候。";Notify();return;}
            if(step==2&&(Plan==null||Plan.Rows.Count==0)){Status="尚未產生圖紙，請先完成出圖範圍。";Notify();return;}
            if(step==3&&(Plan?.CanApply!=true||previewRevision!=revision)){Status=ApplyBlockedReason;Notify();return;}
            if(step==4&&!Data.Records.Any(r=>r.DrawingPackageGuid==Package.PackageGuid)){Status="此出圖包尚未建立；請先產生計畫並確認建立。";Notify();return;}
            if(step==4){RunQa();return;}
            Step=step;Notify();
        }
        public void ConfirmAndApply()
        {
            if(!CanApply){Status=ApplyBlockedReason;Notify();return;}
            var plan=Plan!;
            int confirmedRevision=revision;
            Submit(c=>{if(confirmedRevision!=revision)throw new InvalidOperationException("確認後設定已修改，請重新預覽。");ResultIds=c.Apply(plan,true);previewRevision=-1;Issues=c.Qa(Package.PackageGuid);Data=c.Load();SheetList=c.SheetStatuses(Package.PackageGuid,Issues);Step=4;Status=$"完成 read-back：{ResultIds.Length} 張；QA {Issues.Count(i=>i.Severity=="ERROR")} 錯誤、{Issues.Count(i=>i.Severity=="WARNING")} 提醒。此狀態不是施工核准。";});
        }
        public void RunQa()=>Submit(c=>{Issues=c.Qa(Package.PackageGuid);Data=c.Load();SheetList=c.SheetStatuses(Package.PackageGuid,Issues);Status=$"QA 共 {Issues.Length} 項；需人工複核。";Step=4;});
        public void Open(long id)=>Submit(c=>c.Open(id));
    }
}
#endif
