# Power Ops - RESUME (2026-10-22)

## Where things stand

- **Released candidate: Power Ops 2.0.0** (V2: saved workspaces, tabs, Launchpad, quick actions, Quick Shelf/Ring, Credentials & IDs, File tray, Claude sessions, dark mode). Private **draft** GitHub release `v2.0.0` (the repository is public: keep it draft). Receipt: `docs/evidence/2026-10-22/RECEIPT.md`.
- V2 stack merged to `main` by PR #23 (former draft PRs #2 and #4 closed with a link). Power Ring lives in `powertoy-ring`; it is gone from this repository.
- Storage: `%LOCALAPPDATA%\JUtilityPalette` (default) or `--data-dir <folder>`; `workspace.json` (schema v7), `shell-workspaces.json` (powerops-shell v1), `quick-actions.json`, `file-tray.json`. Install folders never hold user data.

## How to verify

1. `.\scripts\build.ps1` - Release build (0 warnings), smoke suite, workspace tests (114/114 at 2.0.0).
2. Native, desktop idle, PowerShell 7: `tests/native/release-acceptance.ps1` (all selected utilities, clipboard, focus, DPI, theme, idle), then the feature suites `interaction`, `quick-actions-hotkeys`, `quick-shelf`, `quick-ring -RingKey J`, `quick-ring-v2 -RingKey J` (the separate Power Ring owns Ctrl+Alt+Shift+R), `web-apps`, `claude-control`, `file-tray`. Every suite uses a fresh isolated `--data-dir`; `JUTILITY_CLAUDE_HOME` keeps the Sessions tab off the real `~/.claude`.
3. Package: `.\scripts\publish.ps1` -> `artifacts\PowerOps-<version>-win-x64.zip` + `.sha256`; `tests/native/package-lifecycle.ps1 -NewPackage <zip> -OldPackage <older zip>` proves install -> reopen -> rollback -> uninstall with user storage byte-identical (temporary install root, never `%LOCALAPPDATA%\Programs\PowerOps`).

## Left / next

- Physical checks for Julian (one line each in the owner's effort-board `manual-checks.md`): mixed-DPI monitors (only two 144-dpi monitors were connected), MX Master / Logi Options+ acceptance.
- Not in scope: email/OTP codes (planned slice 2), embedded Mongoku report cards against a live Mongoku (optional; `report-cards`, `report-auth`, `maintenance-card`, `web-embedded` suites need it running).
- Known cosmetic gap: some code-built V2 panels (Quick Capture, Quick Shelf/Ring windows, report cards, File tray rows) still use fixed light colours in dark mode.
- Next version: bump `<Version>` in `Directory.Build.props`; never reuse 2.0.0.
