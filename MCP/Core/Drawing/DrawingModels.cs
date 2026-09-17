#if REVIT2026 || DRAWING_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace RevitMCP.Core.Drawing
{
    public enum DrawingViewStrategy { Existing, Duplicate, Dependent }
    public enum DrawingChange { Add, Update, Unchanged, ManualOverride, Conflict, Missing }
    public enum DrawingCopyPolicy { NeverCopy, CopyDefault, UserSelectable, Generated }
    public sealed record DrawingPoint(double X, double Y);
    public sealed record DrawingBounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public bool Valid => new[] { MinX, MinY, MaxX, MaxY }.All(double.IsFinite) && Width > 0 && Height > 0;
        public bool Contains(DrawingBounds b) => b.MinX >= MinX && b.MinY >= MinY && b.MaxX <= MaxX && b.MaxY <= MaxY;
        public bool Overlaps(DrawingBounds b) => Math.Min(MaxX,b.MaxX)>Math.Max(MinX,b.MinX) && Math.Min(MaxY,b.MaxY)>Math.Max(MinY,b.MinY);
    }
    public sealed record DrawingChoice(long Id, string Name, double Elevation = 0)
    { public override string ToString() => Name; }
    public sealed class DrawingParameter
    {
        public long ParameterId { get; set; }
        public string Name { get; set; } = "";
        public string Owner { get; set; } = "Sheet";
        public string Value { get; set; } = "";
        public DrawingCopyPolicy Policy { get; set; }
        public bool Selected { get; set; }
        public string SemanticField { get; set; } = "";
        public string Resolve(DrawingPackageDefinition package,DrawingPlanRow row) => SemanticField switch
        {
            ""=>Value,"Level"=>row.Level.Name,"Zone"=>row.Zone.ZoneName,"DrawingType"=>package.DrawingType,
            "Discipline"=>package.Discipline,"Phase"=>package.Phase,_=>throw new ArgumentException("未知參數映射："+SemanticField)
        };
    }
    public sealed class SheetLayoutSlot
    {
        public string SlotId { get; set; } = "";
        public string Role { get; set; } = "MAIN_PLAN";
        public long SourceViewportId { get; set; }
        public long SourceViewId { get; set; }
        public string ExpectedViewKind { get; set; } = "";
        public string SourceViewName { get; set; } = "";
        public long ViewportTypeId { get; set; }
        public long ViewTemplateId { get; set; }
        public int Scale { get; set; }
        public bool ScaleControlled { get; set; }
        public double AbsoluteX { get; set; }
        public double AbsoluteY { get; set; }
        public double NormalizedX { get; set; }
        public double NormalizedY { get; set; }
        public DrawingBounds Bounds { get; set; } = new(0,0,1,1);
        public DrawingPoint TitleOffset { get; set; } = new(0,0);
        public double TitleLineLength { get; set; }
        public int Rotation { get; set; }
        public string DetailNumber { get; set; } = "";
        public int Priority { get; set; }
        public bool Reuse { get; set; } = true;
    }
    public sealed class SheetTemplateBlueprint
    {
        public long SourceSheetId { get; set; }
        public string SourceSheetUniqueId { get; set; } = "";
        public string SourceSheetNumber { get; set; } = "";
        public string SourceSheetName { get; set; } = "";
        public string SourceDocumentIdentity { get; set; } = "";
        public long TitleBlockFamilyId { get; set; }
        public long TitleBlockTypeId { get; set; }
        public string TitleBlockFamilyName { get; set; } = "";
        public string TitleBlockTypeName { get; set; } = "";
        public DrawingBounds TitleBlockBounds { get; set; } = new(0,0,1,1);
        public DrawingBounds? SheetBounds { get; set; }
        public DrawingPoint TitleBlockLocation { get; set; } = new(0,0);
        public List<SheetLayoutSlot> Viewports { get; set; } = new();
        public List<SheetLayoutSlot> Legends { get; set; } = new();
        public List<SheetLayoutSlot> Schedules { get; set; } = new();
        public List<long> SheetAnnotations { get; set; } = new();
        public List<DrawingParameter> SheetParameterCopyPolicy { get; set; } = new();
        public string GuideGrid { get; set; } = "未支援";
        public int TemplateVersion { get; set; } = 1;
        public List<string> Warnings { get; set; } = new();
        public IEnumerable<SheetLayoutSlot> Slots => Viewports.Concat(Legends.Where(s=>s.Reuse)).Concat(Schedules.Where(s=>s.Reuse));
    }
    public sealed class DrawingTemplateProfile
    {
        public Guid ProfileGuid { get; set; } = Guid.NewGuid();
        public string ProfileName { get; set; } = "";
        public int ProfileVersion { get; set; } = 1;
        public SheetTemplateBlueprint Blueprint { get; set; } = new();
        public string NumberingRule { get; set; } = "";
        public string NamingRule { get; set; } = "{Level} {Zone} {DrawingType}";
        public string ViewNamingRule { get; set; } = "{Level}-{Zone}-{DrawingType}";
        public DrawingViewStrategy ViewStrategy { get; set; } = DrawingViewStrategy.Dependent;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public override string ToString() => ProfileName + "（V" + ProfileVersion + "）";
    }
    public sealed class DrawingZone
    {
        public string ZoneId { get; set; } = "";
        public string ZoneName { get; set; } = "";
        public string ScopeSource { get; set; } = "ScopeBox";
        public long SourceId { get; set; }
        public long[] GridIds { get; set; } = Array.Empty<long>();
        public double PaddingMm { get; set; }
        public DrawingBounds Bounds { get; set; } = new(0,0,1,1);
        public override string ToString() => ZoneName;
    }
    public sealed class DrawingPackageDefinition
    {
        public Guid PackageGuid { get; set; } = Guid.NewGuid();
        public string PackageName { get; set; } = "";
        public string Discipline { get; set; } = "";
        public string DrawingType { get; set; } = "";
        public string Phase { get; set; } = "";
        public DrawingTemplateProfile Profile { get; set; } = new();
        public List<DrawingChoice> Levels { get; set; } = new();
        public List<DrawingZone> Zones { get; set; } = new();
        public Dictionary<long,long> SourceViewsByLevel { get; set; } = new();
        public Dictionary<string,long> SourceViewsByKey { get; set; } = new();
        public HashSet<string> ReservedNumbers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludedKeys { get; set; } = new();
        public Dictionary<string,string> NumberOverrides { get; set; } = new();
        public Dictionary<string,string> NameOverrides { get; set; } = new();
        public bool PreserveManualChanges { get; set; } = true;
        public HashSet<string> ApplyTemplateKeys { get; set; } = new();
        public override string ToString() => PackageName;
    }
    public sealed class DrawingSheetRecord
    {
        public Guid DrawingPackageGuid { get; set; }
        public Guid GeneratedSheetGuid { get; set; } = Guid.NewGuid();
        public Guid TemplateProfileGuid { get; set; }
        public int TemplateProfileVersion { get; set; }
        public string Key { get; set; } = "";
        public long SheetId { get; set; }
        public string SheetUniqueId { get; set; } = "";
        public long ViewId { get; set; }
        public DrawingViewStrategy ViewStrategy { get; set; }
        public string Baseline { get; set; } = "";
        public string Expected { get; set; } = "";
    }
    public sealed class DrawingProjectData
    {
        public int SchemaVersion { get; set; } = 1;
        public List<DrawingTemplateProfile> Profiles { get; set; } = new();
        public List<DrawingPackageDefinition> Packages { get; set; } = new();
        public List<DrawingSheetRecord> Records { get; set; } = new();
    }
    public sealed class DrawingPlanRow
    {
        public string Key { get; set; } = "";
        public string SheetNumber { get; set; } = "";
        public string SheetName { get; set; } = "";
        public string ViewName { get; set; } = "";
        public DrawingChoice Level { get; set; } = new(0, "");
        public DrawingZone Zone { get; set; } = new();
        public long SourceViewId { get; set; }
        public long ExistingSheetId { get; set; }
        public DrawingChange Change { get; set; }
        public List<string> Issues { get; set; } = new();
        public string Status => Change switch { DrawingChange.Add=>"新增", DrawingChange.Update=>"更新", DrawingChange.Unchanged=>"無變更", DrawingChange.ManualOverride=>"人工修改", DrawingChange.Missing=>"元素遺失", _=>"衝突" };
        public string IssueText => string.Join("；",Issues);
        public List<string> Differences { get; set; } = new();
        public string DifferenceText => string.Join("；",Differences);
    }
    public sealed class DrawingPlan
    {
        public DrawingPackageDefinition Package { get; set; } = new();
        public List<DrawingPlanRow> Rows { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public string DocumentIdentity { get; set; } = "";
        public string Signature { get; set; } = "";
        public bool CanApply => Rows.Count > 0 && Errors.Count == 0 && Rows.All(r=>r.Change!=DrawingChange.Conflict && r.Change!=DrawingChange.Missing);
        public string Summary => $"共 {Rows.Count} 張；新增 {Rows.Count(r=>r.Change==DrawingChange.Add)}／更新 {Rows.Count(r=>r.Change==DrawingChange.Update)}／人工修改 {Rows.Count(r=>r.Change==DrawingChange.ManualOverride)}／衝突 {Rows.Count(r=>r.Change==DrawingChange.Conflict||r.Change==DrawingChange.Missing)}";
    }
    public sealed record DrawingQaIssue(long SheetId, string SheetNumber, string Code, string Severity, string Message);
    public sealed record DrawingSheetStatus(long SheetId,string SheetNumber,string SheetName,string Level,string Zone,string DrawingType,string Readiness);
    public static class DrawingTokenEngine
    {
        private static readonly Regex Token = new(@"\{([A-Za-z]+)(?::(0+))?\}", RegexOptions.CultureInvariant);
        public static string Expand(string rule, IReadOnlyDictionary<string,string> values, int sequence)
        {
            if (string.IsNullOrWhiteSpace(rule)) throw new ArgumentException("請填寫圖號／名稱規則。");
            var result=Token.Replace(rule, m=>
            {
                string key=m.Groups[1].Value;
                string value=key=="Sequence"?sequence.ToString(CultureInfo.InvariantCulture):values.TryGetValue(key,out var v)?v:throw new ArgumentException("未知 token："+key);
                if(string.IsNullOrWhiteSpace(value))throw new ArgumentException("token 未提供值："+key);
                if(m.Groups[2].Success)
                {
                    if(!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n))throw new ArgumentException("數值格式需明確整數值："+key);
                    return n.ToString(m.Groups[2].Value,CultureInfo.InvariantCulture);
                }
                return value;
            }).Trim();
            if(result.Length==0||result.IndexOfAny(new[]{'{','}','\r','\n','\t'})>=0)throw new ArgumentException("規則含未解析 token 或無效字元。");
            return result;
        }
    }
    public static class DrawingPlanner
    {
        public static DrawingPlan Generate(DrawingPackageDefinition package, IEnumerable<string> existingNumbers)
        {
            var plan=new DrawingPlan{Package=package};
            if(package.Levels.Count==0||package.Zones.Count==0){plan.Errors.Add("請選擇樓層與分區。");return plan;}
            if(package.Levels.Select(l=>l.Id).Distinct().Count()!=package.Levels.Count||package.Zones.Select(z=>z.ZoneId).Distinct().Count()!=package.Zones.Count)
            {plan.Errors.Add("樓層或分區重複。");return plan;}
            var numbers=new HashSet<string>(existingNumbers,StringComparer.OrdinalIgnoreCase);
            int sequence=0;
            foreach(var level in package.Levels.OrderBy(l=>l.Elevation).ThenBy(l=>l.Id))
            foreach(var zone in package.Zones.OrderBy(z=>z.ZoneId,StringComparer.Ordinal))
            {
                sequence++;
                string key=level.Id.ToString(CultureInfo.InvariantCulture)+"/"+zone.ZoneId;
                if(package.ExcludedKeys.Contains(key))continue;
                var row=new DrawingPlanRow{Key=key,Level=level,Zone=zone,Change=DrawingChange.Add};plan.Rows.Add(row);
                try
                {
                    if(!zone.Bounds.Valid)throw new ArgumentException("分區缺少有效範圍。");
                    if(!package.SourceViewsByKey.TryGetValue(key,out long source)&&!package.SourceViewsByLevel.TryGetValue(level.Id,out source)||source<=0)throw new ArgumentException("尚未指定此樓層的來源視圖。");
                    row.SourceViewId=source;
                    var tokens=new Dictionary<string,string>{{"Discipline",package.Discipline},{"DrawingType",package.DrawingType},{"Level",level.Name},{"Zone",zone.ZoneName},{"Phase",package.Phase}};
                    row.SheetNumber=DrawingTokenEngine.Expand(package.Profile.NumberingRule,tokens,sequence);
                    row.SheetName=DrawingTokenEngine.Expand(package.Profile.NamingRule,tokens,sequence);
                    row.ViewName=DrawingTokenEngine.Expand(package.Profile.ViewNamingRule,tokens,sequence);
                    if(package.NumberOverrides.TryGetValue(key,out var number))row.SheetNumber=DrawingTokenEngine.Expand(number,new Dictionary<string,string>(),sequence);
                    if(package.NameOverrides.TryGetValue(key,out var name))row.SheetName=DrawingTokenEngine.Expand(name,new Dictionary<string,string>(),sequence);
                    if(numbers.Contains(row.SheetNumber)||package.ReservedNumbers.Contains(row.SheetNumber))throw new ArgumentException("圖號已使用或保留。");
                }
                catch(ArgumentException e){row.Change=DrawingChange.Conflict;row.Issues.Add(e.Message);}
            }
            foreach(var group in plan.Rows.Where(r=>r.SheetNumber!="").GroupBy(r=>r.SheetNumber,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
                foreach(var row in group){row.Change=DrawingChange.Conflict;row.Issues.Add("計畫圖號重複。");}
            foreach(var group in plan.Rows.Where(r=>r.ViewName!="").GroupBy(r=>r.ViewName,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
                foreach(var row in group){row.Change=DrawingChange.Conflict;row.Issues.Add("計畫視圖名稱重複。");}
            return plan;
        }
    }
    public static class DrawingSheetQaService
    {
        public static IReadOnlyList<DrawingQaIssue> Layout(long sheetId,string number,DrawingBounds bounds,IReadOnlyList<SheetLayoutSlot> slots)
        {
            var issues=new List<DrawingQaIssue>();
            if(!bounds.Valid)issues.Add(new(sheetId,number,"INVALID_BOUNDS","ERROR","圖框範圍無效。"));
            foreach(var slot in slots)
            {
                if(!slot.Bounds.Valid||!bounds.Contains(slot.Bounds))issues.Add(new(sheetId,number,"OUTSIDE_SHEET","ERROR",slot.Role+" 超出圖框。"));
                foreach(var other in slots.TakeWhile(s=>!ReferenceEquals(s,slot)))
                    if(slot.Bounds.Overlaps(other.Bounds))issues.Add(new(sheetId,number,"LAYOUT_OVERLAP","ERROR",slot.Role+" 與 "+other.Role+" 重疊。"));
            }
            foreach(var duplicate in slots.Where(s=>s.Role!="SCHEDULE"&&s.DetailNumber!="").GroupBy(s=>s.DetailNumber).Where(g=>g.Count()>1))
                issues.Add(new(sheetId,number,"DETAIL_NUMBER_DUPLICATE","ERROR","視埠詳圖號重複："+duplicate.Key));
            return issues;
        }
    }
}
#endif
