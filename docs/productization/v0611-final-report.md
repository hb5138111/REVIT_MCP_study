# v0.6.1.1 真實 CAD 圖框辨識／正規化

ACTUAL_DWG_UAT_PASS。完整機器可讀證據與 SHA 見 [v0611-report.json](v0611-report.json)。依 Domain `construction-drawing-production.md`；公司原始 DWG 不修改、不提交、不外傳。

| # | 項目 | 結果 |
|---|---|---|
| 1 | Actual DWG root cause | 全圖是兩套同尺寸、分離的大座標／大尺度圖框。Global bounds 580,606,934.528190 × 148,142,645.290762 mm 不是紙張大小。另修正 CAD reader 遺留文字第二對齊點的問題，防止 Revit 匯入仍引用原始大座標。 |
| 2 | Remote geometry evidence | 本實檔沒有符合 Domain 少量且極遠規則的 stray cluster；第二套完整圖框不能刪除。兩群間距 144,230,967.669047 mm。 |
| 3 | Cluster analysis | 123 個 model-space 物件完整分成 66 與 57 兩組；INSUNITS=4 mm，1 圖層，無 INSERT。逐物件統計見 v0611-real-cad-titleblock-analysis.json。 |
| 4 | Candidate detection | 2 個 closed rectangular line-loop 候選，包含框內分離文字／圖欄。相同尺寸不合併；幾何集合互斥。 |
| 5 | ISO paper recognition | A0–A4 可直／橫式，兩邊各 ≤1 mm；本實檔兩者均 Custom。與 A3 相對比例誤差 4.149669%，不強制辨識 A3。 |
| 6 | Translation vs scale | 分開保存平移、旋轉與單一倍率。已是紙張尺寸只重新定位；實檔依使用者明確選擇等比例縮至寬 420 mm。禁止 X/Y 分別拉伸。 |
| 7 | Layer selection | 可依圖層確認保留集合；本檔只有一層，所以用候選幾何隔離，不以圖層名稱猜用途。 |
| 8 | Manual border selection | 可選閉合 Polyline 或在全圖點兩個對角；顯示範圍內外數量，需確認保留集合。指定外框本身不授權刪除。 |
| 9 | Normalization UX | 全圖／群組／遠端／選框預覽與目標尺寸、平移、旋轉、倍率、用途、名稱、保留集合確認。變更設定後確認立即失效，載入按鈕顯示缺項。 |
| 10 | Actual DWG candidate | 施工圖 C-16DCCC71ED4F；竣工圖 C-32A26BBF292A。文字只提供用途建議；UAT 明確確認兩用途。 |
| 11 | Actual candidate dimensions | 兩候選均約 218,187,983.429572 × 148,142,645.290762 mm；中心與幾何數見下表。 |
| 12 | Actual paper recommendation | 已確認自訂 420 × 285.166534 mm，保留實際比例；不是標準 A3。 |
| 13 | Actual translation | 先以來源 mm 平移。施工圖 (-200,011,121.54085, -1,412,603.242082)；竣工圖 (162,407,829.557769, -1,412,603.242082)。 |
| 14 | Actual scale | 0.0000019249456060698735，X/Y/Z 共用同一倍率；旋轉 0 rad。 |
| 15 | TitleBlock conversion | 各候選獨立 Family／Type，來源 SHA 與預覽指紋綁定。CAD 文字／ATTDEF 保留為 CAD，無推測 Label／Revision。 |
| 16 | FL1/FL2 production journey | 真實 Panel child ViewModel → Analyze → Select/Preview/Confirm → ExternalEvent → Profile → FL1/FL2 NoZone → Plan → Confirm → Apply → QA。 |
| 17 | Cartoon Set | 先產生 2 列計畫，檢查圖號、來源與版面，確認後才建立。 |
| 18 | Create result | 施工圖 2 Sheet；ElementIds 211625, 211642。竣工圖另建獨立 TitleBlock，本次不建立第二套 Sheet。 |
| 19 | Read-back | 核對 Family Category／Type、候選 ID／幾何集合、尺寸與載入前後原生幾何 SHA；兩 Family 指紋不同且讀回一致。 |
| 20 | QA | 建立後無 ERROR；人工移動測試產生預期 MANUAL_OVERRIDE 提醒，不偽裝正式設計核准。 |
| 21 | Idempotency | 同包重跑 Sheet/View/Viewport 數不增加，Sheet IDs 不變。 |
| 22 | Manual override | 測試主視埠位移 0.005 ft；重新規劃可辨識且預設保留，讀回座標一致。 |
| 23 | Gate C4 | 合成多圖框 journey 與實檔 journey 同版 PASS；最新 33 assertions。 |
| 24 | Gate C5 | 39 PASS / 0 FAIL；涵蓋 A–J、錯誤比例、遠端幾何、單位、大座標、多層、等大雙框、無用途文字、旋轉圖塊、候選污染與失效預覽。 |
| 25 | Regression | A/B/C/C2/C3、Drawing、Terrain、CAD、Earthwork 均見下表；不是僅 Build PASS。 |
| 26 | Build | Release.R26 (.NET 8 / Revit 2026.5)，0 error，2181 既有 warning。ACadSharp 固定 3.7.16 MIT；部署含 notices。 |
| 27 | QA/QC | 73 PASS / 0 FAIL / 2 SKIP；1 warning。此輪略過 C#／MCP Server 重建以保留已測成品；兩者已在獨立 Build Gate PASS。 |
| 28 | Deployment | PASS；canonical scripts/install-addon.ps1，由 publish-v0611.ps1 驗證所有 Gate、C5、實檔及同版 SHA 後執行。 |
| 29 | SHA256 | Build/runtime/deployed: CC182F20E83C00E26AE8DDC62DA3B0233F0479272BF9648455B7CAA0E99A7766。來源只讀 SHA: 9772C6F4D734B2ACA75E27C62B68C47154AAA2DF867F8C1D3DEFEE4FD198A2EB。 |
| 30 | Commits | 實作 46eeee5；測試 eb825df；報告 commit 用 git log -1 --format=%H -- docs/productization/v0611-report.json 解析。 |
| 31 | Push | 目的地 origin/bim-custom；最終遠端 read-back 見 test-artifacts/v0611/final.json。 |
| 32 | Remaining limitations | CAD 僅轉幾何；不推測 Label、Revision、shared parameters 或 OCR。 每張圖紙一個主平面視埠；外部 RVT 維持 PARTIAL。 文字依插入點歸屬候選；Revit 字型替代仍須外觀複核。 旋轉外框需閉合矩形 Polyline；不支援的實體、xref 與陣列 INSERT 明確阻擋。 CAD 分析有明確預算；大型正式模型效能尚未測試。 帶 Instance attributes 的 INSERT 與非 XY 文字明確阻擋，不靜默遺漏。 外觀檢查涵蓋測試 FL2 圖紙的螢幕顯示；未認證列印／字型完全一致或大型正式模型效能。 |
| 33 | Final Status | ACTUAL_DWG_UAT_PASS；停止 v0.6.1.1，不啟動 v0.7。 |

## 候選與各別 Family

ActualCandidateCount = 2。PurposeSuggestion 與 PurposeConfirmation 分離；本次兩用途均明確確認。

| 用途 | Candidate | 幾何數 | 原尺寸 mm | 中心 XY mm | TypeId |
|---|---|---|---|---|---|
| Construction | C-16DCCC71ED4F | 57 | 218,187,983.429571 × 148,142,645.290762 | 309,105,113.255635, 75,483,925.887463 | 211617 |
| AsBuilt | C-32A26BBF292A | 66 | 218,187,983.429572 × 148,142,645.290762 | -53,313,837.842983, 75,483,925.887463 | 211934 |

- Construction: Family `CAD_C-16DCCC71ED4F_P-3DCB314061C6C8C8B5AB79FFD6D8A4D8992C5C85A41F96BD20433F5B801FCA46` / Type `圖框`；ProfileGuid `37e7d83d-fdea-401a-ac4b-94b69619dfda`、Version 1；native SHA `N-0EC01061FF104102ACE4C6A6BD352560EA6DCF96DFD8745B9D6A488B8A0D9325`。
- AsBuilt: Family `CAD_C-32A26BBF292A_P-F8A78235EE20C651FD87B4DDAB40210E75C13692610E818D5B9DB9BF21200F03` / Type `圖框`；ProfileGuid `8fd44e56-4d5d-4d39-bfa6-3da4c7f06f84`、Version 1；native SHA `N-25DED4C5EFC24EEE3B201397B5127D236B15F951951B5B43A66602231D8B377B`。

CrossCandidateContaminationTest = PASS：來源幾何 ID 無交集，66+57=123；ProfileGuid／TypeId 不同，Family 幾何指紋不同，載入前後各自一致。

## Gate 證據

| Gate | 狀態 | PASS | FAIL | 本機 evidence |
|---|---|---|---|---|
| A | PASS | 522 | 0 | test-artifacts/v0611/contracts.json |
| B | PASS | 20 | 0 | test-artifacts/v0611/logic.json |
| C2 | PASS | 115 | 0 | test-artifacts/v0611/workflow-state.json |
| C5 | PASS | 39 | 0 | test-artifacts/v0611/cad-c5.json |
| TerrainLogic | PASS | 211 | 0 | test-artifacts/v0611/terrain-logic.json |
| C | PASS | 20 | 0 | test-artifacts/revit-selftest-cbbcb7a8ccea4a8ba41d77fc7ac79195/runtime.json |
| C3 | PASS | 48 | 0 | test-artifacts/revit-selftest-cbbcb7a8ccea4a8ba41d77fc7ac79195/workflow-runtime.json |
| C4 | PASS | 33 | 0 | test-artifacts/revit-selftest-1343d0864148499ea9626e4b811796b9/drawing-c4-runtime.json |
| DrawingRuntime | PASS | 46 | 0 | test-artifacts/revit-selftest-fc8ba33342194e78929ef56c1d5d3350/drawing-runtime.json |
| TerrainRuntime | PASS | 31 | 0 | test-artifacts/revit-selftest-cbbcb7a8ccea4a8ba41d77fc7ac79195/terrain-runtime.json |
| CadRuntime | PASS | 47 | 0 | test-artifacts/revit-selftest-cbbcb7a8ccea4a8ba41d77fc7ac79195/cad-runtime.json |
| EarthworkNative | PASS | 61 | 0 | test-artifacts/revit-selftest-2842e61c81674e0f9a505337b13690e9/earthwork-workflow.json |
| ActualDWG | PASS | 34 | 0 | test-artifacts/revit-selftest-22f0e115c5394cef978802c43c8931e0/actual-cad-uat.json |
| VisualReview | PASS | — | 0 | test-artifacts/v0611/actual-visual-review.json |
| McpServerBuild | PASS | — | 0 | test-artifacts/v0611/mcp-server-build.log |
| SourceAudit | PASS | 205 | 0 | test-artifacts/source-audit/tests.json |
| Build | PASS | — | 0 | test-artifacts/v0611/build.log |
| QAQC | PASS | 73 | 0 | test-artifacts/v0611/qaqc-final.log |
| Rollback | PASS | — | 0 | test-artifacts/reversible-03295bfd6d5f4c2ca10784c3b80496ec/reversible.json<br>test-artifacts/reversible-1d4a98149f7142bead0267d5b16a6165/reversible.json<br>test-artifacts/reversible-430b574c8e4641a4a714fd198d0e2f8d/reversible.json<br>test-artifacts/reversible-b409333d0e45423ea60295b3dd63bd0b/reversible.json<br>test-artifacts/reversible-fd9c0b2057574c579a286402f134a3aa/reversible.json |
| Deployment | PASS | — | 0 | test-artifacts/v0611/deployment.json |

## 修改範圍

- `MCP/Core/Drawing/CadTitleBlockFileService.cs`
- `MCP/Core/Drawing/CadTitleBlockGeometry.cs`
- `MCP/Core/Drawing/DrawingModels.cs`
- `MCP/Core/Drawing/DrawingProductionJourneyFixture.cs`
- `MCP/Core/Drawing/ExternalTitleBlockService.cs`
- `MCP/RevitMCP.csproj`
- `MCP/ThirdPartyNotices.txt`
- `MCP/UI/DrawingProductionControl.cs`
- `MCP/UI/DrawingProductionViewModel.cs`
- `docs/productization/development-state.json`
- `docs/productization/matrix.json`
- `docs/productization/v0611-cad-capability-audit.md`
- `docs/productization/v0611-final-report.md`
- `docs/productization/v0611-real-cad-titleblock-analysis.json`
- `docs/productization/v0611-real-cad-titleblock-analysis.md`
- `docs/productization/v0611-release-checkpoints.md`
- `docs/productization/v0611-report.json`
- `domain/construction-drawing-production.md`
- `log/2026-09.md`
- `scripts/complete-productization-audit.cjs`
- `scripts/install-addon.ps1`
- `scripts/publish-v0611.ps1`
- `scripts/run-revit-selftest.ps1`
- `scripts/test-productization-audit.cjs`
- `scripts/test-reversible-gate-c.ps1`
- `scripts/test-workflow-contracts.cjs`
- `tests/CadTitleBlock/CadTitleBlock.csproj`
- `tests/CadTitleBlock/Program.cs`
- `tests/CoordinationWorkflow/CoordinationWorkflow.csproj`
- `tests/CoordinationWorkflow/DrawingTests.cs`
