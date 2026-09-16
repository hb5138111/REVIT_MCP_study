# v0.4 Productization Report

## Formal v0.4 release

- Deployment: PASS
- Build / deployed SHA256: F7D3F914D095AB2DB80FB9D867467E6FDF4BC9A31FE91F37868B0796E558443D
- Required / deployed DLLs: 8 / 8
- Manifest count: 1; Assembly: RevitMCP\RevitMCP.dll; FullClassName: RevitMCP.Application
- Post-deployment QAQC: {"PASS":72,"FAIL":0,"WARN":2,"SKIP":3}
- Supplemental static audit: 204 PASS / 0 FAIL

## Historical reversible Gate C

**GATE_C_PASS_ROLLBACK_PASS**. CoordinationFixture coordination-1: 15 PASS, 0 FAIL. Full v0.3 deployment restored.

| Gate | Result |
|---|---|
| A | PASS: 466 passed |
| B | PASS: 20 passed |
| Build R26 | PASS |
| QA/QC | 72 passed / 0 failed / 4 warnings / 1 skipped |
| C | PASS: 15 passed |
| D | N/A: read-only production workflows |
| Rollback | PASS; full file set match true |

## Hashes

- Original v0.3: ECA3CBE0D835F69E45B6D4579224756E62397F80B85E63DBB4F6F9F79991D9DE
- Temporary v0.4 build and deployed: F7D3F914D095AB2DB80FB9D867467E6FDF4BC9A31FE91F37868B0796E558443D
- Restored v0.3: ECA3CBE0D835F69E45B6D4579224756E62397F80B85E63DBB4F6F9F79991D9DE
- Manifest count: 1; Assembly: RevitMCP\RevitMCP.dll; FullClassName: RevitMCP.Application

## Assertions

- PASS pipe_wall_count: expected 1; actual 1; Fixture wall and crossing pipe
- PASS opening_diameter: expected 150; actual 150; 100 mm + 2 * 25 mm
- PASS element_readback: expected True; actual True; UniqueId read-back
- PASS review_honesty: expected True; actual True; Unknown solid edge and opening bottom remain warnings
- PASS pipe_beam_count: expected 1; actual 1; Generated structural-category family
- PASS beam_review: expected True; actual True; Beam review code required
- PASS duct_floor_count: expected 1; actual 1; Vertical duct through slab
- PASS conduit_no_clash: expected 0; actual 0; Remote conduit
- PASS level_scope: expected 0; actual 0; Pre-geometry MEP reference-level filter
- PASS clash_scan: expected 1; actual 1; Clash workflow does not require clearance
- PASS oblique_review: expected True; actual True; Non-orthogonal pipe through wall
- PASS explicit_truncation: expected total=2, returned=1, truncated=true; actual 2/1/True; Total includes matches beyond display cap
- PASS translated_link: expected 1; actual 1; MEP link translated +100 ft; pipe center resolves to host origin
- PASS translated_point: expected 0; actual 0; Host-coordinate center mm
- PASS fixture_saved: expected True; actual True; Disposable artifact only

## Productization inventory

[Complete file inventory and matrix](matrix.md). Each domain has a static capability assessment with SOP line evidence and method-level backend mapping. Engineering approval and runtime certification outside the coordination subset are not claimed.

- NATIVE_READY: 0
- ADAPTER_READY: 18
- RULE_READY: 3
- PROJECT_CONFIG_REQUIRED: 18
- REVIEW_ONLY: 17
- BLOCKED: 15
- META_ONLY: 13

## Architecture and scope

Compiled WorkflowRegistry defines six patterns. DetectReview uses the existing DockablePane and ExternalEvent dispatcher. Clash and opening services support host/link sources, explicit scopes, clearance settings, warning review, navigation and CSV export. Sleeve classification and full RC/SC/SRC compliance stay disabled due to backend gaps. No production write workflow is enabled.

## Release decision

**READY_FOR_OPTIONAL_UAT.** Static capability mapping completed for every domain, including explicit backend/API gaps. Only the read-only coordination subset is enabled; other domains are not claimed runtime certified.

正式 v0.4 deployment：PASS。正式部署與先前臨時測試分開記錄。三個 checkpoint scope 與 final commit 解析方式見 [發布清單](release-checkpoints.md)；實際 commit hashes、push 與 clean-tree 驗證見 local final receipt。 Optional short Native UI UAT after restart; stop here and do not start another productization batch.

## Limits

- Unsigned add-in prompt required user Load Once; subsequently Revit exited normally.
- Grazing geometry is outside the enabled centerline backend capability.
- CoreFixture, TakeoffFixture and DrawingFixture are planned only; this run executed CoordinationFixture.
- Native panel interaction is not covered by this service-level fixture.

## Evidence

- [Verification](../../test-artifacts/verification-1fac6210ae544d1d827969ad7d47f265/report.json)
- [Contracts](../../test-artifacts/source-audit/contracts.json)
- [Logic](../../test-artifacts/verification-1fac6210ae544d1d827969ad7d47f265/logic.json)
- [QAQC](../../test-artifacts/verification-1fac6210ae544d1d827969ad7d47f265/qaqc.log)
- [Runtime](../../test-artifacts/revit-selftest-02386310ad7d4938884ad97922c4dffd/runtime.json)
- [Reversible](../../test-artifacts/reversible-86c37c008ba0473e9d101816c0cd94b3/reversible.json)
- [SemanticAudit](../../test-artifacts/source-audit/tests.json)
- [FormalDeployment](../../test-artifacts/formal-release/deployment.json)
- [PostDeploymentQAQC](../../test-artifacts/formal-release/qaqc.log)
