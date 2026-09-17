using RevitMCP.Core.Drawing;
using RevitMCP.UI;

internal static class DrawingTests
{
    public static void Run(Action<string,object,object?,bool> check)
    {
        void Verify(string name,bool condition)=>check("drawing_"+name,true,condition,condition);
        void Throws(string name,Action action){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Verify(name,rejected);}
        var package=new DrawingPackageDefinition{Discipline="A",DrawingType="Plan",Profile=new(){NumberingRule="{Discipline}-{Sequence:000}"},Levels=new(){new(3,"FL3",20),new(1,"FL1",0),new(2,"FL2",10)},Zones=new(){new(){ZoneId="B",ZoneName="B"},new(){ZoneId="A",ZoneName="A"}},SourceViewsByLevel=new(){{1,11},{2,12},{3,13}}};
        var plan=DrawingPlanner.Generate(package,Array.Empty<string>());
        Verify("six_sheets",plan.CanApply&&plan.Rows.Count==6);
        var mapping=new DrawingParameter{SemanticField="Zone"};Verify("parameter_zone_mapping",mapping.Resolve(package,plan.Rows[0])=="A");mapping.SemanticField="Bogus";Throws("unknown_mapping_blocked",()=>mapping.Resolve(package,plan.Rows[0]));
        Verify("elevation_order",plan.Rows[0].Level.Name=="FL1"&&plan.Rows[5].Level.Name=="FL3");
        Verify("deterministic_zone",plan.Rows[0].Zone.ZoneId=="A");
        Verify("number_tokens",plan.Rows.Select(r=>r.SheetNumber).SequenceEqual(new[]{"A-001","A-002","A-003","A-004","A-005","A-006"}));
        plan=DrawingPlanner.Generate(package,new[]{"a-003"});Verify("existing_collision_blocks",!plan.CanApply&&plan.Rows.Count(r=>r.Change==DrawingChange.Conflict)==1);
        package.Profile.NumberingRule="A";plan=DrawingPlanner.Generate(package,Array.Empty<string>());Verify("all_duplicate_rows_flagged",plan.Rows.All(r=>r.Change==DrawingChange.Conflict));
        package.Profile.NumberingRule="{Missing}";Verify("unknown_token_blocks",!DrawingPlanner.Generate(package,Array.Empty<string>()).CanApply);
        package.Profile.NumberingRule="{Phase}";Verify("empty_token_blocks",!DrawingPlanner.Generate(package,Array.Empty<string>()).CanApply);
        package.Profile.NumberingRule="{Level:00}";Verify("level_name_not_guessed_numeric",!DrawingPlanner.Generate(package,Array.Empty<string>()).CanApply);
        package.Profile.NumberingRule="{Sequence:000}";package.ReservedNumbers.Add("001");Verify("reserved_number_blocks",!DrawingPlanner.Generate(package,Array.Empty<string>()).CanApply);package.ReservedNumbers.Clear();
        package.ExcludedKeys.Add("1/A");plan=DrawingPlanner.Generate(package,Array.Empty<string>());Verify("exclude_keeps_sequence_stable",plan.Rows.Count==5&&plan.Rows[0].SheetNumber=="002");package.ExcludedKeys.Clear();
        Throws("malformed_token",()=>DrawingTokenEngine.Expand("{Sequence:abc}",new Dictionary<string,string>(),1));
        Throws("blank_rule",()=>DrawingTokenEngine.Expand(" ",new Dictionary<string,string>(),1));
        package.SourceViewsByLevel.Remove(2);Verify("missing_level_source",!DrawingPlanner.Generate(package,Array.Empty<string>()).CanApply);package.SourceViewsByLevel[2]=12;
        var slots=new[]{new SheetLayoutSlot{Bounds=new(1,1,3,3),DetailNumber="1"},new SheetLayoutSlot{Role="SCHEDULE",Bounds=new(2,2,4,4)}};
        Verify("schedule_overlap",DrawingSheetQaService.Layout(1,"A",new(0,0,10,10),slots).Any(q=>q.Code=="LAYOUT_OVERLAP"));
        slots[1].Bounds=new(8,8,11,11);Verify("outside",DrawingSheetQaService.Layout(1,"A",new(0,0,10,10),slots).Any(q=>q.Code=="OUTSIDE_SHEET"));
        slots[1].Bounds=new(3,1,5,3);Verify("touching_not_overlap",DrawingSheetQaService.Layout(1,"A",new(0,0,10,10),slots).Count==0);
        slots[1].Role="LEGEND";slots[1].DetailNumber="1";Verify("detail_duplicate",DrawingSheetQaService.Layout(1,"A",new(0,0,10,10),slots).Any(q=>q.Code=="DETAIL_NUMBER_DUPLICATE"));
        Verify("nan_bounds",!new DrawingBounds(0,0,double.NaN,1).Valid);
        var host=new Host(package);var vm=new DrawingProductionViewModel(host);
        Verify("initial_no_write",!vm.CanApply&&host.Applies==0);vm.ConfirmAndApply();Verify("unpreviewed_no_write",host.Applies==0);
        vm.Refresh();Verify("dispatch_pending",vm.Busy);host.Drain();vm.UsePackage(package);vm.GeneratePlan();Verify("generation_queued",vm.Busy&&vm.Plan==null);host.Drain();
        Verify("preview_requires_confirmation_step",vm.Plan?.CanApply==true&&!vm.CanApply);vm.GoToStep(3);Verify("confirmation_enabled",vm.CanApply);
        vm.Invalidate();vm.ConfirmAndApply();Verify("edited_plan_no_write",host.Applies==0);
        vm.GeneratePlan();host.Drain();vm.GoToStep(3);vm.ConfirmAndApply();host.Drain();Verify("confirmed_write",host.Applies==1&&vm.Step==4&&!vm.CanApply);
        vm.GeneratePlan();host.Drain();vm.GoToStep(3);vm.ConfirmAndApply();vm.Invalidate();host.Drain();Verify("edit_after_confirm_before_dispatch",host.Applies==1);
        vm.GeneratePlan();host.Drain();vm.GoToStep(3);vm.DocumentChanged("other");Verify("document_switch_clears",!vm.CanApply&&vm.Plan==null&&vm.Levels.Length==0);
        package.Levels=Enumerable.Range(1,150).Select(i=>new DrawingChoice(i,"FL"+i,i)).ToList();package.SourceViewsByLevel=package.Levels.ToDictionary(l=>l.Id,l=>l.Id+1000);Verify("300_sheet_plan",DrawingPlanner.Generate(package,Array.Empty<string>()).Rows.Count==300);
    }
    private sealed class Host : IDrawingHost,IDrawingContext
    {
        private Action<IDrawingContext>? pending;
        public int Applies;
        public Host(DrawingPackageDefinition package){}
        public bool Submit(string identity,Action<IDrawingContext> action,Action<string> failure){if(pending!=null)return false;pending=action;return true;}
        public void Drain(){var action=pending;pending=null;action?.Invoke(this);}
        public string Identity=>"fixture";
        public DrawingChoice[] Sheets()=>Array.Empty<DrawingChoice>();
        public DrawingChoice[] Levels()=>Array.Empty<DrawingChoice>();
        public DrawingChoice[] Sources(long level)=>Array.Empty<DrawingChoice>();
        public DrawingZone[] Zones()=>Array.Empty<DrawingZone>();
        public DrawingChoice[] Grids()=>Array.Empty<DrawingChoice>();
        public DrawingZone GridZone(string name,long[] grids,double paddingMm)=>new(){ZoneName=name};
        public DrawingChoice[] ViewTemplates()=>Array.Empty<DrawingChoice>();
        public SheetTemplateBlueprint ConfigureViewRule(SheetTemplateBlueprint blueprint,long templateId,int? scale)=>blueprint;
        public DrawingProjectData Load()=>new();
        public SheetTemplateBlueprint Extract(long sheet)=>new();
        public DrawingTemplateProfile SaveProfile(DrawingTemplateProfile profile)=>profile;
        public DrawingPlan Preview(DrawingPackageDefinition package)=>DrawingPlanner.Generate(package,Array.Empty<string>());
        public long[] Apply(DrawingPlan plan,bool confirmed){if(!confirmed||!plan.CanApply)throw new InvalidOperationException();Applies++;return new long[]{1};}
        public DrawingQaIssue[] Qa(Guid package)=>Array.Empty<DrawingQaIssue>();
        public DrawingSheetStatus[] SheetStatuses(Guid package,DrawingQaIssue[] issues)=>Array.Empty<DrawingSheetStatus>();
        public void Open(long sheetId){}
    }
}
