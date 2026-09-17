# v0.6.1 施工圖生產中心 — Production Hardening／外部圖框

**READY_FOR_OPTIONAL_UAT** — Release.R26 0 errors；QA/QC 75 PASS、0 FAIL、0 SKIP（2 個既有／工作樹提醒）。正式部署 8 個 DLL hash 相符、manifest 1 份。

Gate A 515、B 20、C 20、C2 107、C3 48、C4 22；Drawing 46、Terrain logic 211、Terrain runtime 31、CAD 47、Earthwork Native 61，全部 0 FAIL。

Build／C4／deployed SHA256：`9A2A9E1079373E0B70CBED8355D3823250F299A0BFB8B3F7ED685687C4F7177C`。實作 `a025393`；測試 `dfc3027`；本報告 checkpoint 由下方 SELF_COMMIT 規則解析。

本報告涵蓋本次支援範圍，並非整個施工圖 Domain 或公司圖框規範的認證。最終 Gate、SHA、部署及 Git 結果見同目錄 `v061-report.json`。

| # | 項目 | 實作與驗證 |
|---|---|---|
| 1 | UAT failure root cause | 正式 v0.6 隔離副本重現：未選分區使 planner 回零列，GeneratePlan 卻切到版面頁。詳細證據見 failure-analysis。不能斷言這是原始 UAT 唯一原因。 |
| 2 | Fixture bias | 舊 fixture 注入完整來源、分區與命名，掩蓋一般使用者初始路徑缺口。 |
| 3 | Production workflow fix | 初始化 catalog、唯一來源解析、不分區、逐列錯誤、step gate、建立後自動 QA。 |
| 4 | Gate C4 | 真正 Panel child ViewModel、typed ExternalEvent、真實外部檔案、建立及 read-back；不以直接 service.Apply 取代使用者路徑。 |
| 5 | Template architecture | Current Sheet 保留 SheetLayoutTemplate；外部 RFA/CAD 為 TitleBlockTemplate，再衍生 SingleMain 自動版面。 |
| 6 | Current Project Sheet | 沿用版面擷取、相同 Type、共用圖例／明細表及位置驗證。 |
| 7 | External RFA | 類別、Type、各 Type 範圍與參數分析；確認後載入；同名只用現有版本或取消，不覆寫。 |
| 8 | External RVT | PARTIAL；UI 明示尚未啟用，不宣稱跨文件版面／圖例轉移。 |
| 9 | DWG/DXF | 匯入暫存 TitleBlock Family，保存、載入、量測、read-back、關閉與清理。C4 實際使用 Revit 產生的 DWG；DXF 共用匯入路徑，本次未另跑 DXF 圖框 runtime。 |
| 10 | RFT resolution | 尋找已安裝 FamilyTemplatePath；唯一候選可用，多候選／缺少時明確選檔；成功設定保存於使用者 LocalAppData。 |
| 11 | Unit/size | Auto/mm/cm/m/inch/ft；顯示實際 mm 尺寸、A0–A4 建議及預期尺寸差；必須確認。 |
| 12 | TitleBlock limits | CAD 只轉幾何，不把文字推斷成圖號／圖名 Label，不自動建立公司參數、Revision 或法規核准。 |
| 13 | Auto layout | 外部圖框不需手工 Golden Sheet；SingleMain 依實測圖框、安全區域與 offset 定位。 |
| 14 | Safe region | 四邊 margin 加 XY offset；preview 及 read-back 檢查越界。灰圖框、白安全區、藍視埠、紅越界。 |
| 15 | Fast start | 選外部檔案、分析／載入、選樓層、預覽、確認、QA。 |
| 16 | Step 1 | 四種來源選項、可見能力界線；外部 Type／尺寸確認、快速使用。 |
| 17 | Step 2 | 常用圖別／命名／樓層優先；策略、Template、Scale、分區及參數放入進階區。 |
| 18 | Level resolver | 同 Level 的非 Template FloorPlan；唯一候選自動選，多候選不猜，保留合法人工指定，排除工具生成視圖。 |
| 19 | No-zone | 未選分區即每 Level 一列，Zone token 為空，不要求先建立 Scope Box，也不修改來源 crop。 |
| 20 | Validation | 無圖框／未選樓層／缺來源／圖號衝突／過期 Preview 均有原因；不能把零列送往建立。 |
| 21 | Cartoon Set | 每列顯示 Level、來源 View、圖框、Profile、狀態與版面；缺來源列仍保留。 |
| 22 | Confirmation | 明確對話框包含新增 Sheet、View、Viewport 數及圖框警語；取消不呼叫建立。 |
| 23 | Read-back | 驗證 Sheet、Type、View Level、Viewport、位置、版面及工具 ownership，不以 Success=true 代替驗證。 |
| 24 | Automatic QA | 建立後自動 Qa、Load、SheetStatuses 綁定；結果保留 ElementId，可開啟 Sheet；QA 不是施工核准。 |
| 25 | Fixture isolation | ProfileKind + 元素 ExtensibleStorage marker；Production catalog 過濾 fixture，兼容舊 DrawingFixtureTitleBlock Profile。 |
| 26 | C4 A | RFA＋3 個一般 Level＋不分區：3 Sheets、3 Views、3 Viewports、0 QA error。 |
| 27 | C4 B | 實際 DWG＋2 Levels：2 Sheets；載入 Family 內 CAD 幾何存在；暫存目錄已清理。 |
| 28 | C4 C | Current Sheet 擷取後建立 2 Sheets，Viewport 位置與來源相符。 |
| 29 | C4 D | 3 Levels／0 Zones 仍為 3 列、可建立。 |
| 30 | C4 E | 缺一層平面：保留 3 列，其中 2 ready、1 missing source；不回空計畫。 |
| 31 | C4 F | 同圖號顯示衝突、阻擋 Confirm，未建立。 |
| 32 | C4 G | 人工移動 Viewport 被辨識，預設保留並列 QA 提醒。 |
| 33 | C4 H | 重跑保留相同 Sheet IDs，不重複建立。 |
| 34 | v0.6 regression | 舊 Drawing fixture 與協調、基地／CAD、土方 Native 分開使用隔離模型回歸；結果依 JSON。 |
| 35 | Gates | A/B/C/C2/C3/C4 的執行次數、失敗數與 evidence 路徑見 JSON。 |
| 36 | Build | Release.R26；既有 nullable／API 警告保留，錯誤數以本次 build.log 為準。 |
| 37 | QA/QC | 最終檢查結果見 JSON；不將 Skip 當 PASS。 |
| 38 | Deployment | 全 Gate 與 source fingerprint 通過後由 canonical installer 正式部署；保留可逆快照。 |
| 39 | SHA256 | Build＝所有 runtime＝正式 deployed DLL；完整值見 JSON。 |
| 40 | Commits | 實作 a025393；驗證 dfc3027；release 文件 SELF_COMMIT，見 release-checkpoints。 |
| 41 | Push | 目的地僅 origin/bim-custom；完成後讀回 HEAD 與 remote ref。 |
| 42 | Remaining limits | 單一主視埠；RVT PARTIAL；CAD 幾何不等於參數化公司圖框；使用者 13065施工圖圖框.dwg 未提供實際檔案，本次不宣稱測過該檔。大模型尚未做專案規模壓力測試。 |
| 43 | Final Status | READY_FOR_OPTIONAL_UAT；停止於 v0.6.1，不展開 v0.7。 |

使用 SOP：`domain/construction-drawing-production.md`。詳細差異以本次 commits 為準。先前 C4 曾發現 RFA 開啟時 hash 的檔案鎖與初始化事件競態，均已修正後重跑；保留失敗 artifact，不算入最終 PASS。
