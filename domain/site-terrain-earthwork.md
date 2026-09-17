---
name: site-terrain-earthwork
description: "基地測量點匯入、座標定位、Toposolid 與土方計算 SOP；適用 survey terrain import、coordinate alignment、excavation、cut/fill。"
metadata:
  version: "0.5.2"
  updated: "2026-09-17"
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

Boundary quantity：由現況頂面 TIN，將每個 triangle 裁切到明確 boundary，再依 target elevation 的正負深度零線分割；對每個分片積分線性深度。Cut/Fill 分開；v0.5.2 正式紀錄的 GeometricNetVolume = CutBankVolume − FillDesignVolume。既有 engine 的 NetVolume 為 Fill − Cut，僅保留舊 API 相容性；新 UI／紀錄明確轉換語意。Area 是實際覆蓋水平投影；未覆蓋 boundary 必須警告／阻擋精準結果。AverageCutDepth = Cut/CutArea，AverageFillDepth = Fill/FillArea。不得以 bounding box 或全區平均高差當精準體積。第一版 boundary 限明確驗證的凸多邊形；其他形狀不得靜默近似。

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

## 8. 土方區、物流與成本（v0.5.2）

EarthworkZone 以穩定 ZoneGuid 辨識，保留區號／區名、Terrain／Cutter／Boundary Element IDs、可靠邊界、Target Source、Level／高程、方法、計算時間與需複核狀態。重新計算更新同一 GUID，不增加重複紀錄。不同區可以引用同一 Terrain。直接高程或 Level.ProjectElevation + Offset 均按專案長度單位輸入，轉為模型原點基準的 SI metres；不再套 Shared transform。未輸入／非有限高程、缺來源、無效邊界時阻擋計算。

數量分為 CutBankVolume（挖方原地量）、FillDesignVolume（設計幾何填方）、GeometricNetVolume（挖－填）。Cutter trial 只提供 Revit 試算挖方，面積與深度若無可靠資訊須為 null，不能顯示 0 冒充已分析；`CUTTER_FILL_NOT_ANALYZED` 明示沒有設計填方分析。分析不要求先真的 ExcavateBy；實際開挖按鈕需另行確認。

所有係數與單價由本專案 Profile 明確輸入，沒有公司／市場預設值：SwellFactor = Loose Excavated / Bank Cut；FillLooseFactor = Loose Fill Required / Design Fill；ReusableRate 為 0～1。CutLoose = CutBank × Swell；FillLooseDemand = FillDesign × FillLooseFactor；PotentialReusable = CutLoose × ReusableRate；Reused = min(PotentialReusable, FillLooseDemand)；Export = max(CutLoose − Reused,0)；Import = max(FillLooseDemand − Reused,0)。全部中間值保存。

有效車斗容量 = 容量 × 裝載率。容量與裝載率必須為有限正數，裝載率不可超過 1。Export／Import 車次各自 ceiling(鬆方量／有效容量)，零量為零車次；不得為降低車次而靜默截斷正數尾差。Profile 明定 m³ 或 ft³ 作為車斗及體積單價基準，所有幾何 quantity 保持 SI，換算一次。UI 數量依 Project Units；CSV 機器欄位明示 m²／m³，保留完整 Profile 單位。

成本：挖方原地量×挖土單價；外運鬆方×裝載單價；外運車次×運輸單價；外運鬆方×棄土單價；外購鬆方×外購土單價；設計填方×回填施工單價及夯實單價，再加選填動員費。動員費每區計入一次，空白不計。金額以 decimal 計算；幣別是使用者設定，不換匯。明示「成本為依本專案設定之估算值，不是市場報價或合約金額。」既有紀錄保留 Profile 快照，編輯 Profile 不偷偷改寫歷史結果。

## 9. 專案紀錄與 Revit Schedule

專案 Profiles 與 analysis records 存入工具管理的 DataStorage。每次保存需 Preview／Confirm，寫入後 read-back。摘要只 SUM 已存紀錄，不重新 triangulate；顯示「各區範圍可能重疊，總量僅為明細加總。」不同幣別分開摘要，單一 Schedule 不混合幣別。Candidate／需人工複核不代表正式核准。

Schedule architecture 必須經 dedicated record fixture 證明 schedulability 才可發布：每區一個獨立、無視覺幾何的 owned Generic Model record；shared parameters 僅綁定該 category，ExtensibleStorage 保留完整來源及 Profile。不得為結果色圖建立大量永久 DirectShape。若無幾何紀錄無法可靠 schedule，先改架構並重測，不靜默建立可見實體。

建立／更新前列出 Schedule、參數及新增／更新 GUID，明確 Confirm 後單一 transaction group 寫入；欄位、GUID、record count、schedule membership 及每個數量／成本值 read-back 全部一致才成功。使用 `CanTotal` 驗證可加總欄位，grand total 標示各區可能重疊。既有未受工具管理的同名明細表不覆寫，改用安全名稱。來源 Element.VersionGuid 不符時必須重新計算，不以舊數量更新。失敗 rollback 完整 transaction group。

刪除分析紀錄需明確確認，若有對應 schedule record，須一併明列與確認。僅刪工具 ownership 與 ZoneGuid 相符的 analysis record，絕不刪 Terrain／Cutter。刪除造成額外元素連帶影響時 rollback。測試必含兩區建立、A 重算後仍兩區、stable record IDs、逐欄回讀、總量與成本、故障注入 rollback、未授權寫入阻擋及文件切換失效。

## 10. 成本設定檔與成果治理（v0.5.2.1）

設定檔以 ProfileGuid 辨識，ProfileVersion 初次為 1。幣別、單位基準、土方係數、容量、裝載率或單價／動員費等計算欄位改變時增加版本。名稱、車型顯示名稱與封存等 metadata 不改計算版本；歷史顯示仍使用原 snapshot。ProfileHash 對上述計算欄位使用固定順序、文化中立序列化；不含 GUID、名稱、時間、版本或 UI 狀態，decimal scale 等價值採相同 hash。

Production UI 只列出未封存的 Production profiles，並排除 TestFixture 結果／匯出。TestFixture 只在明確注入的 fixture mode 使用，不作正式預設。舊版已知 Runtime fixture only／TEST／Fixture truck 組合在讀取時辨識為 TestFixture；不猜測其他使用者名稱。所有舊版數量與價格保持原值。Clone 產生新 GUID、V1；Archive 保留原資料與歷史 snapshot，不 hard delete 已使用設定檔。

每個計算結果持有不可變 EarthworkProfileSnapshot，含完整設定值、GUID、名稱、版本及 hash。修改設定檔只標記 CostProfileOutdated／Stale，不動歷史成本。使用者按「使用最新設定重新計算」後才產生新 snapshot，確認儲存才更新同 ZoneGuid。歷史區可繼續顯示舊版本及明細；過期來源或設定不得直接標記已複核。

CalculationStatus 區分尚未計算、已計算、結果已過期、計算失敗；ReviewStatus 區分待複核、已複核、有警告。Warnings 存在時顯示有警告，不因人工複核而隱藏。標記已複核只改工具 review metadata，不重新計算；不代表工程數量、合約或測量核准。重新計算回到待複核。

UI 再利用率與裝載率使用百分數，ViewModel 轉換為 fraction 一次。再利用率允許 0～100%；裝載率須大於 0 且不超過 100%，維持有效容量必須大於 0 的既有公式。設定檔分為基本設定、土方性質、運輸、單價及其他成本；每項價格旁明示幣別／體積或車次基準，衍生容量預覽不寫模型。

格式化不改計算精度：數量與金額兩位小數、係數兩位、百分比整數百分數、車次整數。Revit Schedule 在欄位設定 FormatOptions，保留原始 double／decimal 計算值；不修改全專案 Units。新百分比顯示欄位與舊 fraction 參數分離，避免改變舊資料語意。CSV／JSON 保留可追溯原始數值與 snapshot；Native UI 與 Schedule 提供閱讀格式。

摘要表「土方工程摘要 (BIM)」保留日常區名、數量、外運車次、總價、計算／複核狀態、設定檔／版本；詳細表「土方工程明細 (BIM)」保留完整係數、成本拆分與來源。各自持有 ownership，共用穩定 ZoneGuid record；外來同名表不覆寫。正式與測試紀錄以明確 ownership filter 分離，不混用。摘要／詳細表保留適用總計，註明各區若有重疊範圍，總量為各區明細加總。
