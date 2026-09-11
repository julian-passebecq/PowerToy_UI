# Baseline technical audit — 2026-09-08

Reviewed code: `d0a5ff8`, `main`. Initial checkout clean. Scope: all current Core services/models, MainViewModel, MainWindow XAML/code-behind, startup/resources, native summon and placement services, build/run scripts, smoke tests and CI configuration. No production code changed. This is an initial source/build audit, not native UI acceptance.

## Verified baseline

Environment: Windows; `dotnet --version` = 9.0.101. Project targets net8.0 and net8.0-windows. Local PowerShell profile emits unrelated Conda module errors, so audit commands used a no-profile execution path.

| Check | Actual outcome |
| --- | --- |
| `dotnet build .\JUtilityPalette.sln -c Release` | Exit 1; CS0136 at GlobalMouseSummonService.cs:100; 0 warnings, 1 error |
| `dotnet run --project .\tests\JUtility.SmokeTests\JUtility.SmokeTests.csproj -c Release --no-build` | Exit 0; all 7 existing Core checks pass |
| `pwsh -NoProfile -File .\scripts\build.ps1` | Exit 0 despite same failed application build; continues into passing Core smoke checks |
| Isolated Core reproduction: corrupt primary + good backup | Loaded project KeepMe, but backup contents became `CORRUPT` |
| Isolated Core reproduction: schema 999 with unknown field | Load/save changed version to 2 and removed unknown field |
| Isolated Core reproduction: Projects containing null | Load threw NullReferenceException |
| Isolated Core reproduction: import `{}` over one project | Import succeeded; project count changed from 1 to 0 |

Core reproductions used a temporary net8.0 console referencing the existing Core project, with synthetic directories below a unique system temp folder. No default app workspace was read or changed. Reproduction recipes below are sufficient to recreate tests in S01; temporary absolute paths are not required delivery artifacts.

## Findings

| ID | Severity | Evidence / affected location | Impact and decision |
| --- | --- | --- | --- |
| F01 | P0 | Reproduced; GlobalMouseSummonService.cs:90 declares `out ushort button`; :100 redeclares `button` in overlapping scope | Application cannot build. Fix first in S01 A. |
| F02 | P1 | Reproduced; scripts/build.ps1:5-7 does not check native exit codes; run.ps1 has same pattern | Local verification can appear successful after WPF compilation fails. S01 A. CI currently uses separate steps; its remote status was not checked. |
| F03 | P0 | Reproduced; WorkspaceStore.cs:37-40 calls Save during recovery; :58-60 copies existing corrupt primary over backup | Recovery destroys the known-good backup. Separate recovery publication from normal rotation. S01 B. |
| F04 | P1 | Reproduced; WorkspaceStore.cs:133-151 unconditionally stamps current schema | A newer workspace is silently downgraded and unknown data lost on save. Validate version before migration/publication. S01 A. |
| F05 | P1 | Reproduced; WorkspaceStore.cs:153 onward dereferences entries without validating; TryLoad only covers deserialization, not normalization | Valid JSON can crash startup; optional null body/text paths also lack complete handling. S01 A. |
| F06 | P0 | Reproduced `{}` import; WorkspaceStore.cs:73-82 deserializes and immediately saves; MainWindow.xaml.cs Import_Click has no replacement preview/decision | Unrelated JSON can replace useful workspace with empty content. Stage and validate before explicit replacement. S01 A-C. |
| F07 | P1 | Inspection; WorkspaceState DTOs lack change notifications; MainWindow.xaml edit bindings do not drive saves; MainWindow.xaml.cs:30-41 silently ignores close-save failure | Edits can remain unsaved until another action/close; a failed close save can discard them silently. Dirty tracking and safe flush required. S01 C. Actual UI propagation not run. |
| F08 | P1 | Inspection; MainViewModel.cs:265 onward replaces collections without explicitly resetting SelectedNote or PromptPreview | Session code does not guarantee selection/preview consistency after import; WPF may clear some bindings incidentally. Add explicit invariant and test, rather than assuming an observed UI failure. S01 C. |
| F09 | P1 | Inspection; App uses default StartupUri, each MainViewModel owns a new store; WorkspaceStore uses shared fixed .tmp path and no writer ownership | Multiple app instances can overwrite each other's workspace. No multi-process reproduction yet. S01 B. |
| F10 | P1 | Inspection; MainWindow.xaml.cs:309,388 Process.Start and :452 Clipboard.SetText have no boundary error handling | Expected platform failures can escape the click handler. Add adapters and failure tests. S01 C. |
| F11 | P2 | Inspection; archive/pin flags are stored but lists bind directly; RemoveProject does not resolve note ProjectId references | Organization UX and delete/link policy incomplete. Defer to S03; do not silently drop links in S01 validation. |
| F12 | P2 | Inspection; editable PromptPreview is recomposed by copy handlers; recent prompts are display-only | Preview edits can be overwritten by Copy; history cannot yet be recalled through explicit UI actions. Decide clear behavior in S04. |

## Minimal reproduction recipes

**F03:** Save a synthetic workspace with project KeepMe. Copy primary to backup. Overwrite primary with literal `CORRUPT`. Call Load. Assert recovered project is KeepMe, then inspect backup bytes: they are now `CORRUPT` in the baseline.

**F04:** Write `{"SchemaVersion":999,"FutureOnly":"preserve"}` to isolated primary. Call Load then Save. Baseline returns/stores schema 2 and drops FutureOnly.

**F05:** Write `{"SchemaVersion":2,"Projects":[null]}` to isolated primary. Call Load. Baseline throws NullReferenceException instead of a classified invalid-workspace result.

**F06:** Save an isolated workspace with one project. Write `{}` to a separate import file. Call Import on that file. Baseline reports success and returns zero projects.

## Limits and planning decision

Native mouse consumption, focus, mixed DPI, real clipboard contention and visual layout were not exercised. The WPF build blocker prevents running the current app from a fresh successful build. Existing seven checks cover formatter toggles, Copy All exclusion, a URL case, composition order/project variables, basic persistence, v1 migration, and summon defaults; they do not cover the failure cases above.

Decision: authorize S01 reliability foundation before feature expansion. Keep the existing WPF/Core/JSON architecture and add narrow validation, session, ownership and platform boundaries. No feature-development agent, branch, release or automation was started by this audit.
