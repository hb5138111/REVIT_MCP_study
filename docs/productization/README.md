# v0.4 產品化框架與 Self-Test Lab

本次以「施工協調」為第一個 Native 模組；沿用原有營造 BIM 工具面板。

## 分層

- Domain／Skill：開發期方法與能力來源。
- `scripts/run-productization-audit.ps1`：讀取全部 Domain、Skill、TypeScript 工具及 C# Core／Commands，以 Release.R26 參考組件建立 Roslyn method 呼叫圖，輸出 [JSON 清冊](matrix.json) 與 [Markdown 矩陣](matrix.md)，含逐檔 SHA256。狀態分類是保守的開發期判讀，不是 runtime 或工程規則認證。
- `WorkflowRegistry`：編譯式 typed 註冊；定義六種 UI pattern、風險與 capability。Runtime 不解析 Markdown。
- `DetectReviewWorkflowControl`：碰撞與開孔共用的輸入、結果、警告、定位、匯出 UI。
- `CoordinationViewModel` → 原有 `PanelReadOnlyDispatcher` → `CoordinationService` → `ClashDetector` 共用幾何方法。
- 既有 MCP JSON contract 保留；沒有增加 MCP tool。

## 功能邊界

碰撞與開孔候選均唯讀。先明確選擇來源、單一分類、MEP 來源樓層；可依系統名稱包含文字過濾。分類與樓層在幾何運算前過濾。計算配對預算及時間預算為軟體保護措施，不是工程門檻；超限會要求縮小範圍。顯示上限不會影響總符合數；搜尋與警告篩選只處理既有結果。

開孔每側預留量必須由使用者依專案長度單位輸入並保存。設定採 typed version 1，依目前文件身分隔離，只保存於本次 Revit 工作階段；可重設。未保存不得掃描。尺寸採 Domain 的標稱尺寸加雙側預留量。

連結模型使用 `GetTotalTransform()`。定位連結時亮顯連結實例，明細保留連結 ID 與內部元素 ID；不宣稱選到 linked element。定位前核對目前 Document 物件、DocumentIdentity、來源文件身分及元素 UniqueId。

中心線法不涵蓋管件、保溫、中心線未穿越的實體擦碰。未知實體邊距及不可靠的開孔下緣均保留 warning；下緣显示「—」。穿梁／穿柱與斜穿保持人工複核。本版不提供套管分類核准或完整 RC／SC／SRC 合規判定，也不提供模型寫入 Apply。

## Self-Test 四層

| Gate | 執行方式 | 證據 |
|---|---|---|
| A | `node scripts/test-workflow-contracts.cjs test-artifacts/v04` | registry／dispatcher、Domain 引用、必要欄位、原有 UI 與 dispatcher regression |
| B | `dotnet run --project tests/WorkflowLogic -- test-artifacts/v04` | 編譯 production 純邏輯；尺寸、缺值拒絕、邊界、斜穿、warning、唯讀 capability |
| C | `scripts/run-revit-selftest.ps1` | 新建文件的真實 Revit API fixture；未取得同 build hash 的 runtime report 不得 PASS |
| D | 唯讀產品流程為 N/A | fixture 元素仍核對 UniqueId read-back；未來 Apply 必須有獨立 Read-back gate |

完整入口：`scripts/verify-v04.ps1 -Deploy`。所有 gate 通過且部署雜湊相符才可顯示 `READY_FOR_OPTIONAL_UAT`。未通過 runtime gate 不部署。

### Fixture 規劃

- CoordinationFixture（coordination-1 已實機通過 15 項 assertions）：FL1／FL2、牆、樓板、生成之結構分類梁柱族群、穿牆／穿梁管、穿板風管、不碰撞電管、斜穿管、平移 MEP 連結。測试尺寸與位置是合成資料。
- CoreFixture（待實作）：摘要、類型與樓層稽核的實機 regression。
- TakeoffFixture（待實作）：數量與扣除案例。
- DrawingFixture（待實作）：圖紙與視埠案例。

每次在唯一 `test-artifacts/revit-selftest-*` 目錄儲存 RFA、RVT、request、JSON／Markdown 結果；不覆寫既有檔案、不開啟正式 RVT。runner 拒絕已有文件的工作階段。第一版沒有納入可確定重現的 grazing 實體邊界案例。

### 本機啟動限制

已實測 Revit 2026 不採用子程序 APPDATA 覆寫來改變 add-in 搜尋路徑，仍載入使用者正式安裝的 DLL。因此不能把暫存目錄中的 DLL 當成已被執行。launcher 現在先核對真正註冊的 manifest 與 DLL hash；版本不符直接輸出 `RUNTIME_TEST_BLOCKED`，不啟動、不部署。

首次驗證需要一個已批准並完成註冊的測試 Revit host；在此之前，禁止為了測試而越過本任務「測試先於正式部署」的 gate。保留同一份 csproj、manifest、AddInId；不為測試建立第二套正式註冊。

## 稽核範圍與能力界線

全域靜態能力 mapping 已完成。JSON 保留 SOP 條文行號、文件 hash、Tool/schema、command entry、可達 helper method、Revit API、Transaction、Link transform，以及未註冊工具與已知缺陷。Direct domain tools 與 Skill support tools 分開記錄，避免共用 Skill 的寫入工具污染純查詢能力判讀。

這不等同於所有 Domain 的工程語義或 runtime 認證。只有本次協調子流程有匹配 build hash 的 Gate C 證據；其他領域維持未啟用，需各自 adapter 與 fixture。未解析外部呼叫保留於 backend 證據；呼叫圖取各 branch 聯集，不能代替每條 runtime 路徑驗證。

開發期 SourceAudit 使用本機 .NET 10 SDK 隨附 Roslyn（沒有新增 NuGet dependency）；Revit 2026 外掛仍是既有 .NET 8 Release.R26，未新增 Add-in 專案或 manifest。

可回復 Gate C 已完成並恢復 v0.3；其後依明確正式發布授權，已另行部署相同 tested hash 的 v0.4，逐 DLL 與 manifest 驗證 PASS，詳見 [報告](v04-report.md) 與 [續跑狀態](development-state.json)。
