# v0.5.2.1 土方成果／成本設定／明細表 UX Refinement

Status: READY_FOR_OPTIONAL_UAT

## 驗證

- A: PASS — 489 PASS / 0 FAIL
- B: PASS — 20 PASS / 0 FAIL
- C: PASS — 20 PASS / 0 FAIL
- C2: PASS — 65 PASS / 0 FAIL
- C3: PASS — 48 PASS / 0 FAIL
- TerrainLogic: PASS — 211 PASS / 0 FAIL
- TerrainRuntime: PASS — 31 PASS / 0 FAIL
- CadRuntime: PASS — 47 PASS / 0 FAIL
- EarthworkNative: PASS — 61 PASS / 0 FAIL
- Build: PASS
- QAQC: PASS
- Rollback: PASS

Build / runtime SHA256: `AC00D4CB4DF616C4F9FEB0A916E8B53AE8E74A6DB4BF402A1A176DB9F2F0943F`

Formal deployment: PASS

## 功能與資料治理

- Audit：已完成 Profile、snapshot、DataStorage、Schedule ownership 與格式依賴追蹤。
- Production 只顯示未封存 Production Profile；TestFixture 僅在明確測試模式使用。實際 WPF selector runtime 已驗證。
- 成本設定檔分基本、土方、運輸、單價、其他成本五組；百分比 0–100、單位標籤、即時驗證與有效容量預覽。
- Profile GUID 穩定；計算欄位 hash 變動才增加 Version；clone 新 GUID/V1，archive 保留歷史。
- 每區保存完整不可變 snapshot；Profile 更新只標記 Stale，不改寫舊成本，明確重算才使用新版本。
- CalculationStatus 與 ReviewStatus 分開；stale 不允許標記已複核；警告仍保留且不代表核准。
- 金額／數量／係數兩位小數、百分比整數、車次整數；raw precision 不變。
- Summary 13 個可見欄位＋2 個 hidden ownership 欄位；Detail 44 個可見欄位＋2 個 hidden 欄位，分開建立／更新／開啟。
- 原地更新同 ZoneGuid；保護外部同名 Schedule；逐欄、格式、membership、加總 read-back 與故障 rollback。
- 實際 Schedule 金額 174.50、311.00；合計 485.50。V1 歷史 162 保留至明確重算。
- 本版沒有更改 Earthwork geometry algorithms、TIN、Cut/Fill、Cutter、Coordinate、CAD、truck trips 或 soil balance 公式。

## 詳細文件

- [Domain SOP](../../domain/site-terrain-earthwork.md)
- [Profile / Schedule audit](v0521-profile-schedule-audit.md)
- [Commit scopes](v0521-release-checkpoints.md)

## 限制

- Boundary TIN requires a convex planar boundary and planar design elevation; Existing/Proposed remains experimental.
- Cutter provides trial excavation volume only; area/depth unavailable and design fill not analyzed are explicit warnings.
- Zones may overlap; project totals sum saved records and do not prove non-overlap.
- Cost is based solely on user project profiles; it is not a market quote or approved contract sum. Mobilization is per zone.
- One geometry-free Generic Model record per Schedule zone; category-bound shared parameters and DataStorage are disclosed model writes.
- Schedule field format, member values and totals are verified through Revit API read-back; optional visual UAT remains.
- CSV labels SI units explicitly; Native quantities use Project Units, profile capacity/prices explicitly use m³ or ft³.
- Optional visual UAT remains; no production model was used. Existing nullable warnings are retained.

## 下一步

Optional UAT only; stop.

Raw fixture JSON/Markdown、模型、部署備份與 final receipt 僅保留 local ignored evidence。Final commit 由 development-state 的 SELF_COMMIT resolver 解析。
