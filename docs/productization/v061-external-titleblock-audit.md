# v0.6.1 外部圖框能力稽核

本機 RevitAPI 26.5 reference 與 working tree 查核；C4 已完成真實 RFA／DWG 的 A–H 驗證，詳細 evidence 與 SHA 見 v061-report.json；API 存在本身不是 runtime 證據。

| 項目 | 查核與實作決策 |
|---|---|
| 既有能力 | DrawingProductionFixture 已用 NewFamilyDocument→SaveAs→LoadFamily；CadTerrainSelfTest 已以 DXF import／DWG export 生成真實外部測試檔。重用這些 API 範式及 disposable launcher，不新增 MCP tool。 |
| 建立 Family | Application.NewFamilyDocument(string)；必須驗證 OwnerFamily.FamilyCategory 是 OST_TitleBlocks。 |
| RFT | 從 Application.FamilyTemplatePath 搜尋候選，不假設 Chinese／English 路徑。僅唯一候選可自動用；多候選或不可靠時使用者選 .rft，保存 LocalApplicationData 設定。 |
| DWG/DXF | Document.Import(string,DWGImportOptions,View,out ElementId)；需 Transaction、可列印非 template family view。ImportPlacement.Origin，明確單位；只在暫存 Family document 分析，不在專案留下 CAD。 |
| 單位與尺寸 | Auto 只是建議，尺寸以 import bounds 轉 mm；A 系列只提示最接近尺寸，需使用者確認單位／預期尺寸。不能由 bounds 推斷公司資訊欄。 |
| 載入 | 明確確認後，專案 TransactionGroup 內 LoadFamily，建暫時 Sheet 測量 TitleBlock 後 rollback 該測量交易；Family／Type／Category read-back 再提交。既有同名只提供使用目前版本或取消，不 silent overwrite。 |
| 暫存與清理 | 每次專屬 temp directory，finally 關閉背景文件且不保存來源；刪除僅限本操作建立的已驗證路徑。來源／RFT SHA freshness 防止分析後更換檔案。 |
| 參數限制 | RFA 列出現有參數；CAD 僅保留 imported geometry，不推測文字→Label，不產生公司 shared parameters 或 Revision schedule。 |
| CAD persistence | SaveAs RFA 後載入專案；read-back 驗證圖框實際範圍。C4 必須確認 temp 刪除後專案圖框仍存在。 |
| 外部 RVT | 既有 CrossDocument command 有分級重建、matching、CopyElements 與來源關閉；不是現有 Drawing Profile 的可攜引用。跨文件 View Template／Legend／Schedule／Parameter ID remap 尚未通過 C4，本版 UI PARTIAL／停用，不阻擋 Current Sheet、RFA、CAD。 |
| 驗證 | 真實 RFA、Drafting View→DWG 生成器；Panel child ViewModel→ExternalEvent→Load→選樓層→Plan→Confirm→Apply→read-back→QA。A–H 全 PASS 且 hash 同版才能 READY。 |

API 來源：本機 revit-api-2026/applicationservices.md、db-d.md、db-e.md、db-g-i.md。程式參考：MCP/Core/Drawing/DrawingProductionFixture.cs、MCP/Core/Site/CadTerrainSelfTest.cs、MCP/Core/Commands/CommandExecutor.CrossDocument.cs。Domain 方法：construction-drawing-production、sheet-viewport-management、site-terrain-earthwork（僅 CAD 隔離分析安全模式）。
