#if REVIT2026 || SITE_TESTS
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using RevitMCP.Core.Site;

namespace RevitMCP.UI
{
    public enum SiteStepState { NotStarted, Ready, Warning, Complete, Stale }
    public sealed record SiteExcavationOutcome(long TerrainId,long CutterId,double CutBankVolume,string ProjectVolume,bool Executed)
    {
        public override string ToString()=>$"{(Executed?"開挖 read-back 完成":"開挖試算（模型已回復）")}\n挖方（原地量）：{ProjectVolume}\nTerrain {TerrainId} / Cutter {CutterId}\n方法：Revit 開挖體積差；此結果不包含設計填方或開挖面積。";
    }
    public record SiteEarthworkSummary(string Area,string Cut,string Fill,string Net,string MaximumDepth,string Method,string Source,string Target,string Warnings,EarthworkResult? Quantity=null)
    {
        public override string ToString()=>$"計算面積  {Area}\n挖方（原地量）  {Cut}\n填方（設計量）  {Fill}\n幾何淨方（挖－填）  {Net}\n最大深度  {MaximumDepth}\n方法：{Method}\n來源：{Source}\n設計高程：{Target}\n{Warnings}";
    }
    public sealed class SiteControlRow
    {
        public string Name {get;set;}="控制點";
        public double Easting {get;set;} public double Northing {get;set;} public double Elevation {get;set;}
        public double ModelX {get;set;} public double ModelY {get;set;} public double ModelZ {get;set;}
        public string Residual {get;set;}="尚未計算";
    }
    public sealed class CadLayerChoice : INotifyPropertyChanged
    {
        public CadLayerSummary Summary {get;}
        public CadLayerChoice(CadLayerSummary summary){Summary=summary;}
        private bool selected;
        public bool Selected {get=>selected;set{selected=value;PropertyChanged?.Invoke(this,new(nameof(Selected)));}}
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Name=>Summary.Name;
        public string Elevation=>Summary.Bounds==null?"無可靠點":$"{Summary.Bounds.Min.Z:F3} ～ {Summary.Bounds.Max.Z:F3} m";
    }
    public sealed partial class SiteTerrainViewModel
    {
        private bool targetEntered;
        private string targetText="";
        public string TargetElevationText
        {
            get=>targetText;
            set
            {
                targetText=value;
                targetEntered=Context!=null&&double.TryParse(value,NumberStyles.Float,CultureInfo.CurrentCulture,out var parsed)&&double.IsFinite(parsed);
                if(targetEntered)target=double.Parse(value,NumberStyles.Float,CultureInfo.CurrentCulture)*DisplayFactor;
                InvalidateQuantity();
            }
        }
        public bool CanPreviewExcavation=>!Busy&&Context!=null&&terrain>0&&cutter>0&&terrain!=cutter;
        public string CalculationReadiness=>Busy?"Revit 正在處理。":Context==null?"請先讀取模型與專案單位。":terrain<=0?"請選取地形。":!targetEntered||!double.IsFinite(target)?"請輸入有效設計高程（可明確輸入 0）。":!double.IsFinite(tolerance)||tolerance<=0?"計算容差必須為正數。":!CadTerrainAnalysis.ValidBoundary(BoundaryPoints)?"請選取可靠的閉合、平面、凸邊界。":"輸入完整，可計算。";
        public bool CanCalculate=>CalculationReadiness=="輸入完整，可計算。";
        public string EarthworkResultText=>Result is SiteEarthworkSummary summary?summary.ToString():Result is SiteExcavationOutcome excavation?excavation.ToString():"計算結果將顯示於此。";
        public CadTerrainAnalysis? CadAnalysis {get;private set;}
        public ObservableCollection<CadLayerChoice> CadLayers {get;}=new();
        public ObservableCollection<SiteControlRow> ControlRows {get;}=new();
        public IReadOnlyList<string> Columns {get;private set;}=Array.Empty<string>();
        public IReadOnlyList<string[]> SampleRows {get;private set;}=Array.Empty<string[]>();
        public string TerrainName {get;private set;}="尚未選取地形";
        public string CutterName {get;private set;}="尚未選取開挖構件";
        public bool IsCad=>new[]{".dwg",".dxf"}.Contains(Path.GetExtension(file).ToLowerInvariant());
        private CadPlacement placement=CadPlacement.Origin;
        public CadPlacement Placement {get=>placement;set{placement=value;CadAnalysis=null;Dataset=null;Changed();}}
        public int Step {get;private set;}
        private readonly bool[] visited=new bool[4];
        public void GoToStep(int step){if(step<0||step>3)return;visited[Step]=true;Step=step;Notify();}
        public SiteStepState StepState(int step)=>step switch
        {
            0=>Dataset==null?(visited[0]?SiteStepState.Stale:SiteStepState.NotStarted):Dataset.Diagnostics.WarningCount>0?SiteStepState.Warning:SiteStepState.Complete,
            1=>Context==null?SiteStepState.NotStarted:PreviewPoints.Count>0?SiteStepState.Complete:visited[1]?SiteStepState.Stale:SiteStepState.Ready,
            2=>Result is SiteCreateOutcome?SiteStepState.Complete:PreviewPoints.Count>0?SiteStepState.Ready:visited[2]?SiteStepState.Stale:SiteStepState.NotStarted,
            3=>Result!=null&&Result is not SiteCreateOutcome?SiteStepState.Complete:terrain!=0?SiteStepState.Ready:SiteStepState.NotStarted,
            _=>SiteStepState.NotStarted
        };
        public string DisplayLengthUnit=>Context?.LengthUnit??"尚未讀取專案單位";
        public double DisplayFactor=>Context?.MetresPerDisplayUnit??1;
        public double DisplayTolerance {get=>tolerance/DisplayFactor;set=>ToleranceMetres=value*DisplayFactor;}
        public double DisplayGrid {get=>grid/DisplayFactor;set=>GridMetres=value*DisplayFactor;}
        public double DisplayTarget {get=>target/DisplayFactor;set=>TargetElevation=value*DisplayFactor;}
        public IReadOnlyList<SitePoint> BoundaryPoints
        {
            get {try{return boundary.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Split(',').Select(x=>double.Parse(x,CultureInfo.InvariantCulture)).ToArray()).Select(v=>new SitePoint(v[0],v[1],0)).ToArray();}catch{return Array.Empty<SitePoint>();}}
        }
        public string PreviewSummary=>PreviewPoints.Count==0?"尚未產生有效地形預覽。":$"原始 {Dataset?.Diagnostics.InputCount:N0}／有效 {Dataset?.Diagnostics.ValidCount:N0}／使用 {PreviewPoints.Count:N0}\n"+
            $"高程 {PreviewPoints.Min(p=>p.Z)/DisplayFactor:F3} ～ {PreviewPoints.Max(p=>p.Z)/DisplayFactor:F3} {DisplayLengthUnit}\n"+
            $"X {PreviewPoints.Min(p=>p.X)/DisplayFactor:F3} ～ {PreviewPoints.Max(p=>p.X)/DisplayFactor:F3}；Y {PreviewPoints.Min(p=>p.Y)/DisplayFactor:F3} ～ {PreviewPoints.Max(p=>p.Y)/DisplayFactor:F3} {DisplayLengthUnit}\n"+
            $"離模型原點最遠 {PreviewPoints.Max(p=>p.Distance(new(0,0,0)))/DisplayFactor:F3} {DisplayLengthUnit}\n"+
            $"定位：{(IsCad?"依 CAD 匯入定位":mode=="SharedCoordinates"?"共用座標":mode=="LocalCoordinates"?"模型原點座標":"控制點對位")}；品質：{reduction}\n"+
            $"控制點 RMS／最大誤差 {(Alignment?.RMSResidual??0)/DisplayFactor:F4}／{(Alignment?.MaxResidual??0)/DisplayFactor:F4} {DisplayLengthUnit}\n"+
            $"減點高程保守誤差 {(Simplification?.MaxVerticalError??0)/DisplayFactor:F4} {DisplayLengthUnit}（非最終三角網精度認證）";
        public string SourceSummary=>Dataset==null?Path.GetFileName(file):$"{Dataset.SourceName}｜{(File.Exists(file)?new FileInfo(file).Length:0):N0} bytes｜SHA256 {Dataset.SourceSHA256[..Math.Min(12,Dataset.SourceSHA256.Length)]}\n"+
            $"原始 {Dataset.Diagnostics.InputCount:N0}／有效 {Dataset.Diagnostics.ValidCount:N0}／忽略 {Dataset.Diagnostics.RejectedCount+Dataset.Diagnostics.DuplicateCount:N0}\n"+
            $"高程 {Dataset.Diagnostics.Bounds.Min.Z/DisplayFactor:F3} ～ {Dataset.Diagnostics.Bounds.Max.Z/DisplayFactor:F3} {DisplayLengthUnit}";
        public string ConfirmationSummary=>$"有效 {Dataset?.Diagnostics.ValidCount:N0}／忽略 {(Dataset?.Diagnostics.RejectedCount??0)+(Dataset?.Diagnostics.DuplicateCount??0):N0}／實際使用 {PreviewPoints.Count:N0} 點\n品質：{reduction}\n"+
            string.Join("\n",Dataset?.Diagnostics.Warnings.Take(8).Select(w=>w.Message)??Array.Empty<string>())+"\n將建立地形並保存稽核報告。候選資料不代表測量或法規核准。";
        private void InvalidateWrite(){revision++;confirmed=false;Notify();}
        private void InvalidateQuantity(){revision++;ExcavationPreview=null;confirmed=false;if(Result is not SiteCreateOutcome)Result=null;InvalidateEarthworkEstimate();Notify();}
        public void ReadColumns()
        {
            try
            {
                char separator=delimiter switch{"semicolon"=>';',"tab"=>'\t',"space"=>' ',_=>','};
                var rows=File.ReadLines(file).Where(l=>!string.IsNullOrWhiteSpace(l)).Take(11).Select(l=>TerrainPointParser.Split(l,separator)).ToArray();
                if(rows.Length==0)throw new ArgumentException("來源沒有資料。");
                Columns=rows[0].Select((v,i)=>header?$"{v}（欄 {i+1}）":$"欄 {i+1}").ToArray();SampleRows=rows.Skip(header?1:0).Take(10).ToArray();Notify();
            }catch(Exception e){Columns=Array.Empty<string>();SampleRows=Array.Empty<string[]>();Status=e.Message;Notify();}
        }
        public void SetColumn(int role,int index)
        {
            var values=mapping.Split(',').Select(int.Parse).ToArray();values[role]=index;Mapping=string.Join(",",values);
        }
        public int GetColumn(int role)=>int.Parse(mapping.Split(',')[role]);
        public void AnalyzeCad()=>Submit(document,c=>
        {
            CadAnalysis=null;Dataset=null;Changed();var result=c.AnalyzeCad(new(file,placement,units));CadAnalysis=result;CadLayers.Clear();
            foreach(var layer in result.Layers){var choice=new CadLayerChoice(layer);choice.PropertyChanged+=(_,__)=>{Dataset=null;Changed();};CadLayers.Add(choice);}
            Status=$"CAD 已分析並回復模型；找到 {CadLayers.Count} 圖層。請勾選地形圖層，再套用。";
        });
        public void ApplyCadLayers(string? boundaryName=null)
        {
            try{Changed();boundary="";Dataset=CadAnalysis?.Dataset(CadLayers.Where(l=>l.Selected).Select(l=>l.Name),boundaryName)??throw new InvalidOperationException("請先分析 CAD。");
                if(boundaryName!=null){var selected=CadAnalysis.Boundaries.Single(b=>b.Name==boundaryName);Boundary=string.Join(";",selected.Points.Select(p=>FormattableString.Invariant($"{p.X},{p.Y}")));}
                Status=$"已套用圖層：{Dataset.Diagnostics.ValidCount} 有效點。";
            }catch(Exception e){Dataset=null;Status=e.Message;}Notify();
        }
        public void ApplyControlRows()
        {
            ControlPoints=string.Join(";",ControlRows.Select(p=>FormattableString.Invariant($"{p.Easting*DisplayFactor},{p.Northing*DisplayFactor},{p.Elevation*DisplayFactor},{p.ModelX*DisplayFactor},{p.ModelY*DisplayFactor},{p.ModelZ*DisplayFactor}")));
        }
        public void PickControl(SiteControlRow row)=>Submit(document,c=>{var p=c.PickControlPoint();row.ModelX=p.X/DisplayFactor;row.ModelY=p.Y/DisplayFactor;row.ModelZ=p.Z/DisplayFactor;ApplyControlRows();});
        public void UseBoundarySelection()=>Submit(document,c=>{var loop=c.SelectedBoundary();if(!CadTerrainAnalysis.ValidBoundary(loop))throw new ArgumentException("僅接受可靠的閉合、平面、凸邊界。");Boundary=string.Join(";",loop.Select(p=>FormattableString.Invariant($"{p.X},{p.Y}")));boundaryIds=c.SelectedIds();Status=$"已取得 {loop.Count} 頂點的邊界。";});
        public void LocateTerrain()=>Submit(document,c=>Status=c.Locate(terrain));
        public void ConfirmCreate(){AcknowledgeDiagnostics=true;Confirmed=true;Create();}
    }
}
#endif
