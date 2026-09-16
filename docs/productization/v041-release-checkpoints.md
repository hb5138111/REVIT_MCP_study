# v0.4.1 checkpoint scopes

本次以相依性完整為分組原則：controller、service、UI wiring 與 Revit runtime fixture 必須一起編譯，不能先刪舊服務造成中間 checkpoint 無法編譯。因此採下列三個獨立 scope；外掛 source、開發測試工具、發布證據分開。

所有 Gate 與正式部署已驗證；commit 使用一次性 hb5138111 identity，僅 push origin/bim-custom。

## Source — refactor: unify native construction coordination workbench

- `MCP/Application.cs`
- `MCP/Commands/ShowBimConstructionPanelCommand.cs`
- `MCP/Core/CoordinationSelfTest.cs`
- `MCP/Core/CoordinationService.cs`
- `MCP/Core/CoordinationWorkflowSelfTest.cs`
- `MCP/Core/DocumentSessionIdentity.cs`
- `MCP/Core/LevelConstraintAuditService.cs`
- `MCP/Core/ModelSummaryService.cs`
- `MCP/Core/TypeInstanceLocatorService.cs`
- `MCP/Core/TypeInventoryService.cs`
- `MCP/Models/CoordinationModels.cs`
- `MCP/Models/LevelConstraintAuditModels.cs`
- `MCP/Models/LinkSummary.cs`
- `MCP/Models/ModelSummaryModels.cs`
- `MCP/Models/TypeInventoryModels.cs`
- `MCP/Models/WorkflowDefinition.cs`
- `MCP/UI/BimConstructionPanelPage.cs`
- `MCP/UI/BimConstructionPanelViewModel.cs`
- `MCP/UI/CoordinationViewModel.cs`
- `MCP/UI/DetectReviewWorkflowControl.cs`
- `MCP/UI/LevelConstraintAuditViewModel.cs`
- `MCP/UI/PanelReadOnlyDispatcher.cs`
- `MCP/UI/RevitCoordinationHost.cs`
- `MCP/UI/TypeInventoryViewModel.cs`

## Tests — test: enforce native coordination workflow release gates

- `scripts/complete-productization-audit.cjs`
- `scripts/publish-v041.ps1`
- `scripts/test-reversible-gate-c.ps1`
- `scripts/test-workflow-contracts.cjs`
- `scripts/verify-v04.ps1`
- `scripts/verify-v041.ps1`
- `scripts/write-v041-report.cjs`
- `tests/CoordinationWorkflow/CoordinationWorkflow.csproj`
- `tests/CoordinationWorkflow/Program.cs`
- `tests/WorkflowLogic/Program.cs`

## Release — docs: record verified v0.4.1 coordination release

- `docs/productization/development-state.json`
- `docs/productization/matrix.json`
- `docs/productization/matrix.md`
- `docs/productization/README.md`
- `docs/productization/v041-release-checkpoints.md`
- `docs/productization/v041-report.json`
- `docs/productization/v041-report.md`
- `docs/productization/v041-runtime-failure-analysis.md`
- `log/2026-09.md`

## Local-only artifacts

`test-artifacts/` contains fixture RVT/RFA, raw runtime reports, deployment snapshot, full QA logs and final Git receipt. These stay ignored and are not committed. No build output, credentials, production models or machine state are included.

## Final commit identity

The final documentation commit contains development-state.json, so an embedded literal hash of that same commit is impossible. `FinalCommit.Value = SELF_COMMIT` resolves with `git log -1 --format=%H -- docs/productization/development-state.json`. The literal three commit hashes, pushed remote hash, 0/0 sync and clean-tree evidence are written after push to ignored `test-artifacts/v041/final.json`.
