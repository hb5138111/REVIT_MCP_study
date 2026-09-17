# v0.5.2 土方 Native workflow failure analysis

## 基準與重現範圍

- 基準：正式 v0.5.1，commit `b6bae17`，DLL SHA256 `BD8477F9D2E4CF8EF524068B3586263EE7E46A91D4031CF8934BD7D43BBC0E8F`。重現時未替換正式 DLL。
- 測試：新建 standalone disposable fixture；平坦地形頂面 Z=2 m、10×10 m；Floor boundary 5×5 m；專案長度單位 cm。僅測試模型，未開啟使用者工作模型。
- 已實際由 Native UI 取得 Toposolid、讀取選取 Floor 邊界、按計算按鈕。Target=0 cm 時 UI 顯示 Area=25 m²、Cut=50.00 m³、Fill=0.00 m³；typed read-back Cut=50.00000038146972 m³。此條路徑沒有重現全面失效，不應把演算法或 cm 轉換直接判定錯誤。
- Target=100 cm 經實際 TextBox → ViewModel 為 1 m；Cut=25.000000381469732 m³、Area=25 m²、Fill=0。UI 亦顯示 25.00 m³。證明此案例沒有 cm/m/feet 或二次 Shared transform 錯誤。
- Cutter 由 Native UI 選取同一 Floor，Preview=57.5 m³，Confirm 後 read-back=57.49999999999998 m³；Result 卻為 double，Quantity=null、ExcavationPreview=null。UI 結果區顯示「計算結果將顯示於此」，底部 Status 顯示 read-back 完成。
- Cutter 與 Target boundary 不是相同基準：此 fixture 地形厚度 2.3 m，樓板底部低於地形底面，Revit cutter 量為 25×2.3=57.5 m³；不可把它誤判為應有 50 m³。
- Revit 正常退出後移除測試 manifest；正式 v0.5.1 完整原檔案集合／逐檔 SHA256／manifest 與 snapshot 一致，rollback PASS。原始 fixture、狀態 JSON 與 backup 均留於 ignored `test-artifacts/v052`。

## Root Cause

`ExecuteExcavation` 已成功呼叫 Revit、完成 read-back，卻把 `Result` 設為裸 double，並清除 `ExcavationPreview`。`SiteTerrainControl.Refresh` 只接受 `SiteEarthworkSummary`，否則只顯示 Preview；兩者都沒有時回到空白提示。這是已實際重現的 DTO → result binding 斷點，不是 TIN 引擎全面失效。

## Secondary Cause / 已確認的設計缺口

1. Step 4 同時顯示 Cutter 試算／實際開挖與 boundary TIN，缺少明確計算方式選擇與輸入完成狀態。
2. `CalculateBoundary` 沒有 `CanCalculate` gating，空邊界仍可送入 backend；沒有目標標高「尚未輸入」狀態，default 0 被當作明確標高。
3. 高程 TextBox 在 LostFocus 才更新，更新會清空 result cards、縮短頁面並改變 scroll offset。實際首次點擊計算時只完成輸入提交及版面跳動，需再次按計算。改成即時文字驗證與保留結果區高度。
4. 首次載入遇到 Document activation 可能使排隊中的 RefreshContext 失效；必須測試重新整理與單位初始化，不得在 Context 缺失時默認 m 接受使用者輸入。
5. Floor / ModelCurve 已有可靠的直線凸平面邊界擷取，不應改用 bounding box 或再做 Shared transform。TIN input / boundary 均為模型 internal axes 的 metres。

## Missing Test

v0.5／v0.5.1 Terrain runtime 使用 `vm.Boundary="..."`、`vm.TargetElevation=0`，直接呼叫 CalculateBoundary。未串接真實 UIDocument selection、Floor boundary extraction、非零 Project Units TextBox binding、CanCalculate、實際 ExternalEvent 與 lifecycle invalidation。Pure selection test 使用固定 fake IDs。

## Affected Files

- MCP/UI/SiteTerrainControl.cs
- MCP/UI/SiteTerrainViewModel.cs
- MCP/UI/SiteTerrainWorkflow.cs
- MCP/UI/RevitSiteHost.cs
- MCP/UI/BimConstructionPanelViewModel.cs
- MCP/Core/Site/TerrainSelfTest.cs
- tests/SiteTerrain/Program.cs

## 修正與驗證

已加入 typed `SiteExcavationOutcome`，Preview／Execute 共用可測試的顯示文字，保留 Terrain/Cutter IDs、SI 原地量、Project Units 與 Executed 狀態。明確指出 Cutter 結果不包含設計填方／面積，避免捏造數量。

加入 `CanCalculate`／輸入完成說明、明確未填高程、無效／非有限文字阻擋、Context 缺失阻擋，模型切換清除邊界與高程。選取時必要的 Context 由同一 Revit callback 取得。新增 pure regressions 後 131 PASS／0 FAIL。

新增 normal-startup Native workflow fixture，使用實際 UIDocument selection、Floor Sketch 邊界、WPF target binding、production ExternalEvent 與 lifecycle，驗證 50 m³、100 cm → 25 m³、無效文字與 Cutter read-back 顯示。

第一輪 16 PASS／1 FAIL：WPF Loaded 可能在 fixture ExternalEvent 排程後、執行前搶先送出 Context refresh；fixture 提前檢查單位。修正 fixture Busy／Context 等候後，第二輪 **17 PASS／0 FAIL**，build SHA256 `31F222F36C9BA196665833D38757E70B185711D72A6223A34D29A09D740C573B`；Revit 正常退出，完整 v0.5.1 snapshot rollback PASS。此結果只證明 workflow 修正版本，後續加入成本／Schedule 的 build 必須另跑相同及新增 assertions，不能沿用為最終 release PASS。
