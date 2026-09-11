# Test strategy

Separate developer checks, independent testing, and lead logic review. Keep the package-free smoke runner where it remains useful; split tests/helpers into readable files as coverage grows. A small Windows/STA integration executable is permitted when needed to test app/session behavior. A new test framework is not required by this plan.

## Baseline commands

Run each command separately and capture its native exit code. Existing build.ps1 does not yet propagate build failure reliably; B02 fixes that.

```powershell
dotnet restore .\JUtilityPalette.sln
dotnet build .\JUtilityPalette.sln -c Release --no-restore
dotnet run --project .\tests\JUtility.SmokeTests\JUtility.SmokeTests.csproj -c Release --no-build
```

Use the repository-declared target frameworks. Baseline was run with SDK 9.0.101 against net8.0/net8.0-windows; Windows CI declares SDK 8.0.x. Record installed SDK and runtime versions, OS, and whether CI was actually observed. Test the documented scripts after B02. CI should include any newly required automated runner rather than leaving it local-only.

## Isolation and meaningful assertions

- Create a unique synthetic data directory per scenario. Use `--data-dir` for app tests once implemented; never point destructive cases at default LocalAppData.
- Save exact primary/backup/source bytes or hashes before operations. Assert preservation and parseability after failure, not merely file existence or lack of exception.
- Inject targeted write/preservation/publication failures through the narrow filesystem boundary; separately exercise real Windows files, file locks and replace behavior.
- Exercise dirty/save generation sequencing with deterministic coordination, not arbitrary sleeps. Include real WPF binding checks because session tests alone cannot prove event wiring.
- Test normal shutdown and failure recovery. Process termination tests use disposable instances and synthetic data only.
- A failing assertion must produce nonzero exit status. Keep each test independently identifiable and summarize all failures.

## S01 mandatory acceptance matrix

| ID | Setup / operation | Required outcome | Method |
| --- | --- | --- | --- |
| T01 | Restore, build full solution Release, run all registered tests | All succeed; app assembly built from candidate revision; no stale-artifact inference | Automated commands |
| T02 | Controlled failed restore/build/test/run through script harness | Nonzero exit; later dependent steps do not run | Isolated shell stub/harness, success path on real tools |
| T03 | New empty directory; supported v1, v2, explicit empty workspace; recognizable no-version legacy fixture | Only new directory seeds; valid data survives; v1 topmost migrates; IDs/text preserved | Core + actual files |
| T04 | Future/invalid versions, `{}`, unrelated object, wrong types, null entries, bad enums, duplicate/empty IDs | Actionable classified rejection; no source/primary/backup mutation | Core + file byte assertions |
| T05 | Null optional strings/collections, omitted preferences, history >30/duplicates, dangling note link | Documented defaults/history policy; content not lost; dangling ID warned and retained | Core fixtures |
| T06 | Corrupt primary + good backup, accept recovery; repeat load | Recovered content correct; corrupt evidence preserved; backup still valid; no silent reset | Actual files + app recovery interaction |
| T07 | Both files invalid; future primary + valid backup; access denied/locked read | Distinct visible outcome; no seed/overwrite; import/exit/retry as appropriate | Filesystem failures + startup interaction |
| T08 | Save normal state; inject temp-write, preservation/backup and publish failures | No truncated committed file; prior usable state/recovery retained; caller state unchanged; retry succeeds | Fault injection + real file locks |
| T09 | Prepare valid import, cancel; malformed/future import; fail commit; then successful commit | Preparation/cancel/failure preserve live state and managed files; successful replace keeps prior valid backup | Core/session + Windows interaction |
| T10 | Export normal state; fail export write; target active primary/backup/recovery/temp path | Valid export round-trips; failed ordinary replacement preserves prior export; managed targets rejected | Automated files |
| T11 | Launch two instances same directory; launch different directories; terminate/restart synthetic owner | Same-directory second writer blocked before file access; independent directories work; ownership released | Real processes |
| T12 | Edit project/note/module fields, preferences, reorder and toggles in UI; wait debounce, restart | Changes persist; Saved accurate; all corresponding item displays update | Session automation + actual WPF interactions |
| T13 | Save A in flight, edit B; fail save; retry; remove/import while changes tracked | A completion cannot mark B saved; dirty/error state correct; detached old items cannot alter new session | Deterministic session tests |
| T14 | Pending grid/text edit, explicit Save/export/hide/close; inject flush failure | Latest committed editor text used; failure does not silently lose edits; retry/export/cancel/discard follow chosen UI contract | Session tests + actual WPF interactions |
| T15 | Select old note/project, import another workspace and preferences | Selection null or in new collections; preview reset; old objects detached; no partial preference saves | Session tests + binding/event verification |
| T16 | Clipboard contention/failure, launch error, empty text, invalid non-web URL | No crash/false success; no recent prompt added after failed copy; valid copy/open semantics retained | Adapter failures + actual clipboard/browser smoke |
| T17 | Existing field switches, Copy All excludes archived, Extra hidden, module order/variables/history, note archive/pin data | Established content/formatting semantics preserved through save/restart | Core regression + focused UI |
| T18 | Normal/topmost/summon, temporary pin, hide/reveal, import/export dialog, close | No refactor regression, no storage in hook callback; visible recovery from failures | Code inspection + native Windows interaction |

Tester reports every row separately. If a row combines automation and interaction, show both sub-results; a passing mock does not satisfy a blocked interaction check. Lead acceptance requires mandatory cases passed or an explicit lead-approved scope change with rationale.

## Later native/UX release coverage

S02 must expand mouse coverage to each binding, paired down/up consumption, unrelated input, Ctrl changes during click, rapid toggles, mode changes, startup hide, shutdown, hook failure, modal interaction and visibility recovery. Cover single/multiple monitors, negative coordinates, taskbar work areas, mixed DPI, display removal and windows larger than available work area. Record actual hardware; unavailable hardware remains a gap.

S03 covers archive views, selection under filtering, pinned ordering, URL edit errors, delete/note-link policy and search. S04 covers variable casing/whitespace, missing values, project override rules, deterministic ordering, preview/copy semantics and history recall. S05 adds clean-machine installation/run, upgrade data retention, keyboard/focus/accessibility, measured startup and a representative larger synthetic workspace. Lead defines exact acceptance when each sprint is opened.
