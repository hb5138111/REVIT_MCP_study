#if REVIT2026 || SITE_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using RevitMCP.Core.Site;
namespace RevitMCP.UI
{
    public sealed class EarthworkProfileEditor
    {
        private readonly EarthworkProjectProfile source;
        public Dictionary<string,string> Values {get;}=new();
        public EarthworkVolumeUnit Unit {get;set;}
        public EarthworkProfileEditor(EarthworkProjectProfile? profile)
        {
            source=profile??new();Unit=source.VolumeUnit;
            void Set(string key,object? value)=>Values[key]=Convert.ToString(value,CultureInfo.CurrentCulture)??"";
            Set("Name",source.ProfileName);Set("Currency",source.Currency);Set("Swell",source.SwellFactor);Set("Fill",source.FillLooseFactor);Set("Reuse",source.ReusableRate*100);Set("Truck",source.TruckName);Set("Capacity",source.TruckCapacity);Set("Util",source.TruckLoadUtilization*100);
            Set("Excavation",source.ExcavationUnitCost);Set("Loading",source.LoadingUnitCost);Set("Haul",source.HaulCostPerTrip);Set("Disposal",source.DisposalCostPerVolume);Set("Imported",source.ImportedFillCostPerVolume);Set("Backfill",source.BackfillPlacementCostPerVolume);Set("Compaction",source.CompactionCostPerVolume);Set("Mobilization",source.MobilizationCost);
        }
        private double Number(string key,string label)=>double.TryParse(Values[key],NumberStyles.Float,CultureInfo.CurrentCulture,out var value)&&double.IsFinite(value)?value:throw new ArgumentException("請輸入有效的"+label+"。");
        private decimal Price(string key,string label)=>decimal.TryParse(Values[key],NumberStyles.Number,CultureInfo.CurrentCulture,out var value)&&value>=0?value:throw new ArgumentException(label+"須為非負數，可明確輸入 0。");
        public string UnitLabel=>Unit==EarthworkVolumeUnit.CubicMetres?"m³":"ft³";
        public string TruckPreview {get{try{var capacity=Number("Capacity","車斗容量");var percent=Number("Util","裝載率");return $"有效單車容量：{EarthworkPresentation.Number(capacity)} {UnitLabel} × {EarthworkPresentation.Number(percent,0)}% = {EarthworkPresentation.Number(capacity*EarthworkProfiles.Fraction(percent,"裝載率"))} {UnitLabel}／車次";}catch(ArgumentException){return "請輸入車斗容量與裝載率，以預覽有效單車容量。";}}}
        public string SoilPreview {get{try{return $"原地挖方 1.00 {UnitLabel} 開挖後約為 {EarthworkPresentation.Number(Number("Swell","開挖鬆方係數"))} {UnitLabel}\n設計回填 1.00 {UnitLabel} 需要鬆方約 {EarthworkPresentation.Number(Number("Fill","回填需求係數"))} {UnitLabel}";}catch(ArgumentException){return "請輸入係數，以預覽原地量／鬆方量關係。";}}}
        public EarthworkProjectProfile Build()
        {
            double utilization=EarthworkProfiles.Fraction(Number("Util","裝載率"),"裝載率");if(utilization<=0)throw new ArgumentException("裝載率必須大於 0% 且不超過 100%，有效容量不可為 0。");
            var p=source with{ProfileName=Values["Name"].Trim(),Currency=Values["Currency"].Trim(),VolumeUnit=Unit,SwellFactor=Number("Swell","開挖鬆方係數"),FillLooseFactor=Number("Fill","回填需求係數"),ReusableRate=EarthworkProfiles.Fraction(Number("Reuse","可再利用率"),"可再利用率"),TruckName=Values["Truck"].Trim(),TruckCapacity=Number("Capacity","車斗容量"),TruckLoadUtilization=utilization,ExcavationUnitCost=Price("Excavation","挖土單價"),LoadingUnitCost=Price("Loading","裝車單價"),HaulCostPerTrip=Price("Haul","運輸單價"),DisposalCostPerVolume=Price("Disposal","棄土單價"),ImportedFillCostPerVolume=Price("Imported","外購填料單價"),BackfillPlacementCostPerVolume=Price("Backfill","回填施工單價"),CompactionCostPerVolume=Price("Compaction","夯實單價"),MobilizationCost=string.IsNullOrWhiteSpace(Values["Mobilization"])?null:Price("Mobilization","動員費")};p.Validate();return p;
        }
    }
}
#endif
