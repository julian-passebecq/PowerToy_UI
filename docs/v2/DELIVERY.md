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

## V2.1 - slice 7 (embedded web apps: WebView2 "Web" tab)

Date: 2026-09-25. Author: Claude Code (Windows laptop). Code commit: `c4caf56`, parent `e87e398`.

### Changed

- New web-app mode **Embedded in Power Ops**. The Mongoku preset defaults to it; the Gemini, ChatGPT and Claude presets stay in app windows because they rely on the browser sign-in.
- An embedded app opens in a new **Web** module tab (`EmbeddedWebPolicy.ShowInWorkspace`), which is stored in `shell-workspaces.json`. An existing tab is reused, and when the tab limit is reached the active tab is retargeted.
- `EmbeddedWebHost` is the only type that references WebView2, and it is created on the first embedded open. Until then no WebView2 assembly is loaded, no process starts and no profile folder exists.
  - One environment is shared, with its profile in `<data-dir>/webview2`.
  - There is one view per embedded app open in the current workspace. A view is disposed when its tab closes, when the workspace changes, or on **Close web view**, and its processes then exit.
- Hardening:
  - http(s) navigation only;
  - pop-ups and `target=_blank` open in the default browser;
  - all permission requests are denied;
  - no host objects and no web messages;
  - password saving and autofill are off;
  - devtools are off.

  Content export never includes browser data.
- Toolbar: back, forward, reload, **Open outside** (app window or browser), **Close web view**. The status line shows loading, errors, and "waiting for server".
- Control choice: the standard `WebView2` (HwndHost) control. `WebView2CompositionControl` needs the Windows SDK projection (`Microsoft.Windows.SDK.NET`) and **crashed Power Ops in layout** without it. That was caught by the native test and replaced. Airspace does not matter here, because nothing overlays the module area in-window.
- **Inherited accessibility defect fixed:** the module `TabControl` template exposed no module content to UI Automation or screen readers. It now names `PART_SelectedContentHost` and has a collapsed items host, with no visual change.
- First NuGet dependency: `Microsoft.Web.WebView2` 1.0.4191.47.

### Evidence (tested revision `c4caf56`)

- `.\scripts\build.ps1`: **PASS**. 0 warnings, SmokeTests 68/68, WorkspaceTests **57/57** (3 new: reusable Web tab and valid shell state, live-view policy, navigation and pop-up policy plus export exclusion).
- `tests/native/web-embedded.ps1` (stand-in page with a localStorage load counter; input is only a global shortcut plus UI Automation Invoke): **PASS, 17/17**. It observed:
  - lazy: 0 WebView2 processes, no WebView2 modules loaded and no profile folder before first use;
  - the shortcut opens the page in a Power Ops Web tab; the request came from the WebView2 user agent; the tab is persisted;
  - reload keeps localStorage;
  - moving to another tab and back keeps the live page (no reload);
  - **Close web view ends every WebView2 process**;
  - reopening and restarting keep state, and after a restart it stays lazy until opened.
- Memory (working set; the sum over processes overstates because of shared pages), measured at `c4caf56`:

  | State | Power Ops | WebView2 | Total |
  | --- | --- | --- | --- |
  | Never opened | 241.8 MB | 0 processes | 241.8 MB |
  | One embedded page open | 239.8 MB | 6 processes, 372.4 MB | 612.1 MB |
  | After Close web view | 230.9 MB | 0 processes | 230.9 MB |
  | Restarted, not reopened | 221.2 MB | 0 processes | 221.2 MB |

  **Decision:** keep it as opt-in. It costs nothing until used and is fully released on close. It is not suitable for many always-open pages, which matches the policy.
- An IPv6-only (`::1`) local server, like Mongoku's dev server, loads fine embedded.
- Real Mongoku: the user's other session had Mongoku `datapass/control-plane-v1` running at `http://localhost:3100`, reporting `mongo-read-only` with `writesEnabled: false`.
  - A first plain GET returned the home page (HTTP 200, 372 KB, "Datapass Mongo Control").
  - Minutes later its HTML route stopped answering, with requests timing out after 15 s and 60 s, while `/api/health` still answered in 73 ms. The embedded view therefore stayed on `about:blank` waiting, which is correct behaviour; the status text now says it is waiting.
  - Power Ops sent no further requests. That session was actively editing Mongoku, and it cannot be ruled out that the few test page loads contributed.
  - Re-run requested; see the follow-up below. (Original instruction: re-run `tests/native/web-embedded.ps1 -RealUrl http://localhost:3100/` when its page responds.)
- **Follow-up 2026-09-25 01:10, real Mongoku: PASS.** After the user updated Mongoku's `.env`, the server came back with `mode: mongo-read-only` and `writesEnabled: false` (home page HTTP 200, 464 KB in 597 ms). `tests/native/web-embedded.ps1 -RealUrl http://localhost:3100/` at `1a08740` (same app code as `c4caf56`) passed **10/10**:
  - lazy before use;
  - the page loaded inside a Power Ops Web tab with title **Mongoku · Datapass Mongo Control**;
  - Power Ops in front; tab persisted; profile in the data folder;
  - Close web view ended all WebView2 processes; clean exit.

  Memory: 236.6 MB never opened, then **702.1 MB** with Mongoku open (6 WebView2 processes, 465 MB; the real app is heavier than the stand-in), then 229.3 MB after closing. Mongoku still reported `writesEnabled: false` after the test. Nothing inside Mongoku was clicked, and no MongoDB user or credential is involved: Power Ops only loads Mongoku's web UI.
- On the same revision, `interaction`, `quick-actions-hotkeys`, `quick-shelf`, `web-apps` and `quick-ring`: all **PASS**.
- NOT RUN:
  - a pop-up opening the default browser (it would open a tab in the user's browser);
  - blocked non-http navigation from inside a page (covered by unit tests);
  - downloads and file pickers;
  - WebView2 runtime missing (message path only);
  - multi-monitor/DPI.

## V2.1 - slice 8 (Mongoku report cards on the Launchpad)

Date: 2026-09-25. Author: Claude Code (Windows laptop). Code commit: `82032cf`, parent `bff596f`.

### Changed

- **Mongoku reports** section at the top of the Launchpad, one card per configured saved report.
  - Cards are stored in a separate bounded `report-cards.json`: format `powerops-report-cards` v1, at most 8 cards, an http(s) source without credentials, and report ids matching `^[A-Z0-9_]{1,64}$`. Loading fails closed and never overwrites existing bytes.
- **On demand only:** nothing is fetched until **Refresh**. The HTTP handler is created on the first Refresh; it follows no redirects and sends no cookies or credentials, with a 15 s timeout and a streaming 4 MB cap.
- Only `GET {source}/api/datapass/reports/{id}` is used; there is no MongoDB access. The editor lists reports from `GET /api/datapass/workspace` on demand (ids, titles and descriptions only).
- Cards show section **states and row counts only, never row contents**:
  - each state has an explanation (source unbound, truncated, registry unavailable/ambiguous, namespace unresolved);
  - they also show Mongoku's `generatedAt`, its read-only flag and the fetch time;
  - the worst section sets the card colour; a report with no sections is Unknown, never OK;
  - results stay in memory only.
- Failures are explained: not reachable, no answer within 15 s, sign-in required (401/403), redirected (likely a sign-in page), unknown report (Mongoku's own error text), no report API (404), response too large.
- **Open in Mongoku** deep-links to `/foil/report/{id}` (FOIL_*) or `/projects` (GLOBAL_PROJECTS). It uses the same mode as the user's Mongoku web app, including the embedded tab, which navigates straight to the report page.
- Accessibility: card buttons are named "Refresh {title}" / "Open {title} in Mongoku", with `_` spoken as a space (WPF treats the first underscore as an access key). A new card takes the report's title by default.

### Evidence (tested revision `82032cf`)

- `.\scripts\build.ps1`: **PASS**. 0 warnings, SmokeTests 68/68, WorkspaceTests **62/62**. The 5 new tests use a fake HTTP server:
  - parsing, where rows are never kept and missing data stays unknown;
  - validation, the store and preserved corrupt bytes;
  - URIs and deep links;
  - every failure message, including the size cap with and without Content-Length;
  - the report list from the workspace.
- `tests/native/report-cards.ps1` against the live local Mongoku (`datapass/control-plane-v1`, `mongo-read-only`, `writesEnabled: false`; the script refuses to run otherwise): **PASS, 16/16**. Only UI Automation Invoke was used. It observed:
  - four cards "Not loaded yet", and **no TCP connection from Power Ops to Mongoku before Refresh**;
  - FOIL status now shows both sections with the same row counts as the API and "2 unavailable · 0 complete" (both sections SOURCE_UNBOUND in this Mongoku);
  - Global projects shows 7 sections, and its 1 truncated section is stated as "more rows exist than the report limit";
  - no row contents displayed;
  - an unknown report shows Mongoku's "Unknown report: NOPE_UNKNOWN_REPORT";
  - a stopped source shows "Mongoku is not reachable at http://localhost:1";
  - **Open in Mongoku** landed the embedded Mongoku tab on `http://localhost:3100/foil/report/FOIL_STATUS_NOW`;
  - `report-cards.json` is unchanged and no report data is written;
  - clean exit, and Mongoku still `writesEnabled: false`.
- The same revision re-ran `web-embedded`, `interaction`, `quick-actions-hotkeys`, `web-apps` and `quick-ring`: all **PASS**.
- `quick-shelf` failed 2 foreground checks once ("click again" and "Esc focus return") and then passed twice in a row. `QuickShelfWindow.cs` only differs from the 4/4 revision by a type rename. This is the known foreground-lock sensitivity when the desktop is in use.
- NOT RUN:
  - a Mongoku with basic auth or OIDC (the sign-in message path is unit-tested only);
  - a remote Mongoku over https;
  - the card editor dialog end-to-end (UI Automation of the dialog was not scripted).
## V2.1 - slice 9 (sign-in for a protected Mongoku)

Date: 2026-09-25. Author: Claude Code (Windows laptop). Code commit: `21ce53e`, parent `31d7c93`.

### Changed

- Report cards and the embedded Mongoku tab work with a Mongoku protected by `MONGOKU_AUTH_BASIC`. Mongoku answers 401 with `WWW-Authenticate: Basic`, as verified in `src/hooks.server.ts`.
- The user name and password are stored per Mongoku origin in the **Windows Credential Manager** as a generic credential `PowerOps/Mongoku/<origin>`, per Windows user.
  - They are never written to Power Ops JSON, logs, messages or exports, and `BasicCredential.ToString()` hides the password.
  - The unmanaged copy used for writing is wiped.
- **Sent only when safe:** only to that exact origin, only over https or to this computer (localhost / 127.0.0.1 / ::1). A plain-http LAN address is refused before any request is made. The credential goes on a single request with redirects disabled, so it cannot follow a redirect elsewhere.
- **Embedded tab:** it answers WebView2's basic-auth challenge once per navigation, for the app's own origin only. If the sign-in is rejected, WebView2's own prompt appears instead of looping.
- **401 messages:**
  - "asks for a user name and password" (Basic challenge, nothing saved);
  - "rejected the saved user name or password";
  - "uses web sign-in (OIDC)" (a 401 without a Basic challenge, which is Mongoku's OIDC API mode). Cards cannot use OIDC, so the message says to open the report in Mongoku.
- **Manage report cards → Sign-in:** shows the status for the entered address (saved user, never the password), with **Save user and password...** (a PasswordBox dialog) and **Forget sign-in**.
- Accessibility: the status text is its own accessible name.

### Evidence (tested revision `21ce53e`)

- `.\scripts\build.ps1`: **PASS**. 0 warnings, SmokeTests 68/68, WorkspaceTests **64/64**. The 2 new tests cover:
  - origin targets, the https/loopback-only rule, validation, and the password never being printed;
  - sign-in through a fake server: no header without a saved sign-in, rejected vs accepted, the report list using the sign-in, **no request at all** for a LAN http address, and OIDC recognised.
- `tests/native/report-auth.ps1`: **PASS, 14/14, in three consecutive runs**. It uses a local server that answers like `MONGOKU_AUTH_BASIC`; the real Mongoku's auth configuration was not changed. It uses UI Automation patterns only and drives both dialogs, including the PasswordBox. It observed:
  - before saving, the card explains what to do and **no Authorization header is ever sent**;
  - saving through the dialogs stores the entry in Credential Manager for exactly that origin, and the dialog shows the user, not the password;
  - Refresh signs in and the server sees the correct header;
  - **the embedded Mongoku tab opens the protected page without a prompt**;
  - a wrong saved password is reported as rejected;
  - no password appears in any Power Ops data file;
  - Forget removes the entry, and the test entry is always removed at the end.
- Test hygiene: an early run's cleanup used the `cmdkey` tool, which did not reliably find these generic entries, and left two dummy test entries (`PowerOps/Mongoku/http://localhost:46425` and `:44854`). Both were deleted with `CredDelete`. The script now uses the same Credential Manager API as Power Ops, and `PowerOps/*` enumerates to 0 entries.
- Re-run on the same revision: `report-cards` (live read-only Mongoku), `interaction`, `quick-actions-hotkeys`, `quick-shelf`, `web-apps` and `quick-ring` all **PASS**. `web-embedded` failed once only on "Power Ops in front" (foreground lock) and then passed.
- NOT RUN: a real Mongoku started with `MONGOKU_AUTH_BASIC`; an https Mongoku; OIDC (only the message is covered).

### Mongoku session review (read-only, 2026-09-25)

- The Mongoku handoff session merged PR #1 (`aa8ce9a`), and PR #2 (the Mongo cold-start fix) is also on `master`.
- Its connected smoke on the real cluster passed: 27 routes returned 200, and `PUT` returned 403.
- Mongoku now fails in about 5 s instead of hanging 30-90 s when Mongo is unreachable. That was the cause of the earlier "not verified" embedded run.
- **Attention items from that session**, still open:
  - Mongoku currently connects with the **`atlasAdmin`** account;
  - `.mongoku.db` holds the URI with the password in plain text (gitignored);
  - the recommended follow-up is a `mongoku_readonly` user with `read` on `dataprojects_control`.

  Power Ops needs neither: it uses Mongoku's HTTP API and web UI only.
## Correction notice from the Mongoku session (2026-09-25, received by Power Ops)

- **Earlier live-Mongoku evidence ran against an admin-connected Mongoku.** This covers the slice 7 follow-up (embedded Mongoku PASS) and the slice 8 report cards (16/16). Mongoku loaded its connections from its local `.mongoku.db` cache and ignored `MONGOKU_DEFAULT_HOST`; that cache held the admin account.
  - Writes were blocked only by Mongoku's code (`mongo-read-only`, `writesEnabled: false`), not by the database user.
  - The Power Ops observations themselves stand: only read-only GET pages and report APIs were used, and nothing in Mongoku was clicked.
  - The "Mongoku still read-only afterwards" checks must be read as *application-level* read-only.
- **Mongoku is stopped** until the user approves its restart in the Mongoku session. Power Ops must not start it.
  - After the restart, Mongoku uses dedicated read-only database users for 5 sources, with the same health contract.
  - Power Ops still needs no Mongo URI, credential or Atlas ID.
- **Mongoku PRs #2 to #4:**
  - cold-start "not bound" fixed;
  - `displayStatus` values in reports changed;
  - FOIL root: prefer `/?project=foil_project`.

  **Power Ops impact: none in code.** Report cards read only `meta.state` / `returnedRows` / `truncated`, never `displayStatus`/`rawStatus`. The deep link `/foil/report/{id}` is not listed as changed and will be re-checked.
- **To do after the user confirms the restart:** re-run `tests/native/web-embedded.ps1 -RealUrl http://localhost:3100/` and `tests/native/report-cards.ps1`, checking health before and after as before. Expect FOIL sections to be bound now.
## Full re-run after Mongoku moved to read-only database users (2026-09-25 02:25)

Tested revision `095fcdd`, same app code as `21ce53e`. Mongoku was restarted by its own session with dedicated read-only DB users for 5 sources. Health before and after: `ok`, `mongo-read-only`, `readOnly: true`, `writesEnabled: false`.

- `.\scripts\build.ps1`: **PASS**. 0 warnings, SmokeTests 68/68, WorkspaceTests 64/64.
- `web-embedded.ps1 -RealUrl http://localhost:3100/`: **PASS**. "Mongoku · Datapass Mongo Control" loaded embedded. Memory was 222 MB before, 688 MB open (6 WebView2 processes) and 219 MB after closing.
- `report-cards.ps1` (live Mongoku): **PASS 16/16**.
  - **FOIL status now: "All 2 sections complete"**: PM scorecards 27 rows, P0 attention 45 rows. The FOIL PM source is now bound (before: 2 × SOURCE_UNBOUND).
  - Global projects: 7 sections, 1 truncated.
  - "Open in Mongoku" lands on `/foil/report/FOIL_STATUS_NOW`.
  - `/foil/report/FOIL_STATUS_NOW`, `/?project=foil_project` and `/projects` all return HTTP 200.
- `report-auth`, `web-embedded` (stand-in), `interaction`, `quick-actions-hotkeys`, `quick-shelf`, `web-apps`, `quick-ring`: **all PASS on the first attempt**.
- The Mongoku `displayStatus` changes (PRs #2-#4) have no effect: Power Ops reads only section `meta`.
- Windows Credential Manager: 0 `PowerOps/*` entries after the runs.

## MX Master / Logi Options+ manual acceptance - prepared, scheduled for the next session

- `docs/v2/MX_MANUAL_ACCEPTANCE.md`: the checklist, covering Options+ setup, mouse use, the mouse-hook warning and optional items.
- `tests/native/mx-acceptance-setup.ps1` + `tests/native/mx-observer.ps1`: a one-command isolated test instance (Hybrid, the four shortcuts, Mongoku slot 8, three tabs) with a hidden observer log.
  - The setup was verified end to end at 02:29: tabs `home | capture | clipboard`, four shortcuts owned by the instance, the observer logging, and everything stopping when the test window closes.
- A first attempt was started at 02:06 and postponed by the user before any result was recorded; there are no MX results yet.
## Mongoku SOURCE_INVENTORY report support (2026-09-25, commit `aa438a8`)

Prompted by a notice from the Mongoku session (PR #7): `GET /api/datapass/reports/SOURCE_INVENTORY` checks the 10 Mongo sources. It is read-only and metadata only; the live response contains no URI, user or host.

- Report cards understand the new section states:
  - `EMPTY` is healthy: reachable, nothing to list;
  - `REGISTERED_UNBOUND` and `SOURCE_ERROR` are unavailable, each explained.
- Sections without a label use their `sourceId`. When sections carry `trace.resolved`, the card shows **"Sources resolved: n/m"**; FOIL status now shows 2/2.
- A dedicated "federation health" widget was suggested by the Mongoku session. It is **not built**: a SOURCE_INVENTORY report card already gives the resolved count and the per-source states. That is a user decision.
- Accessibility: each report card is now a UI Automation **group** (a plain Border has no automation peer), so its name and status reach screen readers.
- Evidence:
  - `.\scripts\build.ps1`: **PASS**, 0 warnings, SmokeTests 68/68, WorkspaceTests **65/65** (1 new test for the inventory states).
  - `report-cards.ps1`: **PASS** against the live read-only Mongoku, including a SOURCE_INVENTORY card showing "Sources resolved: 10/10" and "All 10 sections complete", matching the API.
  - `web-embedded -RealUrl`, `report-auth` (on re-run), `interaction`, `quick-actions-hotkeys` and `web-apps`: **PASS**.
  - `quick-shelf`, `quick-ring` and `web-embedded` (stand-in) failed only on foreground and focus checks during concurrent desktop use. In one run the safety guard **refused to send Esc to a non-Power Ops window**.
  - Their code is unchanged since `52e3364`/`095fcdd`: this commit only touches report-card files and tests. All of them passed on the first attempt at 02:25. They were not re-run further, to avoid taking over the user's desktop.
  - Mongoku health after the runs: `mongo-read-only`, `writesEnabled: false`. No `PowerOps/*` credentials remain.
## Mongoku recheck (2026-09-25 ~03:45)

- **Mongoku:** `master` at `41e9385`, with PRs #1-#8 merged and CI **success** on `41e9385`.
  - Health: `ok`, `mongo-read-only`, `readOnly: true`, `writesEnabled: false`.
  - The Mongoku session reports 10 sources qualified with one `mongoku_readonly` user per Atlas project. The only open item is in FOIL PM data (the AI Reasoning registry entry), not in Mongoku or Power Ops.
- **API contract sweep over all 19 saved reports** (read-only GETs):
  - all return HTTP 200 with `readOnly: true`;
  - **no section state unknown to Power Ops** (`EMPTY` already appears in 2 FOIL reports);
  - largest response 156 KB (card cap 4 MB); slowest 2.7 s, SOURCE_INVENTORY (card timeout 15 s);
  - no URI, password or Atlas host in any response.
- **Defect found and fixed:** "Open in Mongoku" sent non-`FOIL_*` reports such as SOURCE_INVENTORY to the Mongoku root.
  - Live check: `/foil/report/{id}` returns 200 for all 18 other reports, and 404 for GLOBAL_PROJECTS, which lives under `/projects`.
  - `DeepLink` now uses `/projects` for GLOBAL_PROJECTS and `/foil/report/{id}` for everything else. The unit test was updated.
  - `report-cards.ps1` now checks that every card's target exists in the live Mongoku.
- **Evidence:**
  - `.\scripts\build.ps1`: **PASS**, 0 warnings, 68/68 and 65/65.
  - `report-cards.ps1`: **PASS 19/19**, including SOURCE_INVENTORY "Sources resolved: 10/10" and deep links 3/3.
  - `web-embedded.ps1 -RealUrl http://localhost:3100/`: **PASS**, with 222.6 MB before, 688.9 MB open and 219.4 MB after closing.
  - Mongoku was still `writesEnabled: false` after the runs.
## Mongoku PR #9: five FOIL authority reports (commit `76b0676`)

These reports were announced by the Mongoku session: FOIL_AI_REASONING_RECENT, FOIL_IT_DEV_STATUS, FOIL_FRONT_STATUS, FOIL_DATABRICKS_STATUS and FOIL_FABRIC_STATUS. They have the same contract as the other reports and appear in the card editor's report list automatically.

- Cards now show **Mongoku's own report description**. It carries each report's caveats, for example "nothing here is measured evidence" (Databricks lab) and "no live Fabric deployment is claimed". Power Ops adds no FOIL business logic.
- When Mongoku marks rows with an `authorityBoundary` starting with `NON_AUTHORITATIVE` (FOIL AI reasoning), the card shows a **"Non-authoritative content (…), as declared by Mongoku: not a source of truth"** banner. The banner also appears under a custom card title. Only that marker is read from rows; row contents are never kept (unit-tested).
- Evidence:
  - `.\scripts\build.ps1`: **PASS**, 0 warnings, WorkspaceTests **66/66**.
  - `report-cards.ps1` against the live read-only Mongoku: **PASS 22/22**. It covers the AI reasoning card titled "My AI notes" (banner shown), the Databricks card (caveat shown, no banner) and 5/5 deep links.
  - Contract sweep over **all 24 reports**: all HTTP 200 and `readOnly: true`, no unknown section state, no secret-like content, **24/24 `/foil/report/{id}` or `/projects` pages exist**, largest 156 KB, slowest 3.0 s. Only FOIL_AI_REASONING_RECENT carries a non-authoritative marker.
  - Mongoku health after the runs: `mongo-read-only`, `writesEnabled: false`.
- Only report-card files changed, so the other native scripts are unaffected. They were last all green at 02:25 and on the 03:45 recheck.
- The test harness also retries UI Automation walks, because the report panel is rebuilt on each refresh.
## PR #4 qualification (2026-09-25, requested by the user via the Mongoku session)

Qualified revision: `33a6d89`. It adds only a comment and a test user-name rename on top of `a6503d5`. PR #4 is `codex/power-ops-v2-workspaces` → `codex/to-be-tested-v1`: draft, 32+ commits, mergeable CLEAN. **Not merged: the merge decision is the user's.**

Mongoku under test: `master` `8a873bf`/`187473f`, running on `http://localhost:3100` with read-only database users only.
- Health **before**: `ok`, `mongo-read-only`, `readOnly: true`, `writesEnabled: false`. SOURCE_INVENTORY API: 10/10 resolved, all OK.
- Health **after** all runs: unchanged, `writesEnabled: false`.

| Requested check | Result |
| --- | --- |
| 1. Embedded Mongoku (`web-embedded.ps1 -RealUrl http://localhost:3100/`) | **PASS**: "Mongoku · Datapass Mongo Control" loaded. 226 MB before, 686.5 MB open (6 WebView2 processes), 220.6 MB after closing. |
| 2. Report cards vs live API (`report-cards.ps1`) | **PASS**: SOURCE_INVENTORY "Sources resolved: 10/10" (API 10/10), Refresh, Open in Mongoku (embedded, `/foil/report/FOIL_STATUS_NOW`), 5/5 deep links, AI-reasoning non-authoritative banner, Databricks caveat. |
| 2b. Stored sign-in in Windows Credential Manager (`report-auth.ps1`) | **PASS** against a local basic-auth stand-in. The real Mongoku runs without HTTP auth. |
| 3. `displayStatus` dependencies | **None**: 0 uses of `displayStatus`/`rawStatus` in `src`. Cards read only `meta.state`, `returnedRows`, `truncated`, `trace.resolved` and the `authorityBoundary` marker. |
| 4. No Mongo URI / Atlas ID / DB credential in Power Ops | **Confirmed**: no Mongo URI, Atlas host or DB account in tracked code (the only hit was a test's example HTTP user name `mongoku_readonly`, renamed to avoid confusion); 0 matches in the 9 test data folders; Power Ops calls only `/api/datapass/reports/*`, `/api/datapass/workspace` and Mongoku pages; 0 `PowerOps/*` credentials left. |
| 5. Full suite | `.\scripts\build.ps1` **PASS** (0 warnings, SmokeTests 68/68, WorkspaceTests 66/66). Native scripts: `web-embedded` (real and stand-in), `report-cards`, `report-auth`, `web-apps`, `interaction`, `quick-actions-hotkeys` and `quick-ring` **PASS**. `quick-shelf` failed 2 synthesized-hover checks once (pointer moved during the run) and **PASS**ed on re-run; `QuickShelfWindow.cs` is unchanged since `19738ad`. Windows CI: see the commit on this branch. |

**Scope notes for the merge decision:**
- The generic display of the Mongoku PR #9 reports (description + non-authoritative banner, `76b0676`) is already on this branch. The Mongoku session considered adding those reports as cards to be a follow-up after PR #4. Keep it, or ask for it to be split out.
- PR #4 targets `codex/to-be-tested-v1` (`df3aa16`), not `main`. `main` has 2 commits (the V1 promotion) that this branch does not contain, so merging PR #4 does not update `main`.
- **Still outstanding (user-assisted):** the MX Master / Logi Options+ manual acceptance (`docs/v2/MX_MANUAL_ACCEPTANCE.md`), scheduled for the next session, plus the optional multi-monitor checks.
### Remaining V2.1 work (in order)

1. Next session: MX Master / Logi Options+ manual acceptance with the user (`docs/v2/MX_MANUAL_ACCEPTANCE.md`), plus multi-monitor if a second screen is available.

## Mongoku MAINTENANCE card (2026-09-25, commit `17f1125`)

Requested from the Mongoku session after Mongoku PR #12 added the global read-only `MAINTENANCE` report (`GET /api/datapass/reports/MAINTENANCE`, page `/maintenance`). Mongoku under test: `master` `6f26e71`, started locally with `vite dev --port 3100` (the `pnpm dev` script; pnpm is not installed on this machine), `mongo-read-only`, `writesEnabled: false` before and after.

### Changed
- `Core/Reports/MaintenanceReport.cs`: parses the MAINTENANCE summary row (summary line, top `nextAction`, `sourcesReachable`, the five counts, `backups`) and the rows whose `actionKind` is not `none` (title, summary, next-action label, link only). A row's `openUri` is resolved against the card's Mongoku address and accepted only as a same-origin path under it; anything else falls back to `/maintenance`. Repository names, heads and other row fields are not kept.
- `ReportCards.Parse` attaches that digest only for `MAINTENANCE`; `DeepLink` maps it to `/maintenance`. The same fetch (`FetchAsync`, same handler, same limits) is used; no new HTTP client.
- Launchpad card: summary line as the status, "Next: ...", one counts line, up to 6 rows as links plus "+N more to act on in Mongoku", card title as a link. Links open like "Open in Mongoku" (the user's Mongoku web-app mode: embedded tab, app window or browser). A header button "+ Maintenance card" adds the card when none exists; nothing is fetched until Refresh.
- Unavailable: a section with `trace.resolved: false` (or an unavailable state) is listed as unavailable, and its count reads "unavailable" instead of a number and its rows are hidden. If the summary itself is unavailable, every count reads "unavailable". A stopped Mongoku shows "Maintenance unavailable: Mongoku is not reachable at ...", and the other cards and modules are unaffected.
- No action buttons, agent launch, scheduler or credential field. The report carries no secrets and nothing from it is logged or persisted.

### Evidence (tested revision `17f1125`)
- `.\scripts\build.ps1`: **PASS** (0 errors; smoke tests passed; WorkspaceTests **86/86**, 4 new: parsing, link containment, unresolved/summary-missing/wrongly typed input, fake-server down and read-only GET without credentials).
- `tests/native/maintenance-card.ps1` against the live read-only Mongoku: **PASS 19/19**. The card showed exactly the API's summary line, next action ("Atlas family: Record a verification at the next review") and counts ("Sources 10/10 · projects 8 · heads 0 · projections 1 · audits 4 · reconciliation 0 · backups not recorded"), 6 row links + "+7 more" (13 rows to act on), no repository names or heads. The first row link opened `http://localhost:3100/?project=atlas` in the embedded Mongoku tab. A card on a stopped Mongoku showed "Maintenance unavailable", while the neighbouring card still refreshed.
- `tests/native/report-cards.ps1` (regression, the "Open in Mongoku" helper was generalised): **PASS 22/22**.
- Live values seen: 10/10 sources reachable, 8 of 31 projects to act on, 5 consistent heads, AtlasNote projection not published, 4 partial audits, 0 reconciliation findings. No section was unresolved, so the unresolved path is covered by unit tests only.

### Embedded Mongoku re-run after Mongoku's 5 s `serverSelectionTimeoutMS` fix
`tests/native/web-embedded.ps1 -RealUrl http://localhost:3100/` on `9323610` (the app code under the card change):
- Run 1: **9/10** in 26.2 s. The page loaded ("Mongoku · Datapass Mongo Control"); only "open: Power Ops in front" failed (Windows foreground lock while another app had focus).
- Run 2: **PASS 10/10** in 25.3 s.
- **No 30-90 s hang** in either run. Memory: 224 MB idle, 679 MB with Mongoku open (6 WebView2 processes, 467 MB), 216 MB after closing.
- Cold Mongoku: the first MAINTENANCE API call after `vite dev` started took 11.2 s (on-demand compile), and later calls took about 3 s, within the 15 s card timeout.

## V2.3 - File tray, slice 1 (received files → AI chats)

Date: 2026-09-25. Author: Claude Code (Windows laptop). Branch `claude/file-tray` → `codex/power-ops-v2-workspaces`. Tested revision: `f0c6313` (the feature merged with #12 Claude Control).

WhatsApp Desktop, Messenger and mail clients have no personal API, but they all save attachments to Downloads or a chosen folder. The tray watches those folders and puts the newest files one drag or one Ctrl+V away from ChatGPT, Claude or Gemini. No integration with those apps, no WebView2, no credentials.

### Changed

- **Core `Files/`** (package-free, unit-tested):
  - `FileTrayFilter`: pdf, png, jpg/jpeg, webp, gif, heic, docx, txt. Unfinished downloads (`.crdownload`, `.part`, `.partial`, `.tmp`, `.download`, `.opdownload`) and Office lock files are never shown; hidden, system and empty files are skipped.
  - `FileTrayList`: newest arrival first, one entry per file (case-insensitive full path), bounded to N (default 20, 1-100). A repeated event for an unchanged file keeps its place; "Remove" hides a file for the session until it is written again; a moved file keeps its place.
  - `FileTrayReadiness`: a file is ready only when it is non-empty and no other process has it open for writing. Re-checks are one-shot at 0.25 s doubling to 8 s, and stop after about 2 minutes (a later rename or write starts again).
  - `FileTrayService`: one `FileSystemWatcher` per folder (top level only). Events are filtered by name before any disk access, so a download in progress costs a string check. One one-shot timer exists only while a file is pending; a watcher overflow triggers one rescan. Watchers start first, then one scan of the newest files.
  - `FileTraySettings` + store: separate `file-tray.json` (format `powerops-file-tray` v1, 64 KiB cap): on/off, watch Downloads, up to 8 extra folders with labels, N, and the remembered folder per Repository Hub project. Missing file = defaults, nothing written; malformed or future files fail closed and are never overwritten. **No file names, contents or thumbnails are saved.** Business schema v7 and shell schema 1 are unchanged.
  - `FileTrayText` (txt with BOM detection; docx paragraph text, DTDs refused) and `FileTrayPrompt` (`{{file}}`, `{{file_text}}`).
- **Lazy by default:** nothing is watched until a tray surface is shown or a tray action runs in the session. The settings can turn the tray off entirely (no watcher, no timer).
- **Three surfaces, one list:** the **File tray** module (Knowledge & capture; filters by folder and type, search), a **File tray** section at the top of the **Sidebar** (5 newest), and a flyout opened by the new **`tray.show` Quick Shelf entry** (8 newest). The flyout is a no-activate tool window like the Shelf, so Copy then Ctrl+V works without clicking back into the browser.
- Each row shows an Explorer thumbnail (`IShellItemImageFactory`: the file-type icon when no thumbnail handler exists, e.g. PDFs without a PDF previewer), name, source folder, age, type and size. Thumbnails are made only for rows being shown, on a short-lived STA thread that exits when done, and kept in memory while the file stays in the tray.
- **Actions, all catalog IDs run through `QuickActionDispatcher`** (the row is the target; from a shortcut, the Shelf, the Ring or the Actions → File tray submenu they act on the newest file). All are `Safe` and `GlobalAllowed`:
  - `tray.copyFile` / `tray.copyLatest` "Copy last received file": CF_HDROP plus `Preferred DropEffect = Copy` (pasting in Explorer copies; the file never leaves Downloads).
  - `tray.copyText`: PDF text layer (PdfPig); **Windows OCR** (`Windows.Media.Ocr`, offline, the user's Windows languages) for images and for PDFs without a text layer (first 10 pages); docx and txt in Core. Runs only on click; the text goes to the clipboard and is never stored.
  - `tray.copyImage`: image files, or PDF page 1 rendered by `Windows.Data.Pdf`. Publishes exactly `PNG` + one 32-bit `CF_DIB`. (`DataObject.SetImage` published several full-size formats that the process keeps while they are on the clipboard: **+370 MB** for one page, now **+49 MB**, released when the clipboard changes.) HEIC falls back to Windows imaging and explains the HEIF extension when missing.
  - `tray.open`, `tray.reveal` (Explorer `/select`), `tray.move` (pick a Repository Hub project; its folder is chosen once and remembered in `file-tray.json`; moves, never overwrites: `name (2).ext`), `tray.remove` (hides; **never deletes a file**), `tray.show`.
  - **Drag-out** is a direct gesture on the row (WPF `DoDragDrop` with a FileDrop), not a catalog action, because it cannot run from a shortcut.
- **Prompt Builder:** `{{file}}` (name) and `{{file_text}}` (text, read on Preview/Copy only when a module uses it), a "Tray file" picker (default: newest), Insert buttons, and "Use in Prompt Builder" on each row.
- **Export:** the `tray` module exports one observation ("never exported"); `file-tray.json` is in no export, even with local details.
- `tray.show` is in the **default** Shelf layout (new `quick-actions.json` files only; existing layouts are unchanged). `tray.copyLatest` can be bound in Actions → Global shortcuts (global shortcuts stay off by default).
- **Build:** the app now targets `net8.0-windows10.0.19041.0` for the Windows SDK projection (OCR, PDF rendering). The output folder moved to `bin\Release\net8.0-windows10.0.19041.0\`; every native script and `MX_MANUAL_ACCEPTANCE.md` were updated. The CsWinRT AOT optimizer is disabled because its module initializer loaded `WinRT.Runtime` at startup. Second NuGet dependency: `PdfPig` 0.1.16 (Apache-2.0, no dependencies on net8.0). Output size: +23.6 MB (SDK projection) and +5.5 MB (PdfPig), loaded only on first use.
- Accessibility: rows are UI Automation groups named "file, source, age"; buttons are named "Action: file"; status lines are live regions and keep their text as their accessible name.

### Evidence (tested revision `f0c6313`)

- `.\scripts\build.ps1`: **PASS**. Release build 0 warnings, 0 errors; smoke tests all passed; WorkspaceTests **102/102** (11 new tray tests + 5 from #12). The tray tests cover the type filter and partial downloads, ordering and bounds, dedupe and dismiss, readiness and the retry schedule, a real `FileSystemWatcher` on a temporary folder (drop, `.crdownload` rename, Firefox `.part` rename, locked file, delete, nothing pending when idle), the first scan (hidden/empty/partial/unsupported/subfolders skipped, folders deduped), settings validation and fail-closed storage, docx/txt text, prompt placeholders, export exclusion and old shell files, and the dispatcher/Shelf/Ring/shortcut rules.
- Mutation check: disabling the partial-download filter, the unchanged-file rule or the lock check each made the expected tests fail; restored code passes.
- `tests/native/file-tray.ps1` (Windows 11 Pro 10.0.26200, 150% scaling; isolated `--data-dir`; a temporary watched folder with **Downloads watching turned off**; synthetic fixtures generated by the script): **PASS 34/34** on `f0c6313`. The four runs before the merge, on the same tray code, also passed every app check; one of them hit a test-side false positive (the digits `5519` inside a timestamp), since fixed. Input is UI Automation plus two global shortcuts. It observed:
  - no OCR, PDF or WinRT component loaded at startup; the first scan lists an existing file in about 1.5-2.4 s from opening the module;
  - dropped files appear newest first; a `.crdownload` is not listed until renamed; a file held open for writing appears only after its writer closes it; N=6 drops the oldest;
  - Copy as file: CF_HDROP with the exact path and DropEffect Copy;
  - Copy text: PDF text layer (0.6-1.7 s, no OCR loaded), Word (0.4-1.0 s), PNG by Windows OCR ("RECEIPT NUMBER 5519", 0.5-1.9 s, OCR loaded only now), scanned PDF by OCR fallback (0.6-1.8 s);
  - Copy as image: page 1 as PNG + DIB (1680x2174);
  - the global shortcut copies the newest file; Remove hides a file and keeps it on disk;
  - the Shelf's File tray button opens the flyout **without taking focus**, and the flyout's Copy works; Prompt Builder fills `{{file}}` and `{{file_text}}`; the Sidebar shows the newest files;
  - no fixture name or content in any data file; clean exits;
  - after a restart, still lazy; Move to project folder uses the remembered folder, moves without overwriting, and keeps the row as "Moved to CloudArchi".
- Idle cost (30 s windows, same run, desktop in use; compare slice 3: Off 0 ms / 246.9 MB, Shelf visible 0 ms / 247.6 MB):

  | State | CPU / 30 s | Working set | Private |
  | --- | --- | --- | --- |
  | Shelf visible, tray never opened | 0-62 ms | 201-215 MB | 196-228 MB |
  | Tray open, watching, 6 files, before any OCR | 0-16 ms | 220-226 MB | 187-198 MB |
  | After OCR, PDF text and a PDF page image | 0-62 ms (one 531 ms burst in one run) | 305-365 MB | 298-385 MB (the upper values are from runs before the clipboard-format fix) |

  A 90 s sample after OCR + page image read 0, 0, 0, 31, 16, 16, 16, 0, 16 ms per 10 s: isolated timer ticks, no continuous work. Per step (`memdiag`): opening the tray +5 MB working set; PdfPig about +3 MB; the first Windows OCR **+85 MB private**, which stays for the session (the OCR engine) but does not grow on reuse; a page image on the clipboard +49 MB until the clipboard changes. This is a sanity check, not a benchmark.
- Regression re-runs after the TFM change:
  - `web-embedded.ps1` (stand-in page): every WebView2 check passed (lazy start, load, persisted localStorage, processes end on Close web view, lazy after restart). "open: Power Ops in front" failed in both runs, and **failed the same way on the base build `0d717a7` minutes later**: foreground lock while the desktop was in use, not a regression. Not re-confirmed on an idle desktop.
  - `interaction.ps1`: failed the ring-shortcut check once (another Power Ops instance was running with its own Quick Ring window and bindings), then **PASS**. The base build also passed.
  - Not re-run: `quick-shelf`, `quick-ring`, `quick-actions-hotkeys`, `web-apps` (synthesized focus and input; the desktop was in use), `report-cards`, `report-auth`, `maintenance-card`, `claude-control` (live services). Their code is unchanged apart from the executable path.

### NOT RUN (physical or user-assisted)

1. **Drag-out into a real chat** (ChatGPT, Claude, Gemini upload zones) and **Ctrl+V of a copied file** in those sites: needs the user's signed-in browser. Whether a site accepts a pasted CF_HDROP file is up to the site.
2. The scripted drag (`file-tray.ps1 -Drag`, onto a throwaway drop-target window): the safety guard refused on every attempt because another application's window covered Power Ops. Run it on an idle desktop.
3. Real WhatsApp Desktop and Messenger saves, and the real Downloads folder (the test turns Downloads off on purpose).
4. HEIC (depends on the Windows HEIF extension), WebP/GIF thumbnails and OCR in languages other than the user's profile languages.
5. The folder pickers (Settings → Add folder, Move → Choose folder): the Windows folder dialog is not scripted. The move test seeded the remembered folder instead.
6. Pasting a copied file in Explorer; screen-reader walkthrough; flyout placement on multiple monitors or mixed DPI; flyout keyboard use (it is no-activate, so it has none: use the module or the Sidebar).

### Not in this slice

Email/OTP codes (planned slice 2: read-only Gmail IMAP IDLE with an app password in Windows Credential Manager, codes in memory only, expiring after about 2 minutes, excluded from clipboard history). Embedding Gmail or WhatsApp in WebView2 was rejected (+400-700 MB while open, and it conflicts with the Web tab hardening).


## MX Master / Logi Options+ manual acceptance - partial run (2026-09-25 18:55-22:10)

Observer: the user (physical steps) plus `tests/native/mx-observer.ps1` (logs `observer.log`, `observer2.log`, `observer3.log` in `%TEMP%\powerops-mx-acceptance-20260925-1855`). Tested revision: **`0d717a7`** (branch head at the time, PR #12 included; build.ps1 PASS 68 smoke + 91/91). Hardware: **MX Master 4** (not the MX Master 3S the guide was written for), Logi Options+ 2.7.961922.

| # | Result | Evidence / notes |
| --- | --- | --- |
| A1 | OK | Hybrid selected, four shortcuts (active), MX guide expanded. |
| A2 | OK | Clipboard contained `Ctrl+Alt+Shift+R`. |
| B1-B2 | OK (with a different button) | The user mapped the ring to the MX Master 4 **Sense Panel** (the thumb haptic area Options+ labels "Show Actions Ring") via Gestures > Custom, instead of the small gesture button, which stays free for VS Code. The B2 recording conflict did **not** occur (no need to pause the Power Ops shortcuts). |
| B3 | KO (setup) | Options+ "Add application" did not list `JUtilityPalette.exe`. Fixed in V2.4 below: Back/Forward now switch tabs natively, no Options+ app-specific setting needed. |
| B4 | Noted | Options+ names: "Buttons", "Gestures" (presets "Virtual desktops", custom), "HOLD + MOVE UP/DOWN/LEFT/RIGHT", "CLICK", "Show Actions Ring", "+ ADD APPLICATION". The in-app guide said "Gesture button"; V2.4 now names the Sense Panel. |
| C1 | OK | `quick ring = visible, centre (1282,758), pointer (1278,757)`; also on the second monitor: `centre (3262,-555), pointer (3262,-555)`. |
| C2-C4 | OK | User-observed (snip overlay opens and Esc/cancel does nothing, Esc returns, outside click runs nothing). |
| C5 | OK | Hold + up opens the **Quick capture window** (V2.2 behaviour; the checklist still said "Capture tab"); saved: `captures saved = 2`. Also clears the V2.2 BLOCKED item "Quick capture from a global shortcut while another app is focused". |
| C6 | OK | Hold + down shows the Clipboard library. |
| C7-C8 | Not run | Blocked by B3; covered for V2.4 by `quick-ring-v2.ps1` (synthesized XButton1/2). |
| C9-C11, D1-D3 | Not run | The user stopped the session to move to the ring redesign; to be done at the end of the next pass. |

Other observations: the test instance exited once at 22:07:22 with no crash event in the Windows Application log (most likely closed by the user; not reproduced). The observer stops by design after 3 hours.

User feedback that drove V2.4: the gestures are more useful as Windows commands (Task view, desktop, snap left/right) than as Power Ops actions; the flat ring is too crowded, a second ring (Folders, Apps) is wanted; a whole-screen capture straight to the clipboard; a mouse button mapped to Esc should close Power Ops; Gmail, VS Code, Downloads, Desktop from the ring.

## V2.4 Quick Ring v2 (sub-rings, whole-screen capture, Esc, mouse Back/Forward)

### Changed

- **Sub-rings.** A ring slot can be a `group:<id>` action ("Folders ›", "Apps ›"). Clicking it or pressing its digit swaps the ring to the group's actions in the same window; the centre becomes Back, Esc/Backspace go back, a second Esc closes. Sub-ring slots are tinted. Groups are stored in `quick-actions.json` as `RingGroups` (additive; format stays `powerops-quick-actions` v1; older files load unchanged with no groups). Validation: at most 8 groups, 1-8 actions each, no nesting, names 1-40 characters, glyphs in the Segoe private-use range, dangling references rejected. Deleting a web app removes it from groups, and an emptied group disappears with its slot. Bound to a global shortcut or put on the Shelf, a group opens the ring directly on it.
- **Suggested layout** (fresh installs, and a button in Customize Quick Ring): Screenshot (region), Screenshot (whole screen), Quick Capture, Clipboard, Folders › (Downloads, Desktop, Explorer folder, File tray), Apps › (Terminal + configured web apps), Resume workspace. Existing rings are never changed without that button.
- **Actions > Quick Ring sub-rings…**: new, rename, edit actions, delete.
- **`tool:` actions**: every Tool Launcher entry (VS Code, Windows Terminal, a PowerToys editor…) can go on the ring, a sub-ring or the Shelf. Tools are business data, so they are registered on demand (when a surface or editor opens, and before a tool action runs), never on a timer; a removed tool shows as "Tool (not found)" instead of invalidating the file.
- **`capture.screen`**: copies the monitor under the pointer (physical pixels, per-monitor DPI for that call only) to the clipboard, 180 ms after the ring hides, with a 1.6 s non-activating toast. No file is written. `folder.desktop` opens the Desktop folder.
- **Esc** in the main window hides it (Summon) or minimizes it (other modes), after the controls that use Esc themselves (search clear, drop-downs, menus, dialogs). **Mouse Back/Forward** (XButton1/2) switch Power Ops tabs, unless the Summon hook already owns button 4/5. No Options+ application setting needed.
- **MX guide**: names the MX Master 4 Sense Panel, suggests Windows commands for the moves (Win+Tab, Win+D, Win+Left/Right), says Back/Forward need no setup, and tells how to record a shortcut if the ring opens instead.
- Actions menu no longer lists sub-rings (they open a ring, not an action).

### Evidence (tested revision: this PR's head, on top of `cec1e2b`)

- `.\scripts\build.ps1`: **PASS** (Release, all smoke tests, WorkspaceTests **111/111**, 9 new: two-level defaults, old-file compatibility, group validation, group removal cleanup, web-app removal cleanup, suggested-layout idempotence, tool actions, new catalog actions, guide wording).
- `tests/native/quick-ring-v2.ps1` (new, isolated `--data-dir`): **PASS 12/12**: sub-ring slot marked; digit opens the sub-ring in place with Back as centre; Esc goes back then closes; a sub-ring action runs (throwaway page opened); a group shortcut opens the ring on the group; whole screen lands on the clipboard as an image with the toast; synthesized XButton1/XButton2 move to the previous/next tab; Esc minimizes; clean exit.
- Regression: `quick-ring.ps1` PASS, `quick-shelf.ps1` PASS, `quick-actions-hotkeys.ps1` PASS, `interaction.ps1` PASS after one test fix: the longer Actions menu made its "Interaction settings..." lookup from the desktop root fail, so it now searches inside the Actions menu.

### NOT RUN

The physical MX Master 4 checks (Sense Panel click/moves with the new ring, C7-C11, D1-D3) are left for the user at the end of the next pass. Screen reader, mixed-DPI capture on the second monitor and the Options+ Windows-gesture mapping were not exercised.

## Power Ring 1.0: the separate light ring (2026-09-25)

Direction from the user after the MX session: code the ring first, as a light app of its own with a PowerToys-like look, fully set in JSON (structure, sizes, colours, icons) so it can be edited by hand or by an AI. The big Power Ops app is frozen for now and is opened from the ring.

### Changed

- New `src/PowerRing` (WPF tray app, `PowerRing.exe`) and `src/PowerRing.Core` (config, validation, navigation, key combos; no UI, no dependency on Power Ops). Power Ops is untouched.
- Everything in `%APPDATA%\PowerRing\ring.json`: hotkey, `appearance` (theme, ring/slot/centre/icon/font sizes, slot radius, accent/background/border/slot/hover/icon/text colours, opacity, shadow, numbers, labels, animation), 1-5 profiles (own accent colour), circles of 1-8 items, up to 3 levels. Comments and trailing commas are accepted. `ring.schema.json` (VS Code autocompletion) and `RING_CONFIG.md` (guide to hand to an AI) are written next to it; their single source is `docs/power-ring/`.
- Actions: `run`, `url`, `folder` (paths or downloads/desktop/documents/pictures/videos/music/home), `keys` (sent to the window that was in front: Win+Tab, Win+D, Win+Left...), `text` (copy to clipboard), `screenshot`, `screen-to-clipboard`, `powerops`, `group`. Icons: names, Segoe Fluent codes, or .png/.ico/.jpg/.exe files (an .exe shows its own icon).
- Ring: profile buttons 1-5 on top (Tab, Ctrl+1-5, F1-F5, mouse wheel); slots numbered clockwise (keys 1-8); sub-circle slots carry a › badge; centre = Power Ops on the first circle, Back below; Esc/Backspace/right click go back, Esc on the first circle or an outside click closes.
- Save ring.json and it reloads (debounced FileSystemWatcher); an invalid file is reported with the line or field path and the last good ring stays active.
- Tray icon: open, profile, edit ring.json (VS Code when available), open folder, reload, start with Windows (HKCU Run), exit. `PowerRing.exe --show | --profile N | --exit` reach the running instance (one instance per ring.json).
- Light: software rendering (no Direct3D driver load), workstation non-concurrent GC, templates built in code (no XAML parser), no timers. Idle **37 MB private, 0 ms CPU**; after use 58 MB.
- `scripts/install-power-ring.ps1` publishes to `%LOCALAPPDATA%\Programs\PowerRing` and starts it; build.ps1 runs the new tests.

### Evidence

- `tests/PowerRing.Tests`: **14/14** (defaults valid with 3 levels, comments/trailing commas, round trip, JSON line in errors, field paths in errors, all limits, every action's target rules, key combos, navigator, reload, geometry, icons, schema and guide in sync with the code, store never overwrites the user's file).
- `tests/native/power-ring.ps1` (isolated ring.json, hotkey Ctrl+Alt+Shift+F9): **PASS 23/23**: helper files written; hidden until asked; idle 37.3 MB private, 0 ms CPU in 5 s; hotkey shows the focused ring centred on the pointer; order of profile buttons and slots; levels 2 and 3 by digits; Esc and Backspace back; Tab and Ctrl+1 switch profile (all buttons change); url action opens the page; text action fills the clipboard; a saved change reloads; a broken file shows a notice and keeps the last good ring; a second start with --profile 2 --show drives the running ring; 57.7 MB after use; --exit closes it and releases the hotkey.
- Installed for the user with `install-power-ring.ps1` (running from `%LOCALAPPDATA%\Programs\PowerRing`).

### NOT RUN / known limits

- The physical MX Master 4 Sense Panel with Power Ring, the look on the user's screens, the tray menu and "Start with Windows" are for the user to try.
- The default hotkey is Ctrl+Alt+Shift+R, the same as Power Ops' Quick Ring in Hybrid mode: when Power Ops owns it, Power Ring shows a notice; turn Power Ops' ring shortcut off (Actions > Global shortcuts) or change `hotkey`.
- No graphical editor yet (by design: JSON first). Screen reader pass not done.
