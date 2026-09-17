#if REVIT2026 || SITE_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace RevitMCP.Core.Site
{
    public static class EarthworkExport
    {
        private static string Text(string value)
        {
            if(value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@')value="'"+value;
            return "\""+value.Replace("\"","\"\"")+"\"";
        }
        public static string Csv(IReadOnlyList<EarthworkRecord> records)
        {
            var output=new StringBuilder("ZoneGuid,區號,區名,面積_m2,挖方原地量_m3,填方設計量_m3,幾何淨方_m3,鬆方係數,挖方鬆方量_m3,回填需求係數,回填需求量_m3,再利用率,可再利用鬆方_m3,再利用量_m3,外運量_m3,外購量_m3,車型,車斗容量,容量及單價單位,裝載率,有效容量_m3,外運車次,外購車次,挖土單價,裝載單價,運輸單價,棄土單價,外購土單價,回填單價,夯實單價,動員費,預估總价,幣別,計算方法,計算日期,狀態,TerrainId,來源SHA256,來源版本,完整依據_JSON\r\n");
            foreach(var r in records)
            {
                object?[] row={r.ZoneGuid,r.ZoneNumber,r.ZoneName,r.Quantity.Area,r.Quantity.CutBankVolume,r.Quantity.FillDesignVolume,r.Quantity.GeometricNetVolume,r.Profile.SwellFactor,r.Logistics.CutLooseVolume,r.Profile.FillLooseFactor,r.Logistics.FillLooseDemand,r.Profile.ReusableRate,r.Logistics.PotentialReusableLoose,r.Logistics.ReusedLooseVolume,r.Logistics.ExportLooseVolume,r.Logistics.ImportLooseVolume,r.Profile.TruckName,r.Profile.TruckCapacity,r.Profile.VolumeUnit.ToString(),r.Profile.TruckLoadUtilization,r.Logistics.EffectiveTruckCapacity,r.Logistics.ExportTruckTrips,r.Logistics.ImportTruckTrips,r.Profile.ExcavationUnitCost,r.Profile.LoadingUnitCost,r.Profile.HaulCostPerTrip,r.Profile.DisposalCostPerVolume,r.Profile.ImportedFillCostPerVolume,r.Profile.BackfillPlacementCostPerVolume,r.Profile.CompactionCostPerVolume,r.Cost.MobilizationCost,r.Cost.TotalEstimatedCost,r.Cost.Currency,r.Request.Zone.CalculationMethod.ToString(),r.Request.Zone.LastCalculatedAt.ToString("O"),r.Status,r.Request.Zone.ExistingTerrainId,r.Request.SourceDatasetHash,r.Request.SourceModelSignature,JsonSerializer.Serialize(r)};
                output.AppendLine(string.Join(",",row.Select(v=>v==null?"":v is string s?Text(s):v is IFormattable f?f.ToString(null,CultureInfo.InvariantCulture):Text(v.ToString()??""))));
            }return output.ToString();
        }
        public static void Write(string path,IReadOnlyList<EarthworkRecord> records,int format)
        {
            string json=JsonSerializer.Serialize(new{Version="0.5.2",QuantityUnits="SI m/m²/m³; profile truck/price basis explicitly labelled",Disclaimer=EarthworkEstimator.Disclaimer,SummaryWarning=EarthworkEstimator.OverlapWarning,Records=records},new JsonSerializerOptions{WriteIndented=true});
            string content=format switch{1=>Csv(records),2=>json,3=>"# 土方數量／成本明細\n\n"+EarthworkEstimator.Disclaimer+"\n\n"+EarthworkEstimator.OverlapWarning+"\n\n```json\n"+json+"\n```\n",_=>throw new ArgumentOutOfRangeException(nameof(format))};
            File.WriteAllText(path,content,new UTF8Encoding(true));
        }
    }
}
#endif
