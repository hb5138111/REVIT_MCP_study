# v0.4.1 Runtime failure analysis

## Root Cause

Source trace at stable commit `9b113eb`: `BimConstructionPanelPage` creates two independent coordination views. `BimConstructionPanelViewModel` constructs them without initialization. `ShowBimConstructionPanelCommand` only toggles visibility. Consequently `sourceDocument` is null and `documentIdentity` is empty until the user explicitly refreshes. Every command uses only `!dispatcher.IsBusy` as its readiness condition. Saving clearance in this initial state reaches `Anchor()` and deterministically throws「目前模型已變更，請重新讀取來源。」even though no model switch occurred. This is a confirmed control-flow reproduction, not a claim of observing the user's live model.

## Secondary Causes

- MEP source setter clears Levels but never dispatches level loading. Refresh restores the selected source only when `ReferenceEquals(sourceDocument, doc)` succeeds. Levels are names rather than source-local IDs.
- `Anchor` combines ProjectInformation.UniqueId with managed reference equality. Reference equality is unnecessary and is not evidence of a native document session; whether wrapper replacement caused the reported incident is unproven. ProjectInformation.UniqueId alone also cannot distinguish concurrently open copies or close/reopen sessions.
- No active-document lifecycle handler invalidates and automatically refreshes controller state. A stale result produces an error instead of recovery.
- Busy rejection can silently drop refresh work; several bound inputs are auto-properties without property-change notification.
- Clash and opening views own separate settings and rerun the same geometry scope. Each solid segment becomes a separate row, potentially duplicating one element interaction.

## UX Causes

Manual source bootstrap, no CanScan reason, mandatory named level, arbitrary wall default, duplicate tabs/settings, advanced cap in primary form, raw IDs dominating the table, and a clearance save action enabled without a document anchor.

## Missing Tests

Gate C calls CoordinationService directly in a hidden Document. It never obtains ActiveUIDocument, creates the production controller, exercises ExternalEvent scheduling, changes sources, binds results, or navigates. Existing static tests actually require the obsolete tabs to remain. v0.4 backend PASS is valid evidence for its tested backend only, not for the Native workflow.

New release requires Gate C2 (production controller with a deterministic queued host) and C3 (the same controller, real ActiveUIDocument and real ExternalEvent in disposable Revit). Runtime conclusions remain NOT TESTED until those reports exist for the new DLL.

## Dependency graph and disposition

| Classes / wiring | Classification | Action |
|---|---|---|
| ModelSummaryService / feature-specific summary DTOs | FEATURE_ONLY | Remove with old panel bindings |
| LinkSummary / LinkTransformSummary (originally in ModelSummaryModels) | SHARED_PRIMITIVE | Extract unchanged; still required by LinkedModelHelper and legacy MCP |
| TypeInventoryService / TypeInventoryViewModel / TypeInventoryModels | FEATURE_ONLY | Remove |
| LevelConstraintAuditService / LevelConstraintAuditViewModel / LevelConstraintAuditModels | FEATURE_ONLY | Remove |
| TypeInstanceLocatorService.FindInstanceIds | FEATURE_ONLY | Remove |
| TypeInstanceLocatorService.GetDocumentIdentity | SHARED_PRIMITIVE | Extract document/session identity, used by service and navigation |
| PanelReadOnlyDispatcher | STILL_REQUIRED | Keep ExternalEvent and Busy gate; remove retired request kinds |
| BimConstructionPanelPage / ViewModel | STILL_REQUIRED | Replace with single coordination workbench wiring |
| CoordinationService / ClashDetector helpers | STILL_REQUIRED | One read-only scan; preserve shared legacy MCP geometry engine |
| WorkflowRegistry / Self-Test Lab / installers / release pipeline | STILL_REQUIRED | Extend, do not delete |

Search evidence: usages of the retired feature-specific model/service types are confined to their corresponding Native classes and BimConstructionPanel*. LinkSummary and LinkTransformSummary are the shared exceptions recorded above. The only external use of TypeInstanceLocatorService is CoordinationService/CoordinationViewModel document identity. Domain, MCP tools, CommandExecutor and shared geometry remain available.

## Affected Files

`MCP/UI/BimConstructionPanelPage.cs`, `BimConstructionPanelViewModel.cs`, `CoordinationViewModel.cs`, `DetectReviewWorkflowControl.cs`, `PanelReadOnlyDispatcher.cs`; `MCP/Application.cs`; `MCP/Commands/ShowBimConstructionPanelCommand.cs`; `MCP/Core/CoordinationService.cs`, `CoordinationSelfTest.cs`; `MCP/Models/CoordinationModels.cs`, `WorkflowDefinition.cs`; retired feature classes listed above; workflow tests, release reports and productization matrix generator.

## Capability boundary

SOP: mep-csa-clash-detection, mep-opening-candidate-scan, beam-penetration-base/algorithm/rc/sc/src, sleeve-classification-protocol. Existing curve/solid helper supports framing intersections and transformed links. This supports a BeamPenetration candidate, not RC/SC/SRC approval. Solid edge clearance, oblique projected opening size and complete sleeve classification remain unresolved and review-only/disabled. No new MCP tool or geometry engine is necessary.

### Dependency audit correction
Build verification exposed two shared DTOs nested in ModelSummaryModels: LinkSummary and LinkTransformSummary, also consumed by LinkedModelHelper.GetLinkedModels (legacy MCP). They are SHARED_PRIMITIVE, extracted unchanged to Models/LinkSummary.cs. The feature-only container file is removed; MCP response shape is preserved.
