# v0.5.1 智慧基地地形／土方中心 — CAD Terrain + UX Refinement

Status: READY_FOR_OPTIONAL_UAT

- A: PASS — 489 PASS / 0 FAIL
- B: PASS — 20 PASS / 0 FAIL
- C: PASS — 20 PASS / 0 FAIL
- C2: PASS — 65 PASS / 0 FAIL
- C3: PASS — 48 PASS / 0 FAIL
- TerrainLogic: PASS — 119 PASS / 0 FAIL
- TerrainRuntime: PASS — 31 PASS / 0 FAIL
- CadRuntime: PASS — 47 PASS / 0 FAIL
- Build: PASS
- QAQC: PASS
- Rollback: PASS

Build SHA256: `BD8477F9D2E4CF8EF524068B3586263EE7E46A91D4031CF8934BD7D43BBC0E8F`

Formal deployment: PASS

- CSV/TXT and bounded Revit DWG/DXF import; explicit units and coordinate basis
- Point, PolyLine and Line vertices only; no text elevation pairing, AEC/proxy interpretation or unbounded curve sampling
- CAD boundary is a selected convex planar quantity boundary; not a terrain breakline
- Points-only convex hull; no legal site boundary inference
- Coded points retained; no inferred breakline connectivity
- Simplification error is conservative cell elevation envelope, not certified final-TIN interpolation error
- 20k-point tool guard requires simplification or explicit override
- Boundary quantity supports convex polygons and planar target elevation only
- Existing/proposed surfaces experimental and disabled for formal quantities
- No host/link movement, ProjectLocation writes, phase changes or Revit.ini edits
- Optional visual UAT remains; passing fixtures do not certify arbitrary survey/model conditions

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
- CAD runtime：PASS，47 PASS / 0 FAIL；Terrain 與協調回歸如上方 Gate 清單。
- 完整 v0.5 deployment snapshot rollback：PASS。
- 正式 deployment：PASS；required DLL set、逐檔 hash 與 manifest 均已驗證。
- Deployed SHA256：BD8477F9D2E4CF8EF524068B3586263EE7E46A91D4031CF8934BD7D43BBC0E8F
- Manifest：1；Assembly=RevitMCP\RevitMCP.dll；FullClassName=RevitMCP.Application
- Runtime、build、正式 deployed hash：MATCH。
- 三個 commit scope：[checkpoint 清單](v051-release-checkpoints.md)。Literal hashes / push 同步結果存於發布後本機 receipt；final commit 以 development-state 的 SELF_COMMIT resolver 解析。
- Runtime JSON / Markdown、fixture models、UI render 與 deployment snapshot 保留 local ignored evidence，不推送使用者模型或本機狀態。
- 下一步：Optional UAT only; stop.
