using System.Text.Json;
using RevitMCP.Models;
using RevitMCP.UI;
var checks = new List<object>(); int failed = 0;
void Check(string name, object expected, object? actual, bool pass) { checks.Add(new { TestName=name, Expected=expected, Actual=actual, Passed=pass, Evidence="Executed production CoordinationViewModel with queued ICoordinationHost" }); if(!pass) { failed++; Console.WriteLine("FAIL " + name + ": " + actual); } }
var host = new TestHost(); var vm = new CoordinationViewModel(host);
Check("initial_scan_disabled",false,vm.CanScan,!vm.CanScan);
Check("initial_settings_disabled",false,vm.SaveSettingsCommand.CanExecute(null),!vm.SaveSettingsCommand.CanExecute(null));
vm.Initialize(); Check("initialize_queues_external_work",1,host.Submissions,host.Submissions==1&&host.IsBusy);
vm.Initialize(); Check("initialize_idempotent",1,host.Submissions,host.Submissions==1);
host.Drain();
Check("document_identity","A",vm.DocumentIdentity,vm.DocumentIdentity=="A");
Check("sources_main_and_link",2,vm.Sources.Count,vm.Sources.Count==2);
Check("main_source_default",0,vm.MepSource?.LinkInstanceId,vm.MepSource?.LinkInstanceId==0&&vm.HostSource?.LinkInstanceId==0);
Check("no_geometry_on_initialize",0,host.Context.Scans,host.Context.Scans==0);
Check("levels_main",true,string.Join(",",vm.Levels.Select(l=>l.Name)),vm.Levels.Count==3&&vm.Levels[1].Id==11&&vm.SelectedLevel?.Id==null);
Check("no_arbitrary_host",true,vm.ScanDisabledReason,vm.HostCategory==""&&vm.ScanDisabledReason.Contains("主體分類"));
vm.HostCategory="Walls";
Check("clash_only_without_clearance",true,vm.CanScan,vm.CanScan);
vm.OpeningCandidates=true;
Check("opening_setting_required",false,vm.CanScan,!vm.CanScan&&vm.ScanDisabledReason.Contains("預留量"));
vm.ClearanceText="-1";vm.SaveSettingsCommand.Execute(null);host.Drain();
Check("reject_negative_clearance",null!,vm.ClearanceMm,vm.ClearanceMm==null);
vm.ClearanceText="25 mm";vm.SaveSettingsCommand.Execute(null);host.Drain();
Check("save_explicit_setting",25,vm.ClearanceMm,vm.ClearanceMm==25&&vm.SavedSetting.Contains("25"));
Check("canexecute_ready",true,vm.ScanCommand.CanExecute(null),vm.ScanCommand.CanExecute(null));
vm.SelectedLevel=vm.Levels[1];vm.SystemContains="Fixture"; vm.MaxResults=1;
var request=vm.BuildRequest();
Check("request_fields",true,request,request.MepLinkId==0&&request.HostLinkId==0&&request.LevelId==11&&request.MepCategory=="Pipes"&&request.HostCategory=="Walls"&&request.ClearanceMm==25&&request.OpeningCandidates&&request.MaxResults==1&&request.SystemContains=="Fixture");
vm.ScanCommand.Execute(null);
Check("busy_gate",false,vm.ScanCommand.CanExecute(null),!vm.ScanCommand.CanExecute(null));host.Drain();
Check("dispatcher_scan_executed",1,host.Context.Scans,host.Context.Scans==1);
Check("result_binding",2,vm.RowsView.Count,vm.RowsView.Count==2&&vm.Result?.DocumentIdentity=="A");
Check("summary_total",true,vm.Summary,vm.Summary.Contains("總問題 2"));
vm.SelectedFilter="穿梁候選"; Check("filter_kind",1,vm.RowsView.Count,vm.RowsView.Count==1&&vm.SelectedRow?.ResultKind==CoordinationKind.BeamPenetration);
vm.Search="missing";Check("empty_filter_disables_navigation",false,vm.CanNavigate,!vm.CanNavigate&&vm.SelectedRow==null);
vm.Search="";vm.SelectedFilter="全部";
vm.SelectedRow=vm.RowsView[0];vm.HighlightBothCommand.Execute(null);host.Drain();Check("highlight",1,host.Context.Highlights,host.Context.Highlights==1);
vm.NextCommand.Execute(null);host.Drain();Check("next_visible_row",true,vm.SelectedRow?.Mep.ElementId,vm.SelectedRow?.Mep.ElementId==2&&host.Context.Highlights==2);
vm.PreviousCommand.Execute(null);host.Drain();Check("previous_visible_row",true,vm.SelectedRow?.Mep.ElementId,vm.SelectedRow?.Mep.ElementId==1);
var csv=vm.ExportCsv();Check("unified_csv_schema",15,csv.Split('\n')[0].Trim().Split(',').Length,csv.Split('\n')[0].Trim().Split(',').Length==15);
Check("csv_formula_escape",true,csv.Contains("'=Unsafe"),csv.Contains("'=Unsafe"));
Check("filters_no_rescan",1,host.Context.Scans,host.Context.Scans==1);
vm.MepSource=vm.Sources[1];Check("source_change_invalidates",true,vm.Result==null,vm.Result==null&&vm.SelectedRow==null&&host.IsBusy);host.Drain();
Check("linked_levels_reload",true,vm.Levels.Select(l=>l.Name).ToArray(),vm.Levels.Count==2&&vm.Levels[1].Id==91&&vm.SelectedLevel?.Id==null);
Check("source_change_resets_system",true,vm.SystemContains,vm.SystemContains==""&&vm.ClearanceMm==25);
vm.HostCategory="Walls";vm.ScanCommand.Execute(null);host.Drain();
Check("link_request",9,vm.LastRequest?.MepLinkId,vm.LastRequest?.MepLinkId==9);
host.Context.Identity="B";vm.DocumentChanged("B");
Check("document_change_invalidates",true,vm.Result==null,vm.Result==null&&!vm.CanNavigate&&host.IsBusy);host.Drain();
Check("automatic_document_refresh","B",vm.DocumentIdentity,vm.DocumentIdentity=="B"&&vm.MepSource?.LinkInstanceId==0);
Check("settings_project_isolation",null!,vm.ClearanceMm,vm.ClearanceMm==null);
vm.HostCategory="Walls";vm.OpeningCandidates=false;vm.ScanCommand.Execute(null);
host.Context.Identity="C";host.Drain();
Check("stale_queued_scan_not_run",2,host.Context.Scans,host.Context.Scans==2&&vm.Result==null&&vm.DocumentIdentity=="C");
Check("stale_anchor_auto_recovery",true,vm.Sources.Count,vm.Sources.Count==2&&!host.IsBusy);
host.Context.Identity="A";vm.DocumentChanged("A");host.Drain();
Check("return_project_settings",25,vm.ClearanceMm,vm.ClearanceMm==25);
vm.DocumentChanged("A",true);host.Drain();Check("same_document_content_change_refresh",true,vm.Result==null,vm.Result==null&&vm.Sources.Count==2);
host.Context.NoLevels=true;vm.RefreshSources();host.Drain();vm.HostCategory="Walls";
Check("no_levels_reason",false,vm.CanScan,!vm.CanScan&&vm.ScanDisabledReason.Contains("樓層"));host.Context.NoLevels=false;
host.Context.ThrowRefresh=true;vm.RefreshSources();host.Drain();
Check("refresh_failure_actionable",true,vm.StatusMessage,vm.StatusMessage.Contains("無法重新讀取模型來源")&&!vm.CanScan);
host.Context.ThrowRefresh=false;vm.RefreshSources();host.Drain();vm.HostCategory="Walls";
Check("refresh_retry",true,vm.CanScan,vm.CanScan);
host.Reject=true;vm.RefreshSources();Check("queue_rejection",false,vm.CanScan,!vm.CanScan&&vm.StatusMessage.Contains("無法排程"));host.Reject=false;vm.RefreshSources();host.Drain();
Check("auto_locate_default_on",true,vm.AutoLocateEnabled,vm.AutoLocateEnabled);
Check("3d_navigation_available",true,vm.ThreeDNavigationAvailable,vm.ThreeDNavigationAvailable);
Check("before_scan_empty_state",true,vm.EmptyState,vm.EmptyState.Contains("選擇協調範圍"));
vm.HostCategory="Walls";vm.ScanCommand.Execute(null);
Check("scan_progress",true,vm.StatusMessage,vm.StatusMessage=="掃描中…"&&!vm.CanScan);host.Drain();
host.Context.ActiveView=10;vm.Locate3DCommand.Execute(null);host.Drain();
Check("resolve_orthographic_before_perspective",20,vm.CoordinationViewId,vm.CoordinationViewId==20&&vm.LastFocusVerified);
Check("previous_view_recorded",10,vm.PreviousViewId,vm.PreviousViewId==10&&vm.ReturnPreviousCommand.CanExecute(null));
int enumerations=host.Context.ViewEnumerations;host.Context.ActiveView=21;
vm.NextCommand.Execute(null);host.Drain();
Check("next_keeps_session_view",20,host.Context.ActiveView,host.Context.ActiveView==20&&host.Context.ViewEnumerations==enumerations);
vm.PreviousCommand.Execute(null);host.Drain();Check("previous_keeps_session_view",20,host.Context.ActiveView,host.Context.ActiveView==20&&vm.NavigationPosition=="1 / 2");
host.Context.ActiveView=21;vm.Locate3DCommand.Execute(null);host.Drain();
Check("explicit_locate_prefers_current_3d",21,vm.CoordinationViewId,vm.CoordinationViewId==21);
vm.SelectedRow=vm.RowsView[1];Check("row_change_clears_focus_evidence",false,vm.LastFocusVerified,!vm.LastFocusVerified);vm.SelectedRow=vm.RowsView[0];
vm.ReturnPreviousCommand.Execute(null);host.Drain();Check("return_previous",10,host.Context.ActiveView,host.Context.ActiveView==10&&vm.PreviousViewId==null);
vm.AutoLocateEnabled=false;int submissions=host.Submissions;vm.NextCommand.Execute(null);
Check("auto_off_selection_only",true,vm.NavigationPosition,vm.NavigationPosition=="2 / 2"&&host.Submissions==submissions);
Check("detail_selected_row",true,vm.Detail,vm.Detail.Contains("Element ID：2")&&vm.Detail.Contains("穿透長度")&&vm.Detail.Contains("人工確認"));
vm.SelectedRow=vm.RowsView[0];Check("row_selection_no_api",true,host.Submissions,host.Submissions==submissions&&vm.NavigationPosition=="1 / 2");
vm.AutoLocateEnabled=true;host.Context.Views.RemoveAll(v=>v.Id==21);vm.Locate3DCommand.Execute(null);host.Drain();
Check("invalid_session_view_resolved",20,vm.CoordinationViewId,vm.CoordinationViewId==20);
vm.ReturnPreviousCommand.Execute(null);host.Drain();host.Context.ActiveView=999;vm.Locate3DCommand.Execute(null);host.Drain();vm.ReturnPreviousCommand.Execute(null);host.Drain();
Check("stale_previous_clears_session",true,vm.PreviousViewId,vm.PreviousViewId==null&&vm.CoordinationViewId==null&&vm.StatusMessage.Contains("失效"));
host.Context.Views.Clear();vm.Locate3DCommand.Execute(null);host.Drain();
Check("no_3d_safe_fallback",true,vm.StatusMessage,!vm.ThreeDNavigationAvailable&&!vm.LastFocusVerified&&vm.CoordinationViewId==null&&vm.StatusMessage.Contains("沒有可用的 3D"));
host.Context.Views.Add(new(){Id=20,Usable=true});vm.MepSource=vm.Sources[1];host.Drain();vm.ScanCommand.Execute(null);host.Drain();vm.Locate3DCommand.Execute(null);host.Drain();
Check("linked_navigation_state",true,vm.Detail,vm.ThreeDNavigationAvailable&&vm.SelectedRow?.Mep.LinkInstanceId==9&&vm.Detail.Contains("9:1")&&vm.StatusMessage.Contains("Link instance"));
int scans=host.Context.Scans;vm.FilterBeamCommand.Execute(null);Check("summary_filter_no_rescan",1,vm.RowsView.Count,vm.RowsView.Count==1&&host.Context.Scans==scans);
host.Context.Identity="D";vm.DocumentChanged("D");host.Drain();
Check("document_clears_navigation_session",true,vm.CoordinationViewId,vm.CoordinationViewId==null&&vm.PreviousViewId==null&&!vm.LastFocusVerified);
vm.HostCategory="Walls";vm.OpeningCandidates=false;host.Context.ZeroResults=true;vm.ScanCommand.Execute(null);host.Drain();
Check("zero_results_empty_state",true,vm.EmptyState,vm.EmptyState=="目前範圍未發現協調問題。"&&!vm.CanNavigate);
var lazyViews=new[]{new CoordinationViewOption{Id=200,Usable=true},new CoordinationViewOption{Id=100,Usable=true},new CoordinationViewOption{Id=1,Usable=true,Perspective=true}};
var deterministic=CoordinationViewPolicy.Resolve(999,null,false,id=>lazyViews.FirstOrDefault(v=>v.Id==id),()=>lazyViews);
Check("view_resolution_deterministic",100,deterministic,deterministic==100);
DrawingTests.Run(Check);
var report=new { GateC2=failed==0?"PASS":"FAIL", Passed=checks.Count-failed,Failed=failed,Assertions=checks,Timestamp=DateTimeOffset.UtcNow };
var output=args.Length>0?args[0]:"test-artifacts/v042";Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output,"workflow-state.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
File.WriteAllText(Path.Combine(output,"workflow-state.md"),$"# Native workflow state\n\nGate C2: {report.GateC2}\nPassed: {report.Passed}; Failed: {failed}\n\n"+string.Join("\n",checks.Select(x=>JsonSerializer.Serialize(x))));
Console.WriteLine($"Gate C2 {report.GateC2}: {report.Passed} passed / {failed} failed");return failed==0?0:1;

sealed class TestHost : ICoordinationHost
{
    public bool IsBusy { get; private set; }
    public event EventHandler? BusyChanged;
    private Action<ICoordinationContext>? pending;
    private Action<string>? failure;
    public TestContext Context=new();public int Submissions;public bool Reject;
    public bool Submit(Action<ICoordinationContext> action,Action<string> error)
    { if(IsBusy||Reject)return false;pending=action;failure=error;Submissions++;IsBusy=true;BusyChanged?.Invoke(this,EventArgs.Empty);return true; }
    public void Drain() {int count=0;while(pending!=null) {if(++count>20)throw new Exception("Refresh loop");var action=pending;var error=failure;pending=null;try{action(Context);}catch(Exception ex){error!(ex.Message);}finally{IsBusy=false;BusyChanged?.Invoke(this,EventArgs.Empty);}}}
}
sealed class TestContext : ICoordinationContext
{
    public string Identity="A";public string DocumentIdentity=>Identity;public int Scans,Highlights;public bool NoLevels,ThrowRefresh,ZeroResults;
    public IReadOnlyList<CoordinationSource> GetSources() {if(ThrowRefresh)throw new Exception("fixture failure");return new[]{new CoordinationSource{Name="Main"},new CoordinationSource{LinkInstanceId=9,Name="Link"}};}
    public IReadOnlyList<CoordinationLevel> GetLevels(long id) => NoLevels ? Array.Empty<CoordinationLevel>() : id==0?new[]{new CoordinationLevel{Id=11,Name="FL1"},new CoordinationLevel{Id=12,Name="FL2"}}:new[]{new CoordinationLevel{Id=91,Name="LINK-FL1"}};
    public IReadOnlyList<string> GetMepCategories(long id)=>new[]{"Pipes","Ducts"};
    public double ParseClearance(string text)=>double.Parse(text.Replace(" mm",""),System.Globalization.CultureInfo.InvariantCulture);
    public CoordinationResult Scan(CoordinationRequest request)
    { Scans++;if(ZeroResults)return new CoordinationResult{DocumentIdentity=Identity,Scope=request};return new CoordinationResult{DocumentIdentity=Identity,Scope=request,TotalMatchedCount=2,TotalScanned=3,CountsByKind=new(){{CoordinationKind.OpeningCandidate,1},{CoordinationKind.BeamPenetration,1}},CountsByStatus=new(){{"需人工複核",2}},Rows=new(){new CoordinationRow{ResultKind=CoordinationKind.OpeningCandidate,Mep=new(){ElementId=1,LinkInstanceId=request.MepLinkId},System="=Unsafe",MepLabel="Pipe",WarningCodes=new(){"solid_edge_unknown"}},new CoordinationRow{ResultKind=CoordinationKind.BeamPenetration,Mep=new(){ElementId=2},WarningCodes=new(){"structural_framing_review"}}}}; }
    public long ActiveView = 10; public List<CoordinationViewOption> Views = new() { new(){Id=20,Usable=true}, new(){Id=21,Usable=true},new(){Id=19,Usable=true,Perspective=true},new(){Id=18,Usable=false} };
    public int ViewEnumerations;
    private long? Resolve(CoordinationNavigationSession session,bool keep) => CoordinationViewPolicy.Resolve(ActiveView,session.CoordinationViewId,keep,
        id=>Views.FirstOrDefault(v=>v.Id==id),()=>{ViewEnumerations++;return Views;});
    public bool NavigationAvailable(CoordinationNavigationSession session)=>Resolve(session,false).HasValue;
    public CoordinationNavigationResult Locate(CoordinationRow row,bool mep,bool host,CoordinationNavigationSession session,bool keepSession)
    {
        Highlights++; var target=Resolve(session,keepSession);
        if(!target.HasValue){session.CoordinationViewId=null;return new(){Message="目前模型沒有可用的 3D 視圖，無法執行 3D 定位。"};}
        if(!Views.Any(v=>v.Id==ActiveView&&v.Usable)&&session.PreviousViewId==null)session.PreviousViewId=ActiveView;
        ActiveView=target.Value; session.CoordinationViewId=target;
        return new(){ThreeDAvailable=true,FocusVerified=true,Message=row.Mep.LinkInstanceId!=0?"連結構件以 Link instance 選取":"已在 3D 視圖定位交點"};
    }
    public string ReturnPrevious(CoordinationNavigationSession session)
    {if(session.PreviousViewId==999){session.Clear();return "原視圖已失效";}ActiveView=session.PreviousViewId??ActiveView;session.PreviousViewId=null;return "已返回原視圖";}
}
