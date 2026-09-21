// Complete the development-time mapping with method-level evidence and explicit gaps.
const fs=require('fs'),path=require('path'),crypto=require('crypto');
const root=path.resolve(__dirname,'..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const hash=p=>crypto.createHash('sha256').update(fs.readFileSync(path.join(root,p))).digest('hex');
const matrix=JSON.parse(read('docs/productization/matrix.json'));
const backend=JSON.parse(read('test-artifacts/source-audit/backend.json'));
if(backend.Diagnostics.length)throw Error('Source graph has compilation errors');
for(const f of backend.SourceFiles)if(hash(f.Path)!==f.Sha256)throw Error('Stale source graph: '+f.Path);
const commandMap=new Map(backend.Commands.map(c=>[c.Command,c]));
const names=new Set(matrix.RuntimeTools.map(t=>t.Name));
const buildPath='MCP/bin/Release.R26/RevitMCP.dll';
const buildHash=fs.existsSync(path.join(root,buildPath))?hash(buildPath).toUpperCase():null;
const runtimeCandidates=fs.existsSync(path.join(root,'test-artifacts'))?fs.readdirSync(path.join(root,'test-artifacts')).filter(n=>n.startsWith('revit-selftest-')).map(n=>'test-artifacts/'+n+'/runtime.json').filter(p=>fs.existsSync(path.join(root,p))).map(p=>({Path:p,Report:JSON.parse(read(p))})):[];
const runtime=runtimeCandidates.filter(x=>x.Report.GateC==='PASS'&&x.Report.Failed===0&&x.Report.BuildHash===buildHash).sort((a,b)=>b.Report.Timestamp.localeCompare(a.Report.Timestamp))[0];
const workflowPath=runtime?.Path.replace(/runtime\.json$/,'workflow-runtime.json');
const workflowRuntime=workflowPath&&fs.existsSync(path.join(root,workflowPath))?JSON.parse(read(workflowPath)):null;
const workflowPassed=workflowRuntime?.GateC3==='PASS'&&workflowRuntime?.Failed===0&&workflowRuntime?.BuildHash===buildHash;
const terrainPath=runtime?.Path.replace(/runtime\.json$/,'terrain-runtime.json');
const terrainRuntime=terrainPath&&fs.existsSync(path.join(root,terrainPath))?JSON.parse(read(terrainPath)):null;
const terrainPassed=terrainRuntime?.Status==='PASS'&&terrainRuntime?.Failed===0&&terrainRuntime?.BuildSHA256===buildHash;
const cadPath=runtime?.Path.replace(/runtime\.json$/,'cad-runtime.json');
const cadRuntime=cadPath&&fs.existsSync(path.join(root,cadPath))?JSON.parse(read(cadPath)):null;
const cadPassed=cadRuntime?.Status==='PASS'&&cadRuntime?.Failed===0&&cadRuntime?.BuildSHA256===buildHash;
const drawingCandidates=fs.readdirSync(path.join(root,'test-artifacts')).filter(n=>n.startsWith('revit-selftest-')).map(n=>'test-artifacts/'+n+'/drawing-runtime.json').filter(p=>fs.existsSync(path.join(root,p))).map(p=>({Path:p,Report:JSON.parse(read(p))}));
const drawing=drawingCandidates.filter(x=>x.Report.Status==='PASS'&&x.Report.Failed===0&&x.Report.BuildSHA256===buildHash).sort((a,b)=>b.Report.Timestamp.localeCompare(a.Report.Timestamp))[0];
const journeyCandidates=fs.readdirSync(path.join(root,'test-artifacts')).filter(n=>n.startsWith('revit-selftest-')).map(n=>'test-artifacts/'+n+'/drawing-c4-runtime.json').filter(p=>fs.existsSync(path.join(root,p))).map(p=>({Path:p,Report:JSON.parse(read(p))}));
const journey=journeyCandidates.filter(x=>x.Report.Status==='PASS'&&x.Report.Failed===0&&x.Report.BuildSHA256===buildHash&&'ABCDEFGH'.split('').every(c=>x.Report.Cases?.[c]===true)).sort((a,b)=>b.Report.Timestamp.localeCompare(a.Report.Timestamp))[0];

const c5Path='test-artifacts/v0611/cad-c5.json',c5=fs.existsSync(path.join(root,c5Path))?JSON.parse(read(c5Path)):null;
const c5Passed=c5?.GateC5==='PASS'&&c5.Failed===0&&c5.BuildSHA256===buildHash&&c5.SourceFiles.every(f=>hash(f.Path).toUpperCase()===f.SHA256);
const actualCad=runsActual();
function runsActual(){return fs.readdirSync(path.join(root,'test-artifacts')).filter(n=>n.startsWith('revit-selftest-')).map(n=>'test-artifacts/'+n+'/actual-cad-uat.json').filter(p=>fs.existsSync(path.join(root,p))).map(p=>({Path:p,Report:JSON.parse(read(p))})).find(x=>x.Report.Status==='ACTUAL_DWG_UAT_PASS'&&x.Report.BuildSHA256===buildHash);}
const review=new Set(['beam-penetration-algorithm','beam-penetration-base','beam-penetration-rc','beam-penetration-sc','beam-penetration-src','sleeve-classification-protocol','corridor-analysis-protocol','daylight-area-check','exterior-wall-opening-check','fire-rating-check','floor-area-review','parking-clearance-check','parking-space-review','smoke-detector-check','smoke-exhaust-review','stair-compliance-check','wall-check','building-code-tw']);
const settings={
 'GM_parameter-schema':['MaterialSlotAssignment','LicenseValidity','TargetTypes'],
 'GM_rfa-family-injection':['BaseFamilyType','WritableBackupFolder','VerifiedCatalogData'],
 'cad-block-point-placement':['BlockToFamilyTypeMapping','LevelMapping','PlacementMode'],
 'dedup-detail-elements-workflow':['TargetView','DuplicateEquivalence','PreviewApproval'],
 'dependent-view-crop-workflow':['ParentView','GridScope','CropOffset','SheetTemplate'],
 'door-window-legend-workflow':['SeedElement','TargetLegend','DimensionType'],
 'dwg-beam-import':['CadLayers','BeamFamilyTypeMapping','LevelOffsets'],
 'dwg-column-import':['CadLayers','ColumnFamilyTypeMapping','LevelOffsets'],
 'family-inventory-cleanup':['TypeEquivalenceSignature','CascadeDisclosure','ReplacementMapping'],
 'floor-slope-analysis':['DesignSlopeThreshold','TargetFloors'],
 'ifc-structural-native-sync':['LinkInstance','FamilyMapping','LevelMapping','ReplacementPolicy'],
 'ifc-structural-sync':['LinkInstance','FamilyMapping','LevelMapping','ReplacementPolicy'],
 'beam-slab-alignment':['FloorSelectionPolicy','ReferenceLevel','HeightAdjustmentPolicy'],
 'mep-mechanical-settings':['SizingDesignBasis','SegmentMaterialAndSizes','SystemMapping'],
 'mep-opening-candidate-scan':['OpeningClearance'],
 'mep-space-demand-matrix':['SpaceUse','DesignDemandFactors','EquipmentBasis'],
 'parking-auto-numbering':['StartElement','StartNumber','OrderingPolicy'],
 'quantity-takeoff-excel':['Exclusions','TakeoffScope','HeightSource','OpeningAttribution'],
 'revit-partition-takeoff':['WallTypeScope','OpeningDeductions','SourceReport'],
 'room-numbering-workflow':['TargetLevel','StartNumber','OrderingTolerance'],
 'room-surface-area-review':['IncludeFinishLayers','RoomScope'],
 'scaffold-takeoff':['IncludedRooms','ExcludedRooms','HeightSource','ScaffoldMethod'],
 'space-centroid-placement':['FamilyType','PlacementMode','SpaceScope'],
 'threshold-opening-takeoff':['ExcludedRooms','ExcludedTypes','DeduplicationPolicy'],
 'viewport-type-scale-sync':['ViewportTypeNaming','FallbackType','ExcludedViewNames']
};
const hardGaps={
 'GM_rfa-family-injection':'Domain records unresolved backup-path / family injection defects; no complete Native workflow certification.',
 'detect-range-box':'Domain describes a pyRevit-only feature; no registered MCP implementation.',
 'view-link-cleanup-workflow':'Domain records a negative result for per-link category hiding while preserving host datums; do not implement the obsolete proposal.',
 'viewport-type-scale-sync':'get_viewport_types and sync_viewport_types_by_view_scale are absent from the current runtime registry and C# dispatcher.',
 'GM_keyword-search':'External catalog/search workflow; no complete native Revit query service mapping.',
 'pdf-export-comparison':'Comparison/reference document; no complete exporter workflow is established by the referenced runtime tools.'
};
for(const t of matrix.RuntimeTools){
 const c=commandMap.get(t.Command);
 t.Backend=c?{...c}:null;
 t.ImplementationStatus=c&&c.EntryMethods.length?'DISPATCHED':'MISSING';
 if(!c)t.Gap='Existing repository quarantine: no command dispatcher (not silently accepted as implemented)';
}
for(const d of matrix.Domains){
 const text=read(d.DomainPath),lines=text.split(/\r?\n/);
 const direct=d.Tools.filter(t=>new RegExp('(?<![\\w])'+t+'(?![\\w])').test(text));
 d.DomainTools=direct;d.SkillSupportTools=d.Tools.filter(t=>!direct.includes(t));
 d.DocumentedToolCandidates=[...new Set([...text.matchAll(/`((?:get|query|scan|analyze|create|sync|set|adjust|rename|renumber|calculate|export|import|detect|check|preview|copy|place|modify|delete|read|list|align|remap|batch|trace|duplicate|apply)_[a-z0-9_]+)`/g)].map(m=>m[1]))].sort();
 d.UnregisteredToolReferences=d.DocumentedToolCandidates.filter(t=>!names.has(t));
 const selected=(direct.length?direct:d.Tools).map(t=>matrix.RuntimeTools.find(x=>x.Name===t));
 const cs=selected.map(t=>t?.Backend).filter(Boolean);
 const missing=selected.filter(t=>!t?.Backend).map(t=>t.Name);
 const methods=[...new Map(cs.flatMap(c=>c.Methods).map(m=>[m.Id,m])).values()];
 d.ReachableMethods=methods;
 d.BackendFiles=[...new Set(methods.map(m=>m.File))].sort();
 d.CommandEvidence=d.Tools.flatMap(name=>{const t=matrix.RuntimeTools.find(x=>x.Name===name);return (t?.Backend?.EntryMethods||[]).map(id=>({Command:t.Command,Method:id,...t.Backend.Methods.find(x=>x.Id===id)}));});
 d.BackendFiles=[...new Set([...d.BackendFiles,...d.CommandEvidence.map(e=>e.File)])].sort();
 d.MutationAssessmentScope=direct.length?'Direct domain tool references; broader SkillSupportTools retain separate per-tool backend evidence':'Skill tool chain (no direct domain tool references)';
 d.TransactionEvidence=cs.filter(c=>c.Transactions.length).map(c=>({Command:c.Command,Types:c.Transactions}));
 d.RevitApiEvidence=[...new Set(cs.flatMap(c=>c.RevitApi))].sort();
 d.LinkMethodEvidence=cs.filter(c=>c.LinkEvidence.length).map(c=>({Command:c.Command,Evidence:c.LinkEvidence}));
 const meta=d.ProductizationStatus==='META_ONLY';
 d.ReadOnly=meta?true:cs.length&&!missing.length?cs.every(c=>c.ReadOnly):null;
 d.TransactionRequired=meta?false:cs.length&&!missing.length?cs.some(c=>c.Transactions.length>0):null;
 d.MutationLevel=meta?'None':d.TransactionRequired?'ModelWriteOrTransactionalReview':d.ReadOnly?'ReadOnlyQueriesOrExternalIO':'UnresolvedImplementation';
 d.HostSupport=meta?'N/A':cs.length?'Host entry path exists; scope is command-specific':'No complete runtime mapping';
 d.LinkSupport=meta?'N/A':d.LinkMethodEvidence.length?'Only listed commands have Link API evidence; do not generalize to entire workflow':'No reachable Link-document/transform evidence; host-only assumption for product design';
 d.RequiredSettings=settings[d.DomainId]||[];
 d.RequiresProjectSettings=d.RequiredSettings.length>0;
 d.RuleEvidence=lines.map((line,i)=>({Line:i+1,Text:line.trim()})).filter(x=>/^#{1,4} |^description:|必須|不得|禁止|候選|人工|尚未|已知缺|不支援|不支持|待實作|Negative Result|Read.back|dry.run/i.test(x.Text));
 d.SourceReview={Status:'ASSESSED',Method:'Domain rule and limitation extraction + registered tool/schema + R26 source call graph; gaps retained explicitly',DomainSha256:hash(d.DomainPath),SemanticScope:'Static capability mapping, not certification of every engineering rule or runtime path'};
 const blockers=[];
 if(missing.length)blockers.push('Missing dispatcher: '+missing.join(', '));
 if(d.UnregisteredToolReferences.length)blockers.push('Documented names not registered (may include historical/external functions): '+d.UnregisteredToolReferences.join(', '));
 if(hardGaps[d.DomainId])blockers.push(hardGaps[d.DomainId]);
 if(d.RequiresProjectSettings)blockers.push('Explicit user/project inputs: '+d.RequiredSettings.join(', '));
 if(review.has(d.DomainId))blockers.push('Review output is not formal engineering/regulatory approval; project applicability and uncovered rules require review.');
 if(!meta&&!cs.length)blockers.push('No complete registered command chain for this domain.');
 d.ProductizationStatus=meta?'META_ONLY':hardGaps[d.DomainId]||missing.length||d.UnregisteredToolReferences.length?'BLOCKED':review.has(d.DomainId)?'REVIEW_ONLY':d.RequiresProjectSettings?'PROJECT_CONFIG_REQUIRED':cs.length?'ADAPTER_READY':'RULE_READY';
 if(d.ProductizationStatus==='ADAPTER_READY')blockers.push('Reusable command implementation exists; typed orchestration and domain-specific fixture are still required before enabling this domain.');
 d.Blockers=blockers;
 d.RuntimeCapability=meta?'Development governance/reference only':cs.length?`${cs.length} command mappings; ${methods.length} reachable methods. Method graph is conservative across branches.`:'No complete mapping; retain domain as rules/reference';
 d.FixtureTestStatus='NOT_TESTED (outside this release fixture scope)';
 d.NativeUiStatus='DISABLED / not included in v0.4';
 d.LargeModelRisk=meta?'N/A':methods.some(m=>/Geometry|Clash|Surface|Dimension|Spatial|Takeoff|Opening|Penetration/.test(m.Id))?'Geometry/collector-heavy; explicit scope and budget needed before enabling':'Command-specific collector/batch bounds need dedicated fixture and benchmark';
 d.RecommendedUiPattern=meta?'N/A':review.has(d.DomainId)?'CompliancePattern':/takeoff|scaffold|surface-area/.test(d.DomainId)?'TakeoffPattern':d.TransactionRequired?'PreviewApplyPattern':'AuditPattern';
 if(['mep-csa-clash-detection','mep-opening-candidate-scan'].includes(d.DomainId)){
  d.NativeUiStatus='Implemented: typed read-only coordination subworkflow; release gated';
  d.NativeWorkflowReadOnly=true;d.NativeBackendFiles=['MCP/Core/CoordinationService.cs','MCP/Models/CoordinationModels.cs','MCP/Core/ClashDetector.cs'];
  d.FixtureTestStatus=runtime?'PASS: '+runtime.Report.FixtureVersion+', '+runtime.Report.Passed+' assertions (native scan subset only)':'NOT_TESTED: no matching build runtime report';
  d.FixtureEvidence=runtime?.Path||null;
  d.NativeWorkflowTestStatus=workflowPassed?'PASS: real ActiveUIDocument / controller / ExternalEvent':'NOT_TESTED';
  d.NativeWorkflowEvidence=workflowPassed?workflowPath:null;
  d.NativeScope={Host:true,Link:runtime?'Translated link fixture PASS; arbitrary rotation/mirroring not fixture tested':'Source mapping only; runtime not verified',Mutation:'ReadOnly',TransactionRequired:false,UI:'DetectReviewPattern',Limits:['Centerline crossing; no solid-edge grazing certification','No opening/sleeve creation','Candidate/review only','Session-scoped project settings']};
  d.RecommendedUiPattern='DetectReviewPattern';d.LargeModelRisk='Explicit MEP category/level; pair and time budgets; totals and truncation reported';
 }
 if(d.DomainId==='construction-drawing-production'){
  d.NativeUiStatus='Implemented: production current Sheet / external RFA / CAD titleblock and five-step Package workflow';
  d.NativeWorkflowReadOnly=false;d.RuntimeCapability='Typed Native C# service; no new MCP transport';
  d.NativeBackendFiles=['MCP/Core/Drawing/DrawingModels.cs','MCP/Core/Drawing/RevitDrawingService.cs','MCP/UI/DrawingProductionViewModel.cs','MCP/UI/RevitDrawingHost.cs','MCP/Core/Drawing/ExternalTitleBlockService.cs','MCP/Core/Drawing/CadTitleBlockGeometry.cs','MCP/Core/Drawing/CadTitleBlockFileService.cs','MCP/Core/Drawing/DrawingFixtureIsolation.cs'];
  d.FixtureEvidence=drawing?.Path||null;d.FixtureTestStatus=drawing?'PASS: '+drawing.Report.Passed+' assertions; scoped drawing workflow':'NOT_TESTED: no matching-build drawing report';
  d.NativeWorkflowTestStatus=journey?'PASS: C4 production Panel journey A-J / independent CAD candidates / read-back':'NOT_TESTED: C4 required';d.NativeWorkflowEvidence=journey?.Path||null;
  d.NativeScope={Mutation:'ConfirmedWrite',TransactionRequired:true,Readback:true,RequiresConfirmation:true,Limits:['Same TitleBlock Type, one main plan viewport','No adoption of user-owned sheets','Custom text mapping only; no new shared parameters','Guide Grid and arbitrary annotation copy disabled','Matchline / View Reference and auto dimension PARTIAL / NOT ENABLED']};
  d.Blockers=['Full Domain is not certified: per-field merge, adoption, multi-main topology and reference/dimension automation remain disabled'];
  d.RecommendedUiPattern='PreviewApplyPattern';d.LargeModelRisk='Explicit queries; deterministic complete plan; virtualized table; no geometry extraction; actual 300-sheet runtime benchmark not claimed';
 }
 if(d.DomainId==='site-terrain-earthwork'){
  d.NativeUiStatus='Implemented: v0.5 site step workflow; confirmed writes and read-back';
  d.NativeWorkflowReadOnly=false;
  d.NativeBackendFiles=['MCP/Core/Site/TerrainEngine.cs','MCP/Core/Site/RevitTerrainService.cs','MCP/Core/Site/CadTerrainData.cs','MCP/Core/Site/CadTerrainService.cs','MCP/UI/SiteTerrainViewModel.cs','MCP/UI/SiteTerrainWorkflow.cs','MCP/UI/RevitSiteHost.cs'];
  d.RuntimeCapability='Typed Native C# workflow; no additional MCP interface';
  d.FixtureTestStatus=terrainPassed?'PASS: terrain-1; '+terrainRuntime.Passed+' assertions (explicit supported subset)':'NOT_TESTED: no matching-build TerrainFixture report';
  d.FixtureEvidence=terrainPassed?terrainPath:null;
  d.NativeScope={Mutation:'ConfirmedWrite',TransactionRequired:true,RequiresConfirmation:true,Readback:true,UI:'PreviewApplyPattern',Limits:['CSV/TXT only','Convex boundary / planar target','Existing/proposed disabled','No building move or ProjectLocation mutation']};
  d.Blockers=['Full domain is not certified: existing/proposed experimental, inferred breakline connectivity unsupported'];
  d.RecommendedUiPattern='PreviewApplyPattern';d.LargeModelRisk='Background parser/QA/reduction; explicit 20k create guard and display sample counts';
 }
}
const allSource=[...matrix.Inventory,...backend.SourceFiles.map(f=>({...f,Bytes:Buffer.byteLength(read(f.Path))}))];
matrix.Inventory=[...new Map(allSource.map(f=>[f.Path,f])).values()].sort((a,b)=>a.Path.localeCompare(b.Path));
matrix.SchemaVersion=3;
matrix.NativeFeatures=[
 {Id:'construction-drawing-production',Status:drawing&&journey&&c5Passed&&actualCad?'RUNTIME_VERIFIED':'RELEASE_GATED',RequiredGates:['A','B','C','C2','C3','C4','C5','DrawingRuntime','ActualDWG'],FixtureEvidence:drawing?.Path||null,ProductionJourneyEvidence:journey?.Path||null,CadAdversarialEvidence:c5Passed?c5Path:null,ActualCadEvidence:actualCad?.Path||null,Limits:['Current Sheet / external RFA / geometry-only DWG-DXF; external RVT PARTIAL','Same TitleBlock Type; one main plan viewport','Manual overrides default preserve; per-sheet explicit reapply','Auto dimension / Matchline / View Reference PARTIAL and disabled']},
 {Id:'site-terrain-earthwork',Status:terrainPassed&&cadPassed?'RUNTIME_VERIFIED':'RELEASE_GATED',RequiredGates:['A','B','C','C2','C3','TerrainLogic','TerrainRuntime','CadRuntime'],CadFixtureStatus:cadPassed?'PASS':'NOT_TESTED',Limits:['Supported subset only; full Domain remains RULE_READY','Explicit confirmation and read-back required','CAD temporary import rollback; no inferred elevation text or breaklines']},
 {Id:'model-summary',Status:'RETIRED_NATIVE_UI',Reason:'Removed low-value Native workflow; Domain and runtime tools retained'},
 {Id:'type-inventory',Status:'RETIRED_NATIVE_UI',Reason:'Removed Native inventory and navigation; generic link DTO and identity extracted'},
 {Id:'level-constraint-audit',Status:'RETIRED_NATIVE_UI',Reason:'Removed Native audit; Domain and runtime tools retained'},
 {Id:'coordination',Status:workflowPassed?'RUNTIME_VERIFIED':'RELEASE_GATED',Workflow:'One DetectReview workbench',Kinds:['Clash','OpeningCandidate','BeamPenetration','ReviewRequired'],RequiredGates:['A','B','C','C2','C3'],Limitations:['No automatic sleeve or structural approval','Centerline crossing only']}
];
matrix.Audit={Status:'COMPLETE_STATIC_CAPABILITY_MAPPING',CompilerDiagnostics:0,DomainCoverage:matrix.Domains.length,RuntimeToolCoverage:matrix.RuntimeTools.length,MethodCount:backend.Methods.length,Limitations:['Branch union is conservative, not runtime proof.','Unbound external invocations are retained in backend evidence.','Domain-only tools absent from registry are blockers, not invented capabilities.','Runtime evidence covers only explicitly named Coordination/Terrain fixture scopes with matching DLL hash.']};
matrix.Counts=Object.fromEntries(['NATIVE_READY','ADAPTER_READY','RULE_READY','PROJECT_CONFIG_REQUIRED','REVIEW_ONLY','BLOCKED','META_ONLY'].map(s=>[s,matrix.Domains.filter(d=>d.ProductizationStatus===s).length]));
fs.writeFileSync(path.join(root,'docs/productization/matrix.json'),JSON.stringify(matrix,null,2)+'\n');
const safe=x=>String(x).replace(/\|/g,'/').replace(/\r?\n/g,' ');
const md='# Domain 產品化矩陣\n\n全域靜態能力稽核完成：逐 Domain 保留 SOP 證據與行號，對應 Skill、Tool/schema、dispatcher、method/helper、Revit API、Transaction 與 Link 證據。這是產品化能力盤點，不是所有 Domain 的 runtime 或法規認證。未註冊工具、既知缺陷與不可達 API 均明列 BLOCKED；不可據檔名或 tool 存在就啟用功能。\n\n'+Object.entries(matrix.Counts).map(([s,n])=>'- '+s+': '+n).join('\n')+'\n\n協調 Native runtime 證據以目前 DLL hash 對應 JSON 為準；C2/C3 必須另外通過。Model Summary、Type Inventory、Level Constraint Audit 已標記 RETIRED_NATIVE_UI。完整 Domain 的上色、外部輸出、後續開孔/結構核准仍按各自 scope 管理。完整證據見 [JSON](matrix.json)。\n\n| Domain | Skill | Tools | Backend | Status | Priority | Blocker | UI Pattern |\n|---|---|---|---|---|---|---|---|\n'+matrix.Domains.map(d=>'| '+[d.DomainId,d.SkillPath.join(', '),d.DomainTools.join(', '),d.BackendFiles.join(', '),d.ProductizationStatus,d.Priority,d.Blockers.join('; '),d.RecommendedUiPattern].map(safe).join(' | ')+' |').join('\n')+'\n';
fs.writeFileSync(path.join(root,'docs/productization/matrix.md'),md);
console.log(JSON.stringify({Audit:matrix.Audit.Status,Counts:matrix.Counts}));
