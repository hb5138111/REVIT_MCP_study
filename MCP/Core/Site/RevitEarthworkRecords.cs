#if REVIT2026
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core.Site
{
    /// <summary>Project-owned analysis storage and schedulable records. Never modifies source geometry.</summary>
    internal static class RevitEarthworkRecords
    {
        private static readonly Guid SchemaId=new("768C4D95-AAC9-4B72-87E9-95308E6DAD32");
        private const string Owner="RevitMCP.Earthwork.0.5.2";
        private enum Kind { Text, Number, Area, Volume, Integer }
        private sealed record Column(string Key,string Heading,Kind Type,Func<EarthworkRecord,object?> Value,bool Total=false)
        {
            public Guid Guid=>new(SHA256.HashData(Encoding.UTF8.GetBytes("RevitMCP.Earthwork.Parameters.v1/"+Key)).Take(16).ToArray());
            public string Name=>"BIM_EW_"+Key;
            public ForgeTypeId Spec=>Type switch{Kind.Text=>SpecTypeId.String.Text,Kind.Area=>SpecTypeId.Area,Kind.Volume=>SpecTypeId.Volume,Kind.Integer=>SpecTypeId.Int.Integer,_=>SpecTypeId.Number};
        }
        private static readonly Column[] Columns={
            new("Owner","工具識別",Kind.Text,r=>Owner),new("ZoneGuid","土方區 GUID",Kind.Text,r=>r.ZoneGuid.ToString("D")),
            new("ZoneNumber","土方區編號",Kind.Text,r=>r.ZoneNumber),new("ZoneName","土方區名稱",Kind.Text,r=>r.ZoneName),
            new("Area","開挖面積",Kind.Area,r=>r.Quantity.Area,true),new("CutBank","挖方原地量",Kind.Volume,r=>r.Quantity.CutBankVolume,true),new("FillDesign","填方設計量",Kind.Volume,r=>r.Quantity.FillDesignVolume,true),new("Net","幾何淨方",Kind.Volume,r=>r.Quantity.GeometricNetVolume,true),
            new("Swell","鬆方係數",Kind.Number,r=>r.Profile.SwellFactor),new("CutLoose","挖方鬆方量",Kind.Volume,r=>r.Logistics.CutLooseVolume,true),new("FillFactor","回填需求係數",Kind.Number,r=>r.Profile.FillLooseFactor),new("FillDemand","回填需求量",Kind.Volume,r=>r.Logistics.FillLooseDemand,true),new("ReuseRate","再利用率",Kind.Number,r=>r.Profile.ReusableRate),new("Reused","再利用量",Kind.Volume,r=>r.Logistics.ReusedLooseVolume,true),new("Export","外運量",Kind.Volume,r=>r.Logistics.ExportLooseVolume,true),new("Import","外購量",Kind.Volume,r=>r.Logistics.ImportLooseVolume,true),
            new("Truck","車型",Kind.Text,r=>r.Profile.TruckName),new("Capacity","車斗容量",Kind.Volume,r=>EarthworkUnits.ToCubicMetres(r.Profile.TruckCapacity!.Value,r.Profile.VolumeUnit)),new("Utilization","裝載率",Kind.Number,r=>r.Profile.TruckLoadUtilization),new("ExportTrips","外運車次",Kind.Integer,r=>checked((int)r.Logistics.ExportTruckTrips),true),new("ImportTrips","外購車次",Kind.Integer,r=>checked((int)r.Logistics.ImportTruckTrips),true),
            new("ExcavationPrice","挖土單價",Kind.Number,r=>r.Profile.ExcavationUnitCost),new("LoadingPrice","裝載單價",Kind.Number,r=>r.Profile.LoadingUnitCost),new("HaulPrice","運輸單價／車次",Kind.Number,r=>r.Profile.HaulCostPerTrip),new("DisposalPrice","棄土單價",Kind.Number,r=>r.Profile.DisposalCostPerVolume),new("ImportedPrice","外購土單價",Kind.Number,r=>r.Profile.ImportedFillCostPerVolume),new("BackfillPrice","回填單價",Kind.Number,r=>r.Profile.BackfillPlacementCostPerVolume),new("CompactionPrice","夯實單價",Kind.Number,r=>r.Profile.CompactionCostPerVolume),new("Mobilization","動員費",Kind.Number,r=>r.Cost.MobilizationCost,true),new("TotalCost","預估總價",Kind.Number,r=>r.Cost.TotalEstimatedCost,true),
            new("Currency","幣別",Kind.Text,r=>r.Cost.Currency),new("PriceBasis","體積單價基準",Kind.Text,r=>r.Profile.VolumeUnit==EarthworkVolumeUnit.CubicMetres?"每 m³":"每 ft³"),new("Method","計算方法",Kind.Text,r=>r.Request.Zone.CalculationMethod.ToString()),new("Calculated","計算日期",Kind.Text,r=>r.Request.Zone.LastCalculatedAt.ToString("O")),new("Status","狀態",Kind.Text,r=>r.Status),new("Warnings","提醒",Kind.Text,r=>string.Join("；",r.Request.Zone.Warnings))
        };
        private static Schema? ExistingSchema=>Schema.Lookup(SchemaId);
        private static Schema GetSchema()
        {
            if(ExistingSchema is {} existing)return existing;
            var builder=new SchemaBuilder(SchemaId);builder.SetSchemaName("RevitMCP_EarthworkRecords_v1");builder.SetReadAccessLevel(AccessLevel.Public);builder.SetWriteAccessLevel(AccessLevel.Public);builder.AddSimpleField("Kind",typeof(string));builder.AddSimpleField("Payload",typeof(string));return builder.Finish();
        }
        private static string? Payload(Element element,string kind)
        {
            var schema=ExistingSchema;if(schema==null)return null;var entity=element.GetEntity(schema);
            return entity.IsValid()&&entity.Get<string>(schema.GetField("Kind"))==kind?entity.Get<string>(schema.GetField("Payload")):null;
        }
        private static void Store(Element element,string kind,object value)
        {
            var schema=GetSchema();var entity=new Entity(schema);entity.Set(schema.GetField("Kind"),kind);entity.Set(schema.GetField("Payload"),JsonConvert.SerializeObject(value));element.SetEntity(entity);
        }
        private static Element[] Owned(Document d,Type type,string kind)
        {
            if(ExistingSchema==null)return Array.Empty<Element>();
            return new FilteredElementCollector(d).OfClass(type).WherePasses(new ExtensibleStorageFilter(SchemaId)).Where(e=>Payload(e,kind)!=null).ToArray();
        }
        public static EarthworkProjectData Load(Document d)
        {
            var stores=Owned(d,typeof(DataStorage),"project");if(stores.Length>1)throw new InvalidOperationException("發現重複土方專案資料，不自動合併。");
            return stores.Length==0?new(Array.Empty<EarthworkProjectSettings>(),Array.Empty<EarthworkRecord>()):JsonConvert.DeserializeObject<EarthworkProjectData>(Payload(stores[0],"project")!)??throw new InvalidOperationException("土方專案資料損毀。");
        }
        public static string Signature(Document d,EarthworkZone zone)
        {
            var ids=new[]{zone.ExistingTerrainId,zone.CutterId??0,zone.BaseLevelId??0,d.ActiveProjectLocation.Id.Value}.Concat(zone.BoundaryElementIds).Where(id=>id>0).Distinct().OrderBy(id=>id);
            string value=string.Join(";",ids.Select(id=>{var e=d.GetElement(new ElementId(id))??throw new InvalidOperationException($"來源 Element {id} 已刪除。");return $"{id}:{e.UniqueId}:{e.VersionGuid}";}));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }
        private static void ValidateRecord(Document d,EarthworkRecord record)
        {
            if(record.Request.SourceModelSignature!=Signature(d,record.Request.Zone))throw new InvalidOperationException($"土方區 {record.ZoneNumber} 的來源已變動，請重新計算。");
            var expected=EarthworkEstimator.Calculate(record.Request,record.Quantity,record.Profile);
            if(expected.Logistics!=record.Logistics||expected.Cost!=record.Cost)throw new InvalidOperationException("土方紀錄與 Profile 計算不一致。");
        }
        public static EarthworkProjectData Save(Document d,EarthworkProjectData data,bool confirmed)
        {
            if(!confirmed)throw new InvalidOperationException("儲存專案分析資料需要明確確認。");
            if(data.Records.GroupBy(r=>r.ZoneGuid).Any(g=>g.Count()>1)||data.Profiles.GroupBy(p=>p.ProfileGuid).Any(g=>g.Count()>1))throw new ArgumentException("GUID 重複。");
            foreach(var p in data.Profiles)p.Validate();
            using var tx=new Transaction(d,"BIM 土方分析資料");tx.Start();
            var stores=Owned(d,typeof(DataStorage),"project");if(stores.Length>1)throw new InvalidOperationException("重複專案資料。");
            var store=stores.FirstOrDefault()??DataStorage.Create(d);Store(store,"project",data);
            var read=Load(d);if(JsonConvert.SerializeObject(read)!=JsonConvert.SerializeObject(data))throw new InvalidOperationException("專案分析資料 read-back 不一致。");
            if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("專案分析資料交易未完成。");return read;
        }
        private static Dictionary<Guid,Element> RecordElements(Document d)
        {
            var result=new Dictionary<Guid,Element>();foreach(var e in Owned(d,typeof(DirectShape),"record"))
            {
                var row=JsonConvert.DeserializeObject<EarthworkRecord>(Payload(e,"record")!)??throw new InvalidOperationException("紀錄損毀。");
                if(!result.TryAdd(row.ZoneGuid,e))throw new InvalidOperationException("模型中有重複 ZoneGuid 紀錄，請先檢查。");
            }return result;
        }
        private static ViewSchedule? Schedule(Document d)
        {
            var schedules=Owned(d,typeof(ViewSchedule),"schedule").Cast<ViewSchedule>().ToArray();if(schedules.Length>1)throw new InvalidOperationException("找到多個工具管理的土方明細表。");return schedules.SingleOrDefault();
        }
        public static EarthworkSchedulePreview Preview(Document d,IReadOnlyList<EarthworkRecord> rows)
        {
            if(rows.Count==0)throw new ArgumentException("請先儲存土方區紀錄。");
            if(rows.Select(r=>r.ZoneGuid).Distinct().Count()!=rows.Count)throw new ArgumentException("ZoneGuid 重複。");
            if(rows.Select(r=>r.Cost.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=1)throw new ArgumentException("同一明細表需使用相同幣別，不自動換匯。");
            foreach(var row in rows)ValidateRecord(d,row);
            var records=RecordElements(d);var schedule=Schedule(d);string name=schedule?.Name??"土方工程明細";
            if(schedule==null)
            {
                var names=new FilteredElementCollector(d).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Select(v=>v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int suffix=1;while(names.Contains(name)){name=suffix==1?"土方工程明細 (BIM)":$"土方工程明細 (BIM {suffix})";suffix++;}
            }
            string evidence=JsonConvert.SerializeObject(new{Name=name,Rows=rows,Existing=records.OrderBy(x=>x.Key).Select(x=>new{x.Key,Id=x.Value.Id.Value,Version=x.Value.VersionGuid})});
            return new(name,Columns.Select(c=>c.Heading+" / "+c.Name).ToArray(),rows.Where(r=>!records.ContainsKey(r.ZoneGuid)).Select(r=>r.ZoneGuid).ToArray(),rows.Where(r=>records.ContainsKey(r.ZoneGuid)).Select(r=>r.ZoneGuid).ToArray(),Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence))));
        }
        private static Dictionary<string,ElementId> Bind(Document d)
        {
            string previous=d.Application.SharedParametersFilename,path=Path.Combine(Path.GetTempPath(),"RevitMCP-Earthwork-"+Guid.NewGuid().ToString("N")+".txt");
            try
            {
                File.WriteAllText(path,"# Revit shared parameter file\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\r\n");
                d.Application.SharedParametersFilename=path;var file=d.Application.OpenSharedParameterFile()??throw new InvalidOperationException("無法建立土方參數定義。");var group=file.Groups.Create("RevitMCP Earthwork");
                var category=d.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);var result=new Dictionary<string,ElementId>();
                foreach(var col in Columns)
                {
                    var existing=SharedParameterElement.Lookup(d,col.Guid);
                    if(existing!=null)
                    {
                        var definition=existing.GetDefinition();var binding=d.ParameterBindings.get_Item(definition) as InstanceBinding;
                        if(definition.Name!=col.Name||definition.GetDataType()!=col.Spec||binding==null||binding.Categories.Size!=1||!binding.Categories.Contains(category))throw new InvalidOperationException($"土方參數 {col.Name} 定義／binding 衝突。");
                    }
                    else
                    {
                        var iterator=d.ParameterBindings.ForwardIterator();iterator.Reset();while(iterator.MoveNext())if(iterator.Key.Name==col.Name)throw new InvalidOperationException($"同名參數 {col.Name} 已存在但 GUID 不符。");
                        var definition=group.Definitions.Create(new ExternalDefinitionCreationOptions(col.Name,col.Spec){GUID=col.Guid,UserModifiable=false,Visible=true,HideWhenNoValue=true});
                        var categories=d.Application.Create.NewCategorySet();categories.Insert(category);
                        if(!d.ParameterBindings.Insert(definition,d.Application.Create.NewInstanceBinding(categories),GroupTypeId.Data))throw new InvalidOperationException($"無法 binding {col.Name}。");
                        existing=SharedParameterElement.Lookup(d,col.Guid)??throw new InvalidOperationException("Shared parameter read-back missing");
                    }
                    result.Add(col.Key,existing.Id);
                }return result;
            }
            finally{d.Application.SharedParametersFilename=previous;if(File.Exists(path))File.Delete(path);}
        }
        private static double Internal(Column c,object value)=>c.Type switch
        {Kind.Area=>UnitUtils.ConvertToInternalUnits(Convert.ToDouble(value,CultureInfo.InvariantCulture),UnitTypeId.SquareMeters),Kind.Volume=>UnitUtils.ConvertToInternalUnits(Convert.ToDouble(value,CultureInfo.InvariantCulture),UnitTypeId.CubicMeters),_=>Convert.ToDouble(value,CultureInfo.InvariantCulture)};
        public static EarthworkScheduleResult WriteSchedule(Document d,IReadOnlyList<EarthworkRecord> rows,EarthworkSchedulePreview preview,bool confirmed,Action? beforeReadBack=null)
        {
            if(!confirmed)throw new InvalidOperationException("建立／更新明細表需明確確認。");
            if(Preview(d,rows).Fingerprint!=preview.Fingerprint)throw new InvalidOperationException("Schedule preview 已失效，請重新預覽。");
            using var group=new TransactionGroup(d,"BIM 土方工程明細");group.Start();
            using(var tx=new Transaction(d,"BIM 土方紀錄與明細欄位"))
            {
                tx.Start();var parameters=Bind(d);var records=RecordElements(d);
                foreach(var row in rows)
                {
                    Element element=records.TryGetValue(row.ZoneGuid,out var stored)?stored:DirectShape.CreateElement(d,new ElementId(BuiltInCategory.OST_GenericModel));
                    Store(element,"record",row);
                    foreach(var col in Columns)
                    {
                        var parameter=element.get_Parameter(col.Guid)??throw new InvalidOperationException("Record parameter missing: "+col.Name);var value=col.Value(row);
                        if(value==null){if(parameter.HasValue&&!parameter.ClearValue())throw new InvalidOperationException("Cannot clear unavailable quantity");continue;}
                        bool unchanged=parameter.HasValue&&(col.Type==Kind.Text?parameter.AsString()==(string)value:col.Type==Kind.Integer?parameter.AsInteger()==Convert.ToInt32(value,CultureInfo.InvariantCulture):parameter.AsDouble()==Internal(col,value));
                        if(unchanged)continue;
                        bool set=col.Type==Kind.Text?parameter.Set((string)value):col.Type==Kind.Integer?parameter.Set(Convert.ToInt32(value,CultureInfo.InvariantCulture)):parameter.Set(Internal(col,value));
                        if(!set)throw new InvalidOperationException("Record parameter write failed: "+col.Name);
                    }
                }
                var schedule=Schedule(d)??ViewSchedule.CreateSchedule(d,new ElementId(BuiltInCategory.OST_GenericModel));schedule.Name=preview.ScheduleName;Store(schedule,"schedule",new{Owner});
                var definition=schedule.Definition;definition.ClearFilters();definition.ClearSortGroupFields();definition.ClearFields();definition.IsItemized=true;
                var fields=definition.GetSchedulableFields();ScheduleField? owner=null,number=null;
                foreach(var col in Columns)
                {
                    var available=fields.Single(f=>f.ParameterId==parameters[col.Key]);var field=definition.AddField(available);field.ColumnHeading=col.Heading;field.IsHidden=col.Key is "Owner" or "ZoneGuid";
                    if(col.Total&&field.CanTotal())field.DisplayType=ScheduleFieldDisplayType.Totals;
                    if(col.Key=="Owner")owner=field;if(col.Key=="ZoneNumber")number=field;
                }
                definition.AddFilter(new ScheduleFilter(owner!.FieldId,ScheduleFilterType.Equal,Owner));definition.AddSortGroupField(new ScheduleSortGroupField(number!.FieldId));
                definition.ShowGrandTotal=true;definition.ShowGrandTotalTitle=true;definition.GrandTotalTitle="明細加總（各區可能重疊）";
                if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Schedule transaction failed");
            }
            beforeReadBack?.Invoke();var result=ReadBack(d,rows);
            if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("Schedule group failed");return result;
        }
        public static EarthworkScheduleResult ReadBack(Document d,IReadOnlyList<EarthworkRecord> rows)
        {
            var schedule=Schedule(d)??throw new InvalidOperationException("Schedule read-back missing");var elements=RecordElements(d);
            if(elements.Count!=rows.Count||!elements.Keys.OrderBy(x=>x).SequenceEqual(rows.Select(r=>r.ZoneGuid).OrderBy(x=>x)))throw new InvalidOperationException("Schedule records differ; explicitly delete obsolete analysis records first.");
            var visible=new FilteredElementCollector(d,schedule.Id).WhereElementIsNotElementType().ToElementIds().Select(id=>id.Value).ToHashSet();
            if(!visible.SetEquals(elements.Values.Select(e=>e.Id.Value)))throw new InvalidOperationException("Dedicated records are not reliably schedulable: schedule membership differs.");
            if(schedule.Definition.GetFieldCount()!=Columns.Length)throw new InvalidOperationException("Schedule field count mismatch");
            for(int index=0;index<Columns.Length;index++)
            {
                var col=Columns[index];var field=schedule.Definition.GetField(index);var parameter=SharedParameterElement.Lookup(d,col.Guid);
                if(parameter==null||field.ParameterId!=parameter.Id||field.ColumnHeading!=col.Heading||field.IsHidden!=(col.Key is "Owner" or "ZoneGuid"))throw new InvalidOperationException("Schedule field definition differs: "+col.Name);
                if(col.Total&&field.CanTotal()&&field.DisplayType!=ScheduleFieldDisplayType.Totals)throw new InvalidOperationException("Schedule totals configuration differs: "+col.Name);
            }
            if(!schedule.Definition.ShowGrandTotal||!schedule.Definition.IsItemized)throw new InvalidOperationException("Schedule totals / itemized configuration differs");
            foreach(var row in rows)foreach(var col in Columns)
            {
                var p=elements[row.ZoneGuid].get_Parameter(col.Guid)??throw new InvalidOperationException("Read-back parameter missing");var value=col.Value(row);
                bool equal=value==null?!p.HasValue:col.Type==Kind.Text?p.AsString()==(string)value:col.Type==Kind.Integer?p.AsInteger()==Convert.ToInt32(value):Math.Abs(p.AsDouble()-Internal(col,value))<=1e-8*Math.Max(1,Math.Abs(Internal(col,value)));
                if(!equal)throw new InvalidOperationException("Schedule read-back differs: "+row.ZoneNumber+" / "+col.Name);
            }
            return new(schedule.Id.Value,schedule.Name,elements.ToDictionary(x=>x.Key,x=>x.Value.Id.Value),Columns.Length,"PASS: fields, owned ZoneGuids, record membership and every quantity/cost parameter");
        }
        public static EarthworkProjectData Delete(Document d,Guid zoneGuid,bool deleteScheduleRecord,bool confirmed)
        {
            if(!confirmed)throw new InvalidOperationException("刪除分析紀錄需要明確確認。");
            var data=Load(d);var records=RecordElements(d);
            if(records.ContainsKey(zoneGuid)&&!deleteScheduleRecord)throw new InvalidOperationException("此區已有 Schedule 紀錄；請明確確認一併刪除該紀錄，避免明細表留存舊數量。");
            using var group=new TransactionGroup(d,"BIM 刪除土方分析紀錄");group.Start();
            if(records.TryGetValue(zoneGuid,out var record))
            {
                using var tx=new Transaction(d,"BIM 刪除 owned 土方紀錄");tx.Start();var recordId=record.Id;var deleted=d.Delete(recordId);
                if(deleted.Count!=1||!deleted.Contains(recordId))throw new InvalidOperationException("刪除會影響其他元素，已停止。");tx.Commit();
            }
            var remaining=Save(d,new(data.Profiles,data.Records.Where(r=>r.ZoneGuid!=zoneGuid).ToArray()),true);group.Assimilate();return remaining;
        }
    }
}
#endif
