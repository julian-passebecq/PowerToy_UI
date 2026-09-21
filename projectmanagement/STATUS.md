# Current status

Updated: 2026-09-21

## Active Power Ops checkpoint

| Field | Current value |
| --- | --- |
| Product | **J Utility Palette · Power Ops** — local-first Windows companion |
| Repository | `julian-passebecq/PowerToy_UI` |
| Active branch | `codex/power-ops-suite` |
| Draft PR | #1 — Power Ops suite: Repositories, Portals, Resources, Capture and Clipboard |
| Base | `codex/pro-ai-handover-2026-09-11` |
| Current direction | Repository Hub + Portal Launcher + Resource Hub + Capture + Clipboard + Prompt Builder in one shell |
| Build gate | Full WPF Release build + package-free smoke/regression checks on `windows-latest` |
| Packaging | Self-contained Windows x64/arm64 publish script; manual x64 CI artifact path |
| Native/manual Windows acceptance | **Not yet claimed** — mouse summon, mixed DPI, clean-machine package and full visual interaction still require native validation |

The September 11 handover below is historical. Its compile blocker has been repaired and the product has since expanded substantially.

## Implemented since handover

### Reliable foundation

- fixed the inherited C# mouse-hook scope collision that blocked the WPF build;
- build and run scripts now propagate failures correctly;
- Windows CI uses current Node 24-compatible GitHub Actions releases;
- workspace ownership is scoped per canonical data directory, with optional `--data-dir` isolation for safe test workspaces;
- malformed/unrecognized workspace files are never silently reseeded;
- corrupt primary bytes are preserved as `workspace.invalid.*.json` evidence before valid-backup recovery;
- locked/inaccessible primary files do not silently fall back to backup;
- last-known-good backup is never replaced by malformed primary bytes;
- unsupported future schemas and explicit invalid schema versions are refused rather than rewritten;
- workspace IDs/enums/references are validated before persistence;
- save/export normalize detached snapshots instead of mutating live view-model objects;
- export is same-directory atomic and cannot target the managed primary/backup files;
- import is two-phase: validate/preview first, explicit confirmation before commit;
- import commit keeps the previous primary as backup and clears stale UI selections/search/prompt preview;
- close-save failure is visible and can cancel exit;
- summon mode does not hide after a failed save and ignores summon toggles while modal workflows are active;
- workspace autosaves when leaving the app;
- second launch brings the default workspace forward; different `--data-dir` workspaces may run independently.

### Power Ops shell

- Sidebar quick access is curated to pinned/favorite portals/resources, pinned snippets and active captures; Manage actions open Compact editors instead of creating dead-end items;
- Expanded / Compact / Sidebar layouts retained;
- primary module navigation;
- module-specific second navigation panel;
- contextual top quick ribbon;
- module-aware quick add;
- Ctrl+K current-module search;
- last active module is remembered between launches;
- Sidebar / Compact / Expanded keep independent saved sizes/positions;
- off-screen/oversized placement math is clamped to the nearest monitor work area and covered by smoke tests;
- Settings includes a reset action for saved window layouts.
- Ctrl+K search is limited to the visible full-shell search surface and ignores modified chords such as Ctrl+Shift+K;
- Portal, Resource, Clipboard and Capture lists share exact Enter / Ctrl+C keyboard policies covered by smoke tests;
- Capture Enter moves focus into the title editor while Ctrl+C copies the selected capture;
- custom navigation has explicit keyboard-focus visuals, and core navigation/list/editor/icon actions expose automation names/help text for accessibility tools.

### Repository Hub

- GitHub sync preservation is regression-tested so custom Website / Server / ChatGPT / category / subcategory / notes survive refresh;
- GitHub / Website / Server / ChatGPT link columns;
- project family + subcategory tree;
- multi-family ribbon filtering;
- row selection with all available URLs enabled by default;
- per-link inclusion switches;
- bulk copy by all / GitHub / Website / Server / ChatGPT;
- open selected repositories;
- GitHub discovery via authenticated local `gh` with public fallback;
- reusable saved repository lists preserving per-link choices;
- saved lists can Load / Copy / Open and saving the same list name updates it rather than creating duplicates;
- project archive / restore;
- deletion confirms impact and detaches linked captures / saved-list references safely.

### Portal Launcher

- Starter Pack adds common service portals to fresh or existing workspaces idempotently without overwriting customized entries;
- main portal URL + category + icon key;
- favorites and ribbon pins;
- project-specific sub-links;
- sub-links are visible directly on launcher cards;
- main and sub-links have explicit Open / Copy actions;
- Favorites / Pinned virtual filters;
- no repository-style selection workflow.

### Resource Hub

- exact cross-service quick links for GitHub, Google Drive, Dropbox, OneDrive, SharePoint, Notion and arbitrary HTTPS resources;
- provider / kind / project-group metadata;
- All / Favorites / Pinned / provider filters;
- common provider icons are always available in the top ribbon;
- provider ribbon supports multi-select filtering;
- Open / Copy actions are explicit, with Enter/double-click open and Ctrl+C copy on the focused list;
- repository refresh preserves user-edited resource labels, groups, notes, favorites and pins;
- provider-aware URL dedupe avoids duplicate GitHub/bookmark resources while preserving case-sensitive cloud IDs;
- active Repository Hub projects can be imported as GitHub resources;
- clipboard URLs can be captured with provider/kind inference;
- pinned/favorite resources are exposed in Sidebar quick access;
- additive workspace schema v4 persists resources without affecting external source items.

### Capture

- Inbox / To-do / Quick note / Bookmark / Read later / Transcript;
- dashboard capture board;
- subject filters;
- URL, labels, status, priority, due date and project association;
- pin / complete / archive;
- clipboard URL → Bookmark quick capture with useful inferred names and duplicate-bookmark avoidance;
- single and bulk JSON / Markdown export with project context;
- sidebar capture list excludes archived items.

### Clipboard + Prompt Builder

- reusable one-click clipboard snippets;
- categories, tags and ribbon pins;
- recent prompt recall/copy;
- built-in project placeholders:
  `{{project}}`, `{{repo}}`, `{{site}}`, `{{server}}`, `{{chatgpt}}`, `{{extra}}`;
- arbitrary placeholders such as `{{environment}}` or `{{region}}` are detected and exposed as inputs before composing.

### Packaging

- `scripts/publish.ps1` creates self-contained `win-x64` or `win-arm64` packages;
- manual GitHub Actions dispatch can build and upload the x64 ZIP;
- workspace and credentials remain outside the package.

## Remaining release acceptance

These are not blockers to continued feature work, but they remain before calling Power Ops a final Windows release:

1. native summon/hide interaction pass, including modal/pin/focus transitions (logic hardened, native acceptance still pending);
2. mixed-DPI native validation; off-screen/oversized recovery math is implemented and smoke-tested;
3. native keyboard/focus/screen-reader acceptance across all three layouts; automated policy coverage now includes shell search plus Portal, Resource, Clipboard and Capture list actions, with explicit focus visuals and automation labels;
4. clean-machine validation of the self-contained package;
5. low-level disk fault injection still not covered by ordinary smoke tests (for example out-of-space or permission changes mid-write);
6. final visual polish against the intended Fluent-style Power Ops layout.

## Historical handover

The original September 11 handover is preserved in [../handover/README.md](../handover/README.md) and [../handover/DELIVERY_AUDIT.md](../handover/DELIVERY_AUDIT.md). Its status table should not be interpreted as the current application state.
