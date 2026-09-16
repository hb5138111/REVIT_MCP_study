---
name: site-terrain-earthwork
description: "基地測量點匯入、座標定位、Toposolid 與土方計算 SOP；適用 survey terrain import、coordinate alignment、excavation、cut/fill。"
metadata:
  version: "0.5.1"
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

## 7. CAD 地形來源（v0.5.1）

DWG/DXF 經 Revit DWGImportOptions 匯入既有非 template View；Origin 使用 Import，Shared 使用 Link（Revit Import 的 Shared 路徑在隔離 fixture 回傳 internal error）。TransactionGroup 內擷取 primitive DTO 後必須 rollback。比較匯入前後 ImportInstance、CADLinkType、View ID 集合，失敗不得交付分析結果。不得留下暫存 CAD、View 或圖層；不修改來源檔案。分析前後 SHA256 必須一致。

Origin 與 Shared 為明確選項。Shared 必須通過 ActiveProjectLocation / GetProjectPosition 獨立方向驗證。失敗顯示「CAD 無法依共用座標定位，請檢查測量圖座標與 Revit Shared Coordinates。」不 fallback。根 GeometryInstance 使用 GetSymbolGeometry，根 GetTotalTransform 與根 GeometryInstance.Transform 比對；子 instance 逐層相乘，絕不再對 GetInstanceGeometry 套相同 transform。輸出 dataset 為已定位的模型公尺座標，不再套一次 Shared transform。

圖層依 GraphicsStyleId / GraphicsStyleCategory。支援 Point、PolyLine 頂點與 Line 端點；Arc/其他 Curve 第一版列為需複核，不無限 tessellate。文字、block attribute、proxy/AEC 不推導高程；幾何零高程時明示「CAD 圖層存在高程文字，但幾何沒有可靠 Z 值，第一版不自動配對。」此為資料覆核提醒，不代表 API 能完整辨識原始 CAD 文字。零 Z 可能是真實平面，仍須人工確認。

上限：來源 100 MB、100,000 geometry objects、200,000 vertices、巢狀深度 16。超限中止並完整 rollback：「CAD 地形資料量過大，請縮小圖層或使用減點。」不 silent truncation。圖層選擇後才進入共同 Point QA／減點；同 XY 衝突仍阻擋。閉合邊界僅接受平面、不自交、凸多邊形；所有候選明列供選擇，不選最大者。第一版 CAD 邊界供土方裁切，不冒充 Toposolid breakline 約束。

來源稽核保存 SourceKind、檔名／SHA、CAD 單位／placement／圖層／geometry counts／boundary 與 transform evidence；CSV 保存 delimiter／mapping／units。來源更換使下游座標 preview 與確認失效；土方 cutter 變更不清除已解析來源。建立前對有效、忽略、使用點數與警告作一次明確確認。UI 四步分頁；所有使用者座標輸入依顯示單位轉成 SI，報告保留 SI 定義。

### Shared CAD 垂直基準
隔離 runtime 證實：Shared Link 套用 XY／真北，但 native CAD transform 可能保留匯入參考樓層 Z。DTO chain 明確以經 GetProjectPosition 獨立驗證的 SurveyToInternal.DeltaZ 正規化垂直基準。Native transform、垂直修正量與 effective transform 均保留 evidence；四個基準探針須與 ProjectPosition 一致，不放寬 XY／旋轉檢查。這是資料座標轉換，不是 Origin fallback，也不移動 CAD 或建築。
