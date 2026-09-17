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
        public ObservableCollection<EarthworkProjectProfile> EarthworkProfiles {get;}=new();
        public ObservableCollection<EarthworkRecord> EarthworkRecords {get;}=new();
        private EarthworkProjectData projectData=new(Array.Empty<EarthworkProjectProfile>(),Array.Empty<EarthworkRecord>());
        internal bool EarthworkTestMode {get;set;}
        private bool historicalResult;
        private CalculationStatus calculationState=CalculationStatus.NotCalculated;
        private bool calculationRequested;
        public string CalculationStateText=>EarthworkPresentation.Calculation(CurrentEarthwork?.CalculationStatus??calculationState);
        public string ProfileStaleWarning=>CurrentEarthwork?.CostProfileOutdated==true?$"成本設定已更新，此區仍使用 Version {CurrentEarthwork.ProfileSnapshot.ProfileVersion} 的計算結果。請使用最新設定重新計算。":"";
        public IReadOnlyDictionary<EarthworkScheduleKind,string> ScheduleStates {get;private set;}=new Dictionary<EarthworkScheduleKind,string>();
        public string SummaryScheduleState=>ScheduleStates.TryGetValue(EarthworkScheduleKind.Summary,out var state)?state:"未建立";
        public string DetailScheduleState=>ScheduleStates.TryGetValue(EarthworkScheduleKind.Detail,out var state)?state:"未建立";
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
        public Guid SelectedProfileGuid {get=>profileGuid;set{if(profileGuid==value)return;profileGuid=value;if(!historicalResult)Reestimate();}}
        public string EarthworkMode {get=>earthworkMode;set{earthworkMode=value;InvalidateQuantity();}}
        public string TargetMode {get=>targetMode;set{targetMode=value;InvalidateQuantity();UpdateLevelTarget();}}
        public long EarthworkLevelId {get=>baseLevel;set{baseLevel=value;UpdateLevelTarget();}}
        public string TargetOffsetText {get=>offsetText;set{offsetText=value;UpdateLevelTarget();}}
        public bool CanPreviewEarthwork=>earthworkMode=="RevitCutter"?CanPreviewExcavation:CanCalculate;
        public bool CanSaveZone=>!Busy&&CurrentEarthwork!=null&&!historicalResult&&calculatedRequest!=null;
        public bool CanCreateSchedule=>!Busy&&Context!=null&&EarthworkRecords.Count>0;
        public bool CanConfirmSchedule=>CanCreateSchedule&&SchedulePreview!=null;
        public string EstimateStatus {get;private set;}="請選取 Profile；係數與單價由本專案自行設定。";
        public string FormatEarthworkVolume(double value)=>Context==null?"尚未讀取單位":$"{EarthworkPresentation.Number(value/Context.CubicMetresPerVolumeUnit)} {Context.VolumeUnit}";
        public string FormatEarthworkArea(double? value)=>!value.HasValue?"未提供可靠面積":Context==null?"尚未讀取單位":$"{EarthworkPresentation.Number(value.Value/Context.SquareMetresPerAreaUnit)} {Context.AreaUnit}";
        public string EstimateSummary=>CurrentEarthwork is not {} r?EstimateStatus:$"面積：{FormatEarthworkArea(r.Quantity.Area)}\n挖方（原地量）：{FormatEarthworkVolume(r.Quantity.CutBankVolume)}\n填方（設計量）：{FormatEarthworkVolume(r.Quantity.FillDesignVolume)}\n幾何淨方（挖－填）：{FormatEarthworkVolume(r.Quantity.GeometricNetVolume)}\n\n挖方鬆方量：{FormatEarthworkVolume(r.Logistics.CutLooseVolume)}\n回填需求量：{FormatEarthworkVolume(r.Logistics.FillLooseDemand)}\n可再利用鬆方：{FormatEarthworkVolume(r.Logistics.PotentialReusableLoose)}\n現場再利用：{FormatEarthworkVolume(r.Logistics.ReusedLooseVolume)}\n需外運：{FormatEarthworkVolume(r.Logistics.ExportLooseVolume)}\n需外購：{FormatEarthworkVolume(r.Logistics.ImportLooseVolume)}\n\n有效車斗容量：{FormatEarthworkVolume(r.Logistics.EffectiveTruckCapacity)}\n外運車次：{r.Logistics.ExportTruckTrips:N0}\n外購車次：{r.Logistics.ImportTruckTrips:N0}\n\n預估總價：{EarthworkPresentation.Money(r.Cost.TotalEstimatedCost,r.Cost.Currency)}\n{EarthworkEstimator.Disclaimer}";
        public string EarthworkProjectSummary=>EarthworkRecords.Count==0?"尚無土方明細。":$"{EarthworkRecords.Count} 區；面積 {FormatEarthworkArea(EarthworkRecords.Sum(r=>r.Quantity.Area??0))}{(EarthworkRecords.Any(r=>!r.Quantity.Area.HasValue)?"（僅已知面積；部分區未提供）":"")}\n挖方 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Quantity.CutBankVolume))}；填方 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Quantity.FillDesignVolume))}；幾何淨方 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Quantity.GeometricNetVolume))}\n挖方鬆方量 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Logistics.CutLooseVolume))}；回填需求量 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Logistics.FillLooseDemand))}；現場再利用 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Logistics.ReusedLooseVolume))}\n外運 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Logistics.ExportLooseVolume))}；外購 {FormatEarthworkVolume(EarthworkRecords.Sum(r=>r.Logistics.ImportLooseVolume))}\n外運車次 {EarthworkRecords.Sum(r=>r.Logistics.ExportTruckTrips):N0}；外購車次 {EarthworkRecords.Sum(r=>r.Logistics.ImportTruckTrips):N0}\n"+string.Join("；",EarthworkRecords.GroupBy(r=>r.Cost.Currency).Select(g=>$"{EarthworkPresentation.Money(g.Sum(r=>r.Cost.TotalEstimatedCost),g.Key)}"))+"\n"+EarthworkEstimator.OverlapWarning;
        public string CalculationBasisText=>CurrentEarthwork is not {} r?"尚無有效估算。":$"區 {r.ZoneNumber}／{r.ZoneName}\n地形 Element {r.Request.Zone.ExistingTerrainId}；邊界 {string.Join(", ",r.Request.Zone.BoundaryElementIds)}；Cutter {r.Request.Zone.CutterId}\n方法：{(r.Request.Zone.CalculationMethod==EarthworkMethod.BoundaryTin?"頂面 TIN 裁切積分":"Revit Cutter 開挖體積差")}\n高程來源：{r.Request.Zone.TargetSource}\n計算時間：{r.Request.Zone.LastCalculatedAt:yyyy-MM-dd HH:mm:ss zzz}\n設定檔：{r.ProfileSnapshot.ProfileName} V{r.ProfileSnapshot.ProfileVersion}；體積單價／車斗基準 {(r.Profile.VolumeUnit==EarthworkVolumeUnit.CubicMetres?"m³":"ft³")}\n鬆方係數 {EarthworkPresentation.Number(r.Profile.SwellFactor!.Value)}；回填需求係數 {EarthworkPresentation.Number(r.Profile.FillLooseFactor!.Value)}；再利用率 {EarthworkPresentation.Percent(r.Profile.ReusableRate!.Value)}\n挖土費 {r.Cost.ExcavationCost:N2}；裝載費 {r.Cost.LoadingCost:N2}；外運車資 {r.Cost.HaulCost:N2}；棄土費 {r.Cost.DisposalCost:N2}\n外購材料 {r.Cost.ImportedFillMaterialCost:N2}；回填施工 {r.Cost.BackfillPlacementCost:N2}；夯實 {r.Cost.CompactionCost:N2}；動員費 {r.Cost.MobilizationCost:N2} {r.Cost.Currency}\n動員費按每區計入；外購材料價由 Profile 自行定義，不另推估進場車資。\n{(r.Quantity.Area.HasValue?"":"Cutter 未提供可靠面積及設計填方；只估算已知開挖量。\n")}{EarthworkEstimator.Disclaimer}";
        public string ScheduleAdvancedText=>SchedulePreview==null?"":string.Join("\n",SchedulePreview.Parameters);
        public string SchedulePreviewText=>SchedulePreview==null?"請選擇建立／更新摘要表或完整明細表。":$"{SchedulePreview.ScheduleName}\n新增 {SchedulePreview.CreateZones.Count} 區；更新 {SchedulePreview.UpdateZones.Count} 區\n欄位 {SchedulePreview.Parameters.Count}；加總 {SchedulePreview.TotalFields}\n將寫入工具管理的分析紀錄與專用參數，不修改地形。\n{EarthworkEstimator.OverlapWarning}";
        private void InvalidateEarthworkEstimate(){calculationState=CurrentEarthwork!=null?CalculationStatus.Stale:CalculationStatus.NotCalculated;calculatedRequest=null;calculatedQuantity=null;CurrentEarthwork=null;SchedulePreview=null;EstimateStatus="輸入或模型已變動，請重新預覽土方。";}
        private void ResetEarthworkDocument(){projectData=new(Array.Empty<EarthworkProjectProfile>(),Array.Empty<EarthworkRecord>());historicalResult=false;ScheduleStates=new Dictionary<EarthworkScheduleKind,string>();EarthworkProfiles.Clear();EarthworkRecords.Clear();profileGuid=Guid.Empty;zoneGuid=Guid.NewGuid();zoneNumber="";zoneName="";boundaryIds=Array.Empty<long>();baseLevel=0;offsetText="";ScheduleResult=null;InvalidateEarthworkEstimate();calculationState=CalculationStatus.NotCalculated;}
        private void LoadEarthworkData(EarthworkProjectData data)
        {
            projectData=Core.Site.EarthworkProfiles.Normalize(data);data=projectData;
            if(!EarthworkTestMode&&CurrentEarthwork?.ProfileSnapshot.Values.ProfileKind==EarthworkProfileKind.TestFixture){historicalResult=false;InvalidateEarthworkEstimate();}
            var selectedProfile=profileGuid;
            EarthworkProfiles.Clear();foreach(var p in data.Profiles.Where(p=>Core.Site.EarthworkProfiles.Visible(p,EarthworkTestMode)).OrderBy(p=>p.ProfileName,StringComparer.Ordinal))EarthworkProfiles.Add(p);
            EarthworkRecords.Clear();foreach(var r in data.Records.Where(r=>EarthworkTestMode||r.ProfileSnapshot.Values.ProfileKind==EarthworkProfileKind.Production).OrderBy(r=>r.ZoneNumber,StringComparer.Ordinal).ThenBy(r=>r.ZoneGuid))EarthworkRecords.Add(r);
            profileGuid=EarthworkProfiles.Any(p=>p.ProfileGuid==selectedProfile)?selectedProfile:Guid.Empty;
            if(historicalResult)CurrentEarthwork=EarthworkRecords.SingleOrDefault(r=>r.ZoneGuid==zoneGuid);
            Notify();
        }
        private void UpdateLevelTarget()
        {
            if(targetMode!="LevelOffset"){targetEntered=false;targetText="";InvalidateQuantity();return;}
            var selected=Context?.Levels.SingleOrDefault(l=>l.Id==baseLevel);
            if(selected==null||!double.TryParse(offsetText,NumberStyles.Float,CultureInfo.CurrentCulture,out double offset)||!double.IsFinite(offset)){targetEntered=false;targetText="";InvalidateQuantity();return;}
            TargetElevation=selected.ElevationMetres+offset*DisplayFactor;
        }
        public void NewEarthworkZone(){historicalResult=false;zoneGuid=Guid.NewGuid();zoneNumber="";zoneName="";boundary="";boundaryIds=Array.Empty<long>();cutter=0;targetEntered=false;targetText="";offsetText="";InvalidateQuantity();calculationState=CalculationStatus.NotCalculated;}
        public void PreviewEarthwork(){if(earthworkMode=="RevitCutter")PreviewExcavation();else CalculateBoundary();}
        public void LocateEarthworkZone()=>Submit(document,c=>Status=c.Locate(earthworkMode=="RevitCutter"&&cutter>0?cutter:boundaryIds.FirstOrDefault()>0?boundaryIds[0]:terrain));
        private void CaptureEarthwork(ISiteContext context,EarthworkQuantityResult quantity,EarthworkMethod method)
        {
            historicalResult=false;calculationState=CalculationStatus.Calculated;
            var zone=new EarthworkZone{ZoneGuid=zoneGuid,ZoneNumber=zoneNumber,ZoneName=zoneName,ExistingTerrainId=terrain,CutterId=method==EarthworkMethod.RevitCutter?cutter:null,BoundarySource=method==EarthworkMethod.RevitCutter?"Revit Cutter":boundaryIds.Length>0?"Selected Floor / closed ModelCurve loop":"Explicit verified polygon",BoundaryElementIds=method==EarthworkMethod.RevitCutter?Array.Empty<long>():boundaryIds.ToArray(),Boundary=method==EarthworkMethod.RevitCutter?Array.Empty<SitePoint>():BoundaryPoints.ToArray(),TargetSource=method==EarthworkMethod.RevitCutter?"Cutter geometry":targetMode=="LevelOffset"?"Level.ProjectElevation + Offset":"Direct elevation from internal origin",BaseLevelId=targetMode=="LevelOffset"&&method==EarthworkMethod.BoundaryTin?baseLevel:null,TargetElevation=method==EarthworkMethod.BoundaryTin?target:null,CalculationMethod=method,LastCalculatedAt=DateTimeOffset.UtcNow,Warnings=method==EarthworkMethod.RevitCutter?new[]{"CUTTER_AREA_UNAVAILABLE","CUTTER_FILL_NOT_ANALYZED"}:Array.Empty<string>()};
            calculatedRequest=new(zone,tolerance,Dataset?.SourceSHA256,"Internal axes / metres",context.EarthworkSignature(zone));calculatedQuantity=quantity;Reestimate();
        }
        private void Reestimate()
        {
            if(historicalResult){Notify();return;}
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
        public void SaveEarthworkProfile(EarthworkProjectProfile profile,bool explicitConfirm)
        {
            if(!explicitConfirm){Status="儲存 Project Profile 需要明確確認。";Notify();return;}
            try{if(!EarthworkTestMode&&profile.ProfileKind!=EarthworkProfileKind.Production)throw new ArgumentException("測試設定檔不可用於正式工作流。");profile=Core.Site.EarthworkProfiles.Save(profile,projectData.Profiles.SingleOrDefault(p=>p.ProfileGuid==profile.ProfileGuid));}catch(Exception e){Status=e.Message;Notify();return;}
            var profiles=projectData.Profiles.Where(p=>p.ProfileGuid!=profile.ProfileGuid).Append(profile).ToArray();var rows=projectData.Records.ToArray();var request=calculatedRequest;var quantity=calculatedQuantity;
            Submit(document,c=>{LoadEarthworkData(c.SaveEarthwork(new(profiles,rows),true));profileGuid=profile.ProfileGuid;if(request!=null&&quantity!=null&&request.SourceModelSignature==c.EarthworkSignature(request.Zone)){calculatedRequest=request;calculatedQuantity=quantity;}Reestimate();Status="Project Profile 已儲存並 read-back；既有區紀錄保留原 Profile 快照，需重新套用／儲存才更新。";});
        }
        public void SaveEarthworkZone(bool explicitConfirm)
        {
            if(!explicitConfirm||!CanSaveZone){Status="需要有效數量、Profile 與明確確認才能儲存土方區。";Notify();return;}
            var record=CurrentEarthwork!;var profiles=projectData.Profiles.ToArray();var rows=EarthworkEstimator.Upsert(projectData.Records,record);
            Submit(document,c=>{if(c.EarthworkSignature(record.Request.Zone)!=record.Request.SourceModelSignature)throw new InvalidOperationException("來源已變動，請重新計算。");LoadEarthworkData(c.SaveEarthwork(new(profiles,rows),true));calculatedRequest=record.Request;calculatedQuantity=record.Quantity;Reestimate();Status="土方區已儲存並 read-back；Schedule 須另按預覽／確認更新。";});
        }
        public void SelectEarthworkZone(EarthworkRecord record,bool locate)
        {
            if(Busy)return;var z=record.Request.Zone;zoneGuid=z.ZoneGuid;zoneNumber=z.ZoneNumber;zoneName=z.ZoneName;terrain=z.ExistingTerrainId;cutter=z.CutterId??0;boundaryIds=z.BoundaryElementIds.ToArray();boundary=string.Join(";",z.Boundary.Select(p=>FormattableString.Invariant($"{p.X},{p.Y}")));baseLevel=z.BaseLevelId??0;targetMode="Direct";earthworkMode=z.CalculationMethod.ToString();target=z.TargetElevation??0;targetEntered=z.TargetElevation.HasValue;targetText=targetEntered?(target/DisplayFactor).ToString("G17",CultureInfo.CurrentCulture):"";profileGuid=record.Profile.ProfileGuid;InvalidateQuantity();
            TerrainName=$"來源地形 {terrain}";CutterName=cutter>0?$"來源構件 {cutter}":"尚未選取開挖構件";Status="已載入土方區來源；重新預覽後可更新同一 ZoneGuid。";Notify();if(locate)LocateEarthworkZone();
            historicalResult=true;CurrentEarthwork=record;Notify();
        }
        public void RecalculateLatestProfile()
        {
            var record=EarthworkRecords.SingleOrDefault(r=>r.ZoneGuid==zoneGuid);if(record==null)return;
            var profile=projectData.Profiles.SingleOrDefault(p=>p.ProfileGuid==record.ProfileSnapshot.ProfileGuid);
            if(profile==null||!Core.Site.EarthworkProfiles.Visible(profile,EarthworkTestMode)){Status="原設定檔已封存或無法使用，請選擇可用設定檔後重新預覽。";Notify();return;}
            SelectEarthworkZone(record,false);profileGuid=profile.ProfileGuid;historicalResult=false;PreviewEarthwork();
        }
        public void CloneEarthworkProfile(bool confirmed)
        {
            var source=EarthworkProfiles.SingleOrDefault(p=>p.ProfileGuid==profileGuid);if(source!=null&&confirmed)SaveEarthworkProfile(Core.Site.EarthworkProfiles.Clone(source),true);
        }
        public void ArchiveEarthworkProfile(bool confirmed)
        {
            var source=EarthworkProfiles.SingleOrDefault(p=>p.ProfileGuid==profileGuid);if(source!=null&&confirmed)SaveEarthworkProfile(source with{IsArchived=true},true);
        }
        public void MarkEarthworkReviewed(Guid id,bool confirmed)
        {
            if(!confirmed)return;
            Submit(document,c=>{var data=Core.Site.EarthworkProfiles.Normalize(c.LoadEarthwork());var r=data.Records.Single(r=>r.ZoneGuid==id);
                if(r.CalculationStatus!=CalculationStatus.Calculated||r.CostProfileOutdated||c.EarthworkSignature(r.Request.Zone)!=r.Request.SourceModelSignature)throw new InvalidOperationException("結果已過期，請先重新計算，再標記已複核。");
                LoadEarthworkData(c.SaveEarthwork(new(data.Profiles,EarthworkEstimator.Upsert(data.Records,r with{ReviewStatus=ReviewStatus.Reviewed})),true));Status="已標記 BIM 工具複核；不代表工程數量、合約或測量核准。Schedule 須另行更新。";});
        }
        public void RefreshEarthworkSchedules()=>Submit(document,c=>ScheduleStates=c.EarthworkSchedules());
        public void OpenEarthworkSchedule(EarthworkScheduleKind kind)=>Submit(document,c=>Status=c.OpenEarthworkSchedule(kind));
        public void PreviewEarthworkSchedule()=>PreviewEarthworkSchedule(EarthworkScheduleKind.Detail);
        public void PreviewEarthworkSchedule(EarthworkScheduleKind kind)
        {
            if(!CanCreateSchedule)return;var rows=EarthworkRecords.ToArray();Submit(document,c=>{SchedulePreview=c.PreviewSchedule(rows,kind);Status="請檢查 Schedule、參數與新增／更新區紀錄，確認後才寫入模型。";});
        }
        public void ConfirmEarthworkSchedule(bool explicitConfirm)
        {
            if(!explicitConfirm||!CanConfirmSchedule){Status="需要新的 Schedule preview 與明確確認。";Notify();return;}
            var rows=EarthworkRecords.ToArray();var preview=SchedulePreview!;Submit(document,c=>{ScheduleResult=c.WriteSchedule(rows,preview,true);ScheduleStates=c.EarthworkSchedules();SchedulePreview=null;Status=$"明細表 {ScheduleResult.ScheduleName}：{ScheduleResult.RecordIds.Count} 區／{ScheduleResult.FieldCount} 欄，read-back PASS。";});
        }
        public void DeleteEarthworkZone(Guid id,bool deleteScheduleRecord,bool explicitConfirm)
        {
            if(!explicitConfirm)return;Submit(document,c=>{LoadEarthworkData(c.DeleteEarthwork(id,deleteScheduleRecord,true));NewEarthworkZone();Status="已刪除所選分析紀錄；Terrain 與 Cutter 保留。";});
        }
    }
}
#endif
