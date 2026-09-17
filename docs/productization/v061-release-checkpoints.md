# v0.6.1 release checkpoints

起點：bim-custom / 9b9c34b，working tree 乾淨且與 origin 同步。

1. `feat: harden drawing workflow and support external title blocks`：MCP production typed services、models、Native UI 與必要 fixture wiring，以及施工圖 Domain 更新。圖框與 workflow 共用 Blueprint / Context 契約，因此同一可編譯 checkpoint。
2. `test: gate drawing release on production end-to-end journey`：自動測試、reversible staging、C4 release guard、production failure 與 external API audit。
3. `docs: record verified v0.6.1 release`：43 項報告、source evidence、development state、產品化矩陣與 append-only log。

只有 origin/bim-custom 可推送。禁止 upstream / force push / reset / clean。最終 commit 以 `git log -1 --format=%H -- docs/productization/v061-report.json` 解析；提交後 literal receipt 保存於 ignored `test-artifacts/v061/final.json`，避免文件自我 SHA 循環。

Runtime artifacts、生成的 RFA/DWG/RVT、安裝快照與日誌留在本機 ignored test-artifacts。已追蹤報告保留 Gate 結果、assertions、檔案路徑與 hash，以便追溯；本機 artifact 不代表遠端 repository 也包含測試模型。
