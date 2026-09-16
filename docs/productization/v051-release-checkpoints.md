# v0.5.1 release checkpoints

Branch: `bim-custom`. Destination: `origin/bim-custom` at the user's repository only.

Release requires A/B/C/C2/C3, Terrain logic/runtime, CAD runtime, source audit and QA/QC PASS; tested/current/deployed DLL hashes must match. Reversible test load must restore the complete v0.5 snapshot before formal installer deployment. No runtime artifacts, build output, local state, backup or model files are committed.

## 1. feat: add dwg and dxf terrain sources

- MCP/Core/Site/TerrainEngine.cs
- MCP/Core/Site/CadTerrainData.cs
- MCP/Core/Site/CadTerrainService.cs
- domain/site-terrain-earthwork.md
- docs/productization/v051-cad-terrain-audit.md

## 2. refactor: simplify site terrain workflow ui

- MCP/Core/CoordinationNavigationService.cs
- MCP/UI/BimConstructionPanelViewModel.cs
- MCP/UI/RevitSiteHost.cs
- MCP/UI/SiteTerrainViewModel.cs
- MCP/UI/SiteTerrainWorkflow.cs
- MCP/UI/SiteTerrainControl.cs

## 3. test: cover cad terrain and site workflow

- MCP/Application.cs
- MCP/Core/Site/TerrainSelfTest.cs
- MCP/Core/Site/CadTerrainSelfTest.cs
- scripts/run-revit-selftest.ps1
- scripts/test-reversible-gate-c.ps1
- scripts/complete-productization-audit.cjs
- scripts/publish-v051.ps1
- scripts/verify-v051.ps1
- scripts/write-v051-report.cjs
- tests/SiteTerrain/Program.cs
- tests/SiteTerrain/SiteTerrain.csproj
- tests/fixtures/cad-terrain/generate.py
- tests/fixtures/cad-terrain/simple-points.dxf
- tests/fixtures/cad-terrain/contours-3d.dxf
- tests/fixtures/cad-terrain/README.md
- docs/productization/matrix.json
- docs/productization/development-state.json
- docs/productization/v051-report.json
- docs/productization/v051-report.md
- docs/productization/v051-release-checkpoints.md
- log/2026-09.md

The final state uses `SELF_COMMIT` with an explicit resolver because a Git commit cannot contain its own literal hash. After push, `test-artifacts/v051/final.json` records all three literal commit hashes, branch synchronization and deployment. This receipt is local evidence, not tracked machine state. No unrelated local artifacts are approved for staging.
