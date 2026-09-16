# v0.4.2 — 施工協調 3D Navigation 分析

## Current behavior

`CoordinationRow → CoordinationViewModel.Navigate → RevitCoordinationHost → PanelReadOnlyDispatcher ExternalEvent → fresh ActiveUIDocument → ResolveNavigation → Selection.SetElementIds → ShowElements`。
目前只驗證 Selection read-back；未約束 ActiveView，也未驗證相機是否看得到交點。

## Root cause

使用者 UAT 發現 `ShowElements` 後從 3D 切至 FloorPlan。現有程式將找視圖交由該 API，沒有指定目標 3D。API XML 僅保證顯示元素並縮放，沒有承諾保留目前視圖；因此無法把 Selection 成功當成 3D 定位成功。這是流程缺少視圖與焦點後置條件，不是碰撞計算錯誤。

## Revit API option

依本機 RevitAPIUI 2026.4.10 XML 的 `UIDocument.ActiveView`、`RequestViewChange`、`ShowElements`、`UIView.ZoomAndCenterRectangle` 與 repository `RevitCompatibility.cs`：

- `ActiveView` setter：可在本流程 ExternalEvent 內切換目前文件視圖；不可在 ViewActivated、唯讀文件或開啟 Transaction 時呼叫。
- `RequestViewChange`：非同步；需要後續 ExternalEvent 才能確認已切換，不能同一步假設完成。
- `ShowElements`：沒有固定 3D 的契約。
- `UIView.ZoomAndCenterRectangle`：可在已開啟的指定 UI view 縮放到 model-coordinate rectangle，避免將整個 Link 當焦點。

## Chosen solution

重用既有 dispatcher、document identity、typed reference validation 和 Link instance selection。同步切换 `ActiveView` 並 read-back，使用相符 `UIView` 與掃描結果的 host-coordinate 交點縮放；再檢查 view ID 與 zoom corners。導航不重新掃描 geometry、不開 Transaction、不建立 View、不改 SectionBox 或參數。

選擇順序：目前可用 non-template View3D → session View3D → 依 perspective、ElementId 排序的可用 View3D。上一筆／下一筆優先維持 session View3D。不依賴視圖名稱。第一次從非 3D 切換時記錄 PreviousViewId；跨文件或內容失效清除 session。

視窗框距是 UI framing policy，不是工程 clearance：依穿透長度與標稱尺寸給足局部上下文，至少 3 ft 半徑。不改開孔計算值。Linked element 仍由 Link instance 與主體選取；detail 保留來源及真正 linked ElementId。

## Fallback behavior

沒有可用 3D：保留安全 Selection，顯示「目前模型沒有可用的 3D 視圖，無法執行 3D 定位。」不建立視圖。既有視圖若受 SectionBox、隱藏設定或 perspective 限制，明確回報焦點驗證失敗／限制，不能宣稱完整定位成功。Previous view 不存在時清除 previous session 並說明。Fixture 使用隔離模型既有的 3D 視圖；缺少時不得以 production navigation 建立。

## Runtime prerequisite observed

本次 backend Gate C 18 項通過，但預設 `Default_M_ENU.rte` 建立的 disposable fixture 沒有任何可用 View3D（Expected ≥1，Actual 0）。Gate C3 在前置檢查停止，不能沿用舊版 PASS，也尚未驗證本版相機定位。完整正式 v0.4.1 deployment 已恢復，檔案集合、SHA256 與 manifest 全部驗證 PASS。

使用者提供了含既有 3D 的明確 test template；沒有建立測試 View 的例外。測試從本機 snapshot 建立新的 disposable document，原 template SHA256 於測試後保持一致。Snapshot 的 Revit/CAD references 改指向隔離目錄並設為 unloaded，測試不載入原模型連結。fixture 清理只發生在新建文件。

## Fixture compatibility corrections

- 外部參照：只對 Revit/CAD model links 套用 unload 資料，使用隔離絕對路徑，避免其他資源參照不支援的組合。
- 樓層：以 `ProjectElevation` 校正 fixture 的 0 / 12 ft 基準，避免 template 的 survey elevation display base 改變主體放置高度。
- 樓板：明確使用 non-foundation FloorType；沿用合法 compound structure，替換為 0.5 ft 測試層，再驗證頂面 6 ft。這些值只定義 fixture，不是工程規範。
- 群組清理：重用 repository 的 warning preprocessor，處理 disposable copy 刪除構件後的群組 warnings。
- 模型切換：3D availability 改為 document-only View3D 查詢，不要求剛開啟模型已具備 ActiveView。導航執行時仍 fresh validate ActiveView 與視圖切換結果。完整來源刷新 assertion 保留原條件並輸出每個狀態。

## Verified runtime outcome

最新同一 build 的 Gate C 20 / 20、Gate C3 48 / 48 全部 PASS。涵蓋 FloorPlan → 3D、保持既有 3D、Previous/Next、返回原視圖、精確 Link instance selection、局部交點 focus、無 3D fallback、沒有建立 View、navigation 不使模型變髒、跨 Document session 清除及來源刷新。詳細 SHA256、rollback 與正式 deployment 狀態以 [release report](v042-report.json) 為準。
