# v0.5.2 Earthwork Schedule architecture audit

Status: IMPLEMENTED / runtime feasibility PASS. Final candidate Native workflow: 38 PASS / 0 FAIL on Revit 2026, SHA256 `42987353E7479B66F982F5983AB5FA3131DC9ABC532D4398C49ECD953086581E`. Formal release remains governed by the complete v0.5.2 report.

## Existing capability and gap

`CommandExecutor.CreateViewSchedule` and the RoomSurface schedule helper already demonstrate category-based ViewSchedule creation and schedulable fields. GreenMaterial parameter binding code preserves and restores `Application.SharedParametersFilename` in `finally`; grading and legend adapters demonstrate ExtensibleStorage provenance. Native earthwork will reuse these API patterns in a typed C# service, without adding a TS tool or calling MCP from WPF.

Existing helpers do not provide stable ZoneGuid records, idempotent updates, ownership filtering, quantity read-back, or an atomic schedule/record transaction. A ViewSchedule cannot schedule an arbitrary DTO.

## Options

| Option | Multi-zone and identity | Model impact | Decision |
|---|---|---|---|
| A: schedule Toposolid / Cutter | Multiple zones can reference one terrain, so one element cannot represent every independent quantity record. | Writes analysis parameters onto design elements; risk of overwriting another zone. | Reject for authoritative zone rows. |
| B: dedicated schedulable Earthwork Record | One owned record per stable ZoneGuid; can update without duplicating rows. | Adds analysis records only. Generic Model category parameters are disclosed in preview. | Selected: fixture proved geometry-free record scheduling. |
| C: shared parameters on existing elements | Shares the same one-element/multiple-zone conflict as A. | Alters existing element metadata and mixes calculation provenance with design parameters. | Reject as the record store. Shared parameters remain useful on B's owned records. |

## Implemented B contract

- Try geometry-free Generic Model DirectShape records; do not create solids merely to make rows appear. Installed Revit API documents `DirectShape.CreateElement(Document, ElementId)` separately from `SetShape`; this does **not** itself prove an empty record appears in a schedule. The runtime fixture must prove membership, fields, two rows, totals, and update-in-place before enabling this architecture.
- Stable shared-parameter GUIDs and `BIM_EW_` names, category binding restricted to Generic Models. Preserve any existing binding; incompatible GUID/type/category configuration stops with a validation error. Do not overwrite user parameters by display name.
- Store ownership, ZoneGuid and the full typed record/provenance in ExtensibleStorage. Schedule filter restricts rows to the tool ownership marker. Do not use a user-editable name alone as deletion authority.
- Use one transaction group for parameter binding, records, schedule and read-back. A failure rolls back all model changes. Restore the shared-parameter file setting in `finally`.
- Prefer the existing owned schedule. If an unowned schedule already uses `土方工程明細`, choose a collision-free `土方工程明細 (BIM)` name. Do not modify an unowned schedule.
- `ScheduleField.CanTotal()` gates total display configuration; `ScheduleDefinition.ShowGrandTotal` provides the footer. Area/volume fields use the proper spec and Revit internal values. Number/currency fields retain explicit currency and cost basis. UI summaries sum saved records without extracting geometry again.
- Preview lists schedule name, parameter definitions, new/updated record counts and ZoneGuids. Explicit confirmation is required. Delete analysis and delete linked schedule records are separately disclosed; never delete Terrain/Cutter.

## Required feasibility assertions

1. Two fixture zones create exactly two owned schedulable records.
2. Recalculate A and update: record IDs/ZoneGuids remain stable, row count remains two, quantities and cost change correctly.
3. Read back every required field and record parameter, schedule collector membership and aggregate sums; confirm total configuration where supported.
4. Preserve unowned same-name schedule and unrelated Generic Models.
5. Inject failure after partial writes and verify original element/parameter/schedule state is restored.
6. No geometry is added for analysis visualization. If empty records cannot be scheduled reliably, revise the architecture before implementation/release; do not silently substitute visible solids.

## API evidence

Local Revit 2026.5 API skill: `db-d.md` DirectShape.CreateElement; `db-s.md` ScheduleDefinition, ScheduleField.CanTotal/DisplayType; `db-u-v.md` ViewSchedule.CreateSchedule/GetSchedulableFields; repository SharedParametersFilename/ExtensibleStorage implementations. Compile target remains repository Revit 2026 .NET 8 references. Runtime verification is authoritative for schedulability.

## Observed runtime evidence

Two zones produced two geometry-free records visible to the actual Schedule collector. Recalculating A preserved both record IDs and ZoneGuids. The service read back all 36 field identities, headings, visibility and supported total settings, plus every stored parameter value. Final quantities summed to 37.5 m³ cut, 45 m³ export and TEST 473 estimated cost. These are explicit fixture values, not project defaults. The unowned same-name Schedule remained intact. Injected failure rolled back and the original IDs and values were read back again.

Totals validation covers the actual Schedule configuration and sums of its verified member values; it does not claim a pixel/OCR check of formatted footer text. Raw fixture JSON/Markdown and models remain ignored local evidence. One Generic Model record per saved Schedule zone is an explicit model write, not a visualization solid.
