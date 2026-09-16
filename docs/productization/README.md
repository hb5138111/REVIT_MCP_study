# 營造 BIM 工具與 Self-Test Lab（v0.5）

「營造 BIM 工具」提供「施工協調」與「基地／土方」兩個工作台。模型摘要、族群／類型檢查、樓層／約束檢查已退休；相關 Domain 與 MCP 工具保留。

## 使用流程

1. 開啟工作台，來源與樓層自動載入；模型切換後會清除舊結果並自動刷新。
2. 選主模型或 Link、單一 MEP 分類、單一主體分類。MEP 預設最近可用分類；主體分類須明確選擇。樓層預設全部，可選來源自己的 Level。
3. 若需要建議孔尺寸，勾選計算並保存明確的每側預留量（可輸入 `25 mm` 等帶單位長度）。值依專案 session 隔離，沒有預設工程值。單純碰撞不需要預留量。
4. 開始協調掃描。結果在同一表格分類成開孔、穿梁、一般碰撞與需複核；搜尋與分類只篩選既有結果。
5. 雙擊問題或按「3D定位」同時選取 MEP／主體並縮放到交點。Link 選取 Link instance，明細保留原 linked Element ID。
6. 上一筆／下一筆預設保持同一協調 3D 視圖；關閉「巡覽時自動 3D 定位」僅改變表格選取。按「返回原視圖」回到第一次切換前的視圖。
7. 點摘要數量即篩選既有結果；主表保留八欄，元素 ID、交點、穿透長度與提醒放在問題明細。可匯出目前篩選結果。

系統欄位是真正的「名稱包含文字」篩選，僅在 Pipe／Duct 顯示；不是完整系統 registry。顯示上限放在進階設定。超過顯示上限時，總問題數仍完整，CSV 只包含表格中保留且通過篩選的列。

## 架構與安全

`CoordinationViewModel`（可測 controller）→ `ICoordinationHost` → `RevitCoordinationHost` → `PanelReadOnlyDispatcher` / ExternalEvent → `CoordinationService` → 既有 `ClashDetector` 幾何方法。

- WPF 只負責 binding 與檔案選擇；Native workflow 不經 TS、WebSocket 或 Markdown parser。
- `DocumentSessionIdentity` 使用 native Document equality 與 session token；導航再核對來源與元素 UniqueId，並讀回選取結果。
- 自動刷新只讀文件、Link、Levels、分類是否存在，不抽取幾何。掃描一次限定一組來源與分類，配對上限 50,000、時間預算 15 秒、顯示上限 1–1,000。這些是軟體保護措施，不是工程標準。
- 同一對元素只保留一列。同一主體有多段／多 Solid 交集時，長度與交點代表第一段，另加明確 warning，不能當總穿透長度。
- 保留 Productization Registry、六種 pattern 定義、Self-Test Lab、installer 與 shared MCP Link DTO。

## 3D 導航

重用目前可用的 non-template 3D，否則使用 session 3D，最後依非 perspective 優先與 ElementId 排序選擇既有視圖；不依賴名稱。導航只驗證所選構件、切換視圖、Selection 與 UIView camera，不重新掃描 geometry。沒有既有 3D 時顯示原因並只做安全選取，不建立 View。

既有隱藏設定或剖面框仍可能遮住構件；工具不修改這些設定。焦點不能驗證時會明確提示。完整 3D runtime 驗證需要隔離 fixture template 中有既有可用的 3D 視圖。

## 能力邊界

依據 `domain/mep-csa-clash-detection.md`、`mep-opening-candidate-scan.md` 及穿梁／套管 SOP。只做中心線穿越與標稱尺寸加雙側預留量；不涵蓋管件、保溫、實體擦碰。開孔下緣未知維持 null／「—」。穿梁、穿柱、斜穿與實體邊距未驗證均需人工複核，不提供套管自動核准、RC／SC／SRC 結構核准、開孔建立或模型修改。

## 自動測試

| Gate | 內容 | 入口 |
|---|---|---|
| A | 靜態 contract、registry、退休功能與共享依賴 | `node scripts/test-workflow-contracts.cjs test-artifacts/v042` |
| B | production 尺寸與 warning 純邏輯 | `dotnet run --project tests/WorkflowLogic -- test-artifacts/v042` |
| C | disposable Revit backend fixture | `scripts/test-reversible-gate-c.ps1` |
| C2 | production controller、排隊 host、來源／設定／filter／導航／stale recovery | `dotnet run --project tests/CoordinationWorkflow -- test-artifacts/v042` |
| C3 | 真正 ActiveUIDocument、production controller 與 ExternalEvent | 同一次 reversible fixture session，輸出 `workflow-runtime.json` / `.md` |
| D | N/A：產品功能唯讀 | fixture 與 selection 均有 read-back |

完整驗證入口：`scripts/verify-v042.ps1 -Runtime -ProjectTemplate <isolated-test-template.rte>`。本機預設 template 已確認没有既有 3D，須明確提供含 3D 的隔離 template。只有明確批准正式 release 時才加 `-Deploy`。v0.4.1 的驗證入口保留為歷史版本，本版使用 v042 入口。

coordination-2 fixture 包含牆／板／雙 Solid 梁／柱、Pipe／Duct／Conduit、斜穿、不碰撞樣本、平移 MEP Link、平移 Host Link，以及獨立模型切換與 DocumentChanged。navigation-1 增加既有 3D 視圖解析、camera read-back、巡覽、返回視圖、跨模型 session 與 no-3D fallback。自訂 template 先複製至隔離目錄並隔離原有模型連結；fixture 以 project elevation 與明確 slab type 建立可重現幾何。全程只開啟 launcher 建立的資料；RVT、RFA、完整部署 snapshot 與 raw runtime artifacts 留在 ignored `test-artifacts/`。

Revit 不採用子程序 APPDATA 覆寫來隔離 add-in discovery。因此 reversible runner 先核對 Revit 未執行、完整備份實際 deployment，再透過原 installer 暫載。無論結果如何，正常退出後都恢復完整 snapshot。若有未簽章對話框，需一次性的使用者互動；不殺程序、不覆寫已載入 DLL。

正式部署由 `publish-v042.ps1` 驗證每個 gate、來源 fingerprint 與 Gate C/C3 DLL SHA256，呼叫既有 `install-addon.ps1 -Version 2026`，核對全部 DLL 與 user／machine Addins 中唯一 manifest。失敗則沿用已驗證 snapshot rollback。

目前結果見 [v0.4.2 report](v042-report.md)、[navigation analysis](v042-navigation-analysis.md) 與 [checkpoint scopes](v042-release-checkpoints.md)。歷史 v0.4 報告保留，不代表本版 UI 測試。其他 Productization 批次未啟動。

## 基地／土方（v0.5）

依 [site-terrain-earthwork SOP](../../domain/site-terrain-earthwork.md)：選 CSV/TXT，指定 delimiter/header/欄位索引/units，分析 QA；檢查模型座標後選 Shared / Control Point / Local。Control Point 每組六個數值全部為 m，scale 固定 1。Preview 不建立模型元素；tool tolerance 不是測量規範。

選 Toposolid Type/Level、減點模式並複核診斷。明確勾選本次確認後才可建立；超過 20k 點須減點或明確覆核 override。XY 高程衝突、控制 residual 過大、缺少 Type/Level 均阻擋。任何設定／文件變動會清除 Preview 與確認。資料處理在背景執行，畫面點雲最多 2000 個明示樣本，診斷最多 500 列，完整資料留在報告。

地形建立回讀頂面 bounds、Type/Level、Area/Volume。土方可指定 Terrain/Cutter ElementId，先 Revit rollback 試算，再確認執行開挖；或輸入 internal m 的凸 boundary 與 target elevation 作唯讀 TIN 積分。結果依 Project Units 顯示，稽核使用明確 SI units。JSON/CSV/Markdown 自動保存於「文件/RevitMCP/SiteReports」。

能力界線：points-only convex hull 不是法定基地界；Code 點保留但不推定 breakline connectivity；減點誤差為保守 cell elevation envelope，不冒充最終 TIN 插值認證。Existing/Proposed 標為 experimental、未開放正式量。沒有 building auto-move、ProjectLocation 寫入或 Revit.ini 修改。
