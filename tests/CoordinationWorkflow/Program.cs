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
var report=new { GateC2=failed==0?"PASS":"FAIL", Passed=checks.Count-failed,Failed=failed,Assertions=checks,Timestamp=DateTimeOffset.UtcNow };
var output=args.Length>0?args[0]:"test-artifacts/v041";Directory.CreateDirectory(output);
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
    public string Identity="A";public string DocumentIdentity=>Identity;public int Scans,Highlights;public bool NoLevels,ThrowRefresh;
    public IReadOnlyList<CoordinationSource> GetSources() {if(ThrowRefresh)throw new Exception("fixture failure");return new[]{new CoordinationSource{Name="Main"},new CoordinationSource{LinkInstanceId=9,Name="Link"}};}
    public IReadOnlyList<CoordinationLevel> GetLevels(long id) => NoLevels ? Array.Empty<CoordinationLevel>() : id==0?new[]{new CoordinationLevel{Id=11,Name="FL1"},new CoordinationLevel{Id=12,Name="FL2"}}:new[]{new CoordinationLevel{Id=91,Name="LINK-FL1"}};
    public IReadOnlyList<string> GetMepCategories(long id)=>new[]{"Pipes","Ducts"};
    public double ParseClearance(string text)=>double.Parse(text.Replace(" mm",""),System.Globalization.CultureInfo.InvariantCulture);
    public CoordinationResult Scan(CoordinationRequest request)
    { Scans++;return new CoordinationResult{DocumentIdentity=Identity,Scope=request,TotalMatchedCount=2,TotalScanned=3,CountsByKind=new(){{CoordinationKind.OpeningCandidate,1},{CoordinationKind.BeamPenetration,1}},CountsByStatus=new(){{"需人工複核",2}},Rows=new(){new CoordinationRow{ResultKind=CoordinationKind.OpeningCandidate,Mep=new(){ElementId=1},System="=Unsafe",MepLabel="Pipe",WarningCodes=new(){"solid_edge_unknown"}},new CoordinationRow{ResultKind=CoordinationKind.BeamPenetration,Mep=new(){ElementId=2},WarningCodes=new(){"structural_framing_review"}}}}; }
    public void Highlight(CoordinationRow row,bool mep,bool host){Highlights++;}
}
