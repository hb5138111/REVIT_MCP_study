# v0.4.2 checkpoint scopes

Only commit and push after Gate A/B/C/C2/C3, Build, QA/QC and formal deployment all pass. If runtime is blocked, retain source changes for continuation without release commits.

## 1 — feat: improve coordination 3d navigation and ux

- `MCP/Core/CoordinationNavigationService.cs`
- `MCP/Models/CoordinationModels.cs`
- `MCP/UI/CoordinationViewModel.cs`
- `MCP/UI/DetectReviewWorkflowControl.cs`
- `MCP/UI/RevitCoordinationHost.cs`
- `docs/productization/v042-navigation-analysis.md`

## 2 — test: cover coordination 3d navigation workflow

- `MCP/Core/CoordinationWorkflowSelfTest.cs`
- `MCP/Core/CoordinationSelfTest.cs`
- `tests/CoordinationWorkflow/Program.cs`
- `scripts/test-workflow-contracts.cjs`
- `scripts/test-reversible-gate-c.ps1`
- `scripts/run-revit-selftest.ps1`
- `scripts/verify-v042.ps1`
- `scripts/publish-v042.ps1`
- `scripts/write-v042-report.cjs`
- `docs/productization/README.md`
- `docs/productization/development-state.json`
- `docs/productization/matrix.json`
- `docs/productization/matrix.md`
- `docs/productization/v042-report.json`
- `docs/productization/v042-report.md`
- `docs/productization/v042-release-checkpoints.md`
- `log/2026-09.md`

Before each commit compare staged file names with this exact scope and run `git diff --check`. Runtime RVT/RFA, full snapshots, machine state, build outputs and raw evidence remain ignored in `test-artifacts/`; they must never enter either commit.

Final commit in tracked state uses `SELF_COMMIT`, resolved by `git log -1 --format=%H -- docs/productization/development-state.json`. The literal final hash and remote sync receipt go to ignored `test-artifacts/v042/final.json` after push.
