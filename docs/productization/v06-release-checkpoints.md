# v0.6 release checkpoints

All commits use one-shot identity; branch bim-custom. No source changes after the matching 46/48/61 runtime assertions. Full gate and DLL provenance is in v06-report.json; exact commit hashes and origin synchronization are recorded in ignored test-artifacts/v06/final.json.

1. `64d7d3a` — `feat: add native construction drawing template and package production` — DrawingModels, RevitDrawingService, DrawingProduction ViewModel/Control/Host and Panel wiring.
2. `88c07e2` — `test: verify drawing production and preserve existing runtime gates` — DrawingProductionFixture, Application test wiring, existing test runner additions, launcher/reversible/runtime/static audit extensions and release deployment guard.
3. `docs: record v0.6 drawing scope and verified release` — integrated Domain, capability audit/report/receipts, current source fingerprints and public Domain count/index synchronization.

FinalCommit = SELF_COMMIT resolves with `git log -1 --format=%H -- docs/productization/development-state.json`. No amend, force push or upstream push. Push only origin/bim-custom after all gates PASS.
