# v0.5.2.1 release checkpoints

Exact scopes below. Shared typed Profile / status contracts and both Schedule implementations are kept together because the Native UI consumes them as one workflow. No build output, fixture models, runtime artifacts, backup, credentials or local machine state is committed.

## 1 — refactor: refine earthwork profiles status and summary schedules

- MCP/Core/Site/EarthworkRecords.cs
- MCP/Core/Site/EarthworkProfiles.cs
- MCP/Core/Site/RevitEarthworkRecords.cs
- MCP/UI/EarthworkViewModel.cs
- MCP/UI/EarthworkControl.cs
- MCP/UI/EarthworkProfileWindow.cs
- MCP/UI/EarthworkProfileEditor.cs
- MCP/UI/RevitSiteHost.cs
- MCP/UI/SiteTerrainViewModel.cs
- domain/site-terrain-earthwork.md

## 2 — test: verify profile snapshots isolation and schedule formatting

- MCP/Core/Site/EarthworkWorkflowSelfTest.cs
- tests/SiteTerrain/Program.cs
- tests/SiteTerrain/SiteTerrain.csproj

## 3 — docs: record verified v0.5.2.1 release gates

- scripts/write-v0521-report.cjs
- scripts/publish-v0521.ps1
- docs/productization/v0521-profile-schedule-audit.md
- docs/productization/v0521-release-checkpoints.md
- docs/productization/v0521-report.json
- docs/productization/v0521-report.md
- docs/productization/development-state.json
- docs/productization/matrix.json
- log/2026-09.md

## Release conditions

All required gates PASS on the final Release.R26 hash; Native and regression full v0.5.2 rollbacks PASS; formal repository installer and complete DLL / manifest integrity PASS. Commit exact scopes using one-time identity and push only origin/bim-custom. Verify clean working tree and ahead/behind 0/0. SELF_COMMIT resolves the final state commit; literal commit / push receipt stays in ignored test-artifacts/v0521/final.json. Stop; optional UAT only.
