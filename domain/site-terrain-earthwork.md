---
name: site-terrain-earthwork
description: "基地測量點匯入、座標定位、Toposolid 與土方計算 SOP；適用 survey terrain import、coordinate alignment、excavation、cut/fill。"
metadata:
  version: "0.5"
  updated: "2026-09-16"
  references: ["Autodesk Revit 2026 installed RevitAPI.xml"]
  related: [cad-block-point-placement.md, dwg-column-import.md]
  referenced_by: []
  tags: [terrain, survey, coordinates, earthwork, QAQC]
---

# 基地地形／土方 SOP

## 1. 匯入與 QA

CSV/TXT 明確設定分隔符、header、欄位索引與 m/mm/ft。X/Y/Z 必填、ID/Code 選填；小數點採 invariant decimal，不猜單位或座標基準。所有輸入列須有來源列號；缺值、非有限數值與重複點都留下診斷。重複 XY 同高程只取一點並記錄；不同高程阻擋 Create。離群／孤立點只提醒，不能自行刪除。顯示輸入、有效、拒絕、重複、警告數，XY 範圍、高程與密度。拒絕列須由使用者明確確認後才可 Create。

## 2. 座標基準

純計算使用 metres；Revit API 使用 internal feet，必須明確轉換。SharedCoordinates 為目前 ActiveProjectLocation 的 shared basis；讀取 Internal Origin、Project Base Point、Survey Point、ProjectPosition 與 transform。SurveyToInternal 與 InternalToSurvey 必須互逆，且與 GetProjectPosition 已知值獨立比較，不能只靠自洽 round-trip 證明方向。LocalCoordinates 必須明示輸入已在 Internal Origin／internal axes；不得把一般 Easting/Northing 當 internal X/Y。

ControlPointAlignment：1 點平移；2 點旋轉平移；3+ 點以去中心的 2D rigid least squares 求角度，Z 採平均差；scale 永遠 1。回報每點 residual、RMS、最大誤差。退化／重複控制點不能求旋轉；拒絕。Residual 超過使用者設定的 tool tolerance 時可 Preview，不可 Create。Tool tolerance 不是公司或測量規範。

建築保持不動。Link transform 僅分析；不在本流程執行 Move、Rotate、ProjectLocation 更新或階段修改。

## 3. Preview 與減點

無模型元素的 2D preview，顯示點數、範圍、高程、離原點距離、座標模式、完整 rigid transform、控制點誤差、Type/Level。確定性 grid 減點保留凸包邊界、全域極值與所有非空 Code 點（僅保留 coded points，不宣稱已建立 breakline connectivity）。以明確 grid tolerance 與高程誤差限制運作；回報原始／最終點數、減少比例與平均／最大高程誤差及其定義。誤差無法可靠界定時不宣稱 TIN 精度。大量點須減點或明確覆核 override；不修改 Revit.ini。非凸 surveyed boundary 未提供時，points-only 建立的凸包不能代表法定基地界。

## 4. 建立與回讀

分析 → Preview → 明確 Confirm → Transaction → Create → 重新讀取 ElementId、Category、Type、Level、bounding box、頂面範圍、Area/Volume。實體底面包含 type 厚度，不以底面 Z 冒充 survey 最低點。頂面點範圍與 transformed points 比較；失敗 rollback。任何來源、設定、Document 或模型修改均使舊 preview/confirmation 失效。報告保存失敗不能默默宣稱完整成功。

## 5. 土方

A：指定 host Toposolid 與 Floor/Roof/Toposolid cutter，先 CanBeExcavatedBy；Preview 顯示來源、相交與 Revit 試算（transaction rollback，不留變更）。明確 Confirm 後 ExcavateBy；read-back 量與 Preview 一致，交易失敗 rollback。不移動或修改 cutter。

Boundary quantity：由現況頂面 TIN，將每個 triangle 裁切到明確 boundary，再依 target elevation 的正負深度零線分割；對每個分片積分線性深度。Cut/Fill 分開，Net = Fill − Cut。Area 是實際覆蓋水平投影；未覆蓋 boundary 必須警告／阻擋精準結果。AverageCutDepth = Cut/CutArea，AverageFillDepth = Fill/FillArea。不得以 bounding box 或全區平均高差當精準體積。第一版 boundary 限明確驗證的凸多邊形；其他形狀不得靜默近似。

Existing/Proposed 為 experimental，不宣稱 Revit Graded Region 自動化。沒有可信任的 proposed triangulation 與重疊裁切證據時不輸出正式數量。

Coverage 採獨立 1e-7 relative numeric tolerance，與控制點／減點的 m tolerance 分開。使用者不能放寬控制點 tolerance 以掩蓋 boundary 未覆蓋或 TIN 重疊。

## 6. 稽核與 fixtures

每次建立／計算保存 JSON、CSV、Markdown：source filename/SHA256、counts/rejected rows、units/basis、transform/control residuals、reduction與誤差定義、terrain ID、boundary/target、method/tolerance/warnings、timestamp；Project Units 顯示，機器報告保留明確 SI units。Fixture 僅新建 disposable 模型。平面／斜坡／複合坡、混合 cut/fill、已知座標正反向、失敗回滾與 UI 確認失效皆需 Expected/Actual，不能只驗 Success 或 volume > 0。
