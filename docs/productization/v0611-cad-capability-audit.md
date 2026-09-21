# v0.6.1.1 CAD 能力稽核

依 `CLAUDE.md` → `domain/construction-drawing-production.md` → 既有 workflow / C# 查核。正式發布狀態另見 v0611-report.json；本文件只記錄能力與設計依據。

| 既有能力 | 重用與缺口 |
|---|---|
| ExternalTitleBlockService | 重用 RFT 選擇、來源 SHA freshness、TransactionGroup、Family 載入、暫時 Sheet probe、失敗 rollback。原先整份 CAD import bbox 不能隔離同圖層多圖框。 |
| CadTerrainData / CadTerrainService | 重用 bounded analysis、不可截斷、幾何 DTO 與暫存回復原則。地形取點不能保證 CAD 文字及圖欄轉檔保留，未冒用作圖框轉換。 |
| DwgColumnExecutor / ezdxf_worker | 既有 Python + ezdxf + ODA 讀文字路徑針對連結柱號，沒有圖框隔離／完整轉檔；不繞 TS/MCP 新增固定 Native UI 功能。 |
| DrawingProductionViewModel / RevitDrawingService | 重用正式 ExternalEvent → Profile → Plan → Apply → QA、NoZone、自動樓層來源、idempotency 與人工修改保護。 |
| DrawingProductionJourneyFixture | 延伸同一原生 ViewModel 與 UI，測試前明確選候選、預覽、用途與幾何確認；沒有 direct-service 取代 production journey。 |

新增 typed `CadTitleBlockAnalyzer`／`CadGeometryClusterService` 純幾何判定，以及 `CadTitleBlockFileService` 本機檔案介面。ACadSharp 固定 3.7.16（MIT、net8.0）提供 DWG/DXF 讀寫與 Entity clone／transform，不依賴 AutoCAD／ODA 安裝；參考[官方專案](https://github.com/DomCR/ACadSharp)與該固定套件附帶 XML API。所有公司 CAD 僅在本機記憶體與 ignored test-artifacts／專屬暫存目錄處理；無外部上傳。

Family 採每候選獨立 RFA，避免兩個 Type 共用族內圖形。Family 名稱綁來源與確認幾何的指紋；不覆寫既有 Family。Profile 名稱及用途由使用者確認，與公司規則無關。

本機 RevitAPI 26.5 查核：Document.Import、DWGImportOptions、DocumentValidation.CanDeleteElement、CurveElement.SetGeometryCurve／LineStyle、GeometryInstance.GetInstanceGeometry、Curve.Tessellate、Document.EditFamily。RFT 預定義四條紙張邊界無法刪除；不可假設另存再開即可刪除。處理方法與驗證依 drawing Domain。

界線：無 OCR／智慧文字標籤／Revision；Text 以插入點歸屬候選，字型外觀仍依 Revit。一般軸向矩形可由分段線組成；旋轉矩形需閉合 Polyline。不完整 reader 通知及未知幾何明示阻擋。手動外框不代表自動刪除外部物件。

實檔 UAT 額外發現 ACadSharp 3.7.16 TextEntity.ApplyTransform 未更新 AlignmentPoint（第二對齊點）；InsertPoint／Height 雖正確，Revit 匯入仍讀到原始大座標。已依固定版本原始碼確認並在候選正規化及圖塊展開時明確轉換第二點，DXF read-back 比對兩點與對齊方式。C5 增加大座標置中文字、ATTDEF、旋轉圖塊置中文字；不以放寬 Revit 尺寸驗證掩蓋問題。帶 Instance attributes 的 INSERT 與非 XY 文字目前明示阻擋。
