# v0.4 發布 checkpoint 清單

使用者已明確授權本次正式 v0.4 deployment、以下三個 checkpoint 與 origin/bim-custom push，取代先前只限臨時 Gate C 的限制。正式 deployment 已完成；逐 DLL / manifest 驗證見 v04-report.json。

## Release 前置

- Gate A 466 PASS；補充靜態語義能力 mapping 204 PASS。
- Gate B 20 PASS；Release.R26 0 errors。
- CoordinationFixture coordination-1：15 PASS / 0 FAIL；build hash 與目前 DLL 一致。
- Gate D N/A（正式 workflow 唯讀）；完整 v0.3 rollback PASS。
- 全域矩陣每筆均有靜態 assessment；其他 Domain 的 BLOCKED / REVIEW_ONLY 不代表本次已啟用功能。

## Commit A

`feat: add domain productization framework and self-test lab`

- MCP/Models/WorkflowDefinition.cs
- scripts/audit-productization.cjs
- scripts/complete-productization-audit.cjs
- scripts/run-productization-audit.ps1
- scripts/test-productization-audit.cjs
- tests/SourceAudit/Program.cs
- tests/SourceAudit/SourceAudit.csproj
- docs/productization/matrix.json
- docs/productization/matrix.md
- docs/productization/README.md
- docs/DOCUMENT_AUDIENCE_INVENTORY.md

## Commit B

`feat: add construction coordination center`

- MCP/Core/ClashDetector.cs
- MCP/Core/CoordinationService.cs
- MCP/Models/CoordinationModels.cs
- MCP/UI/CoordinationViewModel.cs
- MCP/UI/DetectReviewWorkflowControl.cs
- MCP/UI/BimConstructionPanelPage.cs
- MCP/UI/BimConstructionPanelViewModel.cs
- MCP/UI/PanelReadOnlyDispatcher.cs

## Commit C

`test: add Revit coordination fixtures`

- MCP/Application.cs
- MCP/Commands/RunCoordinationSelfTestCommand.cs
- MCP/Core/CoordinationSelfTest.cs
- tests/WorkflowLogic/Program.cs
- tests/WorkflowLogic/WorkflowLogic.csproj
- scripts/run-revit-selftest.ps1
- scripts/test-reversible-gate-c.ps1
- scripts/test-workflow-contracts.cjs
- scripts/verify-v04.ps1
- scripts/write-v04-report.cjs
- .gitignore
- docs/productization/v04-report.json
- docs/productization/v04-report.md
- docs/productization/development-state.json
- docs/productization/staging-plan.md
- docs/productization/release-checkpoints.md
- log/2026-09.md

不提交 bin / obj / test-artifacts / RVT / RFA / deployment snapshot。每個 checkpoint 僅含列示 scope；完成三個 checkpoint 後再 push origin/bim-custom。

## 授權後續跑

1. 檢查 Git / development-state.json 與 source fingerprints；若一致，沿用 PASS gates。
2. 確認 Revit.exe 未執行；若仍執行，等待使用者正常關閉。
3. 用既有 installer 部署；逐 DLL hash 與單一 manifest 驗證。
4. 更新正式 deployment evidence / report，執行必要的 deployment QA。
5. 依以上分拆 commit（一次性 identity），push origin/bim-custom 並確認同步、working tree 乾淨。
6. 可選短時間 Native UI UAT；不要求重新逐功能人工測試。

## Final commit 與 local-only receipt

此清單及 development-state.json 隨 Commit C 一同發布。Commit 無法在自己的內容中嵌入自身 SHA，因此 tracked state 使用「包含該狀態檔的 commit」作為明確 self reference：`git log -1 --format=%H -- docs/productization/development-state.json`。

完成 commit 與 push 後，實際三個 commit hash、final commit hash、遠端 SHA、ahead/behind、clean-tree 證據與完整 development-state 寫入 `test-artifacts/formal-release/final.json` 與 `test-artifacts/formal-release/development-state.json`。這兩份為明確允許保留的 ignored local-only receipt，不納入 commit、不含模型或憑證。可由 Git 歷史重新驗證 final commit；避免為了自我引用 hash 新增第四個 commit 或 amend checkpoint。
