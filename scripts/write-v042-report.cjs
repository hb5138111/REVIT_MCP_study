// Reproducible v0.4.2 release report. Local model paths, backups and runtime files stay ignored.
const fs=require('fs'),path=require('path'),crypto=require('crypto');
const root=path.resolve(__dirname,'..');
const read=p=>JSON.parse(fs.readFileSync(path.resolve(root,p),'utf8'));
const hash=p=>crypto.createHash('sha256').update(fs.readFileSync(path.resolve(root,p))).digest('hex').toUpperCase();
const relative=p=>path.relative(root,path.resolve(root,p)).replaceAll('\\','/');
const artifact='test-artifacts/v042';
const a=read(artifact+'/contracts.json'),b=read(artifact+'/logic.json'),c2=read(artifact+'/workflow-state.json');
const sha=hash('MCP/bin/Release.R26/RevitMCP.dll');
const reversible=process.argv[2]?read(path.join(process.argv[2],'reversible.json')):null;
const c=reversible?.RuntimeDirectory&&fs.existsSync(path.join(reversible.RuntimeDirectory,'runtime.json'))?read(path.join(reversible.RuntimeDirectory,'runtime.json')):null;
const c3=reversible?.RuntimeDirectory&&fs.existsSync(path.join(reversible.RuntimeDirectory,'workflow-runtime.json'))?read(path.join(reversible.RuntimeDirectory,'workflow-runtime.json')):null;
const deployment=process.argv[3]?read(process.argv[3]):null;
const temporaryPending=reversible?.Rollback==='PENDING'||reversible?.Rollback==='WAITING_FOR_NORMAL_REVIT_EXIT';
const backend=read('test-artifacts/source-audit/backend.json');
const sourceMatch=backend.SourceFiles.every(f=>hash(f.Path)===f.Sha256.toUpperCase());
const fullQa=fs.readFileSync(path.join(root,artifact,'qaqc.log'),'utf8');
const finalQa=fs.readFileSync(path.join(root,artifact,'qaqc-final-source.log'),'utf8');
const qaPass=[fullQa,finalQa].every(s=>/FAIL\s*:\s*0/.test(s)&&/RESULT: PASSED/.test(s));
const buildPass=/0 個錯誤|0 Error\(s\)/.test(fs.readFileSync(path.join(root,artifact,'build.log'),'utf8'));
const gates={A:{Status:a.GateA,Passed:a.Passed,Failed:a.Failed},B:{Status:b.GateB,Passed:b.Passed,Failed:b.Failed},
 C:{Status:c?.BuildHash===sha?c.GateC:'NOT_TESTED',Passed:c?.Passed??0,Failed:c?.Failed??0},
 C2:{Status:c2.GateC2,Passed:c2.Passed,Failed:c2.Failed},C3:{Status:c3?.BuildHash===sha?c3.GateC3:'NOT_TESTED',Passed:c3?.Passed??0,Failed:c3?.Failed??0},
 Build:{Status:buildPass&&sourceMatch?'PASS':'FAIL'},QAQC:{Status:qaPass?'PASS':'FAIL'},Rollback:{Status:reversible?.Rollback??'NOT_TESTED'}};
const all=Object.values(gates).every(g=>g.Status==='PASS'&&!(g.Failed>0));
const deployed=all&&deployment?.Status==='PASS'&&deployment.Kind==='FORMAL_V042_RELEASE'&&deployment.DeployedSHA256===sha&&deployment.BuildSHA256===sha;
const sanitize=x=>JSON.parse(JSON.stringify(x).replaceAll(root.replaceAll('\\','\\\\'),'<repository>').replaceAll(root.replaceAll('\\','/'),'<repository>'));
const publicAssertions=items=>sanitize(items).map(a=>['host_levels','document_changed_event_refresh'].includes(a.TestName)?{...a,Actual:'Required fixture-level condition: '+(a.Passed?'PASS':'FAIL'),Evidence:a.Evidence+'; inherited template level names omitted; complete local runtime evidence retained'}:a);
const report={Version:'v0.4.2',Task:'施工協調 UX / 3D Navigation Refinement',Branch:'bim-custom',Timestamp:new Date().toISOString(),
 Status:deployed?'READY_FOR_OPTIONAL_UAT':all?'AWAITING_FORMAL_DEPLOYMENT':gates.Build.Status!=='PASS'||gates.QAQC.Status!=='PASS'?'SOURCE_FAILURE':'RUNTIME_TEST_BLOCKED',
 Gates:gates,BuildSHA256:sha,SourceFingerprintVerified:sourceMatch,FixtureVersion:c?.FixtureVersion??'coordination-2',
 RuntimeEvidence:reversible?.RuntimeDirectory?relative(reversible.RuntimeDirectory):null,RollbackEvidence:process.argv[2]?relative(process.argv[2]):null,
 FormalDeployment:deployment??{Status:'NOT_RUN'},
 RootCause:'Unqualified ShowElements did not preserve 3D; selection read-back alone did not verify active view or interaction focus.',
 Navigation:['Existing usable 3D only; current then session then deterministic orthographic preference','UI-only ActiveView switch and UIView focus; no model transaction','Linked IDs resolve to host Link instance; camera focuses host-coordinate interaction','Session-only previous view; next/previous auto locate can be disabled'],
 PreservedShared:['ExternalEvent / Busy gate','Native document/session identity','LinkSummary DTO for MCP','Typed geometry and element navigation','Productization Registry','Self-Test Lab / installer'],
 CapabilityBoundary:['Centerline vs solid only; excludes fittings, insulation and grazing collisions','BeamPenetration is always review-only, not RC/SC/SRC approval','Sleeve classification and structural approval disabled','Opening dimensions require explicit per-project session clearance','Unknown opening bottom remains null','Multiple solid intersections produce one row; length and point identify first segment with warning','Arbitrary rotation/mirroring not certified by this fixture'],
 Assertions:{C:sanitize(c?.Assertions??[]),C2:c2.Assertions,C3:publicAssertions(c3?.Assertions??[])},
 NextRecommendedBatch:deployed?'Optional short UAT only; stop until a new task is authorized.':all?'Formal deployment then checkpoint commits and origin/bim-custom push.':'Complete failed runtime assertions using the explicitly selected isolated .rte template with existing 3D views; retain read-only navigation and mandatory rollback. No release commit or push until every gate passes.',
 FinalCommit:{Value:'SELF_COMMIT',Resolve:'git log -1 --format=%H -- docs/productization/development-state.json',Reason:'A tracked file cannot contain its own commit hash. Literal receipt is local test-artifacts/v042/final.json after push.'}};
fs.writeFileSync(path.join(root,'docs/productization/v042-report.json'),JSON.stringify(report,null,2)+'\n');
fs.writeFileSync(path.join(root,'docs/productization/v042-report.md'),'# v0.4.2 施工協調工作台\n\nStatus: '+report.Status+'\n\n'+Object.entries(gates).map(([k,v])=>`- Gate ${k}: ${v.Status}${v.Passed!==undefined?' — '+v.Passed+' passed / '+v.Failed+' failed':''}`).join('\n')+'\n\nBuild SHA256: `'+sha+'`\n\n正式部署: '+(deployed?'PASS':'NOT_RELEASED')+'\n\n'+report.CapabilityBoundary.map(s=>'- '+s).join('\n')+'\n\n[Failure analysis](v042-navigation-analysis.md) · [Assertions and release evidence](v042-report.json)\n');
fs.writeFileSync(path.join(root,'docs/productization/development-state.json'),JSON.stringify({SchemaVersion:2,Task:'v0.4.2',Branch:'bim-custom',Status:report.Status,UpdatedAt:report.Timestamp,Gates:gates,BuildSHA256:sha,InstalledVersion:deployed?'v0.4.2':temporaryPending?'v0.4.2 TEMPORARY TEST LOAD; ROLLBACK PENDING':'v0.4.1 (stable rollback target)',ProductionDeployment:deployed?'PASS — FORMAL_V042_RELEASE':'NOT_RELEASED',InstalledSHA256:deployed?sha:temporaryPending?reversible?.TemporarySHA256:reversible?.RestoredSHA256??null,FinalCommit:report.FinalCommit,SourceFingerprints:backend.SourceFiles.map(f=>({Path:f.Path,SHA256:hash(f.Path)})),NextAction:temporaryPending?'Resolve Revit unsigned add-in dialog: Load Once to run fixture, or normal exit; then complete full snapshot rollback before any release.':report.NextRecommendedBatch,Report:'docs/productization/v042-report.json'},null,2)+'\n');
console.log(JSON.stringify({Status:report.Status,Gates:gates,BuildSHA256:sha}));
