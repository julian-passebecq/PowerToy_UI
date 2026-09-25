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
