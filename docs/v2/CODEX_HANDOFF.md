# Power Ops V2 - isolated native preview acceptance

Repository: https://github.com/julian-passebecq/PowerToy_UI
Branch: `codex/power-ops-v2-workspaces`
PR: https://github.com/julian-passebecq/PowerToy_UI/pull/4

This is a NEW V2 preview. Do not overwrite or amend the V1 results ledger on `codex/to-be-tested-v1`. Keep the existing laptop test and its workspace intact. No main merge is authorized.

## Prompt for the laptop agent

Read AGENTS.md, docs/v2/ARCHITECTURE.md and docs/v2/DELIVERY.md on the V2 branch. Use the model selected by the user; ordinary testing does not require a model change. Never claim that repository instructions configure your model or grant desktop capabilities.

Create a separate clean worktree or clone for V2. First inspect git status and existing worktrees. Do not stash/reset/delete user changes, switch a dirty worktree, overwrite a directory, or stop unrelated processes. Fetch origin, then create a new local branch tracking origin/codex/power-ops-v2-workspaces in an unused sibling directory. Report its path and exact SHA.

Run `.\scripts\build.ps1`. It now includes the full WPF Release build, existing smoke tests and the additional WorkspaceTests executable. Failures must be investigated before native acceptance.

Launch with a NEW unique isolated directory, without removing earlier test data:

```powershell
$TestData = Join-Path $env:TEMP ("PowerOps-V2-Preview-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $TestData | Out-Null
.\scripts\run.ps1 -DataDirectory $TestData
```

Do not use scripts/codex-live-test-v1.ps1 here: that historical launcher requires the V1 branch. Do not use the user's normal workspace or their currently open V1 acceptance workspace.

Computer control may still be unavailable. Test available capabilities once. If unavailable, use user-assisted tests one small scenario at a time and distinguish user observations from automated evidence. Do not repeatedly retry unavailable providers, install unrequested software or change system permissions.

## Native test matrix

| ID | Scenario | Required observation |
| --- | --- | --- |
| V2-01 | First launch | Launchpad and workspace selector appear; File/Workspace/Tabs/View menus accessible |
| V2-02 | Old modules | Repository, Portals, Tools, Resources, Capture, Clipboard, Prompt Builder and Settings still work |
| V2-03 | Profiles | Switch General/Datapass/Foil/Knowledge/Device; visible navigation changes without deleting records |
| V2-04 | Customize | Add/rename a workspace; hide/show modules; Launchpad/Settings remain reachable |
| V2-05 | Independent tabs | Two Repository tabs with different searches/category filters retain their own view state |
| V2-06 | Shortcuts | Ctrl+T/W/Shift+T/Tab/Shift+Tab/1..9 work outside text editors; normal editor shortcuts are not hijacked |
| V2-07 | Last-tab close | Closing the only tab leaves a usable Launchpad |
| V2-08 | View bookmarks | Save view, alter tabs, restore; original view returns but domain edits are not rolled back |
| V2-09 | Layout | Expanded/Compact/Sidebar and quick ribbon visibility; portrait and small window accessibility |
| V2-10 | Restart persistence | Close/reopen using exactly the same test directory; profiles/tabs/search/filter/bookmarks remain |
| V2-11 | Scoped export | Current-module export has only requested content and no unrelated shell state; selected-module export matches selection |
| V2-12 | All-content export | Includes sections and shell metadata; no media bytes or launcher command/path fields by default |
| V2-13 | Layout import | Valid export/import round trip; preview cancellation changes nothing; corrupt/future/oversized file rejects |
| V2-14 | Managed-file protection | Export refuses a path inside the active data directory |
| V2-15 | Corrupt shell | Use disposable fixture only; preserve corrupt/future shell file, show warning and keep classic shell available |
| V2-16 | Launchpad | Existing saved links appear, external browser opens the chosen URL only; no background site checks |
| V2-17 | Inventory | No scan before click; click scan; paths/file versions or explicit Unknown; no programs/updates executed |
| V2-18 | Feature catalog | Available vs planned boundaries visible; no false live/integration claims |
| V2-19 | Existing Explorer | Folder picker/pins, Ctrl+Shift+E and text editing conflicts unchanged |
| V2-20 | Package | Publish architecture appropriate to this machine into unused temp output, then launch with fresh isolated data |
| V2-21 | Performance | Record idle five-minute CPU/RAM/disk/network, same layout before/after; no numerical claim without measurement |
| V2-22 | Multi-monitor | Move, resize, change layouts and restart on available displays; negative coordinates/mixed DPI if available |

## Evidence and fixes

Create `docs/v2/NATIVE_RESULTS.md` with exact revision, OS/build, architecture, actual model if known, isolated directory identifier (redact private paths), automated command exit codes and PASS/FAIL/BLOCKED/NOT RUN for every row. Split partial passes into subtests rather than count a whole module as passed. Build success is not live UI success.

Reproduce confirmed defects, make focused fixes on the V2 branch, add regression coverage, rerun build.ps1 and repeat the affected native scenario. Do not suppress failing tests to get a green run. Keep user content/secrets out of screenshots, logs and commits.

At completion commit/push only intended code/docs to the V2 branch, verify the remote SHA, leave the PR draft and report actual tests plus remaining gaps. Do not merge to main and do not label the broad V3 design implemented.
