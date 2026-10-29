---
name: beam-clear-height-check
description: "梁下淨高檢討 SOP：以射線量測每支梁（含連結結構模型）梁底到下方樓板頂面的淨高，並回報梁底到天花板頂的管線空間，依使用者指定的要求值判定合格與否。Beam clear height / headroom under beams, ceiling plenum space. 觸發：梁下淨高、樑下淨高、淨高檢討、天花內管線空間、check_beam_clear_height。"
metadata:
  version: "1.0"
  updated: "2026-10-21"
  created: "2026-10-21"
  references:
    - "建築技術規則建築設計施工編（停車空間淨高見 parking-clearance-check.md 所引條文）"
    - "Revit API: ReferenceIntersector（FindReferencesInRevitLinks）、Solid.IntersectWithCurve"
  related:
    - parking-clearance-check.md
    - room-height-limit.md
    - mep-spacing-clearance-check.md
    - mep-space-demand-matrix.md
    - element-coloring-workflow.md
  referenced_by: []
  tags: [梁下淨高, 樑下淨高, 淨高, clear height, headroom, 天花內空間, plenum, 射線, ReferenceIntersector, check_beam_clear_height]
---

# 梁下淨高檢討（Beam Clear Height Check）

## 1. 目的

找出「梁底到下方完成樓板」的淨高不足處，並量出梁底到天花板之間可放管線的空間。
建築專員最常用在：停車空間淨高、居室天花高度可行性、機電管線能否從梁下通過的初步判斷。

## 2. 要求值必須由使用者指定（鐵則）

- `requiredClearHeightMm` **必填、沒有預設值**。不同用途（停車、居室、走廊、機房、設備搬運路徑）要求不同，AI 不得自行代入慣例值。
- 使用者沒說時先問；若是停車空間，先讀 `domain/parking-clearance-check.md` 取得該專案適用的數值，向使用者確認後再執行。
- `minCeilingSpaceMm`（梁底到天花頂至少要留的管線空間）同理，未指定就不檢查。

## 3. 量測方法

| 步驟 | 做法 | 理由 |
|---|---|---|
| 1. 取梁 | 主模型 + 所有已載入的連結模型中的「結構構架」（可用 `categories` 改為或加入風管、管、電纜架，量「管線下淨高」） | 建築模型的梁多半在連結的結構模型裡 |
| 2. 取樣 | 沿梁定位線均分 `samplesPerBeam` 點（預設 5）；每點以垂直線穿過梁實體求該處梁底 Z。定位線偏離實體時往兩側小幅偏移再試 | 可處理斜梁、變斷面梁、偏心定位 |
| 3. 找樓板 | 自梁底往下打射線，只認「樓板」類別（含連結），取第一個樓板頂面 | 淨高以完成面為準；建築樓板通常即完成面 |
| 4. 排除假命中 | 射線起點若落在某樓板內（例：梁底埋在加厚版內），該樓板只會被穿越一次 → 略過，取下一片 | 避免出現 0~50 mm 的假淨高 |
| 5. 天花 | 同一點再打一條只認「天花板」的射線：第一面 = 天花頂、第二面 = 天花底 | 得到梁下到天花頂的管線空間、與天花淨高 |
| 6. 取最不利 | 每支梁取所有取樣點中的最小淨高 | 檢討看最差處 |

**樓層歸屬**：`levels` 以「空間所在樓層」篩選——3F 代表梁底落在 3F 與 4F 樓層之間的梁（也就是 3F 空間頂上的梁），與梁本身的參考樓層（多半是 4F）無關。回傳同時列出 `referenceLevel`（梁的參考樓層）與 `spaceLevel`（下方空間樓層）。`spaceLevel` **一律由梁底高度決定**，不由射線打到的樓板決定。

**樓板是否屬於本層空間**：打到的樓板頂面若低於「本層與下一層標高的中線」，就不是本層空間的地板（多半是射線穿過開口、挑空打到下下層），狀態判為 `FLOOR_BELOW_SPACE_LEVEL`，量到的數值只供參考，不參與合格判定與各樓層最小淨高。中線以上的降版（例如浴廁降版）仍視為本層樓板。

## 4. 狀態判定

| status | 意義 | 建議處理 |
|---|---|---|
| `FAIL` | 淨高 < 要求值（容差 0.5 mm） | 回報 `shortageMm`，列入問題清單 |
| `CEILING_SPACE_LOW` | 淨高合格，但梁底到天花頂 < `minCeilingSpaceMm` | 通知機電／調整天花高度 |
| `NO_FLOOR_BELOW` | 射線往下找不到樓板 | 常見於挑空、樓梯間、開口、邊梁外側；人工確認 |
| `FLOOR_BELOW_SPACE_LEVEL` | 打到的樓板頂低於本層與下一層的中線（見 §3） | 多半是穿過開口或挑空；人工確認，不可當成合格 |
| `NO_GEOMETRY_HIT` | 取樣點皆未穿過梁實體 | 族群幾何異常，人工確認 |
| `PASS` | 合格 | 預設不列出（`includePassed=true` 才列） |

`NO_FLOOR_BELOW`、`FLOOR_BELOW_SPACE_LEVEL`、`NO_GEOMETRY_HIT` 都**不可當成合格**：工具把它們放在 `needsReviewItems`（與不合格清單 `items` 分開），`perLevel` 的 `minClearHeightMm` 只計入有效量測（`FAIL`／`PASS`／`CEILING_SPACE_LOW`），`needsReview` 為該層需人工確認的數量。

`needsReviewItems` 依 `NO_GEOMETRY_HIT` → `FLOOR_BELOW_SPACE_LEVEL` → `NO_FLOOR_BELOW` 排序，少數但較嚴重的項目排前面。兩份清單都受 `maxResults` 限制，統計數字（`failCount` 等）則一律是完整結果；`itemsTotal` / `needsReviewTotal` 大於清單長度時會附 `listNote`，報告須註明「清單僅列前 N 筆」。

## 5. 已知限制

- 只認「樓板」類別為下方完成面；以「一般模型」或「結構基礎」建的地坪不會被命中 → 會成為 `NO_FLOOR_BELOW`。
- 若建築樓板與結構樓板分開建模，射線取到的是第一片（通常是建築完成面），符合淨高定義；但若只有結構版，量到的是結構版頂，需扣除地坪裝修厚度後再判斷。
- 射線檢測依賴「AI檢討_射線用3D」視圖的可見性；工具會自動建立該視圖並確保樓板、天花、連結可見。請勿在該視圖套用樣版或隱藏連結。
- 座標為主模型內部原點（mm）。
- 時間預算（`timeBudgetSec`，預設 240 秒）：大型專案全模型可能需要 20 分鐘以上，超過預算就停止並回傳 `truncated=true` 的部分結果。部分結果的不合格數與各樓層最小淨高都可能偏低，**不可當成完整檢討**；請用 `levels` 分樓層執行。

## 6. 標準流程

1. `get_model_snapshot` 確認文件、樓層、連結是否已載入。
2. 向使用者確認 `requiredClearHeightMm`（與是否要檢查 `minCeilingSpaceMm`）、要檢討的樓層。
3. 先不放標記執行 `check_beam_clear_height`，回報 `perLevel` 摘要與 `FAIL` 前幾名。
4. 使用者要看位置時：`createMarkers=true`（連結中的梁無法直接上色，用標記帶回主模型）或 `highlightInView=true`（主模型中的梁），再用 `export_view_image` 帶 `focusElementIds` 或檢視「AI檢討_結果3D」。
5. 檢討結束後 `clear_review_markers` 清除標記。

## 7. 報告格式

輸出時引用本文件：`Per domain/beam-clear-height-check.md, step 3: ...`，至少包含要求值、檢討樓層、各樓層最小淨高、不合格清單（元素 ID、來源模型、梁類型、淨高、不足量、位置），以及需人工確認的數量與清單（`needsReviewItems`）；若 `truncated=true`，須明講結果不完整。
