# v0.5.2 release checkpoints

Exact file scopes below. The workflow repair and new Step 4 share partial ViewModel/API contracts, so they stay together in checkpoint 1. Runtime test bootstrap is checkpoint 2. No fixture models, build output, backups, raw runtime artifacts or machine-specific receipts are committed.

## 1 — fix: repair earthwork workflow and add quantity cost schedule center

- MCP/Core/Site/EarthworkRecords.cs
- MCP/Core/Site/EarthworkExport.cs
- MCP/Core/Site/RevitEarthworkRecords.cs
- MCP/UI/EarthworkViewModel.cs
- MCP/UI/EarthworkControl.cs
- MCP/UI/EarthworkProfileWindow.cs
- MCP/UI/RevitSiteHost.cs
- MCP/UI/SiteTerrainControl.cs
- MCP/UI/SiteTerrainViewModel.cs
- MCP/UI/SiteTerrainWorkflow.cs
- domain/site-terrain-earthwork.md

## 2 — test: cover native earthwork logistics cost and schedule workflows

- MCP/Application.cs
- MCP/Core/Site/EarthworkWorkflowSelfTest.cs
- tests/SiteTerrain/Program.cs
- tests/SiteTerrain/SiteTerrain.csproj
- scripts/run-revit-selftest.ps1
- scripts/test-reversible-gate-c.ps1

## 3 — docs: record verified v0.5.2 release and deployment gates

- scripts/write-v052-report.cjs
- scripts/publish-v052.ps1
- docs/productization/v052-earthwork-failure-analysis.md
- docs/productization/v052-earthwork-schedule-architecture.md
- docs/productization/v052-release-checkpoints.md
- docs/productization/v052-report.json
- docs/productization/v052-report.md
- docs/productization/development-state.json
- docs/productization/matrix.json
- log/2026-09.md

## Release conditions

All required Gates and QA/QC PASS; native and regression runtime hashes equal current Release.R26 build; both full v0.5.1 snapshot rollbacks PASS; formal repository installer deployment and all required DLL/manifest verification PASS. Then commit each exact scope with one-time identity and push only origin/bim-custom. Final working tree clean and ahead/behind 0/0. Final commit is resolved by the existing SELF_COMMIT convention; literal commit/push receipt stays in ignored test-artifacts/v052/final.json. Stop after release; optional UAT only.
