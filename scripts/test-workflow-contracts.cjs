const fs=require('fs'),path=require('path');
const root=path.resolve(__dirname,'..');const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const matrix=JSON.parse(read('docs/productization/matrix.json'));const tests=[];const contractWarnings=[];
const check=(TestName,Expected,Actual,pass,Evidence)=>tests.push({TestName,Expected,Actual,Passed:pass,AffectedDomain:'productization',AffectedTool:'runtime registry',AffectedBackend:'MCP/Core/CommandExecutor.cs',Evidence});
const source=read('MCP/Core/CoordinationService.cs'),vm=read('MCP/UI/CoordinationViewModel.cs'),registry=read('MCP/Models/WorkflowDefinition.cs');
check('scope_required',true,/CoordinationRules.Validate\(request\)/.test(source),/CoordinationRules.Validate\(request\)/.test(source),'Typed request validation before collectors');
check('native_no_transport',false,/JObject|WebSocket|CommandExecutor|\.md"/.test(source),!/JObject|WebSocket|CommandExecutor|\.md"/.test(source),'Typed service dependencies');
check('native_readonly',false,/new Transaction\(/.test(source),!/new Transaction\(/.test(source),'Service has no model transaction');
check('settings_input',true,read('MCP/UI/RevitCoordinationHost.cs').includes('UnitFormatUtils.TryParse')&&vm.includes('OpeningClearanceMm'),read('MCP/UI/RevitCoordinationHost.cs').includes('UnitFormatUtils.TryParse')&&vm.includes('OpeningClearanceMm'),'Project-units input and typed versioned settings');
check('runtime_no_markdown',false,/ReadAllText|ReadAllLines|EnumerateFiles/.test(registry),!/ReadAllText|ReadAllLines|EnumerateFiles/.test(registry),'Compiled registration');
check('shared_geometry',true,source.includes('ClashDetector.IntersectCenterline'),source.includes('ClashDetector.IntersectCenterline'),'Same curve-to-solid API wrapper as legacy detector');
for(const label of ['RefreshSources','ProjectSettings','Navigation','CoordinationScan'])
 check('dispatcher_'+label,true,read('MCP/UI/PanelReadOnlyDispatcher.cs').includes(label),read('MCP/UI/PanelReadOnlyDispatcher.cs').includes(label),'Shared dispatcher requests');
for(const label of ['ModelSummary','TypeInventory','LevelConstraintAudit','TypeInstanceLocator']) {
 const production=['MCP/UI/BimConstructionPanelPage.cs','MCP/UI/BimConstructionPanelViewModel.cs','MCP/UI/PanelReadOnlyDispatcher.cs','MCP/UI/CoordinationViewModel.cs','MCP/Core/CoordinationService.cs'].map(read).join('\n');
 check('retired_'+label,false,production.includes(label),!production.includes(label),'Retired Native wiring removed');
}
const page=read('MCP/UI/BimConstructionPanelPage.cs');
check('two_product_workbenches',true,page.includes('Header = "施工協調"')&&page.includes('Header = "基地／土方"'),page.includes('Header = "施工協調"')&&page.includes('Header = "基地／土方"'),'v0.5 adds site workflow; no inventory dashboards');
const site=read('MCP/UI/SiteTerrainViewModel.cs'),siteBackend=read('MCP/Core/Site/RevitTerrainService.cs');
for(const token of ['CanExecuteCreate','Confirmed','DocumentChanged','Task.Run','ExcavationPreview','Alignment.MaxResidual'])check('site_state_'+token,true,site.includes(token),site.includes(token),'Production state contract');
for(const token of ['TransactionGroup','SurfaceBounds','CanBeExcavatedBy','TOTAL_EXCAVATION_VOLUME','group.RollBack()'])check('site_backend_'+token,true,siteBackend.includes(token),siteBackend.includes(token),'Read-back and rollback');
check('site_no_hidden_placement',false,/MoveElement|RotateElement|SetProjectPosition|Revit.ini/.test(siteBackend),!/MoveElement|RotateElement|SetProjectPosition|Revit.ini/.test(siteBackend),'No building or shared coordinate mutation');
check('session_identity_native_equality',true,read('MCP/Core/DocumentSessionIdentity.cs').includes('.Equals(document)'),read('MCP/Core/DocumentSessionIdentity.cs').includes('.Equals(document)'),'Native identity, no managed ReferenceEquals');
check('ui_logic_testable',false,/Autodesk.Revit|System.Windows.Controls|System.Windows.Data/.test(vm),!/Autodesk.Revit|System.Windows.Controls|System.Windows.Data/.test(vm),'Production controller links into Gate C2');
check('legacy_link_dto_preserved',true,read('MCP/Models/LinkSummary.cs').includes('class LinkSummary'),read('MCP/Models/LinkSummary.cs').includes('class LinkSummary'),'Shared legacy MCP shape survives retirement');
check('runtime_workflow_real_dispatcher',true,read('MCP/Core/CoordinationWorkflowSelfTest.cs').includes('Vm.ScanCommand.Execute')&&read('MCP/UI/RevitCoordinationHost.cs').includes('dispatcher.TrySubmit'),read('MCP/Core/CoordinationWorkflowSelfTest.cs').includes('Vm.ScanCommand.Execute')&&read('MCP/UI/RevitCoordinationHost.cs').includes('dispatcher.TrySubmit'),'Runtime Gate C3 exercises production workflow');
const navigation=read('MCP/Core/CoordinationNavigationService.cs'),control=read('MCP/UI/DetectReviewWorkflowControl.cs');
for(const pattern of [/new Transaction\(/,/CreateIsometric|CreatePerspective|SetSectionBox|\.Set\(/,/ShowElements\(/,/\{3D\}/,/service\.Scan|IntersectCenterline/])
 check('navigation_forbidden_'+pattern.source,false,pattern.test(navigation),!pattern.test(navigation),'UI-only navigation source');
for(const token of ['ui.ActiveView = view','ZoomAndCenterRectangle','GetZoomCorners','ResolveNavigation(document, row.Mep)','ResolveNavigation(document, row.Host)'])
 check('navigation_contract_'+token,true,navigation.includes(token),navigation.includes(token),'Fresh references, explicit view and camera read-back');
check('double_click_row_only',true,control.includes('ContainerFromElement(table, origin) is DataGridRow')&&control.includes('vm.Locate3DCommand.Execute'),control.includes('ContainerFromElement(table, origin) is DataGridRow')&&control.includes('vm.Locate3DCommand.Execute'),'Header double-click must not navigate');
check('navigation_session_transient',false,/Parameter|WriteAllText/.test(vm),!/Parameter|WriteAllText/.test(vm),'View IDs remain session-only');
const seen=new Set();
for(const tool of matrix.RuntimeTools){
 const quarantine=tool.Command==='check_sanitary_fixture_requirements';
 check('runtime_registry_'+tool.Name,true,tool.Dispatcher.length>0||quarantine,tool.Dispatcher.length>0||quarantine,tool.SchemaFile);
 for(const field of tool.Required){
   const evidence=tool.Dispatcher.map(e=>read(e.File)).join('\n');
   // File-level field presence is deliberately only a basic contract check.
   // A forwarded parameter is traced in the selected helper files below.
   const helpers=matrix.Domains.filter(d=>d.Tools.includes(tool.Name)).flatMap(d=>d.BackendFiles);
   const hasField=[evidence,...helpers.map(read)].some(t=>t.includes('"'+field+'"'));
   if(!hasField&&!quarantine) contractWarnings.push({TestName:'field_review_'+tool.Name+'_'+field,Expected:'Backend field evidence',Actual:'Not resolved by basic file scan',Severity:'WARNING',AffectedTool:tool.Name,Evidence:tool.SchemaFile});
 }
}
for(const domain of matrix.Domains){
 if(domain.ReadOnly===true&&domain.BackendFileContainsTransaction)
  contractWarnings.push({TestName:'readonly_transaction_review_'+domain.DomainId,Expected:'Read-only reachable backend',Actual:'Referenced file contains Transaction; method-level reachability needs review',Severity:'WARNING',Evidence:domain.BackendFiles});
 for(const schema of domain.TypeScriptSchemas){if(seen.has(schema.Tool))continue;seen.add(schema.Tool);
  const cmd=schema.Tool==='query_elements_with_filter'?'query_elements':schema.Tool;
  const evidence=domain.CommandEvidence.filter(e=>e.Command===cmd);
  const quarantine=cmd==='check_sanitary_fixture_requirements';
  check('dispatcher_'+schema.Tool,'Dispatcher or named repository quarantine',evidence.length?evidence[0].File:quarantine?'QUARANTINED':'MISSING',evidence.length>0||quarantine,schema.File);
 }
 for(const skill of domain.SkillPath)check('reference_'+domain.DomainId+'_'+path.basename(path.dirname(skill)),true,fs.existsSync(path.join(root,skill)),fs.existsSync(path.join(root,skill)),domain.DomainPath);
}
// Fail enabled coordination workflows if their mandatory schema fields drift.
const openings=matrix.Domains.find(d=>d.DomainId==='mep-opening-candidate-scan').TypeScriptSchemas.find(t=>t.Tool==='scan_opening_candidates');
for(const field of ['mepSource','structureSource','clearanceMm'])check('opening_required_'+field,true,openings.Required.includes(field),openings.Required.includes(field),openings.File);
const report={TestRunId:crypto.randomUUID(),Timestamp:new Date().toISOString(),GateA:tests.every(t=>t.Passed)?'PASS':'FAIL',Passed:tests.filter(t=>t.Passed).length,Failed:tests.filter(t=>!t.Passed).length,
 Warnings:['File-level transaction evidence is not a complete call graph.','Existing sanitary fixture command remains quarantined by repository QA/QC.','Generic backend required-field matching is a basic schema/dispatcher check, not semantic equivalence.',...contractWarnings],Assertions:tests};
const out=path.resolve(process.argv[2]||path.join(root,'test-artifacts'));fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'contracts.json'),JSON.stringify(report,null,2));
fs.writeFileSync(path.join(out,'contracts.md'),'# Static contracts\n\nGate A: '+report.GateA+'\n\n'+tests.map(t=>`- ${t.Passed?'PASS':'FAIL'} ${t.TestName}`).join('\n'));
console.log(JSON.stringify({GateA:report.GateA,Passed:report.Passed,Failed:report.Failed}));process.exitCode=report.Failed?1:0;
