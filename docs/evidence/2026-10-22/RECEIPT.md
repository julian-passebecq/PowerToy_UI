# Power Ops 2.0.0 - V2 release receipt (2026-10-22)

| Field | Value |
| --- | --- |
| Repository | `julian-passebecq/PowerToy_UI` (public) |
| Lane | Claude, Galaxy finish track 6 (took over the Codex V2 branches idle since 27 Sept) |
| Source | branch `claude/power-ops-v2-release`, code commit `26bf7ea` (PR #23); stack = draft PR #4 `62be3dc` (contains #2 `df3aa16`) + `main` `fcb9021` merged in |
| Version | **2.0.0** (frozen in `Directory.Build.props`) |
| Package | `PowerOps-2.0.0-win-x64.zip`, SHA-256 `5e613dfe9af216f66881bbadd565645c4ceed4aa29f931c1633f8a4c8cabd3e7` |
| Host | Windows 11 Pro 26300, PowerShell 7 + Windows PowerShell 5.1, two monitors (both 144 dpi, 150 %), Julian's separate Power Ring running (owns Ctrl+Alt+Shift+R) |

## Scope decisions

- PR #2 and #4 are superseded by one PR to `main` (#2's base was already promoted to `main` by #3). Recorded in Julian's `questions.md`.
- **Power Ring** was extracted to `powertoy-ring` (2.0.0): its projects (`src/PowerRing*`, `tests/PowerRing.Tests`), docs and `install-power-ring.ps1` are removed here, so this repository can no longer overwrite the installed Ring. Power Ops' own Quick Ring and Quick Shelf stay.
- Not in scope: email/OTP codes, embedded Mongoku against a live Mongoku (optional; its suites `report-cards`, `report-auth`, `maintenance-card`, `web-embedded` were NOT RUN).
- Isolation: every native run uses a fresh `--data-dir` under `%TEMP%`; runs 1-2 of the release check point the Sessions tab and audit card at an empty synthetic Claude home (`JUTILITY_CLAUDE_HOME`, `JUTILITY_AUDITS_DIR`). The default `%LOCALAPPDATA%\JUtilityPalette` folder was hashed before and after every run: unchanged. Julian's installed Power Ring and its data were not touched. Every window opened by the tests was closed by them.

## Defects found natively and fixed

| # | Defect (observed on the merged stack before the fix) | Fix | Test |
| --- | --- | --- | --- |
| D1 | Claude sessions page (from `main`) unreachable: V2 rebuilds the navigation from its module catalog, which did not know it | `sessions` module in `ModuleCatalog` | workspace test + native `Claude sessions: module reachable` (FAIL before, PASS after) |
| D2 | V2 workspace toolbar, menu, Launchpad cards and V2 dialogs hard-coded white: stayed white in dark mode | theme resource references (`ToolbarBrush`, `SurfaceBrush`, `MutedBrush`, ...) | native `theme: dark chrome is dark` (luma 35) / `light chrome is light` (luma 252). Before-fix native state NOT RUN (the before run aborted on D4 first); defect visible in code (`Brushes.White`) |
| D3 | Process was **system-DPI-aware** (no manifest): bitmap-stretched on any monitor whose scale differs from the primary; Quick Shelf drag landed off (dx -625 px for a +120 px drag) | `app.manifest` PerMonitorV2 | native `DPI: window is per-monitor aware` (SystemAware before, PerMonitorV2 after); `quick-shelf` drag FAIL before, PASS after |
| D4 | Workspace selector items announced as `JUtility.Core.Workspaces.WorkspaceProfile` (screen readers, UI Automation) | `WorkspaceProfile.ToString()` returns the name | workspace test + native `selector items are announced by name` |
| D5 | Sessions monitor kept scanning `~/.claude` every 10 s and on every transcript write while the window was hidden or minimized (Quick Ring idle check: 250 ms CPU in 10 s) | timer and watcher pause while hidden/minimized, rescan on return; `JUTILITY_CLAUDE_HOME` test seam | workspace test; native `idle: minimized with the real Claude home` (0 ms / 20 s) and `quick-ring` idle (31 ms / 10 s) |

Test-harness fixes (no product change): `quick-ring*.ps1 -RingKey` (Ctrl+Alt+Shift+R is owned by the separate Power Ring, so the suites self-BLOCKED); `quick-ring.ps1` runs per-monitor aware so window, pointer and UIA coordinates agree with a PerMonitorV2 app.

## Native acceptance on the final build (code `26bf7ea`)

See `native-logs/` for every line. Summary: `native-summary.json`.

| Suite | What it covers | Checks | Result |
| --- | --- | --- | --- |
| `release-acceptance` | Repository Hub, Portal Launcher, Capture, Clipboard copy, Launchpad, Quick actions ribbon, File tray page, Claude sessions, tabs new/close/reopen, saved view, workspace switch, reopen restores, focus (startup, dialog return, Ctrl+K), DPI awareness + every monitor, light/dark chrome, idle CPU minimized | 35 | **PASS** (0 FAIL, 0 BLOCKED) |
| `interaction` | Interaction settings, Hybrid mode, MX guide, recommended shortcuts registered | 16 | **PASS** (0 FAIL, 0 BLOCKED) |
| `quick-actions-hotkeys` | global shortcuts: toggle/summon focus, collision warning, release on exit | 20 (observations) | **PASS** (0 FAIL, 0 BLOCKED) |
| `quick-shelf` | Quick Shelf: no-activate, keyboard, Esc focus return, drag + saved position, auto-hide, per-workspace | 23 | **PASS** (0 FAIL, 0 BLOCKED) |
| `quick-ring` (-RingKey J) | Quick Ring: placement on pointer, foreground, slots, keyboard, digits, click, outside click, corner clamp, idle CPU | 19 | **PASS** (0 FAIL, 0 BLOCKED) |
| `quick-ring-v2` (-RingKey J) | sub-rings, whole-screen capture, Esc, mouse Back/Forward | 12 | **PASS** (0 FAIL, 0 BLOCKED) |
| `web-apps` | web apps in app-window mode from Shelf and shortcut | 7 | **PASS** (0 FAIL, 0 BLOCKED) |
| `claude-control` | Claude Control health dot, live card, embedded tab, start action, GET-only | 18 | **PASS** (0 FAIL, 0 BLOCKED) |
| `file-tray` | File tray: watcher, partial downloads, copy as file/text/image (PDF, Word, OCR), shortcut, flyout, Prompt Builder, privacy, idle cost | 34 | **PASS** (0 FAIL, 0 BLOCKED) |
| `release-acceptance` on the **extracted 2.0.0 package** | same as the first row, exact package bytes (exe SHA-256 `400ADF828C85FDC5...`) | 35 | **PASS** (0 not PASS) |
| Build gate (`scripts/build.ps1`, local + GitHub Actions run #374 on `26bf7ea`) | Release build 0 warnings, smoke suite, workspace tests | 114 workspace tests | **PASS** |

Retained earlier failures (first pass on the merged stack, before fixes): `quick-shelf` drag FAIL (D3); `claude-control` "server off -> falls back to the files" FAIL once (the embedded Claude Control tab was still shown after Tabs > Close tab), PASS on both re-runs - recorded as flaky test input, no product change; `file-tray` run 2 aborted once (main window handle not found within 15 s), PASS on both re-runs; `quick-ring`/`quick-ring-v2` BLOCKED (shortcut owned by Power Ring) then PASS with `-RingKey J`. The first `package-lifecycle` attempt aborted at "installed Power Ops did not start": the test passed a quoted path to `cmd /c` without the extra outer quotes cmd needs (test harness); fixed in the script, PASS on the re-run below.

Not covered here (one line each in Julian's `manual-checks.md`): mixed-DPI monitors (both connected monitors were 144 dpi, so "DPI follows each monitor" was proven only at one scale), physical multi-monitor and MX Master / Logi Options+.

## Package, install, reopen, rollback

| Field | Value |
| --- | --- |
| Package | `PowerOps-2.0.0-win-x64.zip`, 81,596,385 bytes, SHA-256 `5e613dfe9af216f66881bbadd565645c4ceed4aa29f931c1633f8a4c8cabd3e7` |
| Built by | `scripts/publish.ps1 -Runtime win-x64` from code commit `26bf7ea` (exe ProductVersion `2.0.0+26bf7ea907688581c13bb60dde38c27b5d5780a7`), self-contained, no PDBs, ships `install.ps1` + `README.txt` |
| Rollback source | `PowerOps-1.0.0-prefix-win-x64.zip` (SHA-256 `621cc81086b6fcb1b24ab679cda8e71ea4563ff86c90aa82fe0fbd920cfb696a`), built from the pre-fix merge commit `0caa145`; used only for this proof, not published |
| Confidentiality scan | 483 files: no tokens/keys, no private paths, no personal e-mail, no client markers. 1 pattern hit reviewed as false positive (binary bytes `PWD=` inside Microsoft's `PresentationFramework.dll`). Report: `native-logs/confidentiality-scan.json` |

`tests/native/package-lifecycle.ps1` (temporary install root, temporary `--data-dir`, `install.ps1` run under Windows PowerShell 5.1 as the README says):

| Check | Result | Detail |
| --- | --- | --- |
| packages: two distinct versions | **PASS** | old=1.0.0 new=2.0.0 |
| install 1.0.0: current | **PASS** | Installed 1.0.0 in %TEMP%\powerops-install-test-fd4d7376\versions\1.0.0 / Current: 1.0.0   Previous:  / Start: "%TEMP%\powerops-install-test-fd4d7376\PowerOps.c |
| run 1.0.0: started from the install folder | **PASS** | %TEMP%\powerops-install-test-fd4d7376\versions\1.0.0\JUtilityPalette.exe |
| run 1.0.0: closed cleanly | **PASS** |  |
| install 2.0.0: current, previous kept | **PASS** | Installed 2.0.0 in %TEMP%\powerops-install-test-fd4d7376\versions\2.0.0 / Current: 2.0.0   Previous: 1.0.0 / Start: "%TEMP%\powerops-install-test-fd4d7376\Power |
| install 2.0.0: user storage byte-identical | **PASS** |  |
| install 2.0.0: previous version folder unchanged | **PASS** |  |
| install 2.0.0: installed files equal the package | **PASS** |  |
| reopen 2.0.0: started from its version folder | **PASS** | %TEMP%\powerops-install-test-fd4d7376\versions\2.0.0\JUtilityPalette.exe |
| reopen 2.0.0: capture made by 1.0.0 is there | **PASS** |  |
| rollback while running: refused, nothing changed | **PASS** | Power Ops from %TEMP%\powerops-install-test-fd4d7376 is running (pid 62288). Close it  / first; nothing changed. / At %TEMP%\powerops-lifecycle-scratch-fd4d7376 |
| reopen 2.0.0: closed cleanly | **PASS** |  |
| immutability: same version with other bytes refused | **PASS** | Version 2.0.0 is already installed with different files. Versions are immutable: build a new version number. Nothing  / changed. / At %TEMP%\powerops-lifecycle- |
| idempotent: identical re-install changes nothing | **PASS** | Version 2.0.0 already installed (identical files). / Current: 2.0.0   Previous: 1.0.0 / Start: "%TEMP%\powerops-install-test-fd4d7376\PowerOps.cmd"   (user data |
| rollback: 2.0.0 -> 1.0.0 | **PASS** | Rolled back: 2.0.0 -> 1.0.0 (user data untouched) |
| rollback: user storage byte-identical | **PASS** |  |
| reopen after rollback: 1.0.0 running | **PASS** | %TEMP%\powerops-install-test-fd4d7376\versions\1.0.0\JUtilityPalette.exe |
| reopen after rollback: capture still there | **PASS** |  |
| reopen after rollback: closed cleanly | **PASS** |  |
| uninstall: user storage byte-identical | **PASS** |  |
| isolation: default data folder untouched | **PASS** |  |
| isolation: real install folder untouched | **PASS** | absent |

## Delivery

| Field | Value |
| --- | --- |
| Destination | GitHub release `v2.0.0` in `julian-passebecq/PowerToy_UI`, **draft** (id 420197514). The repository is public, so the release stays unpublished: an anonymous API request for it answers 404 |
| Assets | `PowerOps-2.0.0-win-x64.zip` (81,596,385 bytes), `PowerOps-2.0.0-win-x64.zip.sha256` |
| Authenticated re-download | `gh release download v2.0.0` (owner token) -> SHA-256 `5e613dfe9af216f66881bbadd565645c4ceed4aa29f931c1633f8a4c8cabd3e7` = local package = `.sha256` asset: **MATCH** |
| PRs | #23 squash-merged to `main` on green Windows CI; draft PRs #2 and #4 closed with a link to #23 |
| Not done here | publishing the release (would make it public), mixed-DPI and MX Master physical checks (Julian's `manual-checks.md`) |
