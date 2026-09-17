#if REVIT2026 || SITE_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitMCP.Core.Site
{
    public enum EarthworkMethod { BoundaryTin, RevitCutter }
    public enum EarthworkVolumeUnit { CubicMetres, CubicFeet }
    public static class EarthworkUnits
    {
        public static double ToCubicMetres(double value,EarthworkVolumeUnit unit)=>unit switch
        {EarthworkVolumeUnit.CubicMetres=>value,EarthworkVolumeUnit.CubicFeet=>value*Math.Pow(.3048,3),_=>throw new ArgumentOutOfRangeException(nameof(unit))};
        public static double FromCubicMetres(double value,EarthworkVolumeUnit unit)=>value/ToCubicMetres(1,unit);
    }
    public sealed record EarthworkZone
    {
        public Guid ZoneGuid {get;init;}=Guid.NewGuid();
        public string ZoneNumber {get;init;}="";
        public string ZoneName {get;init;}="";
        public long ExistingTerrainId {get;init;}
        public long? CutterId {get;init;}
        public string BoundarySource {get;init;}="";
        public long[] BoundaryElementIds {get;init;}=Array.Empty<long>();
        public SitePoint[] Boundary {get;init;}=Array.Empty<SitePoint>();
        public string TargetSource {get;init;}="";
        public long? BaseLevelId {get;init;}
        public double? TargetElevation {get;init;}
        public EarthworkMethod CalculationMethod {get;init;}
        public string Status {get;init;}="候選／需人工複核";
        public string[] Warnings {get;init;}=Array.Empty<string>();
        public DateTimeOffset LastCalculatedAt {get;init;}
    }
    public sealed record EarthworkCalculationRequest(EarthworkZone Zone,double ToleranceMetres,string? SourceDatasetHash,string CoordinateMode,string SourceModelSignature="");
    /// <summary>All geometry values use SI. Cutter area/depth remain null when not reliably available.</summary>
    public sealed record EarthworkQuantityResult(double? Area,double CutBankVolume,double FillDesignVolume,double? MaximumDepth)
    {
        public double GeometricNetVolume=>CutBankVolume-FillDesignVolume;
        public static EarthworkQuantityResult FromTin(EarthworkResult result)=>new(result.Area,result.CutVolume,result.FillVolume,result.MaxDepth);
    }
    public sealed record EarthworkMaterialProfile(double SwellFactor,double FillLooseFactor,double ReusableRate);
    /// <summary>Nullable inputs are intentional: no market price or soil/truck factor defaults.</summary>
    public sealed record EarthworkProjectProfile
    {
        public int ProfileVersion {get;init;}=1;
        public EarthworkProfileKind ProfileKind {get;init;}=EarthworkProfileKind.Production;
        public bool IsArchived {get;init;}
        public DateTimeOffset CreatedAt {get;init;}
        public DateTimeOffset UpdatedAt {get;init;}
        public Guid ProfileGuid {get;init;}=Guid.NewGuid();
        public string ProfileName {get;init;}="";
        public string Currency {get;init;}="";
        public EarthworkVolumeUnit VolumeUnit {get;init;}=EarthworkVolumeUnit.CubicMetres;
        public double? SwellFactor {get;init;}
        public double? FillLooseFactor {get;init;}
        public double? ReusableRate {get;init;}
        public string TruckName {get;init;}="";
        public double? TruckCapacity {get;init;}
        public double? TruckLoadUtilization {get;init;}
        public decimal? ExcavationUnitCost {get;init;}
        public decimal? LoadingUnitCost {get;init;}
        public decimal? HaulCostPerTrip {get;init;}
        public decimal? DisposalCostPerVolume {get;init;}
        public decimal? ImportedFillCostPerVolume {get;init;}
        public decimal? BackfillPlacementCostPerVolume {get;init;}
        public decimal? CompactionCostPerVolume {get;init;}
        public decimal? MobilizationCost {get;init;}
        public EarthworkMaterialProfile Material=>new(Required(SwellFactor,"鬆方係數"),Required(FillLooseFactor,"回填需求係數"),Required(ReusableRate,"再利用率"));
        private static double Required(double? value,string name)=>value??throw new ArgumentException($"請設定{name}。");
        public void Validate()
        {
            if(ProfileGuid==Guid.Empty||string.IsNullOrWhiteSpace(ProfileName)||string.IsNullOrWhiteSpace(Currency)||string.IsNullOrWhiteSpace(TruckName))throw new ArgumentException("請輸入 Profile 名稱、幣別與車型。");
            var m=Material;
            foreach(var value in new[]{m.SwellFactor,m.FillLooseFactor,Required(TruckCapacity,"車斗容量"),Required(TruckLoadUtilization,"裝載率")})
                if(!double.IsFinite(value)||value<=0)throw new ArgumentException("係數、車斗容量與裝載率必須為有限正數。");
            if(!double.IsFinite(m.ReusableRate)||m.ReusableRate<0||m.ReusableRate>1||TruckLoadUtilization>1)throw new ArgumentException("再利用率須為 0～100%，裝載率須為大於 0 且不超過 100%。");
            foreach(var price in new[]{ExcavationUnitCost,LoadingUnitCost,HaulCostPerTrip,DisposalCostPerVolume,ImportedFillCostPerVolume,BackfillPlacementCostPerVolume,CompactionCostPerVolume})
                if(!price.HasValue||price<0)throw new ArgumentException("所有單價皆須由使用者明確輸入非負值，可明確輸入 0。");
            if(MobilizationCost<0)throw new ArgumentException("動員費不可為負數。");
            _=EarthworkUnits.ToCubicMetres(1,VolumeUnit);
        }
    }
    public sealed record EarthworkLogisticsResult(double CutLooseVolume,double FillLooseDemand,double PotentialReusableLoose,double ReusedLooseVolume,double ExportLooseVolume,double ImportLooseVolume,double EffectiveTruckCapacity,long ExportTruckTrips,long ImportTruckTrips);
    public sealed record EarthworkCostResult(decimal ExcavationCost,decimal LoadingCost,decimal HaulCost,decimal DisposalCost,decimal ImportedFillMaterialCost,decimal BackfillPlacementCost,decimal CompactionCost,decimal MobilizationCost,string Currency)
    {
        public decimal TotalEstimatedCost=>checked(ExcavationCost+LoadingCost+HaulCost+DisposalCost+ImportedFillMaterialCost+BackfillPlacementCost+CompactionCost+MobilizationCost);
    }
    public sealed record EarthworkRecord(EarthworkCalculationRequest Request,EarthworkQuantityResult Quantity,EarthworkProjectProfile Profile,EarthworkLogisticsResult Logistics,EarthworkCostResult Cost,string ToolVersion="0.5.2.1")
    {
        public EarthworkProfileSnapshot ProfileSnapshot {get;init;}=EarthworkProfileSnapshot.Create(Profile);
        public CalculationStatus CalculationStatus {get;init;}=CalculationStatus.Calculated;
        public ReviewStatus ReviewStatus {get;init;}=ReviewStatus.PendingReview;
        public bool CostProfileOutdated {get;init;}
        public Guid ZoneGuid=>Request.Zone.ZoneGuid;
        public string ZoneNumber=>Request.Zone.ZoneNumber;
        public string ZoneName=>Request.Zone.ZoneName;
        public string CalculationLabel=>EarthworkPresentation.Calculation(CalculationStatus);
        public string ReviewLabel=>Request.Zone.Warnings.Length>0?"有警告":EarthworkPresentation.Review(ReviewStatus);
        public string Status=>CalculationLabel+"／"+ReviewLabel;
    }
    public sealed record EarthworkProjectData(IReadOnlyList<EarthworkProjectProfile> Profiles,IReadOnlyList<EarthworkRecord> Records);
    public sealed record EarthworkSchedulePreview(string ScheduleName,IReadOnlyList<string> Parameters,IReadOnlyList<Guid> CreateZones,IReadOnlyList<Guid> UpdateZones,string Fingerprint,EarthworkScheduleKind Kind=EarthworkScheduleKind.Detail,int TotalFields=0);
    public sealed record EarthworkScheduleResult(long ScheduleId,string ScheduleName,IReadOnlyDictionary<Guid,long> RecordIds,int FieldCount,string ReadBack);
    public static class EarthworkEstimator
    {
        public const string Disclaimer="成本為依本專案設定之估算值，不是市場報價或合約金額。";
        public const string OverlapWarning="各區範圍可能重疊，總量僅為明細加總。";
        public static EarthworkRecord Calculate(EarthworkCalculationRequest request,EarthworkQuantityResult q,EarthworkProjectProfile p)
        {
            p.Validate();
            if(request.Zone.ZoneGuid==Guid.Empty||string.IsNullOrWhiteSpace(request.Zone.ZoneName)||string.IsNullOrWhiteSpace(request.Zone.ZoneNumber)||request.Zone.ExistingTerrainId<=0)throw new ArgumentException("土方區編號、名稱、GUID 與來源地形必須有效。");
            foreach(double v in new[]{q.CutBankVolume,q.FillDesignVolume})if(!double.IsFinite(v)||v<0)throw new ArgumentException("挖填體積必須為有限非負值。");
            if(q.Area.HasValue&&(!double.IsFinite(q.Area.Value)||q.Area<0))throw new ArgumentException("面積無效。");
            var material=p.Material;
            double cut=q.CutBankVolume*material.SwellFactor,fill=q.FillDesignVolume*material.FillLooseFactor,potential=cut*material.ReusableRate,reused=Math.Min(potential,fill);
            double export=Math.Max(cut-reused,0),import=Math.Max(fill-reused,0),capacity=EarthworkUnits.ToCubicMetres(p.TruckCapacity!.Value,p.VolumeUnit)*p.TruckLoadUtilization!.Value;
            if(new[]{cut,fill,potential,reused,export,import,capacity}.Any(v=>!double.IsFinite(v))||capacity<=0)throw new ArgumentException("數量或有效車斗容量超出可計算範圍。");
            long Trips(double v)=>v==0?0:checked((long)Math.Ceiling(v/capacity));
            var logistics=new EarthworkLogisticsResult(cut,fill,potential,reused,export,import,capacity,Trips(export),Trips(import));
            decimal Volume(double value)=>checked((decimal)EarthworkUnits.FromCubicMetres(value,p.VolumeUnit));
            var cost=new EarthworkCostResult(checked(Volume(q.CutBankVolume)*p.ExcavationUnitCost!.Value),checked(Volume(export)*p.LoadingUnitCost!.Value),checked(logistics.ExportTruckTrips*p.HaulCostPerTrip!.Value),checked(Volume(export)*p.DisposalCostPerVolume!.Value),checked(Volume(import)*p.ImportedFillCostPerVolume!.Value),checked(Volume(q.FillDesignVolume)*p.BackfillPlacementCostPerVolume!.Value),checked(Volume(q.FillDesignVolume)*p.CompactionCostPerVolume!.Value),p.MobilizationCost??0,p.Currency.Trim());
            _=cost.TotalEstimatedCost;
            return new(request,q,p,logistics,cost);
        }
        public static IReadOnlyList<EarthworkRecord> Upsert(IEnumerable<EarthworkRecord> existing,EarthworkRecord record)
        {
            var rows=existing.ToList();if(rows.GroupBy(r=>r.ZoneGuid).Any(g=>g.Count()>1))throw new InvalidOperationException("土方區 GUID 重複，請先檢查紀錄。");
            int i=rows.FindIndex(r=>r.ZoneGuid==record.ZoneGuid);if(i<0)rows.Add(record);else rows[i]=record;
            return rows.OrderBy(r=>r.ZoneNumber,StringComparer.Ordinal).ThenBy(r=>r.ZoneGuid).ToArray();
        }
    }
}
#endif
