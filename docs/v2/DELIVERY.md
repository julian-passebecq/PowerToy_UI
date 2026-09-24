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

## V2.1 Quick Actions - slice 3 (Quick Shelf)

Date: 2026-09-24. Author: Claude Code (Windows laptop). Code commit: `0547e17`, parent `5a589e9`.

### Changed

- `QuickShelfWindow` is presentation only; every button goes through the shared dispatcher (`ActionSurface.QuickShelf`), and `shelf.toggle` is now registered.
  - It is a borderless tool window (not in Alt+Tab or the taskbar) with `WS_EX_NOACTIVATE`, so clicking a button runs the action without taking focus from the user's application.
  - It is icon-first (Segoe Fluent/MDL2 glyphs), with an accessible name and tooltip per button (label, bound global shortcut, description, and the unavailable reason when one applies). Unavailable actions are dimmed, not hidden.
  - Availability is probed only when the Shelf is shown or hovered. Buttons whose layout is unchanged are updated in place.
- Layout options: horizontal or vertical, always on top, and auto-hide. Auto-hide collapses the Shelf to its handle 0.7 s after the pointer leaves, using a one-shot timer, and expands it on hover.
- Moving: drag the handle, and the position is saved to `quick-actions.json` (`ShelfLeft`/`ShelfTop`, validated) and clamped onto a monitor when shown. Clicking the handle without dragging (or right-clicking it) opens a menu: Customize, Open Power Ops, Hide.
- Keyboard: `shelf.toggle` (Actions menu or a global shortcut) shows the Shelf with focus on the first button. Tab and the arrow keys cycle, Enter runs, and Esc hides and returns focus to the previously focused window.
- Per-workspace: each workspace view can have its own button list, otherwise it inherits the default. The Shelf re-renders when the active workspace changes.
- **Customize Quick Shelf** dialog (Actions menu or Shelf menu):
  - "show at startup" (sets `Mode = QuickShelf`), orientation, always on top, auto-hide;
  - default vs "this workspace only" scope; add, remove, move up/down; revert the workspace to the default.
  - Edits are made on a copy that is validated and saved before it replaces the live settings.
- The Shelf closes with the main window. If `quick-actions.json` is unreadable, `shelf.toggle` reports why and nothing is written.
- Core: `QuickShelfModel` (build items, startup rule), `QuickActionLayouts.Eligible`, `QuickActionSettingsStore.Copy`, and the Shelf position fields. Slice-1 files without a position still load.

### Defect found and fixed during native testing

Clicking a Shelf button while Power Ops owned the foreground **activated the Shelf** despite `WS_EX_NOACTIVATE`: a focusable WPF button takes keyboard focus on mouse down, and `SetFocus` activates the window. It was intermittent and depended on which process was in front. Fixed by making the buttons focusable only in keyboard mode (shown via shortcut), which ends when the Shelf deactivates. The native script now clicks in both foreground states.

### Evidence (tested revision `0547e17`)

- `.\scripts\build.ps1`: **PASS**. Release build with 0 warnings and 0 errors, `JUtility.SmokeTests` **68/68**, `JUtility.WorkspaceTests` **43/43** (4 new).
- `tests/native/quick-actions-hotkeys.ps1`: **PASS** (slice 2 regression).
- `tests/native/quick-shelf.ps1` on Windows 11 Pro 10.0.26200 at 150% scaling, with fresh isolated `--data-dir`, synthesized mouse/keyboard and UI Automation: **PASS in 4 consecutive runs**. It observed:
  - Quick Shelf mode shows the Shelf at startup without taking the foreground;
  - tool-window and no-activate styles are set;
  - the 8 buttons expose their accessible names in the configured order;
  - default placement is top centre;
  - clicking "Open Power Ops" brings the main window forward, and the Shelf never becomes foreground, including when Power Ops already owned the foreground;
  - the shortcut hides and shows the Shelf, and keyboard show focuses the first button;
  - Tab moves to the next button; Esc hides and returns focus to the previous window;
  - dragging moves the Shelf by exactly (120, 90) px and saves the position, which is restored exactly after restart;
  - the active workspace's own list (2 buttons) is used;
  - vertical auto-hide collapses to the handle, expands on hover and collapses again;
  - closing Power Ops closes the Shelf and ends the process.
- Idle sanity (30 s after an 8 s settle, isolated data dirs):

  | Mode | CPU | Handles | Working set |
  | --- | --- | --- | --- |
  | Off | 0 ms | 652 → 651 | 246.9 MB |
  | Quick Shelf visible | 0 ms | 655 → 654 | 247.6 MB |

  This is a sanity check, not a benchmark.

### NOT RUN (user-assisted)

1. Multi-monitor, negative-coordinate and mixed-DPI placement: this laptop has one display.
2. Customize dialog end-to-end: reordering, workspace scope, reverting the workspace to the default.
3. Keyboard operation of the handle menu.
4. Each Shelf action's external effect (snip overlay, Explorer, terminal), and the unavailable messages.
5. Always-on-top off together with other always-on-top applications.

## V2.1 - slice 4 (web apps: Mongoku, Grafana, AI chats)

Date: 2026-09-24. Author: Claude Code (Windows laptop). Decision record: `docs/v2/WEB_SURFACES.md`. Code commit: see the commit titled "V2.1 slice 4: web apps as quick actions".

### Changed

- `quick-actions.json` gains `WebApps` (empty by default; max 24). Each entry has name, http(s) URL, open mode (`AppWindow` = Chrome/Edge `--app=` window with the user's existing profile, or `Browser` = default browser) and a browser choice (`Auto` follows a Chrome default, else Edge).
  - URLs must be absolute `http`/`https` without embedded user name or password (no `file:`/`javascript:`).
- Each web app becomes a `web:<id>` action. `QuickActionDispatcher.ReplaceDynamic` hosts these user-defined actions; they cannot shadow built-in IDs and still have exactly one implementation. They appear in the Actions menu, the Quick Shelf editor and the Global shortcuts dialog.
  - Layout and shortcut validation accepts only web apps that exist.
  - Removing a web app removes it from the Shelf, Ring, workspace overrides and shortcuts. A layout that would become empty returns to the default.
- **Actions → Web apps...** dialog:
  - presets: Mongoku `http://localhost:3100/`, Grafana `http://localhost:3000/` (edit to the real URL), Gemini, ChatGPT, Claude;
  - new, remove, per-app mode and browser;
  - "Test open".
- No browser engine is hosted, nothing is fetched until the user acts, and there is no NuGet dependency.

### Evidence

- `.\scripts\build.ps1`: **PASS**. 0 warnings, SmokeTests 68/68, WorkspaceTests **47/47** (4 new: URL/credential validation, cross-surface use and clean removal, dispatcher dynamic actions, browser choice).
- `tests/native/web-apps.ps1`: **PASS**. Isolated data dir, with a throwaway localhost page standing in for Mongoku:
  - the Shelf shows the named web-app button;
  - zero requests are made before the user acts;
  - a Shelf click opens a Chrome app window (`Chrome_WidgetWin_1`) on the page;
  - the bound global shortcut opens it again;
  - only the test window is closed, and Power Ops exits cleanly.
- Regression re-runs at `64f26e0` were **flaky**:
  - `quick-actions-hotkeys.ps1` failed 1 of 2 runs (`app.open` did not get the foreground) and then passed.
  - `quick-shelf.ps1` failed 2 of 4 runs and passed the other 2. In the failed runs the synthesized drag moved 0 px and focus requests were refused. This matches concurrent real mouse/keyboard use during the runs, which overrides synthesized input and triggers the Windows foreground lock.
  - `QuickShelfWindow.cs` and `Services/` are unchanged since `0547e17` (4/4 PASS on an idle desktop).
  - The scripts now say the desktop must be idle. Treat this as **not yet re-confirmed on an idle desktop** for this commit.
- NOT RUN:
  - the real Mongoku (not running on this laptop);
  - `Browser` mode, which would open a tab in the user's live browser;
  - the dialog UI end-to-end;
  - Edge fallback on a machine without Chrome.

## V2.1 Quick Actions - slice 5 (Quick Ring)

Date: 2026-09-24. Author: Claude Code (Windows laptop). Code commit: `19738ad`, parent `6b66a88`.

### Changed

- `QuickRingWindow`: a transparent, topmost tool window with a disc of up to 8 slots numbered clockwise from the top (built-in actions and web apps) and a centre button that runs `app.open`. It is presentation only: every slot goes through the dispatcher (`ActionSurface.QuickRing`), and `ring.show` is now registered.
- Showing and placement:
  - `ring.show` (global shortcut, Actions menu or a Shelf button) centres the ring on the pointer and clamps it to that monitor's work area (`WindowPlacementService.CenterOnCursor`, `WindowPlacementMath.CenterOn`);
  - it records the previously focused window and activates the ring.
- Keyboard: the centre is focused first; arrows move and wrap; 1-8 (or numpad) run a slot directly; Enter/Space run the focused slot. Esc hides the ring and returns focus to the previous window.
- Running a slot hides the ring and restores focus first, then runs the action, so actions open in the user's context.
- Dismissal: only the disc is hit-testable. An outside click activates the other window, and the ring hides without pulling focus back.
- Availability is probed once per slot on show. The window is created on first use and reused; nothing runs while it is hidden.
- **Customize Quick Ring** dialog: default or per-workspace slots. The list editor is now shared with the Quick Shelf dialog (`MainWindow.LayoutEditor.cs`).
- Core: `QuickSurfaceModel`/`QuickSurfaceItem` shared by Shelf and Ring, and `QuickRingModel` (slot geometry, arrow and digit model).

### Test-safety correction (native scripts)

During the slice 5 runs a probe showed the user actively working in Chrome. The native scripts synthesize arrow, digit, Tab and Esc keys and coordinate clicks, which would go to whatever window is in front if Power Ops is not.
- The scripts now refuse to send a key unless the foreground window belongs to the test's Power Ops process, and refuse to click or drag unless the point is over it. They bring Power Ops forward with a global shortcut, which Windows consumes and never delivers to another app, instead of a title-bar click. They clean up on abort.
- The aborted first ring run stopped before sending any non-shortcut key.
- Earlier slices' runs predate these guards. Their only non-shortcut keys were sent after checks that expected Power Ops to be in front, but that was not enforced.
- Also fixed: PowerShell passes `$null` as `""` to `FindWindow`, so the ring script now uses `[NullString]::Value`.

### Evidence (tested revision `19738ad`)

- `.\scripts\build.ps1`: **PASS**. 0 warnings, SmokeTests 68/68, WorkspaceTests **51/51** (4 new: slot geometry, keyboard model, workspace/web-app ring, pointer placement including negative coordinates).
- `tests/native/quick-ring.ps1`: **PASS, 19/19 checks**. It observed:
  - no ring window exists before first use;
  - the shortcut shows it (first show 137 ms, warm show 28 ms, measured from the synthesized key press to a visible window) centred exactly on the pointer, as the foreground window;
  - slot names in order, slot 1 at the top and slot 2 on the right;
  - the centre is focused first, and the arrows move clockwise and wrap;
  - Esc hides it and returns focus to the previous window;
  - digit 1 and a slot click each run the slot's action (the test page opened);
  - an outside click dismisses it without running anything;
  - the top-left corner clamps to (0,0);
  - the centre brings the minimized Power Ops forward;
  - 15.6 ms CPU (one timer tick) in 10 s while hidden after use;
  - clean exit.
- Re-run on the same revision with the guarded scripts: `quick-actions-hotkeys.ps1`, `quick-shelf.ps1` and `web-apps.ps1` all **PASS**. This re-confirms the Shelf result that was flaky at `64f26e0`.
- NOT RUN:
  - multi-monitor and mixed-DPI placement (single display);
  - MX Master / Logi Options+ invoking the ring (no device in this session);
  - the Customize Quick Ring dialog end-to-end;
  - slots with external effects (screenshot, Explorer, terminal);
  - screen-reader announcement.

## V2.1 Quick Actions - slice 6 (Interaction settings + MX Master / Logi Options+ guide)

Date: 2026-09-24. Author: Claude Code (Windows laptop). Code commit: `ae1ce80`, parent `8a805b6`.

### Changed

- **Actions → Interaction settings...** offers five modes (Off, Quick Shelf, Quick Ring, MX Master guide, Hybrid), each with a one-line explanation. Everything is edited on a copy and saved at once; hotkeys are then re-applied, and the Shelf is shown when the mode is Quick Shelf.
- **Add recommended shortcuts** (`InteractionGuide.AddRecommended`):
  - Shortcuts per mode:
    - Hybrid / MX Master guide: Quick Ring, Show/hide, Quick Capture, Clipboard;
    - Quick Ring: Quick Ring, Show/hide;
    - Quick Shelf: Quick Shelf, Show/hide.
  - Candidates are Ctrl+Alt+Shift+letter with F-key fallbacks. They are typeable, so Logi Options+ can record them, and they pass the conflict policy.
  - Each candidate is probed in Windows before it is offered (`GlobalHotkeyService.IsAvailable`: a trial `RegisterHotKey` on the message window, released immediately).
  - A working user binding is kept. A binding that another program owns is replaced and the report says so.
  - The result is idempotent, nothing is registered until Save, and the report lists every decision.
- **MX Master / Logi Options+ guide** (`InteractionGuide.MxMasterSteps`) is generated from the configured shortcuts:
  - gesture press → Quick Ring; gesture up/down → Quick Capture/Clipboard; a spare button → Show/hide;
  - application-specific Back/Forward for `JUtilityPalette.exe` → Ctrl+Shift+Tab / Ctrl+Tab;
  - each step has a Copy button;
  - a step for an unbound or disabled action is flagged, not promised.

  No Logitech driver or API is used: Options+ sends ordinary keystrokes.
- The Summon mouse-hook double-interception warning appears when relevant, with a one-click "Use Ctrl + middle click for Summon" fix.

### Evidence (tested revision `ae1ce80`)

- `.\scripts\build.ps1`: **PASS**. 0 warnings, SmokeTests 68/68, WorkspaceTests **54/54** (3 new):
  - valid, conflict-free recommendations;
  - keep, fall back, replace, idempotent and nothing-free cases;
  - guide steps reflect the bindings, the warning, and disabled shortcuts.
- `tests/native/interaction.ps1`: **PASS, 16/16**. It drives the dialog through UI Automation Invoke/Select/ExpandCollapse only (no synthesized clicks or typing; the Copy buttons are not pressed, to leave the user's clipboard alone) and observed:
  - the dialog opens from the Actions menu with five modes and Off selected;
  - Hybrid expands the guide;
  - "Add recommended shortcuts" reports: Ctrl+Alt+Shift+R added; **Show/hide's default Ctrl+Alt+Space is owned by another program on this laptop and was replaced with Ctrl+Alt+Shift+P**; N and V added;
  - the guide offers "Copy Ctrl+Alt+Shift+R", and the Back/Forward steps are present;
  - nothing is registered before Save;
  - Save shows no warning, persists Hybrid with the four expected bindings, and all four are then owned by Power Ops;
  - the Quick Ring shortcut shows and hides the ring;
  - exit releases every shortcut.
- The same revision re-ran `quick-actions-hotkeys.ps1`, `quick-shelf.ps1` and `web-apps.ps1`: **PASS**.
- `quick-ring.ps1` failed once, only on its idle check: 125 ms CPU in 10 s, measured 0.9 s after the test restored the main window, so it included that window's layout save and redraw.
  - The script now waits 3 s before measuring.
  - Two re-runs were **PASS, 0 ms CPU in 10 s**.
- NOT RUN:
  - a physical MX Master with Logi Options+ (no device in this session): the guide's button names and Options+ menu wording still need checking against the installed Options+ version;
  - the Copy buttons;
  - the Summon-conflict fix button;
  - Quick Shelf and Quick Ring modes through the dialog.

### Remaining V2.1 work (in order)

1. Optional embedded Web workspace tab (WebView2, lazy, measured; Mongoku first) per `WEB_SURFACES.md`.
2. Mongoku read-only report card (`GET /api/datapass/reports/{id}`, on demand, credential outside JSON) once a deployment is chosen.
3. Native acceptance with a physical MX Master + Logi Options+, multi-monitor/mixed-DPI, and the user-assisted checklists above.
