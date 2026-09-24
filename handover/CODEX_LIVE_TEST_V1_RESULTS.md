# Codex live test v1 results

Date: 2026-09-24
Machine: JULP (Windows computer; make/model not reported by system query)
Windows: Windows 11 Pro, version 10.0.26200, build 26200
Architecture: OS x64; process x64; 22 logical processors; 3 displays detected
Branch: `codex/to-be-tested-v1`
Commit tested: `aa03a4b2f00077fa8a95e69e718359d0da93d01e`
Tester/model: Codex / GPT-6 Luna Medium (requested policy; no High escalation)

## Automated gate

- `scripts/build.ps1`: **PASS**, exit code 0; full Release solution build succeeded with 0 warnings and 0 errors.
- Smoke/regression tests: **PASS**, exit code 0; all smoke tests reported passed, including workspace/media path, persistence, recovery, import/export, keyboard policy, and window-placement checks.
- `scripts/publish.ps1 -Runtime win-x64 -OutputDirectory <isolated temp path>`: **PASS**, exit code 0; self-contained package generated at `%TEMP%\PowerOps-Codex-LiveTest-V1-Package-20260924`.
- Package launch: **PASS for process startup only**; launched `JUtilityPalette.exe` directly with a new `--data-dir`, without `dotnet run`. Process remained alive, exposed the expected J Utility Palette window title, and created `workspace.json`, `workspace.backup.json`, and `media\` in the isolated data directory. The window could not be inspected or operated through the available computer-control provider.
- `scripts/publish.ps1 -Runtime win-arm64`: **N/A**, host OS and process are x64.
- `scripts/run.ps1 -DataDirectory <isolated temp path>`: launched the WPF app and created the expected workspace files; interactive checks were blocked as listed below.
- Pre-test source revision was clean. No implementation fixes were made and no defects were confirmed.

## Live matrix

The CUA computer-control provider returned no native apps or windows (`apps: []`) throughout the pass, including while the WPF process was running. Therefore UI interactions were not executable. Unperformed UI checks remain **BLOCKED**; manually observed checks are updated in the matrix as they are reported.

| Area | Result | Evidence / observation | Fix commit |
| --- | --- | --- | --- |
| Startup / shell | PASS (partial) | User screenshots confirm Repository Hub is displayed with two repository rows and the shell remains responsive. The Compact layout shows a Projects filter pane alongside the module content. Other module navigation and startup/reopen checks remain unverified. | — |
| Repository Hub | BLOCKED | No UI access to add/edit/select/copy/archive/restore/delete test repositories or verify optional GitHub sync. | — |
| Portal Launcher | BLOCKED | No UI access to create/edit/filter/open/copy/delete a temporary portal. | — |
| Tool Launcher | BLOCKED | No UI access to add/edit/open a harmless launcher or inspect command handling. | — |
| System / Cheat Sheet | BLOCKED | No UI access to compare displayed system values or exercise path Open/Copy actions. Host query reported Windows 11 Pro x64 and 22 logical processors. | — |
| Resource Hub | BLOCKED | No UI access to create/edit/filter/open/copy/delete resources or capture/import links. | — |
| Capture | BLOCKED | No UI access to create, edit, archive/restore, or export capture items. | — |
| Clipboard text | BLOCKED | No UI access to add/edit/search/copy/delete snippets or verify Enter/Ctrl+C behavior. | — |
| Clipboard screenshot/image | BLOCKED | No real screenshot/image clipboard transfer or copy-back/paste into another app was performed. No image fixture was imported. | — |
| Clipboard video | BLOCKED | No video fixture import, Open/Copy verification, restart check, or managed-copy deletion was performed. | — |
| Media path safety | BLOCKED | Live import/save paths were not exercised. Automated smoke tests passed the general outside-managed-directory rejection and nested managed path acceptance cases; the full requested hostile-path list was not individually executed. | — |
| Prompt Builder | BLOCKED | No UI access to create/reorder/enable modules, resolve variables, preview/copy, or recall prompts. | — |
| Settings | BLOCKED | No UI access to change or verify settings, export, or two-phase import. | — |
| Summon/hide | BLOCKED | No native interaction with Normal/Always-on-top/Summon modes, focus loss, pin, cursor placement, modal protection, or binding selection. Mouse Button 4/5 and middle-click bindings were not exercised. | — |
| Multi-monitor/DPI | BLOCKED | Three displays were detected (DISPLAY1, DISPLAY2, DISPLAY5), including negative-coordinate work areas. Could not move the window, change layouts, restart, or verify mixed-DPI/off-screen behavior through CUA. | — |
| Self-contained package | PASS | Direct x64 EXE launch succeeded without `dotnet run`; process stayed alive and initialized a new isolated workspace. Interactive package behavior remains unverified due to missing CUA app/window access. | — |
| Performance sanity | BLOCKED | Process memory was observed once (about 207 MiB for the packaged process and 252 MiB for the `run.ps1` process). No module switching or timed idle CPU/disk observation was possible. | — |

### Result counts

- PASS: 2
- FAIL: 0
- BLOCKED: 15
- N/A: 0 live matrix rows (the separate ARM64 package check is N/A on x64)

## Defects found

None confirmed. The live UI could not be inspected, so this pass does not establish that the interactive behaviors are defect-free.

## Blocked tests

- Desktop control reported `apps: []` and exposed only the Codex in-app browser, even while both WPF processes were running. `cua.getApp` requires an enumerated window ID; none was available. No alternate UI-control method was used.
- Manual-assisted observations have begun: Repository Hub navigation and Compact layout are visually confirmed. Other hands-on app workflows, the real Windows image clipboard round-trip, video import, focus/summon checks, monitor moves, and the full performance sanity check remain unexecuted.
- The host reports three displays; mixed-DPI status could not be established from the available UI surface.

## Final status

**NOT READY — live interaction blockers remain**

The automated build/smoke gate and x64 package startup passed. A real interactive Windows acceptance pass is still required before user-test readiness can be claimed.
