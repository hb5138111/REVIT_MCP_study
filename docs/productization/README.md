# 施工協調工作台與 Self-Test Lab（v0.4.1）

「營造 BIM 工具」直接開啟單一施工協調工作台。模型摘要、族群／類型檢查、樓層／約束檢查已退休；相關 Domain 與 MCP 工具保留。

## 使用流程

1. 開啟工作台，來源與樓層自動載入；模型切換後會清除舊結果並自動刷新。
2. 選主模型或 Link、單一 MEP 分類、單一主體分類。MEP 預設最近可用分類；主體分類須明確選擇。樓層預設全部，可選來源自己的 Level。
3. 若需要建議孔尺寸，勾選計算並保存明確的每側預留量（可輸入 `25 mm` 等帶單位長度）。值依專案 session 隔離，沒有預設工程值。單純碰撞不需要預留量。
4. 開始協調掃描。結果在同一表格分類成開孔、穿梁、一般碰撞與需複核；搜尋與分類只篩選既有結果。
5. 亮顯 MEP／主體／兩者、上一筆／下一筆、匯出目前篩選結果。Link 定位亮顯 Link instance，明細保留原元素 ID。

系統欄位是真正的「名稱包含文字」篩選，僅在 Pipe／Duct 顯示；不是完整系統 registry。顯示上限放在進階設定。超過顯示上限時，總問題數仍完整，CSV 只包含表格中保留且通過篩選的列。

## 架構與安全

`CoordinationViewModel`（可測 controller）→ `ICoordinationHost` → `RevitCoordinationHost` → `PanelReadOnlyDispatcher` / ExternalEvent → `CoordinationService` → 既有 `ClashDetector` 幾何方法。

- WPF 只負責 binding 與檔案選擇；Native workflow 不經 TS、WebSocket 或 Markdown parser。
- `DocumentSessionIdentity` 使用 native Document equality 與 session token；導航再核對來源與元素 UniqueId，並讀回選取結果。
- 自動刷新只讀文件、Link、Levels、分類是否存在，不抽取幾何。掃描一次限定一組來源與分類，配對上限 50,000、時間預算 15 秒、顯示上限 1–1,000。這些是軟體保護措施，不是工程標準。
- 同一對元素只保留一列。同一主體有多段／多 Solid 交集時，長度與交點代表第一段，另加明確 warning，不能當總穿透長度。
- 保留 Productization Registry、六種 pattern 定義、Self-Test Lab、installer 與 shared MCP Link DTO。

## 能力邊界

依據 `domain/mep-csa-clash-detection.md`、`mep-opening-candidate-scan.md` 及穿梁／套管 SOP。只做中心線穿越與標稱尺寸加雙側預留量；不涵蓋管件、保溫、實體擦碰。開孔下緣未知維持 null／「—」。穿梁、穿柱、斜穿與實體邊距未驗證均需人工複核，不提供套管自動核准、RC／SC／SRC 結構核准、開孔建立或模型修改。

## 自動測試

| Gate | 內容 | 入口 |
|---|---|---|
| A | 靜態 contract、registry、退休功能與共享依賴 | `node scripts/test-workflow-contracts.cjs test-artifacts/v041` |
| B | production 尺寸與 warning 純邏輯 | `dotnet run --project tests/WorkflowLogic -- test-artifacts/v041` |
| C | disposable Revit backend fixture | `scripts/test-reversible-gate-c.ps1` |
| C2 | production controller、排隊 host、來源／設定／filter／導航／stale recovery | `dotnet run --project tests/CoordinationWorkflow -- test-artifacts/v041` |
| C3 | 真正 ActiveUIDocument、production controller 與 ExternalEvent | 同一次 reversible fixture session，輸出 `workflow-runtime.json` / `.md` |
| D | N/A：產品功能唯讀 | fixture 與 selection 均有 read-back |

完整驗證入口：`scripts/verify-v041.ps1 -Runtime`。只有明確批准正式 release 時才加 `-Deploy`。舊 `verify-v04.ps1` 轉交相同 gate，不能繞過 C2/C3。

coordination-2 fixture 包含牆／板／雙 Solid 梁／柱、Pipe／Duct／Conduit、斜穿、不碰撞樣本、平移 MEP Link、平移 Host Link，以及獨立模型切換與 DocumentChanged。全程只開啟 launcher 建立的資料；RVT、RFA、完整部署 snapshot 與 raw runtime artifacts 留在 ignored `test-artifacts/`。

Revit 不採用子程序 APPDATA 覆寫來隔離 add-in discovery。因此 reversible runner 先核對 Revit 未執行、完整備份實際 deployment，再透過原 installer 暫載。無論結果如何，正常退出後都恢復完整 snapshot。若有未簽章對話框，需一次性的使用者互動；不殺程序、不覆寫已載入 DLL。

正式部署由 `publish-v041.ps1` 驗證每個 gate、來源 fingerprint 與 Gate C/C3 DLL SHA256，呼叫既有 `install-addon.ps1 -Version 2026`，核對全部 DLL 與 user／machine Addins 中唯一 manifest。失敗則沿用已驗證 snapshot rollback。

目前結果見 [v0.4.1 report](v041-report.md)、[failure analysis](v041-runtime-failure-analysis.md) 與 [checkpoint scopes](v041-release-checkpoints.md)。歷史 v0.4 報告保留，不代表本版 UI 測試。其他 Productization 批次未啟動。
