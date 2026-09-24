# V2 foundation - delivery and evidence

Date: 2026-09-24.
Repository: `julian-passebecq/PowerToy_UI`.
Branch: `codex/power-ops-v2-workspaces`.
Draft PR: https://github.com/julian-passebecq/PowerToy_UI/pull/4

## Tested implementation

Code checkpoint: `d151051d9b40c4cde2982e2e0cffc7821b6083ca`.
Windows CI #314: https://github.com/julian-passebecq/PowerToy_UI/actions/runs/36025315380
Result: **completed / success**.
Runner: Windows Server 2025, .NET 8 SDK.

- Full WPF Release solution build: PASS, 0 warnings, 0 errors.
- Existing smoke/regression executable: PASS, all reported checks passed.
- New package-free workspace/export executable: **21/21 PASS**, 0 failed.
- The first CI attempt (#313) found a missing namespace import in the new test harness after the WPF build and old smoke suite had passed. Added the required import and reran the entire gate successfully; no failing tests were suppressed.
- Self-contained publish/artifact steps: SKIPPED by the PR workflow condition, not claimed as tested for V2.
- Native UI, mouse, monitor, clipboard, inventory interaction and idle resource measurements: NOT RUN by this implementation session.

Subsequent handoff/evidence documentation does not change the tested application code. Recheck the actual branch head and CI before running or merging; native acceptance remains separate.

## Added behavior

Named view workspaces with five presets; module visibility and grouped navigation; module tabs with search/filter state; tab keyboard commands; saved view bookmarks; inherited layout/ribbon choice; previewed layout JSON import/export; all/current/selected content JSON export; saved-sites Launchpad; on-demand local PATH/file-version inventory; in-app available/planned feature catalog.

Shell state is a separate bounded/validated schema-1 file. Business schema v7 is unchanged. Current-module and selected-module exports exclude unrelated shell views. The all-content review export can include shell state. Exports omit local launcher commands/arguments/paths by default, but free text and URLs may still be sensitive. No media bytes or complete recovery bundle is implied.

## Not implemented in this preview

Global lazy unloading of inherited editors; arbitrary widget placement/right-side companions; full per-control scroll/selection/undo restoration; multi-window-per-store coordination; complete runtime/Conda/WSL/extension/update inventory; Git/agent live feeds; cloud billing providers; AtlasNote task sync; Mongoku live summary; hardware/screen-time telemetry; RSS/market adapters; maintenance execution; credential-vault integration.

The inventory does not execute programs and does not claim a file version is the active interpreter version. The Launchpad does not run health probes or webviews. Module visibility is not a security boundary or proof of runtime unloading.

## Branch safety

The source base is the user's pushed Explorer checkpoint `df3aa1626fee3c96f6008793214852b273595ffc`. This work does not modify `main`, `codex/to-be-tested-v1`, AtlasNote, Mongoku, cloud resources or the user's laptop. PR #4 is intentionally draft and stacked on the V1 acceptance branch. No merge is authorized by this delivery.

Read `CODEX_HANDOFF.md` for a separate-worktree, unique-data-directory native test plan. Do not use the old V1-only acceptance launcher on the V2 branch. Do not overwrite the V1 results ledger.

## V2.1 Quick Actions - slice 1 (Core action layer)

Date: 2026-09-24. Author: Claude (takeover pass). Parent: `a273620605b108c298b7096ef988572f3fbf7363`.

### Added (`src/JUtility.Core/Actions/`)

- `QuickActionCatalog`: the ONE typed catalog (15 stable IDs): `app.toggle`, `app.open`, `ring.show`, `shelf.toggle`, `capture.region`, `capture.quick`, `clipboard.open`, `folder.downloads`, `folder.explorer`, `terminal.open`, `workspace.resume`, `workspace.next`, `workspace.previous`, `tab.next`, `tab.previous`. Each has label, category, description, glyph, in-app shortcut metadata, `GlobalAllowed` and risk. All built-ins are `Safe`. `mail.latestCode` and other provider-backed IDs are deliberately absent.
- `QuickActionDispatcher`: exactly one handler per ID (duplicate registration rejected), so Full UI, Quick Shelf, Quick Ring, in-app/global shortcuts and MX Master (via Logi Options+ keystrokes) all run the same implementation. Focused-only actions (`tab.*`) are refused from out-of-app surfaces; destructive actions (none today) are Full-UI-only; non-safe actions return `NeedsConfirmation`; availability is probed on demand only; handler exceptions become a `Failed` result instead of crashing a surface.
- `QuickActionSettings` + `QuickActionSettingsStore`: separate bounded file `quick-actions.json` (format `powerops-quick-actions`, version 1, 256 KiB cap). Shell schema 1 and business schema v7 are unchanged. Defaults are opt-in: `Mode = Off`, global shortcuts disabled, so nothing is registered or shown until the user enables it. Load never writes; malformed/future/oversized files fail closed and are preserved; save is atomic with `.backup`.
- Ring (1-8) and Shelf (1-10) layouts with per-workspace overrides (null = inherit). Layouts reject unknown, duplicate, focused-only, destructive and self-referencing entries.
- `HotkeyGesture`: canonical parsing to Win32 `RegisterHotKey` arguments (MOD_* flags + `MOD_NOREPEAT`, VK code). Policy rejects bare/Shift-only keys, plain Ctrl+letter/digit, Alt menu keys, Alt+Space/F4, Win-reserved combos and existing Power Ops in-app shortcuts. Policy is not proof: runtime registration can still fail and must be shown.
- `MouseDoubleInterceptionWarning`: flags the existing `GlobalMouseSummonService` low-level hook (active only in Summon window mode, default Mouse Button 5) when MX Master guide/Hybrid mode is chosen, because Logi Options+ may remap the same Back/Forward button.

Naming note: the V1 Portal "quick actions" (per-portal links, max 3) are an unrelated existing feature and were not changed.

### Evidence

Environment: Linux sandbox, .NET SDK 8.0.131, offline restore (no NuGet sources; projects have no package references).

- `JUtility.WorkspaceTests`: **34/34 PASS** (21 existing + 13 new Quick Actions tests), 0 warnings, 0 errors.
- Mutation check: disabling the out-of-app guard and the Alt+F4 policy each produced the expected failing test; restored code returns 34/34.
- `JUtility.SmokeTests`: 67/68 on Linux. The single failure, `locked export destination preserves previous export and cleans temp`, depends on Windows mandatory `FileShare.None` locking and fails identically on the unmodified parent commit; it passed on Windows CI for the parent. Not a regression; not suppressed.
- **Full WPF Release build: NOT RUN** in this environment (the WindowsDesktop targeting pack is unavailable offline). This slice adds no App code and the App does not import the new namespace, but the Windows CI gate (`.\scripts\build.ps1`) must confirm before the slice is called green.
- Native UI, hotkey registration, Ring/Shelf rendering, MX Master mappings and idle resource use: NOT RUN (no UI exists yet in this slice).

Windows follow-up (2026-09-24, Claude Code on the user's Windows 11 laptop, .NET SDK 9.0.101, tested revision `9fae4238cce61c1a876b0e89b614b741e4c5e421`):

- `.\scripts\build.ps1`: **PASS**. Full solution Release build including the WPF app, `JUtility.SmokeTests` **68/68 PASS** (the Linux-only locking failure does not occur on Windows), `JUtility.WorkspaceTests` **34/34 PASS**.
- Native UI and hotkey behaviour remain NOT RUN; nothing in this slice is wired into the app yet.

## V2.1 Quick Actions - slice 2 (app wiring + opt-in global shortcuts)

Date: 2026-09-24. Author: Claude Code (Windows laptop). Code commit: `16e8311`, parent `88e5792`.

### Changed

- `MainWindow.QuickActions.cs` is the only place catalog IDs receive an implementation. It reuses existing code paths: `OpenExplorerPath`/`OpenPinnedExplorerFolder`, `AddCaptureNote` (extracted from `AddNote_Click`), `LaunchTool`, `RestoreSession`, and the Summon show/hide logic.
  - `capture.region` launches the supported Windows `ms-screenclip:` flow.
  - `folder.downloads` resolves the Downloads known folder (`SHGetKnownFolderPath`).
  - `terminal.open` uses only an explicitly configured Tool Launcher entry (`QuickActionTargets.PickTerminal`) and never guesses an executable.
  - Clipboard/Capture activate an existing tab for that module before retargeting the current tab.
  - `ring.show`/`shelf.toggle` are deliberately unregistered and report "Not available in this build".
- Ctrl+Shift+E and Ctrl+Tab/Ctrl+Shift+Tab now run through the dispatcher (`InAppShortcut` surface), so in-app keys and global/menu surfaces share one implementation.
- New **Actions** menu: every registered action (Full UI surface), with its active global gesture shown, plus **Global shortcuts...** dialog (enable checkbox, add/remove bindings, validation errors inline, per-binding registration result after save).
- `GlobalHotkeyService`: `RegisterHotKey` on a message-only `HwndSource`; no keyboard hook, no timer. Registers only when `GlobalShortcutsEnabled`; each failed binding produces a visible message; all IDs are unregistered on re-apply and on window close. WM_HOTKEY work is deferred out of the window procedure.
- Out-of-app refusals (unavailable, not allowed, failed) bring Power Ops forward and show a message; in-app shortcut refusals only update the status bar.
- `app.toggle` decides "in front" from `GetForegroundWindow()`, not WPF `IsActive` (see defect below).
- Core: `WorkspaceSessions.CycleTab/CycleWorkspace`, `QuickActionHotkeys.Plan` and `DescribeRegistrationFailure`, `QuickActionTargets.PickTerminal`.

### Defect found and fixed during native testing

After a background launch Windows kept another app in the foreground, but WPF still reported `IsActive`, so the first toggle **minimized** a window the user could not see instead of bringing it forward. Reproduced by `tests/native/quick-actions-hotkeys.ps1`; fixed by checking the real foreground window; re-run passes in both directions.

### Evidence (tested revision `16e8311`)

- `.\scripts\build.ps1`: **PASS**. Release solution build with 0 warnings and 0 errors, `JUtility.SmokeTests` **68/68**, `JUtility.WorkspaceTests` **39/39** (5 new).
- `tests/native/quick-actions-hotkeys.ps1` on Windows 11 Pro 10.0.26200. Each run used fresh isolated `--data-dir` folders under `%TEMP%` and never touched the personal workspace. Result: **PASS**. The script observed:
  - a fresh data dir registers nothing and writes no `quick-actions.json`;
  - enabled bindings are owned by Power Ops (a probe gets 1409);
  - toggle brings a non-foreground window forward and minimizes a foreground one, observed in both directions across runs;
  - `app.open` restores and foregrounds;
  - a second instance with the same bindings shows the "Power Ops global shortcuts" warning and keeps running while the first instance keeps the hotkey;
  - closing releases both hotkeys;
  - loading does not rewrite the settings file.
- Machine note: **Ctrl+Alt+Space (the proposed default) is already registered by another application on this laptop**, so enabling the default binding here would show a registration failure. The first test run pressed it three times before this was detected; those presses went to that other application. The default is left unchanged because the feature is off by default and the failure is shown; the user should choose another gesture here.

### NOT RUN (user-assisted checklist)

Observer should record name, date and result for each:

1. Summon window mode: global toggle hides/shows with Open-near-cursor; Hide-on-focus-loss still behaves.
2. Actions menu: each item runs; Terminal with no terminal tool shows the "Add a terminal" message; Explorer with a missing pinned folder shows "Pinned folder not found".
3. `capture.region` opens the Windows snip overlay; `folder.downloads` opens Downloads.
4. `capture.quick` from another app: Power Ops comes forward on Capture with a new item and the title box focused.
5. Workspace next/previous from a global shortcut; tab cycling with Ctrl+Tab while not in a text box.
6. Global shortcuts dialog: invalid gesture (e.g. `Ctrl+C`, `Alt+F4`) is rejected inline; disabling removes registrations immediately.
7. Idle CPU/handles before/after enabling shortcuts; typing in other applications is unaffected.

### Remaining V2.1 work (in order)

1. Quick Shelf window (orientation, always-on-top, auto-hide, per-workspace layout, keyboard accessible).
2. Quick Ring window (6-8 slots, centre opens Power Ops, Esc/outside-click dismiss, monitor/DPI-aware placement).
3. Interaction settings UI + MX Master / Logi Options+ guide (show `MouseDoubleInterceptionWarning`).
4. Native acceptance + idle/latency measurements.
