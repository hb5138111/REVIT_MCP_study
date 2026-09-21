# v0.6.1.1 release checkpoints

- Implementation: 46eeee5 — deterministic CAD clusters/candidates, explicit geometry/purpose/normalization preview, isolated Families, source integrity, text second-alignment-point fix, MIT dependency and notices.
- Tests and release gates: eb825df — C5 39, expanded synthetic C4 33, actual C4 34, input-bound confirmation tests and mandatory same-build actual/C5 release checks.
- Final report checkpoint: resolve with `git log -1 --format=%H -- docs/productization/v0611-report.json`; exact local/remote read-back is `test-artifacts/v0611/final.json`.

Final build/runtime/deployed SHA256: `CC182F20E83C00E26AE8DDC62DA3B0233F0479272BF9648455B7CAA0E99A7766`. All five final reversible runs restored the previous stable inventory before formal deployment. Canonical installer verified 9 DLLs, third-party notices and one manifest.

The earlier actual run on ACFC1BD1… failed safely because CAD text AlignmentPoint retained source coordinates. It is not release evidence. Corrected build CC182F20… passes actual source conversion, 2 sheets, QA, idempotency, manual preservation, independent as-built conversion, native geometry read-back and visual review. Source DWG SHA stayed unchanged. No company text, private source path or sensitive screenshot is committed.

Source counts remain 192 runtime tools / 85 Domain SOP files / 61 skills. Full Domain certification is not implied; enabled scope and remaining limits are in v0611-report.json. Stop at v0.6.1.1.
