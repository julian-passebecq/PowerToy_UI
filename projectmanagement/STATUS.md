# Current status

Updated: 2026-09-21

## Active Power Ops checkpoint

| Field | Current value |
| --- | --- |
| Product | **J Utility Palette · Power Ops** — local-first Windows companion |
| Repository | `julian-passebecq/PowerToy_UI` |
| Active branch | `codex/power-ops-suite` |
| Draft PR | #1 — Power Ops suite: Repository Hub, Portals, Capture and Clipboard |
| Base | `codex/pro-ai-handover-2026-09-11` |
| Current direction | Repository Hub + Portal Launcher + Resource Hub + Capture + Clipboard + Prompt Builder in one shell |
| Build gate | Full WPF Release build + package-free smoke/regression checks on `windows-latest` |
| Packaging | Self-contained Windows x64/arm64 publish script; manual x64 CI artifact path |
| Native/manual Windows acceptance | **Not yet claimed** — mouse summon, mixed DPI, clean-machine package and full visual interaction still require native validation |

The September 11 handover below is historical. Its compile blocker has been repaired and the product has since expanded substantially.

## Implemented since handover

### Reliable foundation

- fixed the inherited C# mouse-hook scope collision that blocked the WPF build;
- build script now propagates failures correctly;
- corrupt-primary recovery preserves the last good backup;
- unrecognizable imports such as `{}` are rejected;
- unsupported future workspace schemas are refused rather than rewritten;
- null collection entries are normalized safely;
- clipboard/browser failures are contained;
- close-save failure is visible and can cancel exit;
- workspace autosaves when leaving the app;
- second launch brings the existing instance forward;
- import clears stale filters and selections.

### Power Ops shell

- Expanded / Compact / Sidebar layouts retained;
- primary module navigation;
- module-specific second navigation panel;
- contextual top quick ribbon;
- module-aware quick add;
- Ctrl+K current-module search;
- last active module is remembered between launches.

### Repository Hub

- GitHub / Website / Server / ChatGPT link columns;
- project family + subcategory tree;
- multi-family ribbon filtering;
- row selection with all available URLs enabled by default;
- per-link inclusion switches;
- bulk copy by all / GitHub / Website / Server / ChatGPT;
- open selected repositories;
- GitHub discovery via authenticated local `gh` with public fallback;
- reusable saved repository lists preserving per-link choices;
- saved lists can Load / Copy / Open;
- project archive / restore;
- deletion confirms impact and detaches linked captures / saved-list references safely.

### Portal Launcher

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
- Open / Copy actions are explicit;
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
- clipboard URL → Bookmark quick capture;
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

1. native summon/hide interaction pass, including modal/pin/focus transitions;
2. mixed-DPI and off-screen window recovery;
3. keyboard/focus/accessibility pass across all three layouts;
4. clean-machine validation of the self-contained package;
5. final destructive-storage/fault-injection regression pass;
6. visual polish against the intended Fluent-style Power Ops layout.

## Historical handover

The original September 11 handover is preserved in [../handover/README.md](../handover/README.md) and [../handover/DELIVERY_AUDIT.md](../handover/DELIVERY_AUDIT.md). Its status table should not be interpreted as the current application state.
