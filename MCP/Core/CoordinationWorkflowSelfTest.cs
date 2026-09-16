using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCP.Models;
using RevitMCP.UI;

namespace RevitMCP.Core
{
    /// <summary>Gate C3: real Native controller and ExternalEvents in launcher-created disposable documents.</summary>
    internal sealed class CoordinationWorkflowSelfTest : IExternalEventHandler
    {
        private readonly string root;
        private readonly BimConstructionPanelViewModel panel;
        private CoordinationViewModel Vm => panel.Coordination;
        private readonly ExternalEvent stepEvent;
        private readonly DispatcherTimer timer;
        private readonly List<CoordinationSelfTest.Assertion> assertions = new List<CoordinationSelfTest.Assertion>();
        private int step;
        private bool queued, complete;
        private DateTime deadline;
        private long mepLink, hostLink, selectedMep, selectedHost;
        private string originalIdentity = "";
        public bool Started { get; private set; }
        public CoordinationWorkflowSelfTest(UIControlledApplication application, string directory)
        {
            root = Path.GetFullPath(directory);
            panel = new BimConstructionPanelViewModel(); panel.AttachLifecycle(application);
            stepEvent = ExternalEvent.Create(this);
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, __) =>
            {
                if (complete || queued || Vm.IsBusy) return;
                var result = stepEvent.Raise(); queued = result == ExternalEventRequest.Accepted || result == ExternalEventRequest.Pending;
            };
        }
        public void Start() { Started = true; deadline = DateTime.UtcNow.AddMinutes(3); timer.Start(); }
        public string GetName() => "Disposable Coordination Native Workflow Gate C3";
        private void Check(string name, object expected, object? actual, bool passed)
        {
            assertions.Add(new CoordinationSelfTest.Assertion { TestName = name, Expected = Convert.ToString(expected) ?? "", Actual = Convert.ToString(actual) ?? "", Passed = passed,
                AffectedBackend = "CoordinationViewModel / RevitCoordinationHost / PanelReadOnlyDispatcher", Evidence = "Real ActiveUIDocument; actual queued ExternalEvent; step " + step });
        }
        private void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private string Fixture(string name)
        {
            string file = Path.GetFullPath(Path.Combine(root, name));
            Require(file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(file), "Fixture path unavailable");
            return file;
        }
        public void Execute(UIApplication app)
        {
            queued = false;
            try
            {
                Require(DateTime.UtcNow < deadline, "Workflow runtime timeout");
                File.WriteAllText(Path.Combine(root,"workflow-progress.txt"), $"Step {step}; busy {Vm.IsBusy}; {Vm.StatusMessage}; {Vm.ScanDisabledReason}");
                if (step > 0) Require(app.ActiveUIDocument != null && app.ActiveUIDocument.Document.PathName.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Refusing non-fixture ActiveUIDocument");
                switch (step)
                {
                    case 0:
                        Require(app.Application.Documents.Size == 0, "C3 requires document-free initial session");
                        var backend = JObject.Parse(File.ReadAllText(Fixture("runtime.json")));
                        Require((string?)backend["GateC"] == "PASS", "Backend Gate C failed");
                        app.OpenAndActivateDocument(Fixture("CoordinationFixture.rvt"));
                        var page = new BimConstructionPanelPage(panel);
                        Check("production_view_data_context", true, page.DataContext == panel, page.DataContext == panel);
                        panel.Initialize(); break;
                    case 1:
                        originalIdentity = Vm.DocumentIdentity;
                        Check("active_document_identity", true, originalIdentity, originalIdentity == DocumentSessionIdentity.GetDocumentIdentity(app.ActiveUIDocument.Document));
                        Check("auto_sources", 3, Vm.Sources.Count, Vm.Sources.Count == 3);
                        Check("host_levels", "FL1/FL2", string.Join("/", Vm.Levels.Select(l=>l.Name)), Vm.Levels.Any(l=>l.Name=="FL1") && Vm.Levels.Any(l=>l.Name=="FL2") && Vm.SelectedLevel?.Id==null);
                        mepLink = Vm.Sources.Single(s=>s.Name.Contains("translated-link")).LinkInstanceId;
                        hostLink = Vm.Sources.Single(s=>s.Name.Contains("translated-host")).LinkInstanceId;
                        Check("no_arbitrary_host", "", Vm.HostCategory, Vm.HostCategory=="");
                        Vm.MepCategory="Pipes";Vm.HostCategory="Walls";Vm.OpeningCandidates=true;
                        Check("missing_setting_disabled",false,Vm.CanScan,!Vm.CanScan&&Vm.ScanDisabledReason.Contains("預留量"));
                        Vm.ClearanceText="25 mm";Vm.SaveSettingsCommand.Execute(null);break;
                    case 2:
                        Check("project_units_setting",25,Vm.ClearanceMm,Vm.ClearanceMm.HasValue&&Math.Abs(Vm.ClearanceMm.Value-25)<0.0001);
                        Vm.SelectedLevel=Vm.Levels.Single(l=>l.Name=="FL1");
                        Check("can_scan",true,Vm.CanScan,Vm.CanScan);Require(Vm.CanScan,Vm.ScanDisabledReason);
                        var request=Vm.BuildRequest();
                        Check("request_fields",true,JsonConvert.SerializeObject(request),request.MepLinkId==0&&request.HostLinkId==0&&request.MepCategory=="Pipes"&&request.HostCategory=="Walls"&&request.LevelId==Vm.SelectedLevel.Id&&request.ClearanceMm.HasValue&&request.OpeningCandidates);
                        Vm.ScanCommand.Execute(null);break;
                    case 3:
                        Require(Vm.Result!=null,Vm.StatusMessage);
                        Check("native_wall_results",2,Vm.Result!.TotalIssues,Vm.Result.TotalIssues==2&&Vm.RowsView.Count==2);
                        Check("native_classification",true,string.Join(",",Vm.RowsView.Select(r=>r.ResultKind)),Vm.RowsView.All(r=>r.ResultKind==CoordinationKind.OpeningCandidate&&r.HasClash&&r.ReviewRequired));
                        Check("native_summary",true,Vm.Summary,Vm.Result.CountsByKind[CoordinationKind.OpeningCandidate]==2&&Vm.Summary.Contains("總問題 2"));
                        Check("native_size",150,Vm.RowsView[0].DiameterMm,Vm.RowsView[0].DiameterMm.HasValue&&Math.Abs(Vm.RowsView[0].DiameterMm!.Value-150)<0.001);
                        Vm.SelectedRow=Vm.RowsView[0];selectedMep=Vm.SelectedRow.Mep.ElementId;selectedHost=Vm.SelectedRow.Host.ElementId;
                        Vm.HighlightBothCommand.Execute(null);break;
                    case 4:
                        var ids=app.ActiveUIDocument.Selection.GetElementIds().Select(i=>i.GetIdValue()).ToArray();
                        Check("native_selection_readback",true,string.Join(",",ids),ids.Contains(selectedMep)&&ids.Contains(selectedHost));
                        Check("view_change_preserves_results",true,Vm.Result!=null,Vm.Result!=null&&Vm.DocumentIdentity==originalIdentity);
                        Vm.NextCommand.Execute(null);break;
                    case 5:
                        Check("native_next_navigation",true,Vm.SelectedRow?.Mep.ElementId,Vm.SelectedRow!=null&&Vm.SelectedRow.Mep.ElementId!=selectedMep&&app.ActiveUIDocument.Selection.GetElementIds().Any(i=>i.GetIdValue()==Vm.SelectedRow.Mep.ElementId));
                        Vm.MepSource=Vm.Sources.Single(s=>s.LinkInstanceId==mepLink);break;
                    case 6:
                        Check("source_change_clear_results",true,Vm.Result==null,Vm.Result==null&&Vm.SelectedRow==null);
                        Check("link_levels",true,string.Join(",",Vm.Levels.Select(l=>l.Name)),Vm.Levels.Any(l=>l.Name=="LINK-FL1")&&!Vm.Levels.Any(l=>l.Name=="FL1")&&Vm.SelectedLevel?.Id==null);
                        Vm.SelectedLevel=Vm.Levels.Single(l=>l.Name=="LINK-FL1");Require(Vm.CanScan,Vm.ScanDisabledReason);Vm.ScanCommand.Execute(null);break;
                    case 7:
                        Require(Vm.Result!=null,Vm.StatusMessage);
                        Check("native_link_scan",1,Vm.Result!.TotalIssues,Vm.Result.TotalIssues==1&&Vm.RowsView[0].Mep.LinkInstanceId==mepLink&&Math.Abs(Vm.RowsView[0].Xmm)<1);
                        Vm.HighlightBothCommand.Execute(null);break;
                    case 8:
                        Check("link_navigation_instance",true,string.Join(",",app.ActiveUIDocument.Selection.GetElementIds()),app.ActiveUIDocument.Selection.GetElementIds().Any(i=>i.GetIdValue()==mepLink));
                        Check("csv_schema",15,Vm.ExportCsv().Split('\n')[0].Trim().Split(',').Length,Vm.ExportCsv().Split('\n')[0].Trim().Split(',').Length==15);
                        Vm.MepSource=Vm.Sources.Single(s=>s.LinkInstanceId==0);break;
                    case 9:
                        Vm.HostSource=Vm.Sources.Single(s=>s.LinkInstanceId==hostLink);Require(Vm.CanScan,Vm.ScanDisabledReason);Vm.ScanCommand.Execute(null);break;
                    case 10:
                        Check("native_translated_host",2,Vm.Result?.TotalIssues,Vm.Result?.TotalIssues==2&&Vm.RowsView.All(r=>r.Host.LinkInstanceId==hostLink));
                        Vm.HostSource=Vm.Sources.Single(s=>s.LinkInstanceId==0);Vm.HostCategory="StructuralFraming";Vm.ScanCommand.Execute(null);break;
                    case 11:
                        Check("native_beam_review",1,Vm.Result?.TotalIssues,Vm.Result?.TotalIssues==1&&Vm.RowsView[0].ResultKind==CoordinationKind.BeamPenetration&&Vm.RowsView[0].WarningCodes.Contains("structural_framing_review"));
                        Check("one_pair_multiple_solids",1,Vm.RowsView.Count,Vm.RowsView.Count==1&&Vm.RowsView[0].WarningCodes.Contains("multiple_intersections"));
                        Vm.SelectedFilter="穿梁候選";Check("native_filter",1,Vm.RowsView.Count,Vm.RowsView.Count==1);Vm.SelectedFilter="全部";
                        Vm.MepCategory="Ducts";Vm.HostCategory="Floors";Vm.ScanCommand.Execute(null);break;
                    case 12:
                        Check("native_duct_floor",1,Vm.Result?.TotalIssues,Vm.Result?.TotalIssues==1&&Vm.RowsView[0].MepCategory=="Ducts"&&Vm.RowsView[0].HostCategory=="Floors");
                        Vm.MepCategory="Conduits";Vm.HostCategory="Walls";Vm.OpeningCandidates=false;Vm.ScanCommand.Execute(null);break;
                    case 13:
                        Check("native_no_clash",0,Vm.Result?.TotalIssues,Vm.Result!=null&&Vm.Result.TotalIssues==0&&Vm.RowsView.Count==0&&!Vm.CanNavigate);
                        app.OpenAndActivateDocument(Fixture("WorkflowSwitch.rvt"));break;
                    case 14:
                        Check("document_switch_refresh",true,Vm.DocumentIdentity,Vm.DocumentIdentity!=originalIdentity&&Vm.Result==null&&Vm.SelectedRow==null&&Vm.Sources.Count==1&&Vm.ClearanceMm==null);
                        app.OpenAndActivateDocument(Fixture("CoordinationFixture.rvt"));break;
                    case 15:
                        Check("return_document_settings",25,Vm.ClearanceMm,Vm.DocumentIdentity==originalIdentity&&Vm.ClearanceMm.HasValue&&Math.Abs(Vm.ClearanceMm.Value-25)<0.001);
                        using(var tx=new Transaction(app.ActiveUIDocument.Document,"Disposable C3 document-change assertion"))
                        { tx.Start();var level=Level.Create(app.ActiveUIDocument.Document,25);level.Name="C3_EDIT";tx.Commit(); }
                        break;
                    case 16:
                        Check("document_changed_event_refresh",true,string.Join(",",Vm.Levels.Select(l=>l.Name)),Vm.Levels.Any(l=>l.Name=="C3_EDIT")&&Vm.Result==null);
                        Finish(app);break;
                }
                step++;
            }
            catch(Exception ex) { Check("runtime_workflow_exception","No exception",ex.ToString(),false); Finish(app); }
        }
        private void Finish(UIApplication app)
        {
            complete=true;timer.Stop();
            try
            {
                foreach(Document doc in app.Application.Documents)
                    if(!doc.IsLinked && doc.IsModified && doc.PathName.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) doc.Save();
            }
            catch(Exception ex) { Check("fixture_save_before_exit","success",ex.Message,false); }
            string hash;
            using(var sha=SHA256.Create())using(var stream=File.OpenRead(Assembly.GetExecutingAssembly().Location))hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
            var report=new {GateC3=assertions.All(a=>a.Passed)&&assertions.Count>=25?"PASS":"FAIL",BuildHash=hash,FixtureVersion=CoordinationSelfTest.FixtureVersion,Timestamp=DateTimeOffset.UtcNow,Passed=assertions.Count(a=>a.Passed),Failed=assertions.Count(a=>!a.Passed),Assertions=assertions};
            File.WriteAllText(Path.Combine(root,"workflow-runtime.json"),JsonConvert.SerializeObject(report,Formatting.Indented));
            File.WriteAllText(Path.Combine(root,"workflow-runtime.md"),"# Native Runtime Workflow\n\nGate C3: "+report.GateC3+"\n\n"+string.Join("\n",assertions.Select(a=>$"- {(a.Passed?"PASS":"FAIL")} {a.TestName}: expected {a.Expected}; actual {a.Actual}")));
            var exit=RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);if(app.CanPostCommand(exit))app.PostCommand(exit);
        }
    }
}
