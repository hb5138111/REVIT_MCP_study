# Gate C 可回復測試載入 — 已授權並完成

2026-09-15 已依使用者明確授權執行一次暫時載入。完整部署目錄、manifest、逐檔 SHA256，以及 installer 會更新的共用 Python worker 均已備份。

- 使用既有 `scripts/install-addon.ps1 -Version 2026 -NonInteractive -KeepBackups -1`。
- build / temporary deployed / runtime loaded SHA256 一致。
- 使用者在未簽章安全對話框選擇 Load Once。
- CoordinationFixture coordination-1：15 PASS / 0 FAIL。
- Revit 正常退出後，從完整 snapshot 還原；沒有 kill process。
- v0.3 hash、全部原始檔案與 hash、manifest、worker 均驗證一致。
- 本檔記錄的是先前臨時測試；其後使用者另行授權正式發布。正式 deployment 證據與三個 checkpoint 計畫請見 v04-report.json 與 release-checkpoints.md。

[JSON / Markdown evidence 與發布判定](v04-report.md)

復原操作入口為 `scripts/test-reversible-gate-c.ps1 -RecoveryDirectory <本次 snapshot 根目錄>`。它會拒絕在 Revit 執行中還原；若正常退出存在時間差，可使用同一 snapshot 重跑 recovery。備份位於 repository test-artifacts，Revit 不會掃描該處的 manifest。
