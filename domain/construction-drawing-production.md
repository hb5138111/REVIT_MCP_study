---
name: construction-drawing-production
description: "施工圖生產中心 SOP：從既有 Sheet 擷取版面，或由外部 RFA／DWG／DXF 建立圖框樣板，保存出圖樣板、規劃樓層分區圖紙、確認建立更新並執行 QA 與 read-back。適用 construction drawing production、Cartoon Set、Drawing Package、人工修改保護。"
metadata:
  version: "0.6.1"
  updated: "2026-09-17"
  references: []
  related: [sheet-viewport-management.md, dependent-view-crop-workflow.md, detail-component-sync.md, auto-dimension-workflow.md, matchline-automation.md, viewport-type-scale-sync.md]
  referenced_by: []
  tags: [施工圖, drawing, sheet, viewport, blueprint, package, QA]
---

# 施工圖生產／Construction drawing production

本 SOP 整合既有圖紙、裁剪、視埠方法；公司圖號、圖框名稱、參數與施工核准標準由專案定義，不由工具推測。正式能力及 runtime 驗證以 [v0.6.1 report](../docs/productization/v061-final-report.md) 為準。

## 五步驟

1. 樣板來源：可選外部圖框（見下方 v0.6.1 規則）；目前文件 Sheet 則讀取 Title Block Type／位置／範圍、視埠位置與種類、比例、View Template、詳圖號、標題 offset／line length、Legend 與 Schedule。擷取不修改模型。
2. 出圖範圍：fresh query Levels，依 ProjectElevation 排序；每層或逐張指定來源視圖，分區可不選；需要分區時指定 Scope Box 或四條正交網格及明確 padding。不同模型不可沿用 session identity 或未驗證的 ElementId。
3. 圖紙計畫：以明確 token 規則產生 Cartoon Set；檢查空值、未知 token、數值格式、重複圖號、既有圖號、保留圖號、視圖名稱及版面。來源已放置的模型視圖必須複製或建立從屬視圖。
4. 預覽建立：無衝突才可確認；Confirm 綁定計畫與模型 freshness。以 TransactionGroup 包住每張 Transaction，任何實作／read-back 失敗整批回復。Native UI 直接呼叫 typed service，不經 MCP／TS。
5. 圖面 QA：讀回實際圖號／圖名／圖框、主要視圖、Template／Scale、Crop／Scope、Viewport Type／中心／詳圖號／標題及共用內容。問題需能開啟實際 Sheet。

## Blueprint 與版本

Blueprint 是資料，不是整張 Sheet 的黑盒複製。Profile 保存 Blueprint 與規則，重新擷取後明確保存才增加版本。舊圖紙標記 TEMPLATE_OUTDATED，不會自動套用。
同圖框 Type 沿用 placement coordinates；跨圖框轉換需獨立驗證，未通過不得直接套用 absolute coordinates。圖框 bounds 不是經認證 printable area；不自行發明 margin。
Scope Box 優先。網格分區若啟用，必須驗證每條網格存在、軸向可靠，padding 明確輸入且只能加一次。從屬視圖沿用母視圖的比例與樣板，不能為了出圖偷偷更改既有母視圖。

## 複製政策

NEVER_COPY：Revision、Revision Cloud、issue state、工具 metadata、未知受保護內建參數。Sheet Number／Name 禁止從來源複製，屬 GENERATED，由計畫設定。
USER_SELECTABLE：可寫入的繪圖／校對／設計／核准者與自訂文字參數；預設不勾選。以 ParameterId 定位，不以 localized LookupParameter 作核心 routing。Project Information driven labels 不重複寫值。
Profile 可將 Level、Zone、DrawingType、Discipline、Phase 映射至勾選的既有文字參數；不建立 Shared Parameter。View 參數映射只允許獨立複製視圖，避免改寫既有母視圖；缺少參數或映射值必須明示，不能默默略過。
Legend／Schedule 可明確沿用同一 View；不複製 Schedule View 本身。任意註記、共用詳圖、自動尺寸及 Matchline 預設停用；只有 Domain、backend、runtime fixture 都成熟才啟用。TextNote 不等於 View Reference。

## 所有權與更新

PackageGuid＋Level／Zone 穩定 key 對應 GeneratedSheetGuid、Sheet UniqueId、ProfileGuid／Version 及最後一次完整 read-back baseline，保存在工具 Extensible Storage，不新增公司 shared parameters。
只管理工具建立或明確納管的元素。既有非工具圖紙同號屬 CONFLICT，不能自動接管。比較 expected／actual／baseline，分類 ADD、UPDATE、UNCHANGED、MANUAL_OVERRIDE、CONFLICT、MISSING。
MANUAL_OVERRIDE 預設保留且提示需人工複核；套用樣板必須明確選擇、重新預覽並確認。遺失元素不能默默重建；內容拓樸變更不得偷偷刪除已有視埠／註記。
v0.6 支援整張或逐張保留／重套；尚未提供同張圖紙逐欄合併。既有公司圖紙納管尚未啟用，因此所有非工具圖紙均保持 Preview only／同號衝突，不透過名稱自動接管。
批次改號採既有 sheet SOP two-pass，先驗證與非更新圖紙無衝突。重跑同一未變更 package 不新增 Sheet、View、Viewport、Legend、Schedule 或 Dimension。

## QA 與性能

ERROR 阻擋 Ready：遺失主要視圖／圖框、圖號衝突、超框、版面重疊、實際寫入不符。WARNING：人工修改、樣板過期；INFO：明確停用的選用共用內容。Ready 只代表工具 QA，不等於正式核准或 Issued for Construction。
圖紙目錄使用工具記錄與實際 Sheet read-back 顯示圖號、圖名、Level、Zone、DrawingType 及 Draft／NeedsReview／Ready，不為目錄增加模型參數。畫面預覽的標題點僅是示意，不代表文字包絡或 printable area 已認證。
大型模型按使用者操作查詢，先 summary／count、再計畫明細；同批一次建立必要字典，不逐張全模型 collector。UI 表格 virtualization，不 silent truncation。純邏輯可背景執行，所有 Revit API 必須在 ExternalEvent context。
測試只使用 disposable fixture；必須包含 Template extraction、建立、重跑、人工移動保留／套用、V2、圖號衝突、QA negative cases 及真實 ViewModel→Dispatcher→read-back 工作流。

## v0.6.1 外部圖框與 Production journey

TemplateSourceKind 區分 CurrentSheet、ExternalRfa、ExternalRvt、Cad。CurrentSheet 為 SheetLayoutTemplate，沿用實際主要 Viewport／Legend／Schedule 位置；ExternalRfa 與 Cad 為 TitleBlockTemplate，只有圖框，使用自動單主視圖配置，不要求先做 Golden Sheet。ExternalRvt 的跨文件引用需獨立 read-back；未通過時標 PARTIAL 並停用。

RFA 分析需驗證 Title Blocks Category、Family／Type、範圍與參數，背景來源文件關閉且不保存。明確確認後才載入專案，以 TransactionGroup 保證失敗回復並 read-back 類型／Category／實際圖框範圍。同名 Family 不覆寫，只允許明確使用目前版本或取消。

DWG／DXF 先於暫存 TitleBlock Family 分析幾何、bounds 與圖層摘要。RFT 由 Revit 設定搜尋，無唯一可靠候選時讓使用者選擇；選擇保存在本機 application setting，不入 Git 或模型。Auto 單位及 A0–A4 尺寸均僅為建議，需使用者確認。確認後 import→暫存 RFA→Load→read-back，關閉背景文件並清理專屬暫存目錄。只保證幾何，不猜測文字與 Label 語意，不建立 Revision／公司 shared parameters。

AutoSheetLayoutService 以 TitleBlock bounds 扣除明確設定的左／右／上／下邊距為可用出圖區，主視埠置中並可設定位移。預設零邊距代表尚未保留資訊欄，不是 printable area 認證。保存於 Profile；不自行猜測公司標題欄。Preview 顯示圖框、可用區與預估視埠，實際 read-back 必須在可用區內。

未選任何 Scope／網格分區定義為 NoZone，不影響產生圖紙：每個 Level 一張，保留來源 Crop／Scope。Zone token 可為空；其他必要 token 空值仍阻擋。LevelViewResolver 以一次 FloorPlan collector 按 GenLevel 分組，排除 template、測試 marker 與工具生成的複製視圖。唯一候選自動選；零或多候選僅標示該樓層缺來源／需選擇，保留其他計畫列。規劃階段不新增 View。

Production UI 隱藏 Fixture marker／ProfileKind 與可辨識的舊測試樣板引用，只有專用 Developer/Test mode 可顯示。一般用戶不需理解 ElementId、internal units 或 raw enum。零列留在範圍設定；衝突阻擋確認；尚未建立不進 QA。所有 disabled 的核心動作顯示缺項；建立成功自動讀回 QA 與可開啟的 Sheet 清單。

Gate C4 必須從 BimConstructionPanel drawing child 初始化，經選檔／樣板、分析、載入確認、選 Levels、自動來源、計畫、確認、ExternalEvent、建立與 QA binding。A 外部 RFA 三層、B 真實外部 DWG 兩層、C 既有 Sheet、D 不分區、E 缺一層來源、F 圖號衝突、G 人工修改、H 同包重跑全部 PASS，且 Build／C4／Deployed SHA 相同，才可宣稱 READY。舊 backend／fixture PASS 不能取代 C4。
