#if REVIT2026 || SITE_TESTS
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using RevitMCP.Core.Site;

namespace RevitMCP.UI
{
    public sealed partial class SiteTerrainViewModel
    {
        public ObservableCollection<EarthworkProjectSettings> EarthworkProfiles {get;}=new();
        public ObservableCollection<EarthworkRecord> EarthworkRecords {get;}=new();
        private Guid zoneGuid=Guid.NewGuid(),profileGuid;
        private string zoneNumber="",zoneName="",earthworkMode="BoundaryTin",targetMode="Direct",offsetText="";
        private long[] boundaryIds=Array.Empty<long>();
        private long baseLevel;
        private EarthworkCalculationRequest? calculatedRequest;
        private EarthworkQuantityResult? calculatedQuantity;
        public EarthworkRecord? CurrentEarthwork {get;private set;}
        public EarthworkSchedulePreview? SchedulePreview {get;private set;}
        public EarthworkScheduleResult? ScheduleResult {get;private set;}
        public string ZoneNumber {get=>zoneNumber;set{zoneNumber=value;Reestimate();}}
        public string ZoneName {get=>zoneName;set{zoneName=value;Reestimate();}}
        public Guid SelectedProfileGuid {get=>profileGuid;set{profileGuid=value;Reestimate();}}
        public string EarthworkMode {get=>earthworkMode;set{earthworkMode=value;InvalidateQuantity();}}
        public string TargetMode {get=>targetMode;set{targetMode=value;InvalidateQuantity();UpdateLevelTarget();}}
        public long EarthworkLevelId {get=>baseLevel;set{baseLevel=value;UpdateLevelTarget();}}
        public string TargetOffsetText {get=>offsetText;set{offsetText=value;UpdateLevelTarget();}}
        public bool CanPreviewEarthwork=>earthworkMode=="RevitCutter"?CanPreviewExcavation:CanCalculate;
        public bool CanSaveZone=>!Busy&&CurrentEarthwork!=null;
        public bool CanCreateSchedule=>!Busy&&Context!=null&&EarthworkRecords.Count>0;
        public bool CanConfirmSchedule=>CanCreateSchedule&&SchedulePreview!=null;
        public string EstimateStatus {get;private set;}="請選取 Profile；係數與單價由本專案自行設定。";
        public string FormatEarthworkVolume(double value)=>Context==null?"尚未讀取單位":$"{value/Context.CubicMetresPerVolumeUnit:N2} {Context.VolumeUnit}";
        public string FormatEarthworkArea(double? value)=>!value.HasValue?"未提供可靠面積":Context==null?"尚未讀取單位":$"{value/Context.SquareMetresPerAreaUnit:N2} {Context.AreaUnit}";
        public string EstimateSummary=>CurrentEarthwork is not {} r?EstimateStatus:$"面積：{FormatEarthworkArea(r.Quantity.Area)}\n挖方（原地量）：{FormatEarthworkVolume(r.Quantity.CutBankVolume)}\n填方（設計量）：{FormatEarthworkVolume(r.Quantity.FillDesignVolume)}\n幾何淨方（挖－填）：{FormatEarthworkVolume(r.Quantity.GeometricNetVolume)}\n\n挖方鬆方量：{FormatEarthworkVolume(r.Logistics.CutLooseVolume)}\n回填需求量：{FormatEarthworkVolume(r.Logistics.FillLooseDemand)}\n可再利用鬆方：{FormatEarthworkVolume(r.Logistics.PotentialReusableLoose)}\n現場再利用：{FormatEarthworkVolume(r.Logistics.ReusedLooseVolume)}\n需外運：{FormatEarthworkVolume(r.Logistics.ExportLooseVolume)}\n需外購：{FormatEarthworkVolume(r.Logistics.ImportLooseVolume)}\n\n有效車斗容量：{FormatEarthworkVolume(r.Logistics.EffectiveTruckCapacity)}\n外運車次：{r.Logistics.ExportTruckTrips:N0}\n外購車次：{r.Logistics.ImportTruckTrips:N0}\n\n預估總價：{r.Cost.TotalEstimatedCost:N2} {r.Cost.Currency}\n{EarthworkEstimator.Disclaimer}";
        public string EarthworkProjectSummary=>EarthworkRecords.Count==0?"尚無土方明細。":$"{EarthworkRecords.Count} 區；面積 {FormatEarthworkArea(EarthworkRecords.Sum(r=>r.Quantity.Area??0))}{(EarthworkRecords.Any(r=>!r.Quantity.Area.HasValue)?"（僅已知面積；部分區未提供）":"")}\n挖方 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Quantity.CutBankVolume))}；填方 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Quantity.FillDesignVolume))}；幾何淨方 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Quantity.GeometricNetVolume))}\n挖方鬆方量 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Logistics.CutLooseVolume))}；回填需求量 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Logistics.FillLooseDemand))}；現場再利用 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Logistics.ReusedLooseVolume))}\n外運 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Logistics.ExportLooseVolume))}；外購 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Logistics.ImportLooseVolume))}\n外運車次 {EarthworkRecords.Sum(r=>r.Logistics.ExportTruckTrips):N0}；外購車次 {EarthworkRecords.Sum(r=>r.Logistics.ImportTruckTrips):N0}\n"+string.Join("；",EarthworkRecords.GroupBy(r=>r.Cost.Currency).Select(g=>$"{g.Sum(r=>r.Cost.TotalEstimatedCost):N2} {g.Key}"))+"\n"+EarthworkEstimator.OverlapWarning;
        public string CalculationBasisText=>CurrentEarthwork is not {} r?"尚無有效估算。":$"區 {r.ZoneNumber}／{r.ZoneName}\n地形 Element {r.Request.Zone.ExistingTerrainId}；邊界 {string.Join(", ",r.Request.Zone.BoundaryElementIds)}；Cutter {r.Request.Zone.CutterId}\n方法：{(r.Request.Zone.CalculationMethod==EarthworkMethod.BoundaryTin?"頂面 TIN 裁切積分":"Revit Cutter 開挖體積差")}\n高程來源：{r.Request.Zone.TargetSource}\n計算時間：{r.Request.Zone.LastCalculatedAt:yyyy-MM-dd HH:mm:ss zzz}\nProfile：{r.Profile.ProfileName}；體積單價／車斗基準 {(r.Profile.VolumeUnit==EarthworkVolumeUnit.CubicMetres?"m³":"ft³")}\n鬆方係數 {r.Profile.SwellFactor}；回填需求係數 {r.Profile.FillLooseFactor}；再利用率 {r.Profile.ReusableRate:P0}\n挖土費 {r.Cost.ExcavationCost:N2}；裝載費 {r.Cost.LoadingCost:N2}；外運車資 {r.Cost.HaulCost:N2}；棄土費 {r.Cost.DisposalCost:N2}\n外購材料 {r.Cost.ImportedFillMaterialCost:N2}；回填施工 {r.Cost.BackfillPlacementCost:N2}；夯實 {r.Cost.CompactionCost:N2}；動員費 {r.Cost.MobilizationCost:N2} {r.Cost.Currency}\n動員費按每區計入；外購材料價由 Profile 自行定義，不另推估進場車資。\n{(r.Quantity.Area.HasValue?"":"Cutter 未提供可靠面積及設計填方；只估算已知開挖量。\n")}{EarthworkEstimator.Disclaimer}";
        public string SchedulePreviewText=>SchedulePreview==null?"請先預覽將建立／更新的 Schedule、Parameters 與 Records。":$"明細表：{SchedulePreview.ScheduleName}\n新增 {SchedulePreview.CreateZones.Count} 區；更新 {SchedulePreview.UpdateZones.Count} 區\n新增區 GUID：{string.Join(", ",SchedulePreview.CreateZones)}\n更新區 GUID：{string.Join(", ",SchedulePreview.UpdateZones)}\n將建立／重用 Generic Models 的專用 shared parameters：\n"+string.Join("\n",SchedulePreview.Parameters)+"\n只寫入工具管理的分析紀錄，不修改 Terrain 或 Cutter。\n"+EarthworkEstimator.OverlapWarning;
        private void InvalidateEarthworkEstimate(){calculatedRequest=null;calculatedQuantity=null;CurrentEarthwork=null;SchedulePreview=null;EstimateStatus="輸入或模型已變動，請重新預覽土方。";}
        private void ResetEarthworkDocument(){EarthworkProfiles.Clear();EarthworkRecords.Clear();profileGuid=Guid.Empty;zoneGuid=Guid.NewGuid();zoneNumber="";zoneName="";boundaryIds=Array.Empty<long>();baseLevel=0;offsetText="";ScheduleResult=null;InvalidateEarthworkEstimate();}
        private void LoadEarthworkData(EarthworkProjectData data)
        {
            var selectedProfile=profileGuid;
            EarthworkProfiles.Clear();foreach(var p in data.Profiles.OrderBy(p=>p.ProfileName,StringComparer.Ordinal))EarthworkProfiles.Add(p);
            EarthworkRecords.Clear();foreach(var r in data.Records.OrderBy(r=>r.ZoneNumber,StringComparer.Ordinal).ThenBy(r=>r.ZoneGuid))EarthworkRecords.Add(r);
            profileGuid=EarthworkProfiles.Any(p=>p.ProfileGuid==selectedProfile)?selectedProfile:Guid.Empty;
            Notify();
        }
        private void UpdateLevelTarget()
        {
            if(targetMode!="LevelOffset"){targetEntered=false;targetText="";InvalidateQuantity();return;}
            var selected=Context?.Levels.SingleOrDefault(l=>l.Id==baseLevel);
            if(selected==null||!double.TryParse(offsetText,NumberStyles.Float,CultureInfo.CurrentCulture,out double offset)||!double.IsFinite(offset)){targetEntered=false;targetText="";InvalidateQuantity();return;}
            TargetElevation=selected.ElevationMetres+offset*DisplayFactor;
        }
        public void NewEarthworkZone(){zoneGuid=Guid.NewGuid();zoneNumber="";zoneName="";boundary="";boundaryIds=Array.Empty<long>();cutter=0;targetEntered=false;targetText="";offsetText="";InvalidateQuantity();}
        public void PreviewEarthwork(){if(earthworkMode=="RevitCutter")PreviewExcavation();else CalculateBoundary();}
        public void LocateEarthworkZone()=>Submit(document,c=>Status=c.Locate(earthworkMode=="RevitCutter"&&cutter>0?cutter:boundaryIds.FirstOrDefault()>0?boundaryIds[0]:terrain));
        private void CaptureEarthwork(ISiteContext context,EarthworkQuantityResult quantity,EarthworkMethod method)
        {
            var zone=new EarthworkZone{ZoneGuid=zoneGuid,ZoneNumber=zoneNumber,ZoneName=zoneName,ExistingTerrainId=terrain,CutterId=method==EarthworkMethod.RevitCutter?cutter:null,BoundarySource=method==EarthworkMethod.RevitCutter?"Revit Cutter":boundaryIds.Length>0?"Selected Floor / closed ModelCurve loop":"Explicit verified polygon",BoundaryElementIds=method==EarthworkMethod.RevitCutter?Array.Empty<long>():boundaryIds.ToArray(),Boundary=method==EarthworkMethod.RevitCutter?Array.Empty<SitePoint>():BoundaryPoints.ToArray(),TargetSource=method==EarthworkMethod.RevitCutter?"Cutter geometry":targetMode=="LevelOffset"?"Level.ProjectElevation + Offset":"Direct elevation from internal origin",BaseLevelId=targetMode=="LevelOffset"&&method==EarthworkMethod.BoundaryTin?baseLevel:null,TargetElevation=method==EarthworkMethod.BoundaryTin?target:null,CalculationMethod=method,LastCalculatedAt=DateTimeOffset.UtcNow,Warnings=method==EarthworkMethod.RevitCutter?new[]{"CUTTER_AREA_UNAVAILABLE","CUTTER_FILL_NOT_ANALYZED"}:Array.Empty<string>()};
            calculatedRequest=new(zone,tolerance,Dataset?.SourceSHA256,"Internal axes / metres",context.EarthworkSignature(zone));calculatedQuantity=quantity;Reestimate();
        }
        private void Reestimate()
        {
            CurrentEarthwork=null;SchedulePreview=null;
            try
            {
                if(calculatedRequest==null||calculatedQuantity==null){EstimateStatus="請先預覽土方。";return;}
                var profile=EarthworkProfiles.SingleOrDefault(p=>p.ProfileGuid==profileGuid)??throw new ArgumentException("請明確選取 Project Profile；未套用預設係數或單價。");
                calculatedRequest=calculatedRequest with{Zone=calculatedRequest.Zone with{ZoneName=zoneName,ZoneNumber=zoneNumber}};
                CurrentEarthwork=EarthworkEstimator.Calculate(calculatedRequest,calculatedQuantity,profile);EstimateStatus="估算完成；儲存前請檢查來源、數量與 Profile。";
            }
            catch(Exception e){EstimateStatus=e.Message;}
            finally{Notify();}
        }
        public void SaveEarthworkProfile(EarthworkProjectSettings profile,bool explicitConfirm)
        {
            if(!explicitConfirm){Status="儲存 Project Profile 需要明確確認。";Notify();return;}
            try{profile.Validate();}catch(Exception e){Status=e.Message;Notify();return;}
            var profiles=EarthworkProfiles.Where(p=>p.ProfileGuid!=profile.ProfileGuid).Append(profile).ToArray();var rows=EarthworkRecords.ToArray();var request=calculatedRequest;var quantity=calculatedQuantity;
            Submit(document,c=>{LoadEarthworkData(c.SaveEarthwork(new(profiles,rows),true));profileGuid=profile.ProfileGuid;if(request!=null&&quantity!=null&&request.SourceModelSignature==c.EarthworkSignature(request.Zone)){calculatedRequest=request;calculatedQuantity=quantity;}Reestimate();Status="Project Profile 已儲存並 read-back；既有區紀錄保留原 Profile 快照，需重新套用／儲存才更新。";});
        }
        public void SaveEarthworkZone(bool explicitConfirm)
        {
            if(!explicitConfirm||!CanSaveZone){Status="需要有效數量、Profile 與明確確認才能儲存土方區。";Notify();return;}
            var record=CurrentEarthwork!;var profiles=EarthworkProfiles.ToArray();var rows=EarthworkEstimator.Upsert(EarthworkRecords,record);
            Submit(document,c=>{if(c.EarthworkSignature(record.Request.Zone)!=record.Request.SourceModelSignature)throw new InvalidOperationException("來源已變動，請重新計算。");LoadEarthworkData(c.SaveEarthwork(new(profiles,rows),true));calculatedRequest=record.Request;calculatedQuantity=record.Quantity;Reestimate();Status="土方區已儲存並 read-back；Schedule 須另按預覽／確認更新。";});
        }
        public void SelectEarthworkZone(EarthworkRecord record,bool locate)
        {
            if(Busy)return;var z=record.Request.Zone;zoneGuid=z.ZoneGuid;zoneNumber=z.ZoneNumber;zoneName=z.ZoneName;terrain=z.ExistingTerrainId;cutter=z.CutterId??0;boundaryIds=z.BoundaryElementIds.ToArray();boundary=string.Join(";",z.Boundary.Select(p=>FormattableString.Invariant($"{p.X},{p.Y}")));baseLevel=z.BaseLevelId??0;targetMode="Direct";earthworkMode=z.CalculationMethod.ToString();target=z.TargetElevation??0;targetEntered=z.TargetElevation.HasValue;targetText=targetEntered?(target/DisplayFactor).ToString("G17",CultureInfo.CurrentCulture):"";profileGuid=record.Profile.ProfileGuid;InvalidateQuantity();
            TerrainName=$"來源地形 {terrain}";CutterName=cutter>0?$"來源構件 {cutter}":"尚未選取開挖構件";Status="已載入土方區來源；重新預覽後可更新同一 ZoneGuid。";Notify();if(locate)LocateEarthworkZone();
        }
        public void PreviewEarthworkSchedule()
        {
            if(!CanCreateSchedule)return;var rows=EarthworkRecords.ToArray();Submit(document,c=>{SchedulePreview=c.PreviewSchedule(rows);Status="請檢查 Schedule、參數與新增／更新區紀錄，確認後才寫入模型。";});
        }
        public void ConfirmEarthworkSchedule(bool explicitConfirm)
        {
            if(!explicitConfirm||!CanConfirmSchedule){Status="需要新的 Schedule preview 與明確確認。";Notify();return;}
            var rows=EarthworkRecords.ToArray();var preview=SchedulePreview!;Submit(document,c=>{ScheduleResult=c.WriteSchedule(rows,preview,true);SchedulePreview=null;Status=$"明細表 {ScheduleResult.ScheduleName}：{ScheduleResult.RecordIds.Count} 區／{ScheduleResult.FieldCount} 欄，read-back PASS。";});
        }
        public void DeleteEarthworkZone(Guid id,bool deleteScheduleRecord,bool explicitConfirm)
        {
            if(!explicitConfirm)return;Submit(document,c=>{LoadEarthworkData(c.DeleteEarthwork(id,deleteScheduleRecord,true));NewEarthworkZone();Status="已刪除所選分析紀錄；Terrain 與 Cutter 保留。";});
        }
    }
}
#endif
