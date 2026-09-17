# v0.6.1 Production workflow failure analysis

狀態：正式 v0.6 UI 失敗路徑已重現；修正後 C4 A–H 通過，證據與最終同版 Gate 見 v061-report.json。下文保留修正前的根因分析。

## 基線與證據

工作分支 bim-custom，開始時 working tree 乾淨。正式 DLL SHA256 為 `5944347E0BC64EF13449944F8ECF447C59998F5EBDBF8F6CF0DCA9D3384E4042`，本輪已讀回核對。使用 test-artifacts/v061/uat-reproduction.rvt 隔離副本；不操作 production model。

本輪實際操作正式 Panel：讀取專案顯示 9 張 Sheet、5 個 Level、0 Scope Box；已保存樣板下拉暴露 Drawing Fixture（V2）。選樣板後顯示 Disposable Drawing Legend、Fixture Levels、Fixture author。全選樓層，不設定分區，產生計畫後仍切換到有版面示意的 Step 3，底部提示「請選擇樓層與分區」，確認按鈕 disabled。點 Step 4 被阻擋且顯示「請先完成無衝突計畫」，但點 Step 5 可以進入空的圖紙／QA 清單。此重現未執行任何建立，也未保存模型。與使用者聲稱可進 Step 4 的差異保留：本輪 Step 4 實際有 gate，不能宣稱所有 navigation 均放行。

## Root Cause

DrawingPlanner.Generate 在 `Levels.Count == 0 || Zones.Count == 0` 直接回傳零列。沒有 Scope Box、也未手動新增網格分區的使用者，即使選好樓層與來源平面，仍無法建立圖紙。這是 source-confirmed defect；尚不能斷言它是使用者原始那次操作的唯一原因。

## Secondary Causes / UX Problems

| 問題 | 原始碼追蹤結果 |
|---|---|
| A、C：進入計畫卻未建立 | GeneratePlan 無條件切 Step=2，包含零列與錯誤計畫。 |
| B、J：按鈕無法使用 | CanApply 要求非 Busy、Step=3、Plan.CanApply、revision 相符；Control 綁定 disabled，但按鈕旁沒有完整 validation summary。 |
| D：來源 mapping | 勾選樓層只查詢 choices，不自動選唯一 FloorPlan；全選時多次 ReadSources 又可能被 Busy 直接略過。 |
| E：空分區 | planner 直接回零列；沒有不分區語意。 |
| G、H：樣板引用 | Profile 保留 SourceSheetId/UniqueId、Type、共用內容引用。Preview 有檢查來源 UniqueId；跨文件／來源遺失會失敗，不能把 fixture Profile 視為可攜樣板。是否發生於使用者模型尚無即時證據。 |
| I：跨步驟 | GoToStep 只限制 Step=3，允許直接 Step=4（QA）；Step=2 也可無 Plan 進入。 |
| K：QA 零張 | SheetStatuses 只讀當前 PackageGuid 的工具紀錄；新 Package 沒有建立紀錄即空陣列。Qa 回 PACKAGE_NOT_CREATED INFO；零張不是建立成功。 |

## Fixture Bias

DrawingProductionFixture 預先注入 Levels、SourceViewsByLevel、兩個 GridRange、NumberingRule 及圖別。測試證明配置完整後的建立／更新能力，沒有證明一般使用者能從初始 UI 取得該狀態。Profile 與模型測試元素沒有 production 隱藏機制。

## Dispatcher 與建立路徑

DrawingProductionControl → DrawingProductionViewModel → IDrawingHost.Submit → RevitDrawingHost.ExternalEvent → Context → RevitDrawingService.Preview/Apply → TransactionGroup → Verify → Store/Load read-back → Qa/SheetStatuses → SheetList binding。空計畫會在 CanApply 被阻擋，根本不呼叫 Apply；因此目前沒有證據指向 Sheet.Create 靜默成功但未產生元素。

## Missing Runtime Coverage

缺少無分區、唯一來源自動解析、缺一層來源但保留其他列、未建立禁止進 QA、正式 UI fixture 隔離、外部實檔分析載入、確認取消、完整 Panel 初始化至 QA binding 的 Gate C4。舊 46 PASS 不足以證明這些使用路徑。

## Affected Files

- MCP/Core/Drawing/DrawingModels.cs：no-zone、命名與逐列 missing source。
- MCP/Core/Drawing/RevitDrawingService.cs：來源解析、引用驗證、no-zone crop／read-back、fixture 過濾。
- MCP/UI/DrawingProductionViewModel.cs：初始化、選擇、step gates、validation、結果綁定。
- MCP/UI/DrawingProductionControl.cs：簡化設定、來源選項、disabled 原因與確認對話框。
- MCP/UI/RevitDrawingHost.cs：typed operations 的 ExternalEvent 接線。
- MCP/Core/Drawing/DrawingProductionFixture.cs、tests/CoordinationWorkflow/DrawingTests.cs：消除 fixture-only state 作為 production journey 證據。
- Application／runtime launcher／release guards：C4 與同 build SHA release requirement。
