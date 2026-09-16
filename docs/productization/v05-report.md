# v0.5 智慧基地地形／土方中心

Status: READY_FOR_OPTIONAL_UAT

- A: PASS — 489 PASS / 0 FAIL
- B: PASS — 20 PASS / 0 FAIL
- C: PASS — 20 PASS / 0 FAIL
- C2: PASS — 65 PASS / 0 FAIL
- C3: PASS — 48 PASS / 0 FAIL
- TerrainLogic: PASS — 89 PASS / 0 FAIL
- TerrainRuntime: PASS — 31 PASS / 0 FAIL
- Build: PASS
- QAQC: PASS
- Rollback: PASS

Build SHA256: `571D1C86435EC680D022AD9BC6040ED1EB17C1221A35BB75DDC9EBE09BCD51E4`

Formal deployment: PASS

- CSV/TXT only; explicit units and coordinate basis
- Points-only convex hull; no legal site boundary inference
- Coded points retained; no inferred breakline connectivity
- Simplification error is conservative cell elevation envelope, not certified final-TIN interpolation error
- 20k-point tool guard requires simplification or explicit override
- Boundary quantity supports convex polygons and planar target elevation only
- Existing/proposed surfaces experimental and disabled for formal quantities
- No host/link movement, ProjectLocation writes, phase changes or Revit.ini edits
- Optional visual UAT remains; passing fixtures do not certify arbitrary survey/model conditions
