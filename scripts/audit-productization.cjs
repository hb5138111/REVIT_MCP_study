// Development-time inventory only. No Markdown is read by the Revit runtime.
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const ts = require('../MCP-Server/node_modules/typescript');
const root = path.resolve(__dirname, '..');
const read = p => fs.readFileSync(path.join(root,p),'utf8');
const files = (dir, recursive=false) => fs.readdirSync(path.join(root,dir),{withFileTypes:true}).flatMap(e => e.isDirectory() ? (recursive ? files(dir+'/'+e.name,true) : []) : [dir+'/'+e.name]);
const domains = [...files('domain'),...files('domain/references')].filter(p=>p.endsWith('.md')).sort();
const skills = files('.claude/skills',true).filter(p=>p.endsWith('/SKILL.md')).sort();
const sources = [...files('MCP/Core'),...files('MCP/Core/Commands')].filter(p=>p.endsWith('.cs')).sort();
const toolFiles = files('MCP-Server/src/tools').filter(p=>p.endsWith('.ts')).sort();
const all = [...domains,...skills,...sources,...toolFiles];
const texts = Object.fromEntries(all.map(p=>[p,read(p)]));
const toolDefinitions = {};
// Evaluate only literal TS tool declarations through the TypeScript AST; never execute source.
function literal(n) {
 if (ts.isStringLiteral(n)||ts.isNumericLiteral(n)) return ts.isNumericLiteral(n)?Number(n.text):n.text;
 if (n.kind===ts.SyntaxKind.TrueKeyword) return true;
 if (n.kind===ts.SyntaxKind.FalseKeyword) return false;
 if (ts.isArrayLiteralExpression(n)) return n.elements.map(literal);
 if (ts.isObjectLiteralExpression(n)) return Object.fromEntries(n.properties.filter(ts.isPropertyAssignment).map(p=>[p.name.text,literal(p.initializer)]));
 return null;
}
for(const p of toolFiles){
 const ast=ts.createSourceFile(p,texts[p],ts.ScriptTarget.Latest,true);
 function walk(n){if(ts.isObjectLiteralExpression(n)){const o=literal(n);if(o.name&&o.inputSchema) toolDefinitions[o.name]={...o,SchemaFile:p};}ts.forEachChild(n,walk);}walk(ast);
}
const registeredModules=[...texts['MCP-Server/src/tools/index.ts'].matchAll(/from "\.\/(.+?)\.js"/g)].map(m=>'MCP-Server/src/tools/'+m[1]+'.ts');
const names=Object.keys(toolDefinitions).filter(n=>registeredModules.includes(toolDefinitions[n].SchemaFile));
const commandFor=n=>n==='query_elements_with_filter'?'query_elements':n;
const clean=s=>s.replace(/\/\*[\s\S]*?\*\//g,'').replace(/^\s*\/\/.*$/gm,'');
const commandEvidence={};
for(const p of sources){const t=clean(texts[p]);for(const m of t.matchAll(/case\s+"([\w_-]+)"\s*:/g)){
 const body=t.slice(m.index,t.indexOf('break;',m.index)+6||m.index+500);
 (commandEvidence[m[1]]??=[]).push({File:p,Dispatch:body.slice(0,1400)});
}}
const mentions=(s,n)=>new RegExp('(?<![\\w])'+n+'(?![\\w])').test(s);
const meta=new Set(['README','qa-checklist','lessons','anti-lessons','frontmatter-standard','path-maintenance-qa','session-context-guard','skill-authoring-standard','tool-capability-boundary','core-reload-boundary','domain-flow-visualization','mep-extension-guide','local-update-workflow']);
const coordination=new Set(['mep-csa-clash-detection','mep-opening-candidate-scan']);
const matrix=domains.map(p=>{
 const t=texts[p],id=path.basename(p,'.md');
 const related=domains.filter(q=>q!==p&&(t.includes(path.basename(q))||t.includes('`'+path.basename(q,'.md')+'`')));
 const linkedSkills=skills.filter(q=>texts[q].includes(path.basename(p))||t.includes(path.basename(path.dirname(q))+'/SKILL.md')||t.includes('- '+path.basename(path.dirname(q))+'\n'));
 const combined=t+'\n'+linkedSkills.map(q=>texts[q]).join('\n');
 const tools=names.filter(n=>mentions(combined,n));
 const commands=tools.map(commandFor);
 const evidence=commands.flatMap(n=>(commandEvidence[n]||[]).map(e=>({Command:n,...e})));
 const helperNames=new Set(evidence.flatMap(e=>[...e.Dispatch.matchAll(/(?:new\s+|_)([A-Z]\w+|[a-z]\w+)\s*[.(]/g)].flatMap(m=>[m[1],m[1][0].toUpperCase()+m[1].slice(1)])));
 const backend=new Set(evidence.map(e=>e.File));
 for(const q of sources){if(t.includes(path.basename(q))||[...helperNames].some(h=>texts[q].includes('class '+h)))backend.add(q);}
 const primary=tools.filter(n=>!/^get_|^query_|^select_|^zoom_/.test(n));
 const missing=commands.filter(n=>!commandEvidence[n]);
 const isMeta=meta.has(id);
 const review=/beam-penetration|sleeve-classification/.test(id);
 const settings=id==='mep-opening-candidate-scan'?['OpeningClearance']:/project.specific|專案.*(?:確認|設定|標準)|不得.*猜/.test(t)?['DomainSpecificInputs']:[];
 let status=isMeta?'META_ONLY':review?'REVIEW_ONLY':missing.length?'BLOCKED':primary.length?'ADAPTER_READY':'RULE_READY';
 if(settings.length&&!coordination.has(id)&&status==='ADAPTER_READY')status='PROJECT_CONFIG_REQUIRED';
 // Full-domain maturity is conservative: native scan is only a subworkflow of the clash SOP.
 const blockers=isMeta?[]:missing.map(n=>'No dispatcher: '+n);
 if(!isMeta&&!primary.length)blockers.push('No complete purpose-specific runtime mapping established');
 if(review)blockers.push('Complete solid classification / engineering rule coverage is not established');
 if(status==='ADAPTER_READY')blockers.push('Domain workflow includes transport-bound or private backend; typed adaptation required');
 if(settings.length)blockers.push('Explicit project settings required: '+settings.join(', '));
 const tx=[...backend].some(q=>/new\s+(?:Sub)?Transaction\s*\(/.test(texts[q]));
 const readOnly=id==='mep-opening-candidate-scan'?true:null;
 return {DomainId:id,DomainPath:p,Description:(t.match(/^description:\s*["']?(.+)$/m)||[])[1]?.replace(/["']$/,'')||t.match(/^# (.+)/m)?.[1]||id,
 Tags:(t.match(/tags:\s*\[(.*?)\]/)||[])[1]?.split(',').map(v=>v.trim())||[],RelatedDomains:related,SkillPath:linkedSkills,
 Tools:tools,CommandNames:commands,TypeScriptSchemas:tools.map(n=>({Tool:n,File:toolDefinitions[n].SchemaFile,Required:toolDefinitions[n].inputSchema.required||[],Schema:toolDefinitions[n].inputSchema})),
 BackendFiles:[...backend],CommandEvidence:evidence,RevitApiEvidence:[...backend].flatMap(q=>[...new Set(texts[q].match(/\b(?:FilteredElementCollector|Transaction|Solid|RevitLinkInstance|ElementTransformUtils|ViewSheet|Wall|Floor|Pipe|Duct)\b/g)||[])].map(a=>q+': '+a)),
 MutationLevel:isMeta?'None':readOnly?'ReadOnlyScan':id==='mep-csa-clash-detection'?'Mixed: read-only scan, transactional coloring, external export':'Needs per-tool review',ReadOnly:readOnly,TransactionRequired:readOnly?false:null,
 NativeWorkflowReadOnly:coordination.has(id)?true:null,
 BackendFileContainsTransaction:tx,HostSupport:coordination.has(id)?'Supported':'Not runtime verified',LinkSupport:coordination.has(id)?'Transformed instance coordinates; fixture required':'Not runtime verified',
 RequiresProjectSettings:settings.length>0,RequiredSettings:settings,RuntimeCapability:coordination.has(id)?'Typed native scan; legacy JSON preserved':primary.length?'Dispatcher-backed; semantics require review':'No complete mapping',
 NativeUiStatus:coordination.has(id)?'Implemented; release gated':'Not enabled',FixtureTestStatus:'NOT_RUN',ProductizationStatus:status,
 Priority:isMeta?'P4':/clash|opening|penetration|sleeve|takeoff|scaffold|drawing|sheet|viewport|dimension|legend|curtain-wall-elevation|rc-filled/.test(id)?'P1':/check|review|audit|inventory|cleanup|number|query|room|datum/.test(id)?'P2':'P3',
 Blockers:blockers,RecommendedUiPattern:isMeta?'—':coordination.has(id)?'DetectReviewPattern':review?'CompliancePattern':/takeoff|quantity/.test(id)?'TakeoffPattern':/create|generation|import|sync/.test(id)?'PreviewApplyPattern':'AuditPattern',
 LargeModelRisk:coordination.has(id)?'Bounded pair budget, explicit category and MEP level required':'Not benchmarked',Testability:coordination.has(id)?'Pure logic + disposable Revit fixture':'Contract inspection; domain-specific fixtures pending'};
});
const inventory=all.map(p=>({Path:p,Sha256:crypto.createHash('sha256').update(texts[p]).digest('hex'),Bytes:Buffer.byteLength(texts[p])}));
const output='docs/productization';fs.mkdirSync(path.join(root,output),{recursive:true});
const stats=Object.fromEntries(['NATIVE_READY','ADAPTER_READY','RULE_READY','PROJECT_CONFIG_REQUIRED','REVIEW_ONLY','BLOCKED','META_ONLY'].map(s=>[s,matrix.filter(r=>r.ProductizationStatus===s).length]));
const runtimeTools=names.sort().map(name=>({Name:name,Command:commandFor(name),SchemaFile:toolDefinitions[name].SchemaFile,Required:toolDefinitions[name].inputSchema.required||[],Dispatcher:commandEvidence[commandFor(name)]||[]}));
fs.writeFileSync(path.join(root,output,'matrix.json'),JSON.stringify({SchemaVersion:1,Inventory:inventory,RuntimeTools:runtimeTools,Counts:stats,Domains:matrix},null,2)+'\n');
const safe=s=>String(s).replace(/\|/g,'/').replace(/\r?\n/g,' ');
fs.writeFileSync(path.join(root,output,'matrix.md'),'# Domain 產品化矩陣\n\n開發期全檔案掃描，非 Runtime 動態 Markdown registry。完整領域與 Native 子流程成熟度分開記錄；未驗證能力不宣稱可直接產品化。自動引用與檔案層 Transaction 掃描屬保守證據，不代表完整呼叫圖或工程規則驗證。\n\n'+Object.entries(stats).map(([k,v])=>`- ${k}: ${v}`).join('\n')+'\n\n| Domain | Skill | Tools | Backend | Status | Priority | Blocker | UI Pattern |\n|---|---|---|---|---|---|---|---|\n'+matrix.map(r=>[r.DomainId,r.SkillPath.join(', '),r.Tools.join(', '),r.BackendFiles.join(', '),r.ProductizationStatus,r.Priority,r.Blockers.join('; '),r.RecommendedUiPattern].map(safe).join(' | ')).map(s=>'| '+s+' |').join('\n')+'\n');
console.log(JSON.stringify({files:inventory.length,domains:matrix.length,skills:skills.length,tools:names.length,stats}));
