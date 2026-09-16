// Evidence-gated release state. Private model/template paths and raw reports stay local.
const fs=require('fs'),path=require('path'),crypto=require('crypto');
const root=path.resolve(__dirname,'..'),out='test-artifacts/v051';
const read=p=>JSON.parse(fs.readFileSync(path.resolve(root,p),'utf8'));
const exists=p=>fs.existsSync(path.resolve(root,p));
const hash=p=>crypto.createHash('sha256').update(fs.readFileSync(path.resolve(root,p))).digest('hex').toUpperCase();
const sha=hash('MCP/bin/Release.R26/RevitMCP.dll');
const snapshot=process.argv[2]?read(path.join(process.argv[2],'reversible.json')):null;
const runtimeFile=n=>snapshot?.RuntimeDirectory&&exists(path.join(snapshot.RuntimeDirectory,n))?read(path.join(snapshot.RuntimeDirectory,n)):null;
const a=read(out+'/contracts.json'),b=read(out+'/logic.json'),c2=read(out+'/workflow-state.json'),site=read(out+'/terrain-logic.json');
const c=runtimeFile('runtime.json'),c3=runtimeFile('workflow-runtime.json'),terrain=runtimeFile('terrain-runtime.json'),cad=runtimeFile('cad-runtime.json');
const backend=read('test-artifacts/source-audit/backend.json');
const sourceMatch=backend.SourceFiles.every(f=>hash(f.Path)===f.Sha256.toUpperCase());
const logPass=name=>exists(out+'/'+name)&&/FAIL\s*:\s*0/.test(fs.readFileSync(path.join(root,out,name),'utf8'))&&/RESULT: PASSED/.test(fs.readFileSync(path.join(root,out,name),'utf8'));
const gate=(r,key)=>({Status:r?.[key]??'NOT_TESTED',Passed:r?.Passed??0,Failed:r?.Failed??0});
const gates={A:gate(a,'GateA'),B:gate(b,'GateB'),C:gate(c?.BuildHash===sha?c:null,'GateC'),C2:gate(c2,'GateC2'),C3:gate(c3?.BuildHash===sha?c3:null,'GateC3'),TerrainLogic:gate(site,'Status'),TerrainRuntime:gate(terrain?.BuildSHA256===sha?terrain:null,'Status'),CadRuntime:gate(cad?.BuildSHA256===sha?cad:null,'Status'),Build:{Status:sourceMatch&&/0 個錯誤|0 Error\(s\)/.test(fs.readFileSync(path.join(root,out,'build.log'),'utf8'))?'PASS':'FAIL'},QAQC:{Status:logPass('qaqc.log')&&logPass('qaqc-source.log')?'PASS':'FAIL'},Rollback:{Status:snapshot?.Rollback??'NOT_TESTED'}};
const all=Object.values(gates).every(g=>g.Status==='PASS'&&!g.Failed);
const deployment=process.argv[3]?read(process.argv[3]):null;
const deployed=all&&deployment?.Status==='PASS'&&deployment.Kind==='FORMAL_V051_RELEASE'&&deployment.DeployedSHA256===sha&&deployment.BuildSHA256===sha;
const next=deployed?'Optional UAT only; stop.':all?'Formal installer deployment, scoped commits and origin/bim-custom push.':'Complete failed/pending gate; maintain verified v0.5 rollback. No release commit/push.';
const finalCommit={Value:'SELF_COMMIT',Resolve:'git log -1 --format=%H -- docs/productization/development-state.json',LiteralReceipt:'test-artifacts/v051/final.json'};
const report={Version:'v0.5.1',Task:'智慧基地地形／土方中心 — CAD Terrain + UX Refinement',Branch:'bim-custom',Timestamp:new Date().toISOString(),Status:deployed?'READY_FOR_OPTIONAL_UAT':all?'AWAITING_FORMAL_DEPLOYMENT':'IN_PROGRESS',Gates:gates,BuildSHA256:sha,SourceFingerprintVerified:sourceMatch,FormalDeployment:deployment??{Status:'NOT_RUN'},Fixtures:{Terrain:terrain?.FixtureVersion??'terrain-1',Coordination:c?.FixtureVersion??'coordination-2'},RuntimeEvidence:snapshot?.RuntimeDirectory?path.relative(root,snapshot.RuntimeDirectory).replaceAll('\\','/'):null,Assertions:{CAD:cad?.Assertions??[],Terrain:terrain?.Assertions??[],PureLogicAndWorkflow:site.Assertions},Benchmarks:site.Benchmarks,Limits:['CSV/TXT and bounded Revit DWG/DXF import; explicit units and coordinate basis','Point, PolyLine and Line vertices only; no text elevation pairing, AEC/proxy interpretation or unbounded curve sampling','CAD boundary is a selected convex planar quantity boundary; not a terrain breakline','Points-only convex hull; no legal site boundary inference','Coded points retained; no inferred breakline connectivity','Simplification error is conservative cell elevation envelope, not certified final-TIN interpolation error','20k-point tool guard requires simplification or explicit override','Boundary quantity supports convex polygons and planar target elevation only','Existing/proposed surfaces experimental and disabled for formal quantities','No host/link movement, ProjectLocation writes, phase changes or Revit.ini edits','Optional visual UAT remains; passing fixtures do not certify arbitrary survey/model conditions'],NextRecommendedBatch:next,FinalCommit:finalCommit};
fs.writeFileSync(path.join(root,'docs/productization/v051-report.json'),JSON.stringify(report,null,2)+'\n');
fs.writeFileSync(path.join(root,'docs/productization/v051-report.md'),'# v0.5.1 智慧基地地形／土方中心 — CAD Terrain + UX Refinement\n\nStatus: '+report.Status+'\n\n'+Object.entries(gates).map(([k,v])=>`- ${k}: ${v.Status}${v.Passed!==undefined?' — '+v.Passed+' PASS / '+v.Failed+' FAIL':''}`).join('\n')+'\n\nBuild SHA256: `'+sha+'`\n\nFormal deployment: '+(deployed?'PASS':'NOT_RELEASED')+'\n\n'+report.Limits.map(x=>'- '+x).join('\n')+'\n');
fs.appendFileSync(path.join(root,'docs/productization/v051-report.md'),`
## 能力與 UX

依 [Domain SOP](../../domain/site-terrain-earthwork.md) 與 [CAD capability audit](v051-cad-terrain-audit.md) 實作。

| 項目 | v0.5.1 結果與邊界 |
|---|---|
| DWG / DXF | Revit 原生暫存 Import / Link；DXF 使用 repository deterministic fixture，DWG 使用 Revit 匯出的隔離 fixture。 |
| CAD entities | Point、PolyLine 頂點、Line 端點；nested block transform 逐層套用一次。 |
| 不可靠資料 | 文字配高程、block attribute、Civil3D/AEC proxy、其他曲線不推導；明列需複核。 |
| Layer discovery | 名稱、幾何類型與數量、有效點、範圍、高程；使用者選圖層與閉合凸平面邊界候選。 |
| CAD placement | Origin / Shared；Shared 失敗不 fallback。Native 與 effective transform 保留證據，包含垂直基準正規化。 |
| Transform | 已知座標、真北旋轉、nested block、Shared round-trip、單位與不重複轉換都有 fixture。 |
| 模型安全 | temporary TransactionGroup rollback；ImportInstance、CADLinkType、View ID 集合回讀一致。 |
| Unified dataset | CSV/TXT 與 DWG/DXF 匯入 TerrainPointDataset；保留 source SHA、單位、mapping、layers 與 transform provenance。 |
| 四步驟 UI | 地形資料 → 座標定位 → 建立地形 → 土方計算；單頁顯示目前階段，提供狀態、上一步／下一步。 |
| CSV mapping | ComboBox 欄位對應與前 10 列預覽；保留原有 parser / QA。 |
| CAD layer UX | CAD source 顯示圖層勾選表；隱藏 CSV mapping。 |
| Coordinate UX | Project Units、可編輯控制點 Grid、模型取點、residual 與座標摘要；CAD 顯示已定位基準。 |
| Preview | 有界 2D 點、CAD 線、邊界、控制點與真北；平面／高程／控制點模式和點數、範圍、誤差摘要。 |
| Terrain creation | Type / Level 安全刷新、平衡預設、僅自訂模式顯示 grid；建立前摘要確認，實際建立後 read-back。 |
| Earthwork UX | 選取 Terrain / Cutter；Floor 邊界或 Model Curve loop；Project Units 標高、數量卡、報告匯出與共用 3D 定位。 |
| Raw inputs | 一般 UI 不要求 ElementId、column index、polygon string、internal metres 或 WRITE。 |
| 大模型保護 | CAD 100 MB / 100k objects / 200k vertices / depth 16；preview 2k 點 / 5k 線；建模 20k 點需減點或明確 override。 |
| 能力限制 | 凸平面邊界；不建立 breakline，不做文字自動配線、Civil3D、LandXML、LAS/LAZ 或正式 Existing/Proposed 數量。 |

## Release evidence

- Release.R26：0 errors；既有 nullable warnings 保留，未作無關清理。
- 完整 QA/QC：74 PASS / 0 FAIL / 2 WARN / 1 SKIP；最終 staged 檢查另記於月誌。
- CAD runtime：${gates.CadRuntime.Status}，${gates.CadRuntime.Passed} PASS / ${gates.CadRuntime.Failed} FAIL；Terrain 與協調回歸如上方 Gate 清單。
- 完整 v0.5 deployment snapshot rollback：${gates.Rollback.Status}。
- 正式 deployment：${deployed?'PASS；required DLL set、逐檔 hash 與 manifest 均已驗證。':'NOT_RELEASED'}
- Deployed SHA256：${deployed?deployment.DeployedSHA256:'N/A'}
- Manifest：${deployed?deployment.ManifestCount+'；Assembly='+deployment.Assembly+'；FullClassName='+deployment.FullClassName:'N/A'}
- Runtime、build、正式 deployed hash：${deployed?'MATCH':'尚未全部核對'}。
- 三個 commit scope：[checkpoint 清單](v051-release-checkpoints.md)。Literal hashes / push 同步結果存於發布後本機 receipt；final commit 以 development-state 的 SELF_COMMIT resolver 解析。
- Runtime JSON / Markdown、fixture models、UI render 與 deployment snapshot 保留 local ignored evidence，不推送使用者模型或本機狀態。
- 下一步：${next}
`);
fs.writeFileSync(path.join(root,'docs/productization/development-state.json'),JSON.stringify({SchemaVersion:2,Task:'v0.5.1',Branch:'bim-custom',Status:report.Status,UpdatedAt:report.Timestamp,Gates:gates,BuildSHA256:sha,InstalledVersion:deployed?'v0.5.1':snapshot?.Rollback==='PASS'?'v0.5':'TEMPORARY TEST LOAD / rollback pending',ProductionDeployment:deployed?'PASS — FORMAL_V051_RELEASE':'NOT_RELEASED',InstalledSHA256:deployed?sha:snapshot?.RestoredSHA256??null,SourceFingerprints:backend.SourceFiles,FinalCommit:finalCommit,NextAction:next,Report:'docs/productization/v051-report.json'},null,2)+'\n');
console.log(JSON.stringify({Status:report.Status,Gates:gates,BuildSHA256:sha}));
