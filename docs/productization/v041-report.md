# v0.4.1 施工協調工作台

Status: READY_FOR_OPTIONAL_UAT

- Gate A: PASS — 465 passed / 0 failed
- Gate B: PASS — 20 passed / 0 failed
- Gate C: PASS — 18 passed / 0 failed
- Gate C2: PASS — 43 passed / 0 failed
- Gate C3: PASS — 30 passed / 0 failed
- Gate Build: PASS
- Gate QAQC: PASS
- Gate Rollback: PASS

Build SHA256: `7677DBDEAA894F091E6F00BF62BFF6BB74F6F2210651006C6405C617A443BCD6`

正式部署: PASS

- Centerline vs solid only; excludes fittings, insulation and grazing collisions
- BeamPenetration is always review-only, not RC/SC/SRC approval
- Sleeve classification and structural approval disabled
- Opening dimensions require explicit per-project session clearance
- Unknown opening bottom remains null
- Multiple solid intersections produce one row; length and point identify first segment with warning
- Arbitrary rotation/mirroring not certified by this fixture

[Failure analysis](v041-runtime-failure-analysis.md) · [Assertions and release evidence](v041-report.json)
