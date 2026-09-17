# v0.6 施工圖生產中心

Status: READY_FOR_OPTIONAL_UAT（僅下列已啟用範圍；未啟用能力明列，不代表完整 Domain 認證）。

本版新增「營造 BIM 工具 → 施工圖生產」五步驟 Native 工作流。認證範圍是目前專案、同 TitleBlock Type、一個主要平面視埠，加上可重用 Legend／Schedule；不等於完整 Domain 的所有能力均已啟用。

## 能力與方法

| 項目 | 結果與邊界 |
|---|---|
| Existing capability audit | [完整能力稽核](v06-drawing-production-capability-audit.md)，以本機 bim-custom source 為準 |
| Reused Domains | sheet-viewport-management、dependent-view-crop-workflow、detail-component-sync、auto-dimension-workflow、matchline-automation、viewport-type-scale-sync；後四者保留限制 |
| Reused Skills | sheet-management、dependent-view-crop、build-revit、deploy-addon；revit-api-2026 查核本機 26.5 API；computer-use 操作隔離 fixture 對話框 |
| Reused Tools / Services | 重用 DocumentSessionIdentity、既有 ExternalEvent／DataStorage 範式、現有 self-test launcher 與 gate framework；不新增 MCP tools。舊 command 自管 Transaction，不直接巢狀包裝 |
| Added Domain | [construction-drawing-production](../../domain/construction-drawing-production.md)；整合 SOP，不在 runtime 解析 Markdown |
| Architecture | Native Control → ViewModel → 專用 RevitDrawingHost ExternalEvent → typed RevitDrawingService；純 DrawingPlanner／DrawingSheetQaService 可獨立測試 |
| Template Sheet extraction | 圖框 Family/Type、位置/範圍、主要視埠、Template/Scale、Type、中心、詳圖號、旋轉、LabelOffset/LineLength、Legend/Schedule、可選文字參數 |
| Title Block strategy | 同 Type 使用實際座標。跨尺寸、旋轉圖框及不可靠 bounds 阻擋；未猜測 printable margin |
| Parameter copy policy | GENERATED 圖號/圖名；Revision/Issue/未知敏感內建參數 NEVER_COPY；繪圖/校對及自訂文字 opt-in；Project Information 不寫入 |
| Parameter mapping | Profile 保存 Level/Zone/DrawingType/Discipline/Phase → 既有 ParameterId；Sheet/TitleBlock/View。View 映射限獨立複製，避免改母視圖；不增加 Shared Parameter |
| Drawing Template Profiles | Extensible Storage 保存 Blueprint/規則/GUID/Version/時間；重新擷取與保存新版本明確分開，不自動改既有圖紙 |
| Drawing Package | 保存專業/圖別/階段命名、樓層/分區/來源/規則、排除列與覆寫；可載入已建立 Package 重跑 |
| Level / Zone | fresh query、ProjectElevation 排序、高程 mm、全選/範圍/多選；Scope Box 或四條未旋轉正交網格，padding 使用者輸入；既有 dependent crop 作分區來源未啟用 |
| View strategy | 使用現有視圖、獨立 Duplicate（不複製詳圖）、AsDependent。現有視圖須已符合比例/樣板/crop，不改寫使用者視圖；模型視圖不能重複上圖紙 |
| Dependent / Crop / Scope | 保護母視圖 Template/Scale；驗證 primary relation、分區 crop 或 Scope Id。旋轉網格裁剪不自動猜測 |
| Naming / Numbering | 六種 token、整數零補位、確定排序、保留/既有/計畫圖號衝突、two-pass 改號；不把 FL1 自動猜成數字 1 |
| Cartoon Set | 狀態/圖號/圖名/樓層/分區/視圖/問題/差異；搜尋、排除列、逐張改號/名稱/來源、重新預覽後才確認 |
| Layout Preview | 顯示圖框、依分區與比例估計的主視埠、Legend/Schedule、標題點示意；超框/重疊紅色阻擋。標題文字包絡尚未認證 |
| Viewport placement / title | Type/Rotation/Center/DetailNumber/LabelOffset/LabelLineLength 沿用樣板，實際 API read-back；不使用 default Type 冒充樣板 |
| Legend / Schedule | 每項可選沿用；重用原 View，按 Slot 放置，不複製 Schedule View；revision schedule 排除，split schedule 阻擋 |
| Detail components | PARTIAL / NOT ENABLED：已有舊命令，但未認證泛用公司詳圖安全複製及去重 |
| Auto dimension | PARTIAL / NOT ENABLED：已有 auto_dimension_walls，尚未認證本 Package 尺寸 Preview/去重 |
| Matchline / View Reference | PARTIAL / NOT ENABLED：舊 TextNote/DetailLine 不是真正目標引用，不以文字冒充 |
| Manual override | actual/baseline 比對，預設保留；可逐張明確重套並重新預覽。尚未提供同張逐欄 merge |
| Template versioning | Profile 改版標記 TEMPLATE_OUTDATED；顯示主視埠位移 mm、Type/Scale/Template、圖號/圖名差異，確認後才更新 |
| Idempotency | PackageGuid + Level/Zone key + Sheet UniqueId + baseline/expected；同包重跑不新增，遺失/同號非工具圖紙不接管 |
| Drawing QA / readiness | 超框、重疊、主要視埠/圖框、Type/Template/Scale、Crop、Level/Zone、詳圖號、人工修改、樣板過期；Draft/NeedsReview/Ready 只代表工具 QA |
| Drawing directory | Native 圖紙目錄讀回實際圖號/圖名，顯示 Level/Zone/圖別/QA；開啟實際 Sheet。未新增 Revit Sheet List Schedule |
| Large model safeguards | 按操作讀取、不開頁掃描、scoped Sheet collectors、完整確定排序計畫、表格 virtualization、不 silent truncate；300 張純計畫測試通過，300 張 Revit 實際耗時未測 |
| Runtime fixture | disposable RVT/RFA、3 Levels、2 zones、實際 Grids、Reference Sheet、Legend、Schedule、Scale Template；未使用 production RVT/Central/Link |
| Extraction tests | 圖框/主視埠/Legend/Schedule/Template/Scale 控制與 generated parameter policy |
| Batch creation tests | 3×2 = 6；逐張圖號/名稱/Type/中心/Scale/Template/Crop/共用內容與所選參數 read-back |
| Update / override tests | 第二次不重複，人工移動保留/明確重套，V2 待套/確認後更新；新增 Existing 與 Duplicate 策略測試 |
| QA / safety tests | 超框、重疊、錯比例、圖號衝突、未確認/竄改預覽攔截；第二張故障整批 rollback，無 orphan sheets/views/package |

## Gates 與證據

| Gate | 狀態 |
|---|---|
| A | PASS — 499 / 0 FAIL |
| B | PASS — 20 / 0 FAIL |
| C2 | PASS — 97 / 0 FAIL（含 32 drawing cases） |
| Drawing C3 | PASS — 46 / 0 FAIL |
| TerrainLogic | PASS — 211 / 0 FAIL |
| Build | PASS — Release.R26、0 errors；既有 nullable warnings 保留，未升級套件 |
| QA/QC | PASS — 完整命令 75 PASS / 0 FAIL / 2 WARN / 0 SKIP；實際部署目錄已驗證 |
| C / Coordination C3 / regression | PASS — C 20、Coordination C3 48、TerrainRuntime 31、CadRuntime 47、EarthworkNative 61；均 0 FAIL，全部同本版 hash |
| Deployment | PASS — 8 個 DLL hash 全數相符；manifest count = 1；Assembly RevitMCP\RevitMCP.dll；FullClassName RevitMCP.Application |
| Git commits / Push | 依 [三個 checkpoint scopes](v06-release-checkpoints.md)；最終 literal receipt 保存於 test-artifacts/v06/final.json，僅 origin/bim-custom |

Drawing runtime SHA256：`5944347E0BC64EF13449944F8ECF447C59998F5EBDBF8F6CF0DCA9D3384E4042`。
原始 JSON、fixture、畫面與部署備份位於 local ignored `test-artifacts`；最終 machine-readable release receipt 使用 v06-report.json。

## 未啟用與限制

- 一張多個主要模型視埠、跨圖框自適應、內容拓樸增減及既有公司圖紙納管未啟用；非工具圖紙維持 Preview only／衝突，不自動接管。
- 同張逐欄合併、逐列不同 Template/Zone、DuplicateWithDetailing、既有 dependent crop 作分區來源尚未啟用。
- Phase 僅命名／映射 token；不變更 Revit view phase。View 參數只接受既有可寫文字，不猜單位或公司語意。
- Guide Grid、任意註記/共用詳圖、自動尺寸、Matchline／View Reference 未啟用；External RVT optional 路徑未整合。
- Actual viewport read-back 是最終幾何檢查；2D preview 為估計包絡，文字標題及 printable area 需人工複核。
- 本版未加入 PDF、DWG export、Revision automation、v0.7 或工程數量中心。

## 實際修改與收尾

- Core：MCP/Core/Drawing/DrawingModels.cs、RevitDrawingService.cs、DrawingProductionFixture.cs。
- Native UI：DrawingProductionControl.cs、DrawingProductionViewModel.cs、RevitDrawingHost.cs；現有 Panel 與 Application 只增加 tab、生命週期與隔離 fixture 接線。
- Tests / governance：既有 CoordinationWorkflow runner 增加 DrawingTests；擴充既有 launcher、reversible gate、static contract、productization audit；publish-v06.ps1 沿用正式部署 guard。
- Domain、矩陣、development-state 與公開 85 Domain 計數同步；未修改既有地形/土方/協調演算法。
- NuGet 首次網路受限，以本機快取完成相同依賴 build；未更新套件。回歸首次預設樣板無 3D，改用上版 test-artifacts 隔離樣板重測 PASS。歷次失敗與還原證據保留。
- 殘留 QA warnings：已隔離的舊 orphan tool 與未 staging 檔案提醒；不是新 runtime failure。
- Build = Runtime = Deployed SHA256，已核對全部三類 runtime 報告。沒有 production model 測試。下一步僅限已啟用範圍的 optional UAT；停止，不開始 v0.7。
