# v0.5 基地／土方 capability audit

Baseline: `ff637d6`, `bim-custom`, clean working tree. v0.4.2 remains the stable deployment until all new and regression gates pass.

| Layer | Existing capability | Reuse / gap |
|---|---|---|
| Domain | `cad-block-point-placement`, `dwg-column-import` describe CAD coordinates | No survey import, shared-coordinate or earthwork SOP; add `site-terrain-earthwork` first |
| Skill | DWG column import handles a different source/workflow | No survey terrain skill; native fixed workflow needs no new AI skill |
| MCP | `grading-tools.ts`: `grade_toposolid_to_floors` | Existing floor-bottom footprint grading only; no new MCP tool needed |
| C# backend | `CommandExecutor.ToposolidGrading.cs`, `RevitToposolidGradingAdapter` | Existing phase-changing design-copy grading is not equivalent to confirmed excavation; preserve unchanged |
| Coordinates | Existing link transforms, `RevitCompatibility.GetIdValue` | Reuse long IDs; add explicit bidirectional survey/internal transform and independently checked control alignment |
| Toposolid | Existing SlabShapeEditor, footprints, solid sampling | No survey file import/create/read-back contract |
| Excavation | No use of CanBeExcavatedBy/ExcavateBy found | Add confirmed typed R26 service with volume read-back |
| Cut/fill | Existing adapter reads built-in cut/fill on graded copies | Add clipped-triangle integration; do not silently invoke phase changes |
| Units | `UnitUtils`, `UnitFormatUtils`, project-unit command | Use internal feet only at API boundary; pure engine uses explicit metres |
| Safety / UI | Native coordination panel, ExternalEvent, document session identity, self-test launcher and reversible full snapshot | Preserve read-only dispatcher; separate write dispatcher and invalidatable preview tokens |

## API evidence

Local Revit 2026 API skill and installed XML expose Toposolid.Create(points, type, level), CanBeExcavatedBy and ExcavateBy. These signatures are compile checks, not runtime evidence. ProjectLocation.GetProjectPosition and transform direction require nonzero-translation/nonzero-rotation fixture comparisons. Existing grading does not establish automated Graded Region support.

## Implementation contract

CSV/TXT parsing, diagnostics, rigid alignment and reduction run outside Revit API. No automatic building movement, ProjectLocation change, phase change or Revit.ini edit. Preview has no model elements. Create/excavate require a fresh document-bound preview and explicit confirmation, followed by read-back inside a rollback-capable transaction group. Existing/proposed surface comparison is experimental until separately certified. No capability/runtime PASS is inferred from API availability.
