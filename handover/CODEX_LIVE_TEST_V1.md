# Codex live test v1 handoff

Updated: 2026-09-24

## Copy/paste prompt for Codex GPT-6 Luna Medium

### Model / reasoning policy

Use **GPT-6 Luna — Medium** as the default for the complete live acceptance pass.

- Desktop/computer control itself does **not** require High reasoning.
- Stay on Medium for navigation, UI interaction, build/test commands, the test matrix, and straightforward fixes.
- Escalate to **GPT-6 Luna — High** only for a confirmed difficult defect: persistent failure after one careful fix/retest cycle, unclear multi-layer root cause, WPF focus/summon/DPI/native interop/concurrency/state-recovery issues, or a risky cross-cutting fix.
- Do not use High merely because the task controls the PC.
- Do not run the main acceptance pass on Light if that would reduce reliability.

> You are taking over the Power Ops / J Utility Palette repository for a **live Windows acceptance test**, not a source-only review.
>
> Use **GPT-6 Luna — Medium** for the normal pass. Computer-control does not require High. Escalate to **Luna High** only for a confirmed difficult defect that remains hard after a careful reproduce/inspect/fix/retest cycle or involves complex WPF/native/state behavior. Do not switch to High just to control the desktop.
>
> Work on branch `codex/to-be-tested-v1`.
>
> Read `AGENTS.md` and this file first. Then run the full automated gate and **launch the real WPF app on my Windows computer**. Use your available computer-control / desktop automation to click through the UI, type, copy/paste, open dialogs, switch layouts, test clipboard screenshots/media, test summon/hide behavior, and verify persistence by closing/reopening the app.
>
> Use an isolated test workspace with `--data-dir` for anything destructive or stateful. Do not use my personal files as test fixtures. Do not alter credentials, PATH, registry, Windows services, firewall, cloud resources, or authentication.
>
> For every scenario record PASS / FAIL / BLOCKED. If you find a real defect, reproduce it, patch the smallest safe fix on this branch, add a regression test where practical, run `.\scripts\build.ps1`, repeat the live scenario, and commit the fix. Do not merge.
>
> When finished, write `handover/CODEX_LIVE_TEST_V1_RESULTS.md`, commit it, and report the final commit SHA plus the PASS/FAIL/BLOCKED summary.
>
> If computer-control, a second monitor, Mouse Button 4/5, or another physical capability is unavailable, say BLOCKED for that item rather than pretending it was tested.

---

## Starting revision

This branch was created from the green Power Ops head after the clipboard/media debug pass.

Implementation baseline under test:

`95eed387e6719765d05ccbbaa0190bf0fbdcd9f5`

That baseline passed Windows CI run **#302** before the handoff-only files were added. It was later promoted to `main` through merge commit `96fdaf580ff9556dbc7330ae8c7831fabf0cb8ae`; Windows CI **#304** passed before promotion and **#305** passed on `main` after promotion.

The acceptance pass still belongs on `codex/to-be-tested-v1` so any live defects and fixes remain isolated until reviewed. Codex must rerun the local build/smoke gate on this branch before live UI testing.

Source branch:

`codex/power-ops-suite`

Live-test branch:

`codex/to-be-tested-v1`

The handoff branch contains the current Power Ops implementation plus test instructions. Do not merge during the acceptance pass.

## First commands

From the repository root:

```powershell
git status
git branch --show-current
git log -1 --oneline
.\scripts\build.ps1
```

Expected branch:

```text
codex/to-be-tested-v1
```

The build script performs:

1. restore;
2. full Release solution build;
3. smoke/regression test execution.

Do not continue to live UI testing if the full gate fails. Fix that first.

## Isolated live-test workspace

Use a disposable workspace for all stateful tests:

```powershell
$TestData = Join-Path $env:TEMP "PowerOps-Codex-LiveTest-V1"
Remove-Item $TestData -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $TestData | Out-Null
.\scripts\run.ps1 -DataDirectory $TestData
```

Expected files will be created inside that directory, including `workspace.json`, backups, and `media\`.

Never use the user's live Power Ops workspace as a destructive test fixture.

## Required live test matrix

### 1. Startup / shell / persistence

Use the real Windows UI.

Verify:

- app starts without exception;
- Dashboard appears;
- all primary modules are reachable:
  - Dashboard
  - Repository Hub
  - Portal Launcher
  - Tool Launcher
  - System / Cheat Sheet
  - Resource Hub
  - Capture
  - Clipboard
  - Prompt Builder
  - Settings
- Expanded / Compact / Sidebar switch correctly;
- layout state persists after close/reopen;
- Ctrl+K focuses current-module search where available;
- Escape clears the active search;
- Save succeeds;
- close/reopen with the same isolated `--data-dir` preserves data.

### 2. Repository Hub

Create temporary test-only content and verify:

- add project;
- repository URL;
- website URL;
- server/backend URL;
- ChatGPT URL;
- category/family;
- subcategory;
- note;
- row selection;
- per-link inclusion toggles;
- Copy All;
- copy individual link types;
- save/load repository list;
- archive/restore;
- delete test project.

If local `gh` is authenticated:

- run GitHub sync;
- confirm custom Website/Server/ChatGPT/category/subcategory/note fields survive refresh;
- do not change anything on GitHub itself.

### 3. Portal Launcher

Create a temporary test portal and verify:

- add/edit portal;
- main URL;
- category;
- icon key;
- favorite;
- top-ribbon pin;
- quick action;
- project-specific sub-link;
- category filtering;
- copy URL;
- open only a harmless public URL;
- delete the test portal.

### 4. Tool Launcher

Verify:

- existing tool list renders;
- a harmless temporary launcher entry can be created/edited;
- opening a harmless local application works;
- working directory/arguments behave correctly;
- no shell-injection or malformed-command crash occurs from normal UI input.

Do not use this pass to make system changes.

### 5. System / Cheat Sheet

Verify displayed values against the actual PC:

- Windows description;
- OS architecture;
- process architecture;
- recommended package architecture;
- .NET runtime;
- machine/user;
- logical processor count.

Test safe Open/Copy actions for paths such as:

- hosts file;
- Git config;
- SSH config path;
- VS Code settings;
- PowerShell profile;
- Windows Terminal settings.

Do not edit protected/system files.

### 6. Resource Hub

Create temporary test-only resources and verify:

- add/edit resource;
- provider;
- kind;
- group/project;
- note;
- favorite;
- pin;
- provider/group filtering;
- Open;
- Copy;
- clipboard URL capture using a harmless URL;
- import active Repository Hub projects;
- duplicate handling;
- delete test resources.

### 7. Capture

Create one test item for each:

- Inbox
- To-do
- Quick note
- Bookmark
- Read later
- Transcript

Verify where relevant:

- title;
- subject/category;
- body;
- URL/source;
- labels;
- status;
- priority;
- due date;
- project association;
- pinned;
- completed;
- archived;
- JSON export;
- Markdown export.

Confirm archived items disappear from normal active views and restore correctly.

### 8. Clipboard — text

Verify:

- add snippet;
- edit title;
- category;
- tags;
- body;
- pin;
- one-click copy;
- Enter to copy when focused;
- Ctrl+C to copy when focused;
- search;
- category filters;
- delete test snippet.

### 9. Clipboard — real screenshot/image

This must be a real Windows clipboard interaction.

Using desktop control:

1. put a real screenshot or harmless image on the Windows clipboard;
2. open Clipboard → Images & clips;
3. click **Paste image**.

Verify:

- item appears;
- preview renders;
- title/category/tags edit correctly;
- project association works;
- pin works;
- managed file appears under `<test-data>\media\`;
- `workspace.json` contains metadata and a relative `media/...` path;
- JSON does not contain base64 image bytes;
- Copy copies an actual image back to the Windows clipboard;
- paste that image into another safe app/control to prove clipboard format;
- close/reopen Power Ops and verify preview persists;
- delete the test image;
- verify both workspace metadata and managed test file are removed.

### 10. Clipboard — short video clip

Create or use a tiny harmless MP4/WebM/MOV fixture under a temporary directory.

Import it using **Import media**.

Verify:

- item appears as Video;
- UI shows the video placeholder instead of attempting image decode;
- Open launches the default Windows player;
- Copy uses file-drop clipboard semantics;
- title/category/tags/project/pin persist;
- restart preserves metadata;
- delete removes the managed test copy.

### 11. Media path/security regression

Use only the isolated test workspace.

Verify workspace import/save rejects media paths such as:

```text
workspace.json
../outside.png
media/../workspace.json
media//broken.png
C:\temp\unsafe.png
```

Verify a valid nested managed path such as:

```text
media/Datapass/nested.png
```

is accepted and normalized correctly.

Do not point tests at real personal files.

### 12. Prompt Builder

Verify:

- add module;
- edit title/category/body;
- enable/disable;
- reorder;
- project selection;
- built-in variables:
  - `{{project}}`
  - `{{repo}}`
  - `{{site}}`
  - `{{server}}`
  - `{{chatgpt}}`
  - `{{extra}}`
- custom variable such as `{{environment}}`;
- preview;
- copy prompt;
- copy prompt + project;
- recent prompt recall/copy.

### 13. Settings

Verify:

- Normal;
- Always on top;
- Summon / hide;
- hide on focus loss;
- open near cursor;
- summon binding selection;
- reset saved window layouts;
- GitHub owner field;
- starter catalog;
- open data folder;
- JSON export;
- two-phase JSON import confirmation.

Use only isolated data for destructive import tests.

### 14. Real summon/hide interaction

This is a native acceptance test.

Using actual desktop control and available physical inputs, test:

- Normal mode;
- Always-on-top mode;
- Summon/hide mode;
- hide on focus loss;
- temporary Keep open pin;
- near-cursor placement;
- supported Mouse Button 4/5/middle-click variants;
- summon while another app has focus;
- modal dialog protection;
- hide behavior after modal close.

If the physical device/control layer cannot emit Mouse Button 4/5 or middle-click, mark those exact rows BLOCKED.

### 15. Multi-monitor / DPI

If the PC has multiple monitors or mixed DPI:

- move window between monitors;
- switch layouts;
- close/reopen;
- verify saved placement is visible and sensible;
- ensure app does not reopen off-screen.

If unavailable, mark BLOCKED.

### 16. Packaging

Run:

```powershell
.\scripts\publish.ps1 -Runtime win-x64
```

Launch:

```text
artifacts\PowerOps-win-x64\JUtilityPalette.exe
```

with a fresh isolated `--data-dir`.

Verify the self-contained package starts and works without relying on `dotnet run`.

If the machine is ARM64, also test:

```powershell
.\scripts\publish.ps1 -Runtime win-arm64
```

### 17. Performance sanity

Using Task Manager or available local telemetry:

- record approximate idle CPU;
- record approximate RAM;
- switch modules repeatedly;
- leave app idle for a few minutes;
- watch for unexpected continuous CPU/disk activity;
- verify the app is not constantly rescanning or writing.

This is a sanity check, not a benchmark.

## Defect handling

If a test fails:

1. reproduce once;
2. record exact steps;
3. capture exception/message/screenshot if useful;
4. identify root cause;
5. make the smallest safe fix;
6. add/extend regression coverage when practical;
7. run:
   ```powershell
   .\scripts\build.ps1
   ```
8. repeat the live test;
9. commit the fix to `codex/to-be-tested-v1`;
10. record the fix commit in the results file.

Do not refactor unrelated code.

## Required results file

Create:

`handover/CODEX_LIVE_TEST_V1_RESULTS.md`

Use:

```markdown
# Codex live test v1 results

Date:
Machine:
Windows:
Architecture:
Branch:
Commit tested:
Tester/model:

## Automated gate
- build.ps1:
- smoke tests:
- publish x64:
- publish arm64:

## Live matrix
| Area | Result | Evidence / observation | Fix commit |
| --- | --- | --- | --- |
| Startup / shell | PASS/FAIL/BLOCKED | | |
| Repository Hub | PASS/FAIL/BLOCKED | | |
| Portal Launcher | PASS/FAIL/BLOCKED | | |
| Tool Launcher | PASS/FAIL/BLOCKED | | |
| System / Cheat Sheet | PASS/FAIL/BLOCKED | | |
| Resource Hub | PASS/FAIL/BLOCKED | | |
| Capture | PASS/FAIL/BLOCKED | | |
| Clipboard text | PASS/FAIL/BLOCKED | | |
| Clipboard screenshot/image | PASS/FAIL/BLOCKED | | |
| Clipboard video | PASS/FAIL/BLOCKED | | |
| Media path safety | PASS/FAIL/BLOCKED | | |
| Prompt Builder | PASS/FAIL/BLOCKED | | |
| Settings | PASS/FAIL/BLOCKED | | |
| Summon/hide | PASS/FAIL/BLOCKED | | |
| Multi-monitor/DPI | PASS/FAIL/BLOCKED | | |
| Self-contained package | PASS/FAIL/BLOCKED | | |
| Performance sanity | PASS/FAIL/BLOCKED | | |

## Defects found

### BUG-001
- Reproduction:
- Expected:
- Actual:
- Root cause:
- Fix:
- Regression:
- Retest:
- Fix commit:

## Blocked tests

...

## Final status

READY FOR USER TEST

or

NOT READY — confirmed remaining defects:
- ...
```

## Evidence rules

Useful evidence:

- screenshots of Power Ops only;
- exact exception text;
- relevant JSON snippets;
- file existence checks;
- build/test output;
- commit SHA after each fix.

Do not commit evidence containing:

- tokens;
- passwords;
- private keys;
- unrelated personal messages;
- browser cookies/session data;
- personal screenshots unrelated to the test.

## Completion criteria

The live-test branch can be handed back only when:

- `.\scripts\build.ps1` passes;
- every applicable live scenario is PASS;
- every FAIL is fixed/retested or clearly documented;
- BLOCKED items are honestly identified;
- no personal data was damaged;
- `handover/CODEX_LIVE_TEST_V1_RESULTS.md` exists;
- all fixes and results are committed to `codex/to-be-tested-v1`.

Final response should provide:

1. final commit SHA;
2. PASS / FAIL / BLOCKED summary;
3. confirmed defects fixed;
4. remaining blockers;
5. whether the branch is READY FOR USER TEST.
