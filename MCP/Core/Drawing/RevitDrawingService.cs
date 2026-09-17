#if REVIT2026
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;

namespace RevitMCP.Core.Drawing
{
    /// <summary>API-context-only operations. No command dispatcher or nested legacy transactions.</summary>
    internal sealed class RevitDrawingService
    {
        private readonly Document document;
        private static readonly Guid SchemaId = new("96988DDA-0E40-4E43-BEC5-BA8B0B322A83");
        private const double VerificationTolerance = 1e-7; // feet; numerical read-back precision, not a design margin.
        public RevitDrawingService(Document document) { this.document=document; }
        public string Identity => DocumentSessionIdentity.GetDocumentIdentity(document);
        public static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;
        private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value))));
        private static DrawingBounds Box(BoundingBoxXYZ box) => new(box.Min.X,box.Min.Y,box.Max.X,box.Max.Y);
        private static DrawingBounds Box(Outline box) => new(box.MinimumPoint.X,box.MinimumPoint.Y,box.MaximumPoint.X,box.MaximumPoint.Y);
        private static DrawingPoint Point(XYZ point) => new(point.X,point.Y);
        private static XYZ Xyz(DrawingPoint point) => new(point.X,point.Y,0);
        private static double Round(double value) => Math.Round(value,7);
        private HashSet<long> HiddenFixtureIds()
        {
            var hidden=new HashSet<long>();if(DrawingFixtureIsolation.DeveloperMode)return hidden;
            var data=Load();
            foreach(var profile in data.Profiles.Concat(data.Packages.Select(p=>p.Profile)).Where(p=>!DrawingFixtureIsolation.Visible(p)))
            {
                hidden.Add(profile.Blueprint.SourceSheetId);hidden.Add(profile.Blueprint.TitleBlockTypeId);
                foreach(var slot in profile.Blueprint.Slots){hidden.Add(slot.SourceViewId);hidden.Add(slot.ViewTemplateId);}
                foreach(var package in data.Packages.Where(p=>p.Profile.ProfileGuid==profile.ProfileGuid))
                {
                    foreach(var level in package.Levels)hidden.Add(level.Id);
                    foreach(var view in package.SourceViewsByLevel.Values)hidden.Add(view);
                    foreach(var record in data.Records.Where(r=>r.DrawingPackageGuid==package.PackageGuid)){hidden.Add(record.SheetId);hidden.Add(record.ViewId);}
                }
            }
            return hidden;
        }
        public DrawingChoice[] Sheets()
        {
            var hidden=HiddenFixtureIds();return new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s=>!s.IsPlaceholder&&DrawingFixtureIsolation.Visible(s)&&!hidden.Contains(s.Id.Value)).OrderBy(s=>s.SheetNumber,StringComparer.Ordinal).ThenBy(s=>s.Name,StringComparer.Ordinal).Select(s=>new DrawingChoice(s.Id.Value,s.SheetNumber+" | "+s.Name)).ToArray();
        }
        public DrawingChoice[] Levels()
        {
            var hidden=HiddenFixtureIds();return new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().Where(l=>DrawingFixtureIsolation.Visible(l)&&!hidden.Contains(l.Id.Value)).OrderBy(l=>l.ProjectElevation).ThenBy(l=>l.Id.Value).Select(l=>new DrawingChoice(l.Id.Value,l.Name,l.ProjectElevation)).ToArray();
        }
        public Dictionary<long,DrawingChoice[]> SourcesByLevel()
        {
            var hidden=HiddenFixtureIds();var generated=Load().Records.Where(r=>r.ViewStrategy!=DrawingViewStrategy.Existing).Select(r=>r.ViewId).ToHashSet();
            return new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v=>!v.IsTemplate&&DrawingFixtureIsolation.Visible(v)&&!hidden.Contains(v.Id.Value)&&!generated.Contains(v.Id.Value)&&v.ViewType==ViewType.FloorPlan&&v.GenLevel!=null).GroupBy(v=>v.GenLevel.Id.Value).ToDictionary(g=>g.Key,g=>g.OrderBy(v=>v.Name,StringComparer.Ordinal).Select(v=>new DrawingChoice(v.Id.Value,v.Name)).ToArray());
        }
        public DrawingChoice[] Sources(long level) => SourcesByLevel().TryGetValue(level,out var views)?views:Array.Empty<DrawingChoice>();
        public DrawingZone[] Zones() => new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType().OrderBy(e=>e.Id.Value).Select(e=>new DrawingZone{ZoneId=e.UniqueId,ZoneName=e.Name,SourceId=e.Id.Value,Bounds=Box(e.get_BoundingBox(null)??throw new InvalidOperationException("Scope Box 缺少範圍。"))}).ToArray();
        public DrawingChoice[] Grids()=>new FilteredElementCollector(document).OfClass(typeof(Grid)).Cast<Grid>().OrderBy(g=>g.Name,StringComparer.Ordinal).Select(g=>new DrawingChoice(g.Id.Value,g.Name)).ToArray();
        public DrawingChoice[] ViewTemplates()=>new[]{new DrawingChoice(-1,"不套用視圖樣板")}.Concat(new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Where(v=>v.IsTemplate&&DrawingFixtureIsolation.Visible(v)).OrderBy(v=>v.Name,StringComparer.Ordinal).Select(v=>new DrawingChoice(v.Id.Value,v.Name))).ToArray();
        private static bool ControlsScale(View? template)=>template!=null&&template.GetTemplateParameterIds().Contains(new ElementId(BuiltInParameter.VIEW_SCALE))&&!template.GetNonControlledTemplateParameterIds().Contains(new ElementId(BuiltInParameter.VIEW_SCALE));
        public SheetTemplateBlueprint ConfigureViewRule(SheetTemplateBlueprint blueprint,long templateId,int? scale)
        {
            var copy=Clone(blueprint);var slot=copy.Viewports.Single();
            var template=templateId==-1?null:document.GetElement(new ElementId(templateId)) as View;
            if(templateId!=-1&&(template==null||!template.IsTemplate))throw new ArgumentException("視圖樣板已不存在。");
            bool controlled=ControlsScale(template);
            if(scale.HasValue&&scale<=0)throw new ArgumentException("比例需為正整數。");
            if(controlled&&scale.HasValue&&scale!=template!.Scale)throw new ArgumentException("比例由 View Template 控制，不能另外覆寫。");
            slot.ViewTemplateId=templateId;slot.ScaleControlled=controlled;slot.Scale=controlled?template!.Scale:scale??slot.Scale;return copy;
        }
        public DrawingZone GridZone(string name,long[] gridIds,double paddingMm)
        {
            if(string.IsNullOrWhiteSpace(name)||!double.IsFinite(paddingMm)||paddingMm<0||gridIds.Distinct().Count()!=4)throw new ArgumentException("網格分區需名稱、四條不同的正交直線軸線與非負 padding（mm）。");
            var xs=new List<double>();var ys=new List<double>();
            foreach(long id in gridIds)
            {
                var grid=document.GetElement(new ElementId(id)) as Grid??throw new ArgumentException("網格已遺失。");
                if(grid.Curve is not Line line)throw new ArgumentException("僅支援正交直線網格。");
                if(Math.Abs(line.Direction.X)<VerificationTolerance)xs.Add(line.Origin.X);
                else if(Math.Abs(line.Direction.Y)<VerificationTolerance)ys.Add(line.Origin.Y);
                else throw new ArgumentException("旋轉網格需另行定義座標系；本版不自動猜測。");
            }
            if(xs.Count!=2||ys.Count!=2||xs[0]==xs[1]||ys[0]==ys[1])throw new ArgumentException("需兩條 X 邊界與兩條 Y 邊界。");
            double padding=paddingMm/304.8;
            return new DrawingZone{ZoneId="grid:"+string.Join(",",gridIds.OrderBy(id=>id)),ZoneName=name,ScopeSource="GridRange",GridIds=gridIds.OrderBy(id=>id).ToArray(),PaddingMm=paddingMm,Bounds=new(xs.Min()-padding,ys.Min()-padding,xs.Max()+padding,ys.Max()+padding)};
        }
        public DrawingProjectData ProductionData()
        {
            var data=Load();data.Profiles=data.Profiles.Where(DrawingFixtureIsolation.Visible).ToList();
            data.Packages=data.Packages.Where(p=>DrawingFixtureIsolation.Visible(p.Profile)).ToList();
            var allowed=data.Packages.Select(p=>p.PackageGuid).ToHashSet();data.Records=data.Records.Where(r=>allowed.Contains(r.DrawingPackageGuid)).ToList();return data;
        }
        private FamilyInstance TitleBlock(ViewSheet sheet) => new FilteredElementCollector(document,sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().Cast<FamilyInstance>().Single();
        public SheetTemplateBlueprint Extract(long sheetId)
        {
            var sheet=document.GetElement(new ElementId(sheetId)) as ViewSheet??throw new ArgumentException("樣板圖紙不存在。");
            if(sheet.IsPlaceholder)throw new ArgumentException("佔位圖紙不能作為樣板。");
            var title=TitleBlock(sheet);
            var location=title.Location as LocationPoint??throw new ArgumentException("圖框位置不支援。");
            if(Math.Abs(location.Rotation)>VerificationTolerance)throw new ArgumentException("第一版僅支援未旋轉圖框。");
            var bounds=Box(title.get_BoundingBox(sheet)??throw new ArgumentException("圖框無可靠範圍。"));
            if(!bounds.Valid)throw new ArgumentException("圖框範圍無效。");
            var result=new SheetTemplateBlueprint {SourceSheetId=sheetId,SourceSheetUniqueId=sheet.UniqueId,SourceSheetNumber=sheet.SheetNumber,SourceSheetName=sheet.Name,SourceDocumentIdentity=Identity,TitleBlockFamilyId=title.Symbol.Family.Id.Value,TitleBlockTypeId=title.GetTypeId().Value,TitleBlockFamilyName=title.Symbol.Family.Name,TitleBlockTypeName=title.Symbol.Name,TitleBlockBounds=bounds,TitleBlockLocation=Point(location.Point),SheetBounds=new(sheet.Outline.Min.U,sheet.Outline.Min.V,sheet.Outline.Max.U,sheet.Outline.Max.V)};
            foreach(var id in sheet.GetAllViewports().OrderBy(id=>id.Value))
            {
                var viewport=(Viewport)document.GetElement(id);var view=(View)document.GetElement(viewport.ViewId);
                var slot=ReadSlot(viewport,view,bounds);
                if(view.ViewType==ViewType.Legend){slot.Role="LEGEND";result.Legends.Add(slot);}else result.Viewports.Add(slot);
            }
            foreach(var instance in new FilteredElementCollector(document,sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().OrderBy(s=>s.Id.Value))
            {
                var schedule=(ViewSchedule)document.GetElement(instance.ScheduleId);
                if(schedule.IsTitleblockRevisionSchedule)continue;
                if(schedule.IsSplit())throw new ArgumentException("第一版不支援分割 Schedule 樣板。");
                result.Schedules.Add(new SheetLayoutSlot{SlotId=instance.UniqueId,Role="SCHEDULE",SourceViewName=schedule.Name,SourceViewId=instance.ScheduleId.Value,SourceViewportId=instance.Id.Value,AbsoluteX=instance.Point.X,AbsoluteY=instance.Point.Y,Bounds=Box(instance.get_BoundingBox(sheet)??throw new ArgumentException("Schedule 缺少範圍。"))});
            }
            result.SheetParameterCopyPolicy=Parameters(sheet,"Sheet").Concat(Parameters(title,"TitleBlock")).ToList();
            if(result.Viewports.Count==1)result.SheetParameterCopyPolicy.AddRange(Parameters(document.GetElement(new ElementId(result.Viewports[0].SourceViewId)),"View"));
            result.Warnings.Add("只沿用勾選的文字參數；不複製修訂、發行狀態、Project Information 或任意註記。Guide Grid 尚未支援。");
            return result;
        }
        private SheetLayoutSlot ReadSlot(Viewport viewport,View view,DrawingBounds bounds)
        {
            XYZ center=viewport.GetBoxCenter();
            var template=document.GetElement(view.ViewTemplateId) as View;
            bool controlled=ControlsScale(template);
            return new SheetLayoutSlot{SlotId=viewport.UniqueId,SourceViewportId=viewport.Id.Value,SourceViewId=view.Id.Value,SourceViewName=view.Name,ExpectedViewKind=view.ViewType.ToString(),ViewportTypeId=viewport.GetTypeId().Value,ViewTemplateId=view.ViewTemplateId.Value,Scale=view.Scale,ScaleControlled=controlled,AbsoluteX=center.X,AbsoluteY=center.Y,NormalizedX=(center.X-bounds.MinX)/bounds.Width,NormalizedY=(center.Y-bounds.MinY)/bounds.Height,Bounds=Box(viewport.GetBoxOutline()),TitleOffset=Point(viewport.LabelOffset),TitleLineLength=viewport.LabelLineLength,Rotation=(int)viewport.Rotation,DetailNumber=viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString()??""};
        }
        private static IEnumerable<DrawingParameter> Parameters(Element element,string owner)
        {
            var selectable=new HashSet<long>{(long)BuiltInParameter.SHEET_DRAWN_BY,(long)BuiltInParameter.SHEET_CHECKED_BY,(long)BuiltInParameter.SHEET_DESIGNED_BY,(long)BuiltInParameter.SHEET_APPROVED_BY};
            foreach(Parameter p in element.Parameters)
            {
                if(p.StorageType!=StorageType.String||p.IsReadOnly)continue;
                bool generated=p.Id.Value==(long)BuiltInParameter.SHEET_NUMBER||p.Id.Value==(long)BuiltInParameter.SHEET_NAME;
                // Unknown custom text parameters stay opt-in. Revision/issue-like fields remain denied.
                bool sensitive=System.Text.RegularExpressions.Regex.IsMatch(p.Definition.Name,@"revision|issue|修訂|發行|發圖",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                yield return new DrawingParameter{ParameterId=p.Id.Value,Name=p.Definition.Name,Owner=owner,Value=p.AsString()??"",Policy=generated?DrawingCopyPolicy.Generated:!sensitive&&(selectable.Contains(p.Id.Value)||p.Id.Value>0)?DrawingCopyPolicy.UserSelectable:DrawingCopyPolicy.NeverCopy};
            }
        }
        public DrawingProjectData Load()
        {
            var schema=Schema.Lookup(SchemaId);if(schema==null)return new();
            var values=new FilteredElementCollector(document).OfClass(typeof(DataStorage)).Where(e=>e.GetEntity(schema).IsValid()).ToArray();
            if(values.Length>1)throw new InvalidOperationException("施工圖中心儲存重複，請先複核。");
            if(values.Length==0)return new();
            var result=JsonConvert.DeserializeObject<DrawingProjectData>(values[0].GetEntity(schema).Get<string>("Json"))??throw new InvalidOperationException("施工圖資料無法讀取。");
            if(result.SchemaVersion!=1)throw new InvalidOperationException("不支援此施工圖儲存版本。");return result;
        }
        private void Store(DrawingProjectData data)
        {
            var schema=Schema.Lookup(SchemaId);
            if(schema==null){var builder=new SchemaBuilder(SchemaId);builder.SetSchemaName("RevitMCPDrawingProduction");builder.SetReadAccessLevel(AccessLevel.Public);builder.SetWriteAccessLevel(AccessLevel.Public);builder.AddSimpleField("Json",typeof(string));schema=builder.Finish();}
            var storage=new FilteredElementCollector(document).OfClass(typeof(DataStorage)).Cast<DataStorage>().SingleOrDefault(e=>e.GetEntity(schema).IsValid())??DataStorage.Create(document);
            var entity=new Entity(schema);entity.Set("Json",JsonConvert.SerializeObject(data));storage.SetEntity(entity);
        }
        public DrawingTemplateProfile SaveProfile(DrawingTemplateProfile profile)
        {
            var data=Load();var copy=Clone(profile);if(string.IsNullOrWhiteSpace(copy.ProfileName))throw new ArgumentException("請輸入樣板名稱。");
            var previous=data.Profiles.SingleOrDefault(p=>p.ProfileGuid==copy.ProfileGuid);
            copy.ProfileVersion=previous==null?1:previous.ProfileVersion+1;copy.UpdatedAt=DateTimeOffset.UtcNow;copy.Blueprint.TemplateVersion=copy.ProfileVersion;
            data.Profiles.RemoveAll(p=>p.ProfileGuid==copy.ProfileGuid);data.Profiles.Add(copy);
            using var transaction=new Transaction(document,"保存施工圖樣板");transaction.Start();Store(data);
            if(Hash(Load())!=Hash(data))throw new InvalidOperationException("樣板保存 read-back 不一致。");
            if(transaction.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("樣板保存失敗。");return copy;
        }
        private string Actual(ViewSheet sheet)
        {
            var blueprint=Extract(sheet.Id.Value);
            var views=blueprint.Viewports.Select(s=>(View)document.GetElement(new ElementId(s.SourceViewId))).Select(v=>new{Id=v.UniqueId,Name=v.Name,Template=v.ViewTemplateId.Value,v.Scale,Crop=v.CropBoxActive,Transform=new[]{v.CropBox.Transform.Origin,v.CropBox.Transform.BasisX,v.CropBox.Transform.BasisY}.Select(p=>new[]{Round(p.X),Round(p.Y),Round(p.Z)}).ToArray(),Min=new[]{Round(v.CropBox.Min.X),Round(v.CropBox.Min.Y),Round(v.CropBox.Min.Z)},Max=new[]{Round(v.CropBox.Max.X),Round(v.CropBox.Max.Y),Round(v.CropBox.Max.Z)},Scope=v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP)?.AsElementId().Value,Primary=v.GetPrimaryViewId().Value}).ToArray();
            return Hash(new{sheet.UniqueId,sheet.SheetNumber,sheet.Name,blueprint.TitleBlockTypeId,blueprint.TitleBlockLocation,Slots=blueprint.Slots.Select(s=>new{s.SourceViewId,s.ViewportTypeId,X=Round(s.AbsoluteX),Y=Round(s.AbsoluteY),s.TitleOffset,s.TitleLineLength,s.Rotation,s.DetailNumber}),Parameters=blueprint.SheetParameterCopyPolicy.Select(p=>new{p.Owner,p.ParameterId,p.Value}),Views=views});
        }
        private static string Expected(DrawingPackageDefinition package,DrawingPlanRow row)=>Hash(new{row.SheetNumber,row.SheetName,row.ViewName,row.SourceViewId,row.Zone,Profile=package.Profile});
        public DrawingPlan Preview(DrawingPackageDefinition package)
        {
            package=Clone(package);
            var data=Load();var records=data.Records.Where(r=>r.DrawingPackageGuid==package.PackageGuid).ToDictionary(r=>r.Key);
            var sheets=new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToArray();
            var ownedIds=records.Values.Select(r=>r.SheetId).ToHashSet();
            var plan=DrawingPlanner.Generate(package,sheets.Where(s=>!ownedIds.Contains(s.Id.Value)).Select(s=>s.SheetNumber));plan.DocumentIdentity=Identity;
            var blueprint=package.Profile.Blueprint;
            if(blueprint.AutoLayout)
            {
                try
                {
                    var safe=AutoSheetLayoutService.SafeBounds(package.Profile);var slot=blueprint.Viewports.Single();
                    slot.AbsoluteX=(safe.MinX+safe.MaxX)/2+package.Profile.OffsetXmm/304.8;slot.AbsoluteY=(safe.MinY+safe.MaxY)/2+package.Profile.OffsetYmm/304.8;
                    slot.Bounds=new(slot.AbsoluteX-.001,slot.AbsoluteY-.001,slot.AbsoluteX+.001,slot.AbsoluteY+.001);
                    if(document.GetElement(new ElementId(blueprint.TitleBlockTypeId)) is not FamilySymbol symbol||symbol.Category.Id.Value!=(long)BuiltInCategory.OST_TitleBlocks||symbol.Family.Id.Value!=blueprint.TitleBlockFamilyId||symbol.UniqueId!=blueprint.TitleBlockTypeUniqueId)plan.Errors.Add("圖框類型已遺失，請重新載入。");
                }catch(Exception e){plan.Errors.Add(e.Message);}
            }
            if(!blueprint.AutoLayout&&document.GetElement(new ElementId(blueprint.SourceSheetId))?.UniqueId!=blueprint.SourceSheetUniqueId)plan.Errors.Add("樣板來源不屬於此模型或已遺失，請重新擷取。");
            if(blueprint.Viewports.Count!=1)plan.Errors.Add("第一版需一個主要平面視埠；共用 Legend／Schedule 可多個。");
            var layout=DrawingSheetQaService.Layout(0,"",blueprint.TitleBlockBounds,blueprint.Slots.ToArray());plan.Errors.AddRange(layout.Where(q=>q.Severity=="ERROR").Select(q=>q.Message));
            foreach(var parameter in blueprint.SheetParameterCopyPolicy.Where(p=>p.Selected))
                if(parameter.Policy!=DrawingCopyPolicy.UserSelectable&&parameter.Policy!=DrawingCopyPolicy.CopyDefault)plan.Errors.Add("禁止複製受保護參數："+parameter.Name);
            var names=new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().ToDictionary(v=>v.Id.Value,v=>v.Name);
            foreach(var row in plan.Rows)
            {
                row.TitleBlockName=blueprint.TitleBlockFamilyName+" / "+blueprint.TitleBlockTypeName;row.ProfileName=package.Profile.ProfileName;
                try
                {
                    if(row.Change==DrawingChange.Conflict)continue;
                    var source=document.GetElement(new ElementId(row.SourceViewId)) as ViewPlan??throw new ArgumentException("來源必須是平面視圖。");
                    row.SourceViewName=source.Name;
                    if(source.IsTemplate||source.GenLevel?.Id.Value!=row.Level.Id)throw new ArgumentException("來源視圖與樓層不符。");
                    foreach(var parameter in blueprint.SheetParameterCopyPolicy.Where(p=>p.Selected))
                    {
                        if(parameter.Owner=="View"&&package.Profile.ViewStrategy!=DrawingViewStrategy.Duplicate)throw new ArgumentException("視圖參數映射僅支援獨立複製視圖，以免修改使用者母視圖。");
                        if(parameter.Owner=="View"&&!Parameters(source,"View").Any(p=>p.ParameterId==parameter.ParameterId&&p.Policy==DrawingCopyPolicy.UserSelectable))throw new ArgumentException("來源視圖缺少可寫入映射參數："+parameter.Name);
                        if(string.IsNullOrWhiteSpace(parameter.Resolve(package,row))&&parameter.SemanticField!="")throw new ArgumentException("參數映射值未設定："+parameter.Name);
                    }
                    if(row.Zone.ScopeSource=="ScopeBox")
                    {var scope=document.GetElement(new ElementId(row.Zone.SourceId));if(scope?.UniqueId!=row.Zone.ZoneId)throw new ArgumentException("Scope Box 分區已遺失。");if(Box(scope.get_BoundingBox(null))!=row.Zone.Bounds)throw new ArgumentException("Scope Box 範圍已變更，請重新讀取。");}
                    else if(!row.Zone.IsUnzoned&&(row.Zone.ScopeSource!="GridRange"||!row.Zone.Bounds.Valid))throw new ArgumentException("不支援此分區來源。");
                    else if(row.Zone.GridIds.Length>0&&GridZone(row.Zone.ZoneName,row.Zone.GridIds,row.Zone.PaddingMm).Bounds!=row.Zone.Bounds)throw new ArgumentException("網格範圍已變更，請重新定義分區。");
                    var option=package.Profile.ViewStrategy==DrawingViewStrategy.Dependent?ViewDuplicateOption.AsDependent:ViewDuplicateOption.Duplicate;
                    if(package.Profile.ViewStrategy!=DrawingViewStrategy.Existing&&!source.CanViewBeDuplicated(option))throw new ArgumentException("來源不支援選定複製方式。");
                    if(package.Profile.ViewStrategy==DrawingViewStrategy.Dependent&&source.GetPrimaryViewId()!=ElementId.InvalidElementId)throw new ArgumentException("請選母視圖建立從屬視圖。");
                    var slot=blueprint.Viewports.Single();
                    if(slot.Scale<=0)throw new ArgumentException("比例無效。");
                    if(slot.ViewTemplateId!=-1&&!source.IsValidViewTemplate(new ElementId(slot.ViewTemplateId)))throw new ArgumentException("所選 View Template 不適用此視圖。");
                    var projected=Clone(slot);
                    double width=Math.Max(slot.Bounds.Width,row.Zone.IsUnzoned?source.Outline.Max.U-source.Outline.Min.U:row.Zone.Bounds.Width/slot.Scale),height=Math.Max(slot.Bounds.Height,row.Zone.IsUnzoned?source.Outline.Max.V-source.Outline.Min.V:row.Zone.Bounds.Height/slot.Scale);
                    projected.Bounds=new(slot.AbsoluteX-width/2,slot.AbsoluteY-height/2,slot.AbsoluteX+width/2,slot.AbsoluteY+height/2);
                    row.PreviewBounds=projected.Bounds;
                    var projectedIssues=DrawingSheetQaService.Layout(0,row.SheetNumber,blueprint.AutoLayout?AutoSheetLayoutService.SafeBounds(package.Profile):blueprint.TitleBlockBounds,new[]{projected}.Concat(blueprint.Legends.Where(s=>s.Reuse)).Concat(blueprint.Schedules.Where(s=>s.Reuse)).ToArray());
                    if(projectedIssues.Count>0)throw new ArgumentException("分區按比例投影的版面衝突："+string.Join("；",projectedIssues.Select(q=>q.Message)));
                    if(source.ViewType.ToString()!=slot.ExpectedViewKind)throw new ArgumentException("來源視圖種類與樣板不符。");
                    if(package.Profile.ViewStrategy==DrawingViewStrategy.Existing)
                    {
                        row.ViewName=source.Name;
                        if(source.Scale!=slot.Scale||source.ViewTemplateId.Value!=slot.ViewTemplateId||(!row.Zone.IsUnzoned&&!source.CropBoxActive))throw new ArgumentException("現有視圖的比例／樣板／Crop 必須已符合樣板；工具不改寫此視圖。");
                        if(row.Zone.ScopeSource=="ScopeBox"&&source.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP)?.AsElementId().Value!=row.Zone.SourceId)throw new ArgumentException("現有視圖 Scope Box 與分區不符。");
                        if(row.Zone.ScopeSource=="GridRange")
                        {
                            var crop=source.CropBox;var min=crop.Transform.OfPoint(crop.Min);var max=crop.Transform.OfPoint(crop.Max);
                            if(Math.Abs(min.X-row.Zone.Bounds.MinX)>VerificationTolerance||Math.Abs(min.Y-row.Zone.Bounds.MinY)>VerificationTolerance||Math.Abs(max.X-row.Zone.Bounds.MaxX)>VerificationTolerance||Math.Abs(max.Y-row.Zone.Bounds.MaxY)>VerificationTolerance)throw new ArgumentException("現有視圖 Crop 與分區不符。");
                        }
                    }
                    if(package.Profile.ViewStrategy==DrawingViewStrategy.Dependent&&(source.Scale!=slot.Scale||source.ViewTemplateId.Value!=slot.ViewTemplateId))throw new ArgumentException("從屬視圖的母視圖比例／樣板須先符合；工具不改母視圖。");
                    if(records.TryGetValue(row.Key,out var record))
                    {
                        row.ExistingSheetId=record.SheetId;
                        var sheet=document.GetElement(new ElementId(record.SheetId)) as ViewSheet;
                        if(sheet==null||sheet.UniqueId!=record.SheetUniqueId){row.Change=DrawingChange.Missing;row.Issues.Add("工具建立圖紙已遺失。");continue;}
                        var actualLayout=Extract(sheet.Id.Value);
                        if(sheet.SheetNumber!=row.SheetNumber)row.Differences.Add("圖號："+sheet.SheetNumber+" → "+row.SheetNumber);
                        if(sheet.Name!=row.SheetName)row.Differences.Add("圖名："+sheet.Name+" → "+row.SheetName);
                        if(actualLayout.Viewports.Count==1)
                        {
                            var a=actualLayout.Viewports[0];double mm=Math.Sqrt(Math.Pow(a.AbsoluteX-slot.AbsoluteX,2)+Math.Pow(a.AbsoluteY-slot.AbsoluteY,2))*304.8;
                            if(mm>VerificationTolerance*304.8)row.Differences.Add($"主要視埠位移 {mm:0.###} mm");
                            if(a.Scale!=slot.Scale)row.Differences.Add($"比例 1:{a.Scale} → 1:{slot.Scale}");
                            if(a.ViewportTypeId!=slot.ViewportTypeId)row.Differences.Add("Viewport Type 變更");
                            if(a.ViewTemplateId!=slot.ViewTemplateId)row.Differences.Add("View Template 變更");
                        }
                        if(Actual(sheet)!=record.Baseline){row.Change=DrawingChange.ManualOverride;row.Issues.Add("MANUAL_OVERRIDE：保留人工修改，需另行複核。");}
                        else row.Change=Expected(package,row)==record.Expected?DrawingChange.Unchanged:DrawingChange.Update;
                        if(record.TemplateProfileGuid!=package.Profile.ProfileGuid||record.TemplateProfileVersion!=package.Profile.ProfileVersion)row.Issues.Add("TEMPLATE_OUTDATED：此更新需明確確認。");
                        if(row.Change==DrawingChange.ManualOverride&&(!package.PreserveManualChanges||package.ApplyTemplateKeys.Contains(row.Key)))row.Change=DrawingChange.Update;
                        if(row.Change==DrawingChange.Update)
                        {
                            if(record.ViewStrategy!=package.Profile.ViewStrategy)throw new ArgumentException("既有圖紙不可直接切換視圖建立策略。");
                            if(record.ViewStrategy==DrawingViewStrategy.Existing&&record.ViewId!=row.SourceViewId)throw new ArgumentException("更新不可替換原本的現有視圖。");
                            var current=Extract(sheet.Id.Value);
                            if(current.TitleBlockTypeId!=blueprint.TitleBlockTypeId||current.Viewports.Count!=1||!current.Legends.Select(s=>s.SourceViewId).SequenceEqual(blueprint.Legends.Where(s=>s.Reuse).Select(s=>s.SourceViewId))||!current.Schedules.Select(s=>s.SourceViewId).SequenceEqual(blueprint.Schedules.Where(s=>s.Reuse).Select(s=>s.SourceViewId)))
                                throw new ArgumentException("更新內容拓樸或圖框 Type 不符；不會刪除／替換既有內容。");
                            var existingView=(View)document.GetElement(new ElementId(record.ViewId));
                            if(package.Profile.ViewStrategy==DrawingViewStrategy.Dependent&&existingView.GetPrimaryViewId().Value!=row.SourceViewId)throw new ArgumentException("不能變更既有從屬視圖的母視圖。");
                            if(names.Any(n=>n.Key!=record.ViewId&&string.Equals(n.Value,row.ViewName,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("更新視圖名稱與既有視圖衝突。");
                        }
                    }
                    else if(package.Profile.ViewStrategy==DrawingViewStrategy.Existing)
                    {if(!Viewport.CanAddViewToSheet(document,new ElementId(blueprint.SourceSheetId),source.Id))throw new ArgumentException("現有模型視圖已放置或不可上圖紙。");}
                    else if(names.Values.Contains(row.ViewName,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("視圖名稱已使用。");
                }
                catch(Exception e){row.Change=DrawingChange.Conflict;row.Issues.Add(e.Message);}
            }
            var desiredNumbers=plan.Rows.Where(r=>r.Change is DrawingChange.Add or DrawingChange.Update).Select(r=>r.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(package.Profile.ViewStrategy==DrawingViewStrategy.Existing)
                foreach(var group in plan.Rows.GroupBy(r=>r.SourceViewId).Where(g=>g.Count()>1))foreach(var row in group){row.Change=DrawingChange.Conflict;row.Issues.Add("同一模型視圖不可配置至多張圖紙。");}
            var changingIds=plan.Rows.Where(r=>r.Change==DrawingChange.Update).Select(r=>r.ExistingSheetId).ToHashSet();
            foreach(var sheet in sheets.Where(s=>!changingIds.Contains(s.Id.Value)&&desiredNumbers.Contains(s.SheetNumber)))
                foreach(var row in plan.Rows.Where(r=>string.Equals(r.SheetNumber,sheet.SheetNumber,StringComparison.OrdinalIgnoreCase)&&r.ExistingSheetId!=sheet.Id.Value)){row.Change=DrawingChange.Conflict;row.Issues.Add("圖號與未更新圖紙衝突。");}
            plan.Signature=Signature(plan);return plan;
        }
        private string Signature(DrawingPlan plan)=>Hash(new{Identity,Package=plan.Package,Rows=plan.Rows,Errors=plan.Errors,Source=SourceFingerprint(plan.Package.Profile.Blueprint),Sheets=Sheets(),Sources=plan.Rows.Select(r=>r.SourceViewId).Distinct().OrderBy(x=>x).Select(id=>{var v=document.GetElement(new ElementId(id)) as View;return new{Id=id,Version=v?.VersionGuid};}),Zones=plan.Package.Zones.Select(z=>new{z.SourceId,Version=document.GetElement(new ElementId(z.SourceId))?.VersionGuid}),Storage=Load()});
        private object SourceFingerprint(SheetTemplateBlueprint blueprint)=>blueprint.AutoLayout?new{blueprint.TitleBlockTypeId,Version=document.GetElement(new ElementId(blueprint.TitleBlockTypeId))?.VersionGuid,FamilyVersion=document.GetElement(new ElementId(blueprint.TitleBlockFamilyId))?.VersionGuid}:Extract(blueprint.SourceSheetId);
        public long[] Apply(DrawingPlan preview,bool confirmed,Action? afterWrite=null)
        {
            if(!confirmed||!preview.CanApply)throw new InvalidOperationException("請先完成有效預覽並確認。");
            if(preview.DocumentIdentity!=Identity)throw new InvalidOperationException("文件已切換。");
            var fresh=Preview(preview.Package);if(!fresh.CanApply||fresh.Signature!=preview.Signature)throw new InvalidOperationException("模型或計畫已變更，請重新預覽。");
            preview=fresh;var data=Load();var ids=new List<long>();var blueprint=preview.Package.Profile.Blueprint;
            using var group=new TransactionGroup(document,"施工圖生產中心");group.Start();
            try
            {
                using(var renumber=new Transaction(document,"釋放更新圖號"))
                {
                    renumber.Start();
                    foreach(var row in fresh.Rows.Where(r=>r.Change==DrawingChange.Update))((ViewSheet)document.GetElement(new ElementId(row.ExistingSheetId))).SheetNumber="_DRAWING_"+Guid.NewGuid().ToString("N");
                    if(renumber.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("圖號第一階段失敗。");
                }
                foreach(var row in fresh.Rows)
                {
                    if(row.Change==DrawingChange.Update)
                    {
                        using var update=new Transaction(document,"更新圖紙 "+row.SheetNumber);update.Start();
                        var sheet=(ViewSheet)document.GetElement(new ElementId(row.ExistingSheetId));
                        var record=data.Records.Single(r=>r.DrawingPackageGuid==preview.Package.PackageGuid&&r.Key==row.Key);
                        sheet.SheetNumber=row.SheetNumber;sheet.Name=row.SheetName;
                        var view=(View)document.GetElement(new ElementId(record.ViewId));if(record.ViewStrategy!=DrawingViewStrategy.Existing)view.Name=row.ViewName;
                        var main=blueprint.Viewports.Single();
                        if(preview.Package.Profile.ViewStrategy==DrawingViewStrategy.Duplicate){view.ViewTemplateId=new ElementId(main.ViewTemplateId);if(!main.ScaleControlled)view.Scale=main.Scale;}
                        if(record.ViewStrategy!=DrawingViewStrategy.Existing)ApplyZone(view,row.Zone);
                        var title=TitleBlock(sheet);ElementTransformUtils.MoveElement(document,title.Id,Xyz(blueprint.TitleBlockLocation)-((LocationPoint)title.Location).Point);
                        var current=Extract(sheet.Id.Value);
                        foreach(var pair in current.Viewports.Concat(current.Legends).Zip(blueprint.Viewports.Concat(blueprint.Legends.Where(s=>s.Reuse))))
                        {
                            var viewport=(Viewport)document.GetElement(new ElementId(pair.First.SourceViewportId));var slot=pair.Second;
                            viewport.ChangeTypeId(new ElementId(slot.ViewportTypeId));viewport.Rotation=(ViewportRotation)slot.Rotation;
                            viewport.LabelOffset=Xyz(slot.TitleOffset);viewport.LabelLineLength=slot.TitleLineLength;
                            viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER).Set(slot.DetailNumber);document.Regenerate();viewport.SetBoxCenter(new XYZ(slot.AbsoluteX,slot.AbsoluteY,0));
                        }
                        foreach(var pair in current.Schedules.Zip(blueprint.Schedules.Where(s=>s.Reuse)))((ScheduleSheetInstance)document.GetElement(new ElementId(pair.First.SourceViewportId))).Point=new XYZ(pair.Second.AbsoluteX,pair.Second.AbsoluteY,0);
                        WriteParameters(sheet,title,view,preview.Package,row);
                        document.Regenerate();Verify(sheet,row,preview.Package);record.Baseline=Actual(sheet);record.Expected=Expected(preview.Package,row);record.TemplateProfileGuid=preview.Package.Profile.ProfileGuid;record.TemplateProfileVersion=preview.Package.Profile.ProfileVersion;
                        if(update.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("更新提交失敗："+row.SheetNumber);
                        Verify(sheet,row,preview.Package);ids.Add(sheet.Id.Value);continue;
                    }
                    if(row.Change!=DrawingChange.Add){ids.Add(row.ExistingSheetId);continue;}
                    try
                    {
                        using var transaction=new Transaction(document,"建立圖紙 "+row.SheetNumber);transaction.Start();
                        var sheet=ViewSheet.Create(document,new ElementId(blueprint.TitleBlockTypeId));sheet.SheetNumber=row.SheetNumber;sheet.Name=row.SheetName;
                        var title=TitleBlock(sheet);var point=(LocationPoint)title.Location;ElementTransformUtils.MoveElement(document,title.Id,Xyz(blueprint.TitleBlockLocation)-point.Point);
                        var source=(ViewPlan)document.GetElement(new ElementId(row.SourceViewId));
                        var view=preview.Package.Profile.ViewStrategy==DrawingViewStrategy.Existing?source:(View)document.GetElement(source.Duplicate(preview.Package.Profile.ViewStrategy==DrawingViewStrategy.Dependent?ViewDuplicateOption.AsDependent:ViewDuplicateOption.Duplicate));
                        if(preview.Package.Profile.ViewStrategy!=DrawingViewStrategy.Existing)view.Name=row.ViewName;
                        if(preview.Package.Profile.ProfileKind==DrawingProfileKind.Fixture){DrawingFixtureIsolation.Mark(sheet);DrawingFixtureIsolation.Mark(view);DrawingFixtureIsolation.Mark(title);}
                        var main=blueprint.Viewports.Single();
                        if(preview.Package.Profile.ViewStrategy==DrawingViewStrategy.Duplicate){view.ViewTemplateId=new ElementId(main.ViewTemplateId);if(!main.ScaleControlled)view.Scale=main.Scale;}
                        if(preview.Package.Profile.ViewStrategy!=DrawingViewStrategy.Existing)ApplyZone(view,row.Zone);
                        WriteParameters(sheet,title,view,preview.Package,row);
                        Place(sheet,view,main);
                        foreach(var legend in blueprint.Legends.Where(s=>s.Reuse))Place(sheet,(View)document.GetElement(new ElementId(legend.SourceViewId)),legend);
                        foreach(var schedule in blueprint.Schedules.Where(s=>s.Reuse))ScheduleSheetInstance.Create(document,sheet.Id,new ElementId(schedule.SourceViewId),new XYZ(schedule.AbsoluteX,schedule.AbsoluteY,0));
                        document.Regenerate();afterWrite?.Invoke();Verify(sheet,row,preview.Package);
                        var record=new DrawingSheetRecord{DrawingPackageGuid=preview.Package.PackageGuid,TemplateProfileGuid=preview.Package.Profile.ProfileGuid,TemplateProfileVersion=preview.Package.Profile.ProfileVersion,Key=row.Key,SheetId=sheet.Id.Value,SheetUniqueId=sheet.UniqueId,ViewId=view.Id.Value,ViewStrategy=preview.Package.Profile.ViewStrategy,Baseline=Actual(sheet),Expected=Expected(preview.Package,row)};
                        data.Records.Add(record);ids.Add(sheet.Id.Value);
                        if(transaction.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Transaction 未提交。");
                        Verify(sheet,row,preview.Package);
                    }
                    catch(Exception e){throw new InvalidOperationException("圖紙 "+row.SheetNumber+"／建立或 read-back 失敗："+e.Message,e);}
                }
                using(var transaction=new Transaction(document,"保存施工圖追溯資料"))
                {
                    transaction.Start();data.Packages.RemoveAll(p=>p.PackageGuid==preview.Package.PackageGuid);data.Packages.Add(Clone(preview.Package));Store(data);
                    if(Hash(Load())!=Hash(data))throw new InvalidOperationException("Package read-back 失敗。");
                    if(transaction.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Package 提交失敗。");
                }
                if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("整批提交失敗。");return ids.ToArray();
            }
            catch { if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw; }
        }
        private static void WriteParameters(ViewSheet sheet,FamilyInstance title,View view,DrawingPackageDefinition package,DrawingPlanRow row)
        {
            foreach(var parameter in package.Profile.Blueprint.SheetParameterCopyPolicy.Where(p=>p.Selected))
            {
                if(parameter.Policy!=DrawingCopyPolicy.UserSelectable&&parameter.Policy!=DrawingCopyPolicy.CopyDefault)throw new InvalidOperationException("禁止複製受保護參數。");
                Element target=parameter.Owner switch{"Sheet"=>sheet,"TitleBlock"=>title,"View"=>view,_=>throw new InvalidOperationException("未知參數來源。")};
                var allowed=Parameters(target,parameter.Owner).SingleOrDefault(p=>p.ParameterId==parameter.ParameterId);
                if(allowed?.Policy!=DrawingCopyPolicy.UserSelectable)throw new InvalidOperationException("參數不存在或不允許寫入："+parameter.Name);
                var p=target.Parameters.Cast<Parameter>().Single(p=>p.Id.Value==parameter.ParameterId);
                if(!p.Set(parameter.Resolve(package,row)))throw new InvalidOperationException("參數無法寫入："+parameter.Name);
            }
        }
        private void Place(ViewSheet sheet,View view,SheetLayoutSlot slot)
        {
            if(!Viewport.CanAddViewToSheet(document,sheet.Id,view.Id))throw new InvalidOperationException("視圖不可放置於圖紙："+view.Name);
            var viewport=Viewport.Create(document,sheet.Id,view.Id,new XYZ(slot.AbsoluteX,slot.AbsoluteY,0));viewport.ChangeTypeId(new ElementId(slot.ViewportTypeId));viewport.Rotation=(ViewportRotation)slot.Rotation;
            viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER).Set(slot.DetailNumber);
            viewport.LabelOffset=Xyz(slot.TitleOffset);viewport.LabelLineLength=slot.TitleLineLength;document.Regenerate();viewport.SetBoxCenter(new XYZ(slot.AbsoluteX,slot.AbsoluteY,0));
        }
        private void ApplyZone(View view,DrawingZone zone)
        {
            if(zone.IsUnzoned)return;
            var scope=view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if(scope==null||scope.IsReadOnly)throw new InvalidOperationException("Scope／Crop 被樣板控制，請先確認來源設定。");
            if(!scope.Set(zone.ScopeSource=="ScopeBox"?new ElementId(zone.SourceId):ElementId.InvalidElementId))throw new InvalidOperationException("Scope 設定失敗。");
            view.CropBoxActive=true;
            if(zone.ScopeSource=="GridRange")
            {
                var crop=view.CropBox;
                if(!crop.Transform.BasisX.IsAlmostEqualTo(XYZ.BasisX)||!crop.Transform.BasisY.IsAlmostEqualTo(XYZ.BasisY))throw new InvalidOperationException("網格分區只支援未旋轉平面。");
                var inverse=crop.Transform.Inverse;
                var min=inverse.OfPoint(new XYZ(zone.Bounds.MinX,zone.Bounds.MinY,0));var max=inverse.OfPoint(new XYZ(zone.Bounds.MaxX,zone.Bounds.MaxY,0));
                crop.Min=new XYZ(min.X,min.Y,crop.Min.Z);crop.Max=new XYZ(max.X,max.Y,crop.Max.Z);view.CropBox=crop;
            }
        }
        private void Verify(ViewSheet sheet,DrawingPlanRow row,DrawingPackageDefinition package)
        {
            var actual=Extract(sheet.Id.Value);var expected=package.Profile.Blueprint;
            void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("read-back："+message);}
            Require(sheet.SheetNumber==row.SheetNumber&&sheet.Name==row.SheetName,"圖號／圖名不符。");
            Require(actual.TitleBlockTypeId==expected.TitleBlockTypeId,"圖框 Type 不符。");
            Require(actual.Viewports.Count==1&&actual.Legends.Count==expected.Legends.Count(s=>s.Reuse)&&actual.Schedules.Count==expected.Schedules.Count(s=>s.Reuse),"視埠／共用內容數量不符。");
            foreach(var pair in actual.Viewports.Concat(actual.Legends).Zip(expected.Viewports.Concat(expected.Legends.Where(s=>s.Reuse))))
            {
                var a=pair.First;var b=pair.Second;
                Require(a.ViewportTypeId==b.ViewportTypeId&&a.Scale==b.Scale&&a.ViewTemplateId==b.ViewTemplateId,"視埠 Type／比例／樣板不符。");
                Require(Math.Abs(a.AbsoluteX-b.AbsoluteX)<VerificationTolerance&&Math.Abs(a.AbsoluteY-b.AbsoluteY)<VerificationTolerance,"視埠中心不符。");
                Require(a.DetailNumber==b.DetailNumber&&a.Rotation==b.Rotation&&Math.Abs(a.TitleOffset.X-b.TitleOffset.X)<VerificationTolerance&&Math.Abs(a.TitleOffset.Y-b.TitleOffset.Y)<VerificationTolerance&&Math.Abs(a.TitleLineLength-b.TitleLineLength)<VerificationTolerance,"視埠標題／詳圖號不符。");
            }
            foreach(var pair in actual.Schedules.Zip(expected.Schedules.Where(s=>s.Reuse)))Require(pair.First.SourceViewId==pair.Second.SourceViewId&&Math.Abs(pair.First.AbsoluteX-pair.Second.AbsoluteX)<VerificationTolerance&&Math.Abs(pair.First.AbsoluteY-pair.Second.AbsoluteY)<VerificationTolerance,"Schedule 位置不符。");
            var view=(View)document.GetElement(new ElementId(actual.Viewports.Single().SourceViewId));
            Require(view.GenLevel?.Id.Value==row.Level.Id,"視圖樓層不符。");
            if(!row.Zone.IsUnzoned)Require(view.CropBoxActive,"Crop 未啟用。");
            if(row.Zone.ScopeSource=="ScopeBox")Require(view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP)?.AsElementId().Value==row.Zone.SourceId,"Scope 不符。");
            else if(!row.Zone.IsUnzoned) {var crop=view.CropBox;var min=crop.Transform.OfPoint(crop.Min);var max=crop.Transform.OfPoint(crop.Max);Require(Math.Abs(min.X-row.Zone.Bounds.MinX)<VerificationTolerance&&Math.Abs(min.Y-row.Zone.Bounds.MinY)<VerificationTolerance&&Math.Abs(max.X-row.Zone.Bounds.MaxX)<VerificationTolerance&&Math.Abs(max.Y-row.Zone.Bounds.MaxY)<VerificationTolerance,"Crop 範圍不符。");}
            if(package.Profile.ViewStrategy==DrawingViewStrategy.Dependent)Require(view.GetPrimaryViewId().Value==row.SourceViewId,"母視圖不符。");
            foreach(var parameter in expected.SheetParameterCopyPolicy.Where(p=>p.Selected))Require(actual.SheetParameterCopyPolicy.Any(p=>p.Owner==parameter.Owner&&p.ParameterId==parameter.ParameterId&&p.Value==parameter.Resolve(package,row)),"參數不符："+parameter.Name);
            var errors=DrawingSheetQaService.Layout(sheet.Id.Value,sheet.SheetNumber,(expected.AutoLayout?AutoSheetLayoutService.SafeBounds(package.Profile):actual.TitleBlockBounds),actual.Slots.ToArray()).Where(q=>q.Severity=="ERROR").ToArray();
            Require(errors.Length==0,string.Join("；",errors.Select(e=>e.Message)));
        }
        public DrawingSheetStatus[] SheetStatuses(Guid packageId,DrawingQaIssue[] issues)
        {
            var data=Load();var package=data.Packages.SingleOrDefault(p=>p.PackageGuid==packageId);if(package==null)return Array.Empty<DrawingSheetStatus>();
            var rows=DrawingPlanner.Generate(package,Array.Empty<string>()).Rows.ToDictionary(r=>r.Key);var bySheet=issues.ToLookup(i=>i.SheetId);
            return data.Records.Where(r=>r.DrawingPackageGuid==packageId).Select(r=>
            {
                var sheet=document.GetElement(new ElementId(r.SheetId)) as ViewSheet;rows.TryGetValue(r.Key,out var row);var found=bySheet[r.SheetId];
                return new DrawingSheetStatus(r.SheetId,sheet?.SheetNumber??"遺失",sheet?.Name??"",row?.Level.Name??"",row?.Zone.ZoneName??"",package.DrawingType,sheet==null||found.Any(i=>i.Severity=="ERROR")?"Draft":found.Any(i=>i.Severity=="WARNING")?"NeedsReview":"Ready");
            }).OrderBy(s=>s.SheetNumber,StringComparer.Ordinal).ToArray();
        }
        public DrawingQaIssue[] Qa(Guid packageId)
        {
            var data=Load();var issues=new List<DrawingQaIssue>();
            var package=data.Packages.SingleOrDefault(p=>p.PackageGuid==packageId);
            if(package==null)return new[]{new DrawingQaIssue(0,"","PACKAGE_NOT_CREATED","INFO","此出圖包尚未建立。")};
            var planned=DrawingPlanner.Generate(package,Array.Empty<string>()).Rows.ToDictionary(r=>r.Key);
            foreach(var record in data.Records.Where(r=>r.DrawingPackageGuid==packageId))
            {
                try
                {
                    var sheet=document.GetElement(new ElementId(record.SheetId)) as ViewSheet??throw new InvalidOperationException("圖紙遺失。");
                    var blueprint=Extract(record.SheetId);issues.AddRange(DrawingSheetQaService.Layout(record.SheetId,sheet.SheetNumber,blueprint.TitleBlockBounds,blueprint.Slots.ToArray()));
                    var expected=package.Profile.Blueprint;
                    if(expected.AutoLayout)issues.AddRange(DrawingSheetQaService.Layout(record.SheetId,sheet.SheetNumber,AutoSheetLayoutService.SafeBounds(package.Profile),blueprint.Viewports));
                    void Qa(bool condition,string code,string severity,string message){if(!condition)issues.Add(new(record.SheetId,sheet.SheetNumber,code,severity,message));}
                    Qa(!string.IsNullOrWhiteSpace(sheet.Name),"MISSING_SHEET_NAME","ERROR","圖名為空。");
                    Qa(blueprint.TitleBlockTypeId==expected.TitleBlockTypeId,"WRONG_TITLEBLOCK","ERROR","圖框 Type 與出圖包不符。");
                    Qa(blueprint.Viewports.Count==1,"MISSING_MAIN_VIEW","ERROR","主要視圖數量不符。");
                    if(blueprint.Viewports.Count==1&&expected.Viewports.Count==1)
                    {
                        var actual=blueprint.Viewports[0];var desired=expected.Viewports[0];
                        Qa(actual.ViewTemplateId==desired.ViewTemplateId,"WRONG_VIEW_TEMPLATE","WARNING","View Template 與樣板不同。");
                        Qa(actual.Scale==desired.Scale,"WRONG_VIEW_SCALE","WARNING","視圖比例與樣板不同。");
                        Qa(actual.ViewportTypeId==desired.ViewportTypeId,"WRONG_VIEWPORT_TYPE","WARNING","視埠 Type 與樣板不同。");
                        var view=(View)document.GetElement(new ElementId(actual.SourceViewId));Qa(planned.TryGetValue(record.Key,out var zoneRow)&&zoneRow.Zone.IsUnzoned||view.CropBoxActive,"CROP_DISABLED","ERROR","主要視圖未啟用 Crop。");
                        Qa(actual.SourceViewId==record.ViewId,"WRONG_MAIN_VIEW","ERROR","主要視圖與工具紀錄不符。");
                        planned.TryGetValue(record.Key,out var row);
                        Qa(row!=null,"MISSING_LEVEL_ZONE","ERROR","此圖紙的樓層／分區已不在計畫。");
                        if(row!=null&&!row.Zone.IsUnzoned)
                        {
                            var crop=view.CropBox;var min=crop.Transform.OfPoint(crop.Min);var max=crop.Transform.OfPoint(crop.Max);
                            Qa(max.X-min.X<=row.Zone.Bounds.Width+VerificationTolerance&&max.Y-min.Y<=row.Zone.Bounds.Height+VerificationTolerance,"CROP_TOO_LARGE","WARNING","Crop 大於計畫分區範圍。");
                        }
                    }
                    if(Actual(sheet)!=record.Baseline)issues.Add(new(record.SheetId,sheet.SheetNumber,"MANUAL_OVERRIDE","WARNING","人工修改，需複核。"));
                    if(data.Profiles.Any(p=>p.ProfileGuid==record.TemplateProfileGuid&&p.ProfileVersion!=record.TemplateProfileVersion))issues.Add(new(record.SheetId,sheet.SheetNumber,"TEMPLATE_OUTDATED","WARNING","保存樣板已改版，未自動套用。"));
                }catch(Exception e){issues.Add(new(record.SheetId,"","MISSING","ERROR",e.Message));}
            }
            return issues.ToArray();
        }
    }
}
#endif
