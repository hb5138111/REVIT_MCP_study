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
    public record SiteChoice(long Id,string Name,double ElevationMetres=0);
    public record SiteContextSnapshot(string DocumentIdentity,SiteTransform SharedToInternal,string CoordinateEvidence,IReadOnlyList<SiteChoice> Types,IReadOnlyList<SiteChoice> Levels,double MetresPerDisplayUnit=1,string LengthUnit="m",double SquareMetresPerAreaUnit=1,string AreaUnit="m²",double CubicMetresPerVolumeUnit=1,string VolumeUnit="m³");
    public record SiteCreateRequest(IReadOnlyList<SitePoint> Points,long TypeId,long LevelId,double Tolerance,bool LargeOverride,object Audit);
    public record SiteCreateOutcome(long ElementId,string Summary);
    public interface ISiteContext
    {
        CadTerrainAnalysis AnalyzeCad(CadTerrainRequest request)=>throw new NotSupportedException("CAD runtime 未提供。");
        SitePoint PickControlPoint()=>throw new NotSupportedException("請在 Revit 選點。");
        IReadOnlyList<SitePoint> SelectedBoundary()=>throw new NotSupportedException("請在 Revit 選取邊界。");
        IReadOnlyList<SitePoint> BoundaryFromIds(IReadOnlyList<long> ids)=>throw new NotSupportedException("邊界重讀未提供。");
        string Locate(long id)=>throw new NotSupportedException("3D 導航未提供。");
        string FormatVolume(double cubicMetres)=>$"{cubicMetres:F4} m³";
        long[] SelectedIds()=>Array.Empty<long>();
        string EarthworkSignature(EarthworkZone zone)=>"fixture";
        EarthworkProjectData LoadEarthwork()=>new(Array.Empty<EarthworkProjectProfile>(),Array.Empty<EarthworkRecord>());
        EarthworkProjectData SaveEarthwork(EarthworkProjectData data,bool confirmed)=>throw new NotSupportedException("專案分析資料 runtime 未提供。");
        EarthworkSchedulePreview PreviewSchedule(IReadOnlyList<EarthworkRecord> rows)=>throw new NotSupportedException("Schedule runtime 未提供。");
        EarthworkSchedulePreview PreviewSchedule(IReadOnlyList<EarthworkRecord> rows,EarthworkScheduleKind kind)=>PreviewSchedule(rows);
        IReadOnlyDictionary<EarthworkScheduleKind,string> EarthworkSchedules()=>new Dictionary<EarthworkScheduleKind,string>();
        string OpenEarthworkSchedule(EarthworkScheduleKind kind)=>throw new NotSupportedException("Schedule runtime 未提供。");
        EarthworkScheduleResult WriteSchedule(IReadOnlyList<EarthworkRecord> rows,EarthworkSchedulePreview preview,bool confirmed)=>throw new NotSupportedException("Schedule runtime 未提供。");
        EarthworkProjectData DeleteEarthwork(Guid zoneGuid,bool deleteScheduleRecord,bool confirmed)=>throw new NotSupportedException("刪除分析紀錄 runtime 未提供。");
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
    public sealed partial class SiteTerrainViewModel : INotifyPropertyChanged
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
        private string file="",mode="SharedCoordinates",units="m",delimiter="comma",controls="",boundary="",mapping="0,1,2,-1,-1",reduction="平衡";
        private double grid=1,tolerance=.01,target;
        private bool header=true,acknowledge,largeOverride,confirmed;
        private long type,level,terrain,cutter;
        public string FilePath {get=>file;set{file=value;Dataset=null;CadAnalysis=null;CadLayers.Clear();boundary="";Changed();}}
        public string Units {get=>units;set{units=value;Dataset=null;CadAnalysis=null;Changed();}}
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
        public long TypeId {get=>type;set{type=value;InvalidateWrite();}}
        public long LevelId {get=>level;set{level=value;InvalidateWrite();}}
        public long TerrainId {get=>terrain;set{terrain=value;InvalidateQuantity();}}
        public long CutterId {get=>cutter;set{cutter=value;InvalidateQuantity();}}
        public string Boundary {get=>boundary;set{boundary=value;InvalidateQuantity();}}
        public double TargetElevation {get=>target;set{target=value;targetEntered=true;targetText=(value/DisplayFactor).ToString("G17",CultureInfo.CurrentCulture);InvalidateQuantity();}}
        public bool Confirmed {get=>confirmed;set{confirmed=value && !Busy && (PreviewPoints.Count>0||ExcavationPreview.HasValue);Notify();}}
        public bool CanCreate=>!Busy && Context!=null && Dataset!=null && PreviewPoints.Count>=3 && Dataset.Diagnostics.ConflictCount==0 &&
            (Dataset.Diagnostics.WarningCount==0||acknowledge) && (Alignment==null||Alignment.MaxResidual<=tolerance) &&
            (PreviewPoints.Count<=20000||largeOverride) && Context.Types.Any(t=>t.Id==type)&&Context.Levels.Any(l=>l.Id==level);
        public bool CanExecuteCreate=>CanCreate&&Confirmed;
        public bool CanConfirmCreate=>!Busy&&Context!=null&&Dataset!=null&&PreviewPoints.Count>=3&&Dataset.Diagnostics.ConflictCount==0&&(Alignment==null||Alignment.MaxResidual<=tolerance)&&Context.Types.Any(t=>t.Id==type)&&Context.Levels.Any(l=>l.Id==level);
        public bool CanExcavate=>!Busy&&Confirmed&&ExcavationPreview.HasValue;
        public void DocumentChanged(string identity,bool modified=false)
        {
            if(identity!=document){document=identity;Context=null;Dataset=null;CadAnalysis=null;CadLayers.Clear();terrain=0;cutter=0;boundary="";targetEntered=false;targetText="";ResetEarthworkDocument();Result=null;Changed();Status="模型已切換，請重新分析座標與匯入。";}
            else if(modified){Changed();Status="模型已修改，請重新 Preview。";}
            Notify();
        }
        private void Changed(){revision++;PreviewPoints=Array.Empty<SitePoint>();ExcavationPreview=null;confirmed=false;Transform=null;Alignment=null;Simplification=null;Result=null;InvalidateEarthworkEstimate();foreach(var row in ControlRows)row.Residual="尚未計算";Notify();}
        private void Notify()=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(""));
        public async Task ImportAsync()
        {
            if(IsCad){AnalyzeCad();return;}
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
        public void RefreshContext()=>Submit("",c=>
        {
            var previous=Context;Context=c.Snapshot();document=Context.DocumentIdentity;
            if(previous!=null&&previous.MetresPerDisplayUnit!=Context.MetresPerDisplayUnit){targetEntered=false;targetText="";offsetText="";}
            if(!Context.Types.Any(t=>t.Id==type))type=Context.Types.FirstOrDefault()?.Id??0;
            if(!Context.Levels.Any(l=>l.Id==level))level=Context.Levels.FirstOrDefault()?.Id??0;
            if(previous?.DocumentIdentity!=Context.DocumentIdentity||previous.SharedToInternal!=Context.SharedToInternal||previous.MetresPerDisplayUnit!=Context.MetresPerDisplayUnit)Changed();else InvalidateWrite();
            Detail=Context.CoordinateEvidence;Status="座標與類型／樓層已重新讀取；建築保持不動。";
            LoadEarthworkData(c.LoadEarthwork());ScheduleStates=c.EarthworkSchedules();
        });
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
                    SiteTransform transform=dataset.CoordinateBasis==TerrainCoordinateBasis.PositionedModelMetres?new(0,0,0,0):selectedMode switch
                    {"SharedCoordinates"=>context.SharedToInternal,"LocalCoordinates"=>new(0,0,0,0),"ControlPointAlignment"=>(alignment=ControlPointAlignment.Solve(ParseControls(controlText))).Transform,_=>throw new ArgumentException("座標模式無效。")};
                    double size=selectedReduction switch{"原始" or "精細"=>0,"平衡"=>1,"高效能"=>5,"自訂"=>selectedGrid,_=>throw new ArgumentException("減點模式無效。")};
                    var simplified=TerrainSimplifier.Reduce(dataset.Points,size,selectedTolerance);
                    return (transform,alignment,simplified,points:simplified.Points.Select(p=>transform.Apply(p.Point)).ToArray());
                });
                if(token!=revision)return;Transform=output.transform;Alignment=output.alignment;Simplification=output.simplified;PreviewPoints=output.points;
                var bounds=TerrainBounds.Of(PreviewPoints);double distance=PreviewPoints.Max(p=>p.Distance(new(0,0,0)));
                Detail=$"{mode}；{units} → internal m；Scale=1\nXY {bounds.Min.X:F3},{bounds.Min.Y:F3} → {bounds.Max.X:F3},{bounds.Max.Y:F3}\nZ {bounds.Min.Z:F3} → {bounds.Max.Z:F3}；離原點最遠 {distance:F3} m\nΔ {Transform.DeltaX:F6},{Transform.DeltaY:F6},{Transform.DeltaZ:F6} m；θ {Transform.Rotation:F9} rad\nResidual RMS/Max {Alignment?.RMSResidual ?? 0:F6}/{Alignment?.MaxResidual ?? 0:F6} m\n點數 {Simplification.OriginalPointCount} → {Simplification.FinalPointCount}；減少 {Simplification.ReductionPercent:F1}%\n減點高程 envelope Mean/Max {Simplification.MeanVerticalError:F6}/{Simplification.MaxVerticalError:F6} m（非最終 TIN 插值認證）\nBoundary：points-only convex hull；Code 點保留，不宣稱 breakline connectivity。";
                Status=CanPreviewCreate()?"預覽完成；檢查摘要後按建立，於確認視窗核准本次操作。":"預覽完成，但資料檢查／控制點誤差／點數或類型與樓層阻擋建立。";
            }
            catch(Exception e){Status=e.Message;}
            finally{Busy=false;Notify();}
        }
        private bool CanPreviewCreate()=>Dataset?.Diagnostics.ConflictCount==0&&(Alignment==null||Alignment.MaxResidual<=tolerance);
        public void Create()
        {
            if(!CanExecuteCreate){Status="建立被阻擋：需要有效 Preview、QA 與明確確認。";Notify();return;}
            var request=new SiteCreateRequest(PreviewPoints.ToArray(),type,level,tolerance,largeOverride,Audit());confirmed=false;
            Submit(document,c=>{var result=c.Create(request,true);Result=result;terrain=result.ElementId;TerrainName=$"本次建立：{Context?.Types.FirstOrDefault(t=>t.Id==type)?.Name} / {Context?.Levels.FirstOrDefault(l=>l.Id==level)?.Name}";PreviewPoints=Array.Empty<SitePoint>();Detail=result.Summary;Status="地形建立及 read-back 完成；JSON／CSV／Markdown 已保存。";});
        }
        public void PreviewExcavation()
        {
            if(!CanPreviewExcavation){Status="請先讀取模型單位並選取地形與開挖構件。";Notify();return;}Changed();calculationRequested=true;Submit(document,c=>{ExcavationPreview=c.Excavate(terrain,cutter,false,false,null,Audit());Result=new SiteExcavationOutcome(terrain,cutter,ExcavationPreview.Value,c.FormatVolume(ExcavationPreview.Value),false);CaptureEarthwork(c,new(null,ExcavationPreview.Value,0,null),EarthworkMethod.RevitCutter);Status="開挖試算完成，模型已回復；尚未執行開挖。";});
        }
        public void UseSelection(bool isTerrain)=>Submit(document,c=>{if(Context==null){Context=c.Snapshot();document=Context.DocumentIdentity;}var selected=c.SelectedElement(isTerrain);if(isTerrain){terrain=selected.Id;TerrainName=selected.Name;}else{cutter=selected.Id;CutterName=selected.Name;}InvalidateQuantity();Status=isTerrain?"已取得選取的地形。":"已取得選取的開挖構件。";});
        public void ExecuteExcavation()
        {
            if(!CanExcavate){Status="開挖被阻擋：需要新的試算與明確確認。";Notify();return;}
            double expected=ExcavationPreview!.Value;confirmed=false;var audit=Audit();
            Submit(document,c=>{var volume=c.Excavate(terrain,cutter,true,true,expected,audit);ExcavationPreview=null;Result=new SiteExcavationOutcome(terrain,cutter,volume,c.FormatVolume(volume),true);Status=$"開挖 read-back 完成：{c.FormatVolume(volume)}；報告已保存。";});
        }
        public void CalculateBoundary()
        {
            if(!CanCalculate){Status=CalculationReadiness;Notify();return;}
            var polygon=BoundaryPoints.ToArray();double elevation=target;var ids=boundaryIds.ToArray();long selectedLevel=baseLevel;bool useLevel=targetMode=="LevelOffset";string offset=offsetText;InvalidateQuantity();calculationRequested=true;
            Submit(document,c=>
            {
                var current=c.Snapshot();if(Context==null||current.MetresPerDisplayUnit!=Context.MetresPerDisplayUnit)throw new InvalidOperationException("專案單位已改變；請重新讀取單位並輸入高程。");
                if(ids.Length>0)polygon=c.BoundaryFromIds(ids).ToArray();
                if(useLevel)elevation=(current.Levels.SingleOrDefault(l=>l.Id==selectedLevel)??throw new InvalidOperationException("參考 Level 已刪除。")).ElevationMetres+double.Parse(offset,NumberStyles.Float,CultureInfo.CurrentCulture)*current.MetresPerDisplayUnit;
                boundary=string.Join(";",polygon.Select(p=>FormattableString.Invariant($"{p.X},{p.Y}")));target=elevation;targetText=(target/DisplayFactor).ToString("G17",CultureInfo.CurrentCulture);
                Result=c.Calculate(terrain,polygon,elevation,tolerance,Audit());if(Result is SiteEarthworkSummary summary&&summary.Quantity!=null)CaptureEarthwork(c,EarthworkQuantityResult.FromTin(summary.Quantity),EarthworkMethod.BoundaryTin);Status="TIN 裁切積分完成；Project Units 結果與報告已保存。";
            });
        }
        public object Audit()=>new{Timestamp=DateTimeOffset.UtcNow,Dataset?.SourceKind,Dataset?.SourceName,Dataset?.SourceSHA256,Dataset?.Provenance,Units=units,Diagnostics=Dataset?.Diagnostics,CoordinateMode=mode,Transform,ControlPoints=controls,Alignment,Simplification,TypeId=type,LevelId=level,TerrainId=terrain,CutterId=cutter,Boundary=boundary,TargetElevation=target,ToleranceMetres=tolerance,AcknowledgeDiagnostics=acknowledge,LargePointOverride=largeOverride};
        public static IReadOnlyList<SiteControl> ParseControls(string text)=>text.Split(new[]{'\n',';'},StringSplitOptions.RemoveEmptyEntries).Select(line=>{var v=line.Split(',').Select(x=>double.Parse(x,CultureInfo.InvariantCulture)).ToArray();if(v.Length!=6)throw new ArgumentException("控制點每列 surveyX,Y,Z,internalX,Y,Z，全部 m。");return new SiteControl(new(v[0],v[1],v[2]),new(v[3],v[4],v[5]));}).ToArray();
        private void Submit(string expected,Action<ISiteContext> work)
        {
            if(Busy)return;Busy=true;Notify();int token=revision;
            if(!host.Submit(expected,c=>{try{if(token!=revision)throw new InvalidOperationException("排隊期間設定已變更；請重新 Preview 與確認。");work(c);}catch(Exception e){Status=e.Message;if(calculationRequested)calculationState=CalculationStatus.Failed;}finally{calculationRequested=false;Busy=false;Notify();}},error=>{Status=error;Busy=false;Notify();}))
            {Busy=false;Status="Revit 忙碌，請稍後重試。";Notify();}
        }
    }
}
#endif
