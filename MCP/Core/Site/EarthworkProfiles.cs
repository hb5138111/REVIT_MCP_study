#if REVIT2026 || SITE_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RevitMCP.Core.Site
{
    public enum EarthworkProfileKind { Production, TestFixture }
    public enum CalculationStatus { NotCalculated, Calculated, Stale, Failed }
    public enum ReviewStatus { PendingReview, Reviewed, Warning }
    public enum EarthworkScheduleKind { Summary, Detail }
    public sealed record EarthworkProfileSnapshot(EarthworkProjectProfile Values,string ProfileHash)
    {
        public Guid ProfileGuid=>Values.ProfileGuid;
        public string ProfileName=>Values.ProfileName;
        public int ProfileVersion=>Values.ProfileVersion;
        public static EarthworkProfileSnapshot Create(EarthworkProjectProfile p)=>new(p,EarthworkProfiles.Hash(p));
    }
    public static class EarthworkProfiles
    {
        // Stable ordered payload, invariant numeric JSON; identity/name/timestamps/UI state deliberately excluded.
        public static string Hash(EarthworkProjectProfile p)
        {
            var payload=new {Currency=p.Currency.Trim().ToUpperInvariant(),p.VolumeUnit,p.SwellFactor,p.FillLooseFactor,p.ReusableRate,p.TruckCapacity,p.TruckLoadUtilization,ExcavationUnitCost=p.ExcavationUnitCost?.ToString("G29",CultureInfo.InvariantCulture),LoadingUnitCost=p.LoadingUnitCost?.ToString("G29",CultureInfo.InvariantCulture),HaulCostPerTrip=p.HaulCostPerTrip?.ToString("G29",CultureInfo.InvariantCulture),DisposalCostPerVolume=p.DisposalCostPerVolume?.ToString("G29",CultureInfo.InvariantCulture),ImportedFillCostPerVolume=p.ImportedFillCostPerVolume?.ToString("G29",CultureInfo.InvariantCulture),BackfillPlacementCostPerVolume=p.BackfillPlacementCostPerVolume?.ToString("G29",CultureInfo.InvariantCulture),CompactionCostPerVolume=p.CompactionCostPerVolume?.ToString("G29",CultureInfo.InvariantCulture),MobilizationCost=(p.MobilizationCost??0).ToString("G29",CultureInfo.InvariantCulture)};
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
        }
        public static EarthworkProjectProfile NormalizeLegacy(EarthworkProjectProfile p)=>p.CreatedAt==default&&p.ProfileName=="Runtime fixture only"&&p.Currency=="TEST"&&p.TruckName=="Fixture truck"?p with{ProfileKind=EarthworkProfileKind.TestFixture}:p;
        public static bool Visible(EarthworkProjectProfile p,bool testMode)=>!p.IsArchived&&(testMode||p.ProfileKind==EarthworkProfileKind.Production);
        public static EarthworkProjectProfile Save(EarthworkProjectProfile draft,EarthworkProjectProfile? previous)
        {
            draft.Validate();if(previous!=null&&(previous.ProfileGuid!=draft.ProfileGuid||previous.ProfileKind!=draft.ProfileKind))throw new ArgumentException("不可變更設定檔身分或用途；請使用複製。");
            var now=DateTimeOffset.UtcNow;
            return draft with{ProfileVersion=previous==null?1:checked(previous.ProfileVersion+(Hash(previous)==Hash(draft)?0:1)),CreatedAt=previous?.CreatedAt is {} created&&created!=default?created:now,UpdatedAt=now};
        }
        public static EarthworkProjectProfile Clone(EarthworkProjectProfile source)=>source with{ProfileGuid=Guid.NewGuid(),ProfileName=source.ProfileName+" - 複本",ProfileVersion=1,IsArchived=false,CreatedAt=default,UpdatedAt=default};
        public static EarthworkRecord Refresh(EarthworkRecord r,IEnumerable<EarthworkProjectProfile> profiles)
        {
            var current=profiles.SingleOrDefault(p=>p.ProfileGuid==r.ProfileSnapshot.ProfileGuid);
            bool stale=current==null||current.ProfileVersion!=r.ProfileSnapshot.ProfileVersion||Hash(current)!=r.ProfileSnapshot.ProfileHash;
            return r with{CostProfileOutdated=stale,CalculationStatus=stale?CalculationStatus.Stale:r.CalculationStatus};
        }
        public static EarthworkProjectData Normalize(EarthworkProjectData data)
        {
            var profiles=data.Profiles.Select(NormalizeLegacy).ToArray();
            return new(profiles,data.Records.Select(r=>Refresh(r with{Profile=NormalizeLegacy(r.Profile),ProfileSnapshot=r.ProfileSnapshot with{Values=NormalizeLegacy(r.ProfileSnapshot.Values)}},profiles)).ToArray());
        }
        public static double Fraction(double percent,string label)
        {
            if(!double.IsFinite(percent)||percent<0||percent>100)throw new ArgumentException(label+"必須介於 0%～100%。");return percent/100;
        }
    }
    public static class EarthworkPresentation
    {
        public static string Number(double n,int decimals=2)=>n.ToString("N"+decimals,CultureInfo.CurrentCulture);
        public static string Money(decimal n,string currency)=>n.ToString("N2",CultureInfo.CurrentCulture)+" "+currency;
        public static string Percent(double fraction)=>Number(fraction*100,0)+" %";
        public static string Calculation(CalculationStatus status)=>status switch{CalculationStatus.Calculated=>"已計算",CalculationStatus.Stale=>"結果已過期",CalculationStatus.Failed=>"計算失敗",_=>"尚未計算"};
        public static string Review(ReviewStatus status)=>status switch{ReviewStatus.Reviewed=>"已複核",ReviewStatus.Warning=>"有警告",_=>"待複核"};
    }
}
#endif
