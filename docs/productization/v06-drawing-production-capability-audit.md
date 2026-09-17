# v0.6 施工圖生產能力稽核

稽核基線：2026-09-17，bim-custom，91eaf33；開始時 working tree clean。
此表區分既有 source 能力與 v0.6 runtime 認證；既有 command 不等於整合工作流已通過。

| 能力 | 現有來源 | 判定／重用邊界 |
|---|---|---|
| Sheet／編號 | domain/sheet-viewport-management.md；CommandExecutor.Sheet.cs；sheet-tools.ts | 可重用唯一性與 two-pass 規則；既有建立沒有 Blueprint／ownership |
| Title Block | GetTitleBlocks、CreateSheets；CommandExecutor.TitleblockAlign.cs | 可讀取載入 Type、定位；新版只支援同 Type，不能猜 printable margin |
| View duplication | CommandExecutor.ViewDuplicate.cs | WithDetailing 已有，含自動改名／部分成功語意；新版需精確名稱、原子回復 |
| Dependent | domain/dependent-view-crop-workflow.md；CommandExecutor.DependentView.cs | AsDependent 已有；必須保護母視圖設定及驗證 primary relation |
| Crop／Grid | CalculateGridBounds、CreateDependentViews、CommandExecutor.ViewCropBox.cs | 舊 CalculateGridBounds 單一網格 offset 加兩次、缺網格可保留 sentinel，不直接包裝 |
| Scope Box | CommandExecutor.ScopeBox.cs；scope-box-tools.ts | 使用 VIEWER_VOLUME_OF_INTEREST_CROP；新版按 ID，不以同名第一筆代替 |
| View Template | CommandExecutor.ViewCreation.cs；view-creation-tools.ts | batch_apply_view_template／create_floor_plans_from_template 已有；需 template 控制 scale 預檢 |
| Viewport layout | CommandExecutor.ViewportPosition.cs；viewport-position-tools.ts | position_viewports_on_sheet；既有錨點算法可參考，新版同圖框沿用實際中心 |
| Viewport title | MoveViewportTitles | LabelOffset；2026.5 本機 API 同時提供 LabelLineLength，需保存與 read-back |
| Legend | CommandExecutor.Legend.cs、CrossDocument.cs | 可讀取及放置；每張沿用同一 Legend View，不重複建立 View |
| Schedule | CommandExecutor.Schedule.cs、CrossDocument.cs | ScheduleSheetInstance.Create 已使用；需排除 revision schedule／處理不支援的分割表 |
| Matchline／View Reference | domain/matchline-automation.md | PARTIAL；SOP 使用 TextNote／DetailLine，不能冒稱真正 View Reference。核心停用 |
| Detail sync | domain/detail-component-sync.md；CommandExecutor.DetailComponent.cs | 有公司特定類型參數規則；不能自動套用至泛用出圖 |
| Detail copy | detail-copy-tools.ts；CommandExecutor.DetailCopy.cs | copy_detail_items_to_views 存在；必須 opt-in、安全篩選，禁止複製 revision cloud |
| Dimension | domain/auto-dimension-workflow.md；dimension-tools.ts；CommandExecutor.Dimension.cs | auto_dimension_walls 存在；缺此 package 的 Preview／去重 fixture，PARTIAL／不預設啟用 |
| External RVT | cross-document-tools.ts；CommandExecutor.CrossDocument.cs | copy_sheets_from_file 存在，有 TransactionGroup；有依名称匹配及警告續跑，不作核心 Blueprint 路徑 |
| Guide grid／saved position | 本次 tools／Commands 搜尋 | 未找到可直接重用的完整 typed abstraction；不得虛構支援 |
| Sheet collection | 本次 tools／Commands 搜尋 | Future，本版不實作 |
| QA | GetSheetViewportDetails、get_viewport_map | 只有 metadata，不是 package expected/actual QA；需補專用純邏輯與 native read-back |
| typed service | CoordinationService、RevitTerrainService、RevitEarthworkRecords | 已有 Native service／ExternalEvent／session identity／Extensible Storage 範式；沒有 DrawingPackage／SheetTemplateBlueprint |
| Skills | sheet-management、dependent-view-crop、align-views-on-sheets、batch-apply-view-template、copy-detail-items、copy-sheets-cross-project、auto-dimension、detail-component-sync | 查到 workflow；採最小必要組合，Domain 優先 |
| 大模型 | 既有 Commands 局部每張重新 collector、全模型 view map | 新流程按使用者觸發查詢，批次一次索引、deterministic ordering、明示筆數、UI virtualization |

## 真正缺口與實作方向

缺 Blueprint／Profile、Package token planner、ownership baseline、人工 override／版本差異、Preview 與 Confirm 的 freshness binding、原子 create/update、完整 QA 與 Drawing fixture。
Native UI → 專用 ExternalEvent → typed service；不新增 TS tool、不繞 WebSocket，不直接呼叫自行開 Transaction 的舊 command。
使用現有 DocumentSessionIdentity 防跨模型、Extensible Storage 保存 package baseline；profile 變動不得 silent apply。

## 既有測試證據

development-state.json 與 v0521-report.md 記錄 v0.5.2.1 release PASS；不當成 v0.6 證據。
repository 的產品化矩陣實際名稱為 matrix.json，沒有 productization-matrix.json；本次搜尋未找到 self-test-latest.json。保留既有證據，不製造同名 PASS 報告。
