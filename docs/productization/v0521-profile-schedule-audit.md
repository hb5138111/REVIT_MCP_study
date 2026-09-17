# v0.5.2.1 Profile / Schedule capability audit

Baseline: v0.5.2, commit d8a57e6; clean bim-custom synchronized with origin at task start. Implementation and runtime verification are pending.

| Question | Current implementation / safe reuse |
|---|---|
| Profile storage | RevitEarthworkRecords stores EarthworkProjectData JSON in one owned DataStorage using schema 768C4D95-AAC9-4B72-87E9-95308E6DAD32. |
| Identity | ProfileGuid exists; name is display metadata. No version/kind/archive fields yet. |
| Zone reference | EarthworkRecord.Profile holds an immutable record copy, identified by GUID. |
| Snapshot | Full calculation settings already saved with each result; explicit version/hash/snapshot provenance is missing. |
| Schedule records | Geometry-free Generic Model DirectShape, one per ZoneGuid; full DTO in ExtensibleStorage and schedulable shared parameters. |
| Ownership | Schema Kind=record/schedule plus stable ownership parameter; foreign same-name schedules are protected. Retain existing schema and parameter GUIDs for compatibility. |
| Fixture profiles | EarthworkWorkflowSelfTest creates Runtime fixture only / TEST / Fixture truck through the same profile save path. Production selector currently lists all profiles. |
| Formatting | UI uses N2, Schedule Number fields inherit project settings; raw costs are decimal but converted to Revit double at the parameter boundary. Explicit Schedule format is absent. |
| Status | Zone.Status mixes candidate and review semantics; calculation and review need independent typed states. |
| Reusable infrastructure | Typed C# context, ExternalEvent dispatcher, immutable record DTOs, source signatures, DataStorage, shared parameter binding, Preview fingerprint, TransactionGroup rollback, read-back and reversible fixture runner. No new MCP tool required. |

## Design decisions

- Preserve geometry, soil-balance, truck and cost formulas. Add deterministic hash of calculation-affecting profile fields, versioned profile snapshots, explicit apply-latest/recalculate and independent review state.
- Preserve old serialized property names. Legacy v0.5.2 profiles become V1; the exact known fixture signature is classified TestFixture on read. No implicit production defaults. Legacy records retain their original numbers/snapshot.
- Production selectors and result exports exclude TestFixture; explicit fixture mode is injected into the test ViewModel only. Archived profiles remain in storage and historical snapshots remain readable.
- Summary and Detail use separate owned Schedule kinds but share existing ZoneGuid records. Legacy schedule kind is recognized as Detail. Keep existing parameter GUIDs; add new status/provenance/cost-component fields. Numeric raw values remain unchanged; percentage presentation uses separate percent-valued fields to avoid silently changing legacy fractions.
- Currency/quantity presentation uses two decimal places, factors two, percentages whole percent and trips integer. Schedule FormatOptions overrides are local to fields, not Project Units. Currency is explicit and arbitrary; no exchange rates or market prices.
- Profile save, clone/archive and review writes require confirmation; no historical result is recalculated when a profile is edited. Model/source staleness is checked before review or calculation writes.

## Required proof

Pure/state: version/hash/snapshot immutability, clone/archive, production isolation, percent conversion, validation/cancel, formatting 162/311/473, stale/recalculate/review. Runtime: actual selector visibility, V1→V2 preservation and update, two schedules, stable two rows, fields/formatting/totals/ownership/rollback; full v0.5.2 regression suite on the final build. Reversible staging restores full v0.5.2 before formal deployment.
