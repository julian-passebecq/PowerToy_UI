# V2.1 manual acceptance - MX Master / Logi Options+ (user-assisted)

Prepared: 2026-09-25. Scheduled with the user for the next session. Observer: the user, plus the automatic log.

Everything else in V2.1 is covered by `.\scripts\build.ps1` and the scripts in `tests/native/`. This checklist covers only what needs the physical mouse, Logi Options+ or human judgement.

## 0. Setup (one command)

```powershell
cd D:\PROJ\PowerToy_UI-v2
.\scripts\build.ps1
.\tests\native\mx-acceptance-setup.ps1
```

The setup script:

- starts an **isolated** Power Ops test instance whose window title ends with `— powerops-mx-acceptance-<date>`;
- puts it in Hybrid mode with Ctrl+Alt+Shift+R (Quick Ring), P (show/hide), N (Quick Capture) and V (Clipboard);
- makes Mongoku ring slot 8 and opens 3 tabs: Launchpad | Capture | Clipboard;
- starts a hidden observer, which writes `observer.log` in the test folder and logs the ring, the foreground window, the active tab and the capture count. Other apps are logged by process name only.

The script refuses to run if any of the four shortcuts is already taken. Close the test window at the end; the observer stops by itself.

Logi Options+ 2.7.961922 is installed on this laptop, and so is Logitech G HUB. Mongoku should be running on `http://localhost:3100/` for C9 only.

## 1. Configuration

| # | Step | Expected |
| --- | --- | --- |
| A1 | Test instance: Actions → Interaction settings… | "Hybrid (recommended)" is selected. Four shortcuts are listed as **(active)**. The MX Master guide is expanded with "Copy …" buttons. |
| A2 | Press one "Copy Ctrl+Alt+Shift+R" button and paste it somewhere | The clipboard contains `Ctrl+Alt+Shift+R`. |
| B1 | Logi Options+ → MX Master → buttons | — |
| B2 | Gesture button: click → keyboard shortcut Ctrl+Alt+Shift+R; up → …+N; down → …+V | Options+ records each shortcut. **Check:** while Power Ops owns a shortcut, Windows may deliver it to Power Ops, so Options+ may not record it and the ring opens instead. If so, record KO. The fix is to pause the test instance's shortcuts (Actions → Global shortcuts → untick → Save and apply), record them in Options+, then re-enable. |
| B3 | Options+ → add application `JUtilityPalette.exe` (path `D:\PROJ\PowerToy_UI-v2\src\JUtility.App\bin\Release\net8.0-windows\JUtilityPalette.exe`): Back → Ctrl+Shift+Tab, Forward → Ctrl+Tab | App-specific mapping saved. |
| B4 | Write down the **exact Options+ menu names** used in B2/B3 | Compare with the in-app guide wording and report any difference. |

## 2. Using the mouse

Start each step with Chrome (or another app) in front unless stated otherwise.

| # | Step | Expected (observer evidence) |
| --- | --- | --- |
| C1 | Gesture button click | The Quick Ring opens centred on the pointer and has focus (`quick ring = visible`, centre ≈ pointer). |
| C2 | Press **1** | The ring closes and the Windows snip overlay opens (Esc cancels it). |
| C3 | Gesture click, then **Esc** | The ring closes and Chrome is back in front. |
| C4 | Gesture click, then click outside the ring | The ring closes and nothing runs. |
| C5 | Gesture **up** | Power Ops comes forward on Capture with a new item and the title box focused (`captures saved` +1 after typing). |
| C6 | Gesture **down** | Power Ops shows the Clipboard library. |
| C7 | With Power Ops in front: **Back**, **Forward** | The previous/next Power Ops tab (`active tab` changes, wraps 1↔3). |
| C8 | In Chrome: Back / Forward | Normal browser history (the app-specific mapping does not leak). |
| C9 | Gesture click → slot 8 (Mongoku) | The Mongoku Web tab opens inside Power Ops. |
| C10 | Keyboard Ctrl+Alt+Shift+P twice | Power Ops shows, then minimizes. |
| C11 | Throughout | G HUB does not react to the MX buttons, and there are no double actions. |

## 3. Mouse-hook conflict warning

| # | Step | Expected |
| --- | --- | --- |
| D1 | Test instance Settings: window behaviour **Summon / hide**, summon mouse binding **Mouse button 5** | — |
| D2 | Actions → Interaction settings… (Hybrid) | A yellow warning about the mouse summon hook appears. |
| D3 | Press "Use Ctrl + middle click for Summon (applies now)" | The warning disappears and the Settings binding shows Ctrl + middle click. |

## 4. Optional (same sitting)

- Customize Quick Ring… / Customize Quick Shelf…: reorder, "only for this workspace", "Use default for this workspace".
- Global shortcuts…: an invalid gesture (`Ctrl+C`, `Alt+F4`) is rejected inline; unticking removes the registrations.
- Actions menu: Downloads, Explorer folder, Terminal (with and without a terminal tool), Screenshot.
- Second monitor, if available: the ring at a screen edge and on the second monitor, and the Shelf dragged across.

## Results

| # | OK/KO | Notes |
| --- | --- | --- |
| A1 | | |
| A2 | | |
| B2 | | |
| B3 | | |
| B4 | | |
| C1–C11 | | |
| D1–D3 | | |

Attach the `observer.log` path, and record the tested revision (`git rev-parse HEAD`).
