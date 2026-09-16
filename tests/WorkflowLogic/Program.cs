using RevitMCP.Models;
using System.Text.Json;
var rows = new List<object>();
int failures = 0;
void Check(string name, object expected, object actual, bool pass)
{
    rows.Add(new { TestName=name, Expected=expected, Actual=actual, Passed=pass, AffectedDomain="mep-opening-candidate-scan", AffectedTool="scan_opening_candidates", AffectedBackend="CoordinationRules", Evidence="Executed compiled production pure logic" });
    if(!pass)failures++;
}
void Throws(string name, Action action) { bool threw=false;try{action();}catch(ArgumentException){threw=true;}Check(name,true,threw,threw); }
Check("round_size",150,CoordinationRules.OpeningSize(100,25),CoordinationRules.OpeningSize(100,25)==150);
Check("zero_clearance_explicit",100,CoordinationRules.OpeningSize(100,0),CoordinationRules.OpeningSize(100,0)==100);
Check("rectangular_width",250,CoordinationRules.OpeningSize(200,25),CoordinationRules.OpeningSize(200,25)==250);
Throws("missing_clearance",()=>CoordinationRules.Validate(new CoordinationRequest { MepCategory="Pipes",HostCategory="Walls",LevelName="FL1",OpeningCandidates=true }));
Throws("negative_clearance",()=>CoordinationRules.OpeningSize(100,-1));
Throws("nan_clearance",()=>CoordinationRules.OpeningSize(100,double.NaN));
Throws("infinite_clearance",()=>CoordinationRules.OpeningSize(100,double.PositiveInfinity));
Throws("unbounded_scope",()=>CoordinationRules.Validate(new CoordinationRequest { MepCategory="Pipes",HostCategory="Walls" }));
Throws("max_results_zero",()=>CoordinationRules.Validate(new CoordinationRequest { MaxResults=0 }));
Throws("max_results_over_limit",()=>CoordinationRules.Validate(new CoordinationRequest { MaxResults=1001 }));
foreach(var category in new[]{"StructuralFraming","StructuralColumns"}) Check(category+"_review",true,CoordinationRules.Classify(category,100,1,true).Count>0,CoordinationRules.Classify(category,100,1,true).Count>0);
Check("short_boundary_below",true,CoordinationRules.Classify("Walls",9.999,1,true).Contains("short_intersection"),CoordinationRules.Classify("Walls",9.999,1,true).Contains("short_intersection"));
Check("short_boundary_equal",false,CoordinationRules.Classify("Walls",10,1,true).Contains("short_intersection"),!CoordinationRules.Classify("Walls",10,1,true).Contains("short_intersection"));
Check("oblique_small_angle",true,CoordinationRules.Classify("Walls",100,0.99999,true).Contains("oblique_penetration"),CoordinationRules.Classify("Walls",100,0.99999,true).Contains("oblique_penetration"));
Check("missing_normal",true,CoordinationRules.Classify("Walls",100,null,true).Contains("host_normal_unresolved"),CoordinationRules.Classify("Walls",100,null,true).Contains("host_normal_unresolved"));
Check("missing_size",true,CoordinationRules.Classify("Walls",100,1,false).Contains("size_data_missing"),CoordinationRules.Classify("Walls",100,1,false).Contains("size_data_missing"));
Check("pattern_count",6,WorkflowRegistry.Patterns.Count,WorkflowRegistry.Patterns.Count==6);
Check("no_write_enabled",true,WorkflowRegistry.Definitions.Where(w=>w.Enabled).All(w=>w.Risk==WorkflowRiskLevel.ReadOnly&&!w.Capabilities.HasFlag(WorkflowCapability.Apply)),WorkflowRegistry.Definitions.Where(w=>w.Enabled).All(w=>w.Risk==WorkflowRiskLevel.ReadOnly&&!w.Capabilities.HasFlag(WorkflowCapability.Apply)));
Check("sleeve_disabled",false,WorkflowRegistry.Definitions.Single(w=>w.Id=="sleeves").Enabled,!WorkflowRegistry.Definitions.Single(w=>w.Id=="sleeves").Enabled);
var report=new {TestRunId=Guid.NewGuid(),Timestamp=DateTimeOffset.UtcNow,GateB=failures==0?"PASS":"FAIL",Passed=rows.Count-failures,Failed=failures,Assertions=rows};
string output=args.Length>0?args[0]:Path.GetTempPath();Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output,"logic.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
File.WriteAllText(Path.Combine(output,"logic.md"),$"# Pure logic tests\n\nGate B: {report.GateB}\n\nPassed: {report.Passed}; Failed: {failures}\n");
Console.WriteLine($"Gate B {report.GateB}: {report.Passed} passed, {failures} failed");return failures==0?0:1;
