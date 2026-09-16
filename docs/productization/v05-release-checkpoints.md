# v0.5 release checkpoints

Only commit after all gates and formal deployment PASS. Runtime artifacts, private templates, build output and snapshots remain ignored/local.

## feat: add survey terrain import and coordinate alignment

- `CLAUDE.md`
- `docs/BIM_MCP/2026-06/dwg-column.html`
- `docs/BIM_MCP/2026-07/beam-penetration.html`
- `docs/BIM_MCP/2026-07/harvest-waves.html`
- `docs/BIM_MCP/index.html`
- `docs/BIM_MCP/reference/architecture-v2.html`
- `docs/BIM_MCP/reference/contributor-template.html`
- `docs/BIM_MCP/reference/domain-index.html`
- `docs/BIM_MCP/reference/industry-evidence.html`
- `docs/BIM_MCP/reference/personal-llm-wiki.html`
- `docs/BIM_MCP/reference/philosophy-22-propositions.html`
- `docs/BIM_MCP/reference/skills-index.html`
- `docs/BIM_MCP/reference/three-constitutions.html`
- `docs/BIM_MCP/shared.js`
- `docs/DOCUMENT_AUDIENCE_INVENTORY.md`
- `docs/productization/v05-site-capability-audit.md`
- `domain/README.md`
- `domain/site-terrain-earthwork.md`
- `MCP/Core/Site/TerrainEngine.cs`
- `README.md`
- `README.zh-TW.md`

## feat: add toposolid creation and earthwork analysis

- `docs/productization/README.md`
- `MCP/Core/Site/RevitTerrainService.cs`
- `MCP/UI/BimConstructionPanelPage.cs`
- `MCP/UI/BimConstructionPanelViewModel.cs`
- `MCP/UI/RevitSiteHost.cs`
- `MCP/UI/SiteTerrainControl.cs`
- `MCP/UI/SiteTerrainViewModel.cs`

## test: add terrain coordinate and earthwork fixtures

- `docs/productization/development-state.json`
- `docs/productization/matrix.json`
- `docs/productization/matrix.md`
- `docs/productization/v05-release-checkpoints.md`
- `docs/productization/v05-report.json`
- `docs/productization/v05-report.md`
- `log/2026-09.md`
- `MCP/Application.cs`
- `MCP/Core/Site/TerrainSelfTest.cs`
- `scripts/complete-productization-audit.cjs`
- `scripts/publish-v05.ps1`
- `scripts/test-productization-audit.cjs`
- `scripts/test-workflow-contracts.cjs`
- `scripts/verify-v05.ps1`
- `scripts/write-v05-report.cjs`
- `tests/SiteTerrain/Program.cs`
- `tests/SiteTerrain/SiteTerrain.csproj`

FinalCommit uses SELF_COMMIT in tracked state; resolve through the final development-state commit. Literal hashes and sync receipt remain in test-artifacts/v05/final.json.
