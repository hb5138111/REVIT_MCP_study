# v0.5.2 土方數量／成本中心

Status: READY_FOR_OPTIONAL_UAT

## 驗證

- A: PASS — 489 PASS / 0 FAIL
- B: PASS — 20 PASS / 0 FAIL
- C: PASS — 20 PASS / 0 FAIL
- C2: PASS — 65 PASS / 0 FAIL
- C3: PASS — 48 PASS / 0 FAIL
- TerrainLogic: PASS — 184 PASS / 0 FAIL
- TerrainRuntime: PASS — 31 PASS / 0 FAIL
- CadRuntime: PASS — 47 PASS / 0 FAIL
- EarthworkNative: PASS — 38 PASS / 0 FAIL
- Build: PASS
- QAQC: PASS
- Rollback: PASS

Build / runtime SHA256: `42987353E7479B66F982F5983AB5FA3131DC9ABC532D4398C49ECD953086581E`

Formal deployment: PASS

## 修正與產品能力

- 根因：Cutter 回傳裸 double，UI 只處理 SiteEarthworkSummary，實際成功但顯示空白。已改 typed result，補真實 Selection → WPF binding → ExternalEvent → result fixture。
- 次因：LostFocus 清結果造成版面跳動；未明確輸入高程仍可能保留舊值。固定結果區，加入有限數值／單位／Document 失效防護，計算前重讀邊界與 Level。
- Quantity：挖方原地量、填方設計量、幾何淨方＝挖－填；保留完整 typed provenance。
- Logistics：鬆方／回填需求係數與再利用率由 Project Profile 輸入，保存 potential/reused/export/import；車次 ceiling(鬆方／有效容量)。
- Cost：挖土、裝載、外運車資、棄土、外購材料、回填、夯實與選填動員費，decimal 金額，無市場預設價。
- Multi-zone：穩定 ZoneGuid、新增／重算／明確確認刪除、只加總已存紀錄、不同幣別分開摘要。
- Native UI：Step 4 土方工程，Floor／ModelCurve 邊界或 Cutter、直接高程或 Level＋Offset、Profile editor、結果與計算依據、明細表、既有 3D 定位。
- Schedule：geometry-free owned Generic Models + shared parameters；36 欄，Preview／Confirm、安全同名處理、同 GUID 原地更新、逐欄與 membership read-back、故障 rollback。
- Export：Excel-friendly UTF-8 BOM CSV、JSON、Markdown；文字欄防公式注入。

## 依據與詳細設計

- [Domain SOP](../../domain/site-terrain-earthwork.md)
- [Failure analysis](v052-earthwork-failure-analysis.md)
- [Schedule architecture](v052-earthwork-schedule-architecture.md)
- [Commit scopes](v052-release-checkpoints.md)

## 限制

- Boundary TIN requires a convex planar boundary and planar design elevation; Existing/Proposed remains experimental.
- Cutter provides trial excavation volume only; area/depth unavailable and design fill not analyzed are explicit warnings.
- Zones may overlap; project totals sum saved records and do not prove non-overlap.
- Cost is based solely on user project profiles; it is not a market quote or approved contract sum. Mobilization is per zone.
- One geometry-free Generic Model record per Schedule zone; category-bound shared parameters and DataStorage are disclosed model writes.
- Schedule totals configuration and member sums verified; formatted footer pixels are not an asserted quantity source.
- CSV labels SI units explicitly; Native quantities use Project Units, profile capacity/prices explicitly use m³ or ft³.
- Optional visual UAT remains; no production model was used. Existing nullable warnings are retained.

## 下一步

Optional UAT only; stop.

Raw fixture JSON/Markdown、模型、部署備份與 final receipt 僅保留 local ignored evidence。Final commit 由 development-state 的 SELF_COMMIT resolver 解析。
