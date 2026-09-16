#if REVIT2026 || SITE_TESTS
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using RevitMCP.Core.Site;

namespace RevitMCP.UI
{
    public record SiteChoice(long Id,string Name);
    public record SiteContextSnapshot(string DocumentIdentity,SiteTransform SharedToInternal,string CoordinateEvidence,IReadOnlyList<SiteChoice> Types,IReadOnlyList<SiteChoice> Levels);
    public record SiteCreateRequest(IReadOnlyList<SitePoint> Points,long TypeId,long LevelId,double Tolerance,bool LargeOverride,object Audit);
    public record SiteCreateOutcome(long ElementId,string Summary);
    public interface ISiteContext
    {
        SiteContextSnapshot Snapshot();
        SiteChoice SelectedElement(bool terrain);
        SiteCreateOutcome Create(SiteCreateRequest request,bool confirmed);
        double Excavate(long terrain,long cutter,bool execute,bool confirmed,double? expected,object audit);
        object Calculate(long terrain,IReadOnlyList<SitePoint> boundary,double elevation,double tolerance,object audit);
    }
    public interface ISiteHost
    {
        bool Submit(string expectedDocument,Action<ISiteContext> work,Action<string> failed);
    }
    /// <summary>Production step workflow; pure state with explicit invalidation and confirmation.</summary>
    public sealed class SiteTerrainViewModel : INotifyPropertyChanged
    {
        private readonly ISiteHost host;
        private int revision;
        private string document="";
        public SiteTerrainViewModel(ISiteHost host){this.host=host;}
        public event PropertyChangedEventHandler? PropertyChanged;
        public SiteContextSnapshot? Context {get;private set;}
        public TerrainPointDataset? Dataset {get;private set;}
        public SimplificationResult? Simplification {get;private set;}
        public AlignmentResult? Alignment {get;private set;}
        public IReadOnlyList<SitePoint> PreviewPoints {get;private set;}=Array.Empty<SitePoint>();
        public SiteTransform? Transform {get;private set;}
        public object? Result {get;private set;}
        public bool Busy {get;private set;}
        public string Status {get;private set;}="1. 匯入測量點；尚未建立模型元素。";
        public string Detail {get;private set;}="";
        public double? ExcavationPreview {get;private set;}
        private string file="",mode="SharedCoordinates",units="m",delimiter="comma",controls="",boundary="0,0;5,0;5,5;0,5",mapping="0,1,2,-1,-1",reduction="原始";
        private double grid=1,tolerance=.01,target;
        private bool header=true,acknowledge,largeOverride,confirmed;
        private long type,level,terrain,cutter;
        public string FilePath {get=>file;set{file=value;Dataset=null;Changed();}}
        public string Units {get=>units;set{units=value;Dataset=null;Changed();}}
        public string Delimiter {get=>delimiter;set{delimiter=value;Dataset=null;Changed();}}
        public bool HasHeader {get=>header;set{header=value;Dataset=null;Changed();}}
        public string Mapping {get=>mapping;set{mapping=value;Dataset=null;Changed();}}
        public string CoordinateMode {get=>mode;set{mode=value;Changed();}}
        public string ControlPoints {get=>controls;set{controls=value;Changed();}}
        public string ReductionMode {get=>reduction;set{reduction=value;Changed();}}
        public double GridMetres {get=>grid;set{grid=value;Changed();}}
        public double ToleranceMetres {get=>tolerance;set{tolerance=value;Changed();}}
        public bool AcknowledgeDiagnostics {get=>acknowledge;set{acknowledge=value;confirmed=false;Notify();}}
        public bool LargePointOverride {get=>largeOverride;set{largeOverride=value;confirmed=false;Notify();}}
        public long TypeId {get=>type;set{type=value;Changed();}}
        public long LevelId {get=>level;set{level=value;Changed();}}
        public long TerrainId {get=>terrain;set{terrain=value;Changed();}}
        public long CutterId {get=>cutter;set{cutter=value;Changed();}}
        public string Boundary {get=>boundary;set{boundary=value;Changed();}}
        public double TargetElevation {get=>target;set{target=value;Changed();}}
        public bool Confirmed {get=>confirmed;set{confirmed=value && !Busy && (PreviewPoints.Count>0||ExcavationPreview.HasValue);Notify();}}
        public bool CanCreate=>!Busy && Context!=null && Dataset!=null && PreviewPoints.Count>=3 && Dataset.Diagnostics.ConflictCount==0 &&
            (Dataset.Diagnostics.WarningCount==0||acknowledge) && (Alignment==null||Alignment.MaxResidual<=tolerance) &&
            (PreviewPoints.Count<=20000||largeOverride) && Context.Types.Any(t=>t.Id==type)&&Context.Levels.Any(l=>l.Id==level);
        public bool CanExecuteCreate=>CanCreate&&Confirmed;
        public bool CanExcavate=>!Busy&&Confirmed&&ExcavationPreview.HasValue;
        public void DocumentChanged(string identity,bool modified=false)
        {
            if(identity!=document){document=identity;Context=null;Dataset=null;terrain=0;cutter=0;Result=null;Changed();Status="模型已切換，請重新分析座標與匯入。";}
            else if(modified){Changed();Status="模型已修改，請重新 Preview。";}
            Notify();
        }
        private void Changed(){revision++;PreviewPoints=Array.Empty<SitePoint>();ExcavationPreview=null;confirmed=false;Transform=null;Alignment=null;Simplification=null;Notify();}
        private void Notify()=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(""));
        public async Task ImportAsync()
        {
            if(Busy)return;Changed();Dataset=null;Busy=true;Status="解析／QA 中…";Notify();int token=revision;
            try
            {
                var map=mapping.Split(',').Select(v=>int.Parse(v,CultureInfo.InvariantCulture)).ToArray();
                if(map.Length!=5)throw new ArgumentException("欄位索引格式 X,Y,Z,ID,Code（0 起算，選填 -1）。");
                char separator=delimiter switch{"comma"=>',',"semicolon"=>';',"tab"=>'\t',"space"=>' ',_=>throw new ArgumentException("分隔符無效。")};
                var options=new PointImportOptions(separator,header,map[0],map[1],map[2],map[3],map[4],units);string path=file;
                var data=await Task.Run(()=>TerrainPointParser.Read(path,options));
                if(token!=revision)return;Dataset=data;Status=$"輸入 {data.Diagnostics.InputCount}／有效 {data.Diagnostics.ValidCount}／拒絕 {data.Diagnostics.RejectedCount}／重複 {data.Diagnostics.DuplicateCount}／提醒 {data.Diagnostics.WarningCount}";
                Detail=$"診斷顯示 {Math.Min(500,data.Diagnostics.WarningCount)} / {data.Diagnostics.WarningCount}；完整診斷保留於稽核報告。\n"+string.Join(Environment.NewLine,data.Diagnostics.Warnings.Take(500).Select(w=>$"列 {w.Line}: {w.Code} — {w.Message}"));
            }
            catch(Exception e){Status=e.Message;}
            finally{Busy=false;Notify();}
        }
        public void RefreshContext()=>Submit("",c=>{Context=c.Snapshot();document=Context.DocumentIdentity;type=Context.Types.FirstOrDefault()?.Id??0;level=Context.Levels.FirstOrDefault()?.Id??0;Changed();Detail=Context.CoordinateEvidence;Status="座標來源已重新讀取；建築保持不動。";});
        public async Task PreviewAsync()
        {
            if(Busy)return;
            if(Context==null||Dataset==null){Status="先匯入資料並檢查建築／基地座標。";Notify();return;}
            Changed();Busy=true;Notify();int token=revision;
            try
            {
                if(!double.IsFinite(tolerance)||tolerance<=0)throw new ArgumentException("請輸入正數 tool tolerance（m，非測量規範）。");
                var context=Context;var dataset=Dataset;var selectedMode=mode;var controlText=controls;var selectedReduction=reduction;double selectedGrid=grid,selectedTolerance=tolerance;
                var output=await Task.Run(()=>
                {
                    AlignmentResult? alignment=null;
                    SiteTransform transform=selectedMode switch
                    {"SharedCoordinates"=>context.SharedToInternal,"LocalCoordinates"=>new(0,0,0,0),"ControlPointAlignment"=>(alignment=ControlPointAlignment.Solve(ParseControls(controlText))).Transform,_=>throw new ArgumentException("座標模式無效。")};
                    double size=selectedReduction switch{"原始"=>0,"平衡"=>1,"高效能"=>5,"自訂"=>selectedGrid,_=>throw new ArgumentException("減點模式無效。")};
                    var simplified=TerrainSimplifier.Reduce(dataset.Points,size,selectedTolerance);
                    return (transform,alignment,simplified,points:simplified.Points.Select(p=>transform.Apply(p.Point)).ToArray());
                });
                if(token!=revision)return;Transform=output.transform;Alignment=output.alignment;Simplification=output.simplified;PreviewPoints=output.points;
                var bounds=TerrainBounds.Of(PreviewPoints);double distance=PreviewPoints.Max(p=>p.Distance(new(0,0,0)));
                Detail=$"{mode}；{units} → internal m；Scale=1\nXY {bounds.Min.X:F3},{bounds.Min.Y:F3} → {bounds.Max.X:F3},{bounds.Max.Y:F3}\nZ {bounds.Min.Z:F3} → {bounds.Max.Z:F3}；離原點最遠 {distance:F3} m\nΔ {Transform.DeltaX:F6},{Transform.DeltaY:F6},{Transform.DeltaZ:F6} m；θ {Transform.Rotation:F9} rad\nResidual RMS/Max {Alignment?.RMSResidual ?? 0:F6}/{Alignment?.MaxResidual ?? 0:F6} m\n點數 {Simplification.OriginalPointCount} → {Simplification.FinalPointCount}；減少 {Simplification.ReductionPercent:F1}%\n減點高程 envelope Mean/Max {Simplification.MeanVerticalError:F6}/{Simplification.MaxVerticalError:F6} m（非最終 TIN 插值認證）\nBoundary：points-only convex hull；Code 點保留，不宣稱 breakline connectivity。";
                Status=CanPreviewCreate()?"Preview 完成；確認診斷與 Type/Level 後，勾選確認才能建立。":"Preview 完成，但 QA／residual／點數或 Type/Level 阻擋建立。";
            }
            catch(Exception e){Status=e.Message;}
            finally{Busy=false;Notify();}
        }
        private bool CanPreviewCreate()=>Dataset?.Diagnostics.ConflictCount==0&&(Alignment==null||Alignment.MaxResidual<=tolerance);
        public void Create()
        {
            if(!CanExecuteCreate){Status="建立被阻擋：需要有效 Preview、QA 與明確確認。";Notify();return;}
            var request=new SiteCreateRequest(PreviewPoints.ToArray(),type,level,tolerance,largeOverride,Audit());confirmed=false;
            Submit(document,c=>{var result=c.Create(request,true);Result=result;terrain=result.ElementId;PreviewPoints=Array.Empty<SitePoint>();Detail=result.Summary;Status="地形建立及 read-back 完成；JSON／CSV／Markdown 已保存。";});
        }
        public void PreviewExcavation()
        {
            if(Busy)return;Changed();Submit(document,c=>{ExcavationPreview=c.Excavate(terrain,cutter,false,false,null,Audit());Status=$"Revit rollback 試算開挖 {ExcavationPreview:F6} m³；請確認後執行。";});
        }
        public void UseSelection(bool isTerrain)=>Submit(document,c=>{var selected=c.SelectedElement(isTerrain);if(isTerrain)terrain=selected.Id;else cutter=selected.Id;Changed();Detail=selected.Name;Status=isTerrain?"已取得選取的 Toposolid。":"已取得選取的 Cutter。";});
        public void ExecuteExcavation()
        {
            if(!CanExcavate){Status="開挖被阻擋：需要新的試算與明確確認。";Notify();return;}
            double expected=ExcavationPreview!.Value;confirmed=false;var audit=Audit();
            Submit(document,c=>{var volume=c.Excavate(terrain,cutter,true,true,expected,audit);ExcavationPreview=null;Result=volume;Status=$"開挖 read-back 完成：{volume:F6} m³；報告已保存。";});
        }
        public void CalculateBoundary()
        {
            if(Busy)return;Submit(document,c=>{var polygon=boundary.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(line=>{var v=line.Split(',').Select(x=>double.Parse(x,CultureInfo.InvariantCulture)).ToArray();if(v.Length!=2)throw new ArgumentException("Boundary 格式 x,y;x,y（internal m）。");return new SitePoint(v[0],v[1],0);}).ToArray();Result=c.Calculate(terrain,polygon,target,tolerance,Audit());Status="TIN 裁切積分完成；Project Units 結果與報告已保存。";});
        }
        public object Audit()=>new{Timestamp=DateTimeOffset.UtcNow,Dataset?.SourceName,Dataset?.SourceSHA256,Units=units,Diagnostics=Dataset?.Diagnostics,CoordinateMode=mode,Transform,ControlPoints=controls,Alignment,Simplification,TypeId=type,LevelId=level,TerrainId=terrain,CutterId=cutter,Boundary=boundary,TargetElevation=target,ToleranceMetres=tolerance,AcknowledgeDiagnostics=acknowledge,LargePointOverride=largeOverride};
        public static IReadOnlyList<SiteControl> ParseControls(string text)=>text.Split(new[]{'\n',';'},StringSplitOptions.RemoveEmptyEntries).Select(line=>{var v=line.Split(',').Select(x=>double.Parse(x,CultureInfo.InvariantCulture)).ToArray();if(v.Length!=6)throw new ArgumentException("控制點每列 surveyX,Y,Z,internalX,Y,Z，全部 m。");return new SiteControl(new(v[0],v[1],v[2]),new(v[3],v[4],v[5]));}).ToArray();
        private void Submit(string expected,Action<ISiteContext> work)
        {
            if(Busy)return;Busy=true;Notify();int token=revision;
            if(!host.Submit(expected,c=>{try{if(token!=revision)throw new InvalidOperationException("排隊期間設定已變更；請重新 Preview 與確認。");work(c);}catch(Exception e){Status=e.Message;}finally{Busy=false;Notify();}},error=>{Status=error;Busy=false;Notify();}))
            {Busy=false;Status="Revit 忙碌，請稍後重試。";Notify();}
        }
    }
}
#endif
