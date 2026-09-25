# Power Ops suite direction

Updated 2026-09-21.

This document is the current product direction for the standalone `PowerToy_UI` / J Utility Palette app. It supersedes any accidental implementation of these ideas inside VizLens.

## Product boundary

Power Ops is one local-first Windows utility with several focused modules. It is not a GitHub clone, a cloud dashboard, or a browser automation product.

The shell should preserve the fast-launch character of the existing app:

- global top ribbon for pinned/frequent items;
- primary left navigation for modules;
- secondary left navigation for the current module's categories/tree;
- scrollable center workspace;
- local JSON persistence and explicit import/export;
- open and copy actions are always distinct.

## Window modes

The existing three window modes remain useful and should not be flattened into one oversized dashboard.

- **Expanded** becomes the full Power Ops workspace: primary left navigation, module-specific secondary tree/categories, top quick-launch ribbon, and the scrollable center surface.
- **Compact** keeps the same data but favors the current module and quick actions.
- **Sidebar / Summon** is the fastest companion view for opening a portal, copying a snippet, or dropping a capture without occupying the desktop.

This lets the richer launcher/dashboard exist without making the summon workflow heavy.

## Modules

### 1. Repository Hub

Purpose: gather all links belonging to a development project without navigating GitHub repeatedly.

Each project/repository row supports these explicit link fields:

1. GitHub repository
2. Website
3. Server / hosting / backend
4. ChatGPT conversation

Repository Hub keeps multi-selection.

When a row is selected, every available link in that row is selected for Copy All by default. Individual link cells have their own include checkbox so a link can be excluded without deselecting the row.

Bulk actions:

- Copy all selected URLs
- Copy GitHub URLs
- Copy website URLs
- Copy server URLs
- Copy ChatGPT URLs
- Open selected GitHub repositories

Saved repository lists/bookmarks remember both repository membership and per-link include switches.

### 2. Portal Launcher

Purpose: open frequently used service portals and project-specific sub-pages.

This is deliberately different from Repository Hub: there is no multi-selection.

Examples:

- Microsoft Fabric
- Azure
- Databricks
- Vercel
- Netlify
- Cloudflare
- GitHub
- LinkedIn
- AtlasNote
- AtlasCode
- portfolio and other personal sites

A portal stores:

- name
- category
- optional icon key
- main URL
- zero or more named sub-links
- optional project/tag
- pinned-to-ribbon flag
- favorite flag

Interaction contract:

- pinned portal icon in the top ribbon opens the portal's main URL directly;
- category items in the secondary left panel filter the center workspace;
- center portal cards show the main Open action plus saved sub-links;
- a + action adds either a new portal or a new sub-link;
- URLs are user-maintained; no live-site discovery or browser automation is required.

### 3. Resource Hub

Purpose: collect exact destinations across storage, code and knowledge services in one local quick-link catalog.

Examples:

- GitHub repository
- Google Drive folder or document
- Dropbox folder
- OneDrive / SharePoint location
- Notion page
- dashboard or arbitrary HTTPS link

A resource stores:

- name
- provider
- kind (Repository, Folder, Document, Dashboard, Link, etc.)
- project/group/topic
- URL
- optional note
- favorite and ribbon-pin flags
- optional source-project link when imported from Repository Hub

Interaction contract:

- common providers (GitHub, Google Drive, Dropbox, OneDrive, Notion, SharePoint) are always available in the top ribbon as filters;
- provider filters can be combined from the ribbon;
- secondary navigation offers All / Favorites / Pinned plus providers present in the workspace;
- cards expose explicit Open and Copy actions;
- **Import repos** creates or refreshes GitHub Resource Hub links from active Repository Hub projects;
- **Clipboard URL** creates a resource and infers provider/kind where practical;
- pinned/favorite resources are available in Sidebar/Summon quick access.

Portal Launcher remains the service-home surface; Resource Hub is for exact destinations.

### 4. Capture / Stickies

Purpose: quickly capture information that can later be exported to the user's website or another system.

Capture types mirror the user's existing dashboard concept:

- Inbox
- To-do
- Quick note
- Bookmark
- Read later
- Transcript

Common fields:

- title
- subject/category
- body
- URL/source
- tags
- status
- optional priority/due date
- pinned/completed/archive state
- optional project association

Transcript is a long-text mode using the same local item model, not a separate app.

Exports should support at least JSON and Markdown. CSV can be added for list-like captures.

### 5. Clipboard Library

Purpose: keep frequently copied text and visual references immediately available without turning the app into a full asset-management system.

Clipboard contains two internal surfaces:

- **Text** — one-click copy for premade prompts, commands, URLs, signatures, fragments, etc.
- **Images & clips** — screenshots pasted from the Windows clipboard plus imported image files and short reference videos.

Text snippet fields:

- title
- category
- text
- pinned
- sort order
- optional tags

Media fields:

- image/video kind
- title
- category
- tags
- optional project association
- workspace-relative managed-file path
- MIME type and byte size
- pinned state
- created/updated timestamps

Media bytes are stored in `<workspace>/media/`; `workspace.json` stores metadata and relative paths only. This keeps JSON export small enough for AI audit and keeps custom `--data-dir` workspaces portable.

The top ribbon can expose pinned text snippets. Media search/filtering participates in the same Clipboard category pane.

### 6. Prompt Builder

Prompt Builder remains a separate primary module because it composes ordered reusable modules, project variables and recent prompt history rather than acting as a single clipboard item.

### 7. Dashboard

Dashboard is summary/navigation, not another editor.

Useful content:

- recently used portals
- recently updated repository rows
- pinned captures
- due to-do items
- recent clipboard items/prompts
- saved repository lists
- counts by project/category

### Sessions (Claude Code)

Read-only live view of every Claude Code session on this PC, grouped by project (collapsible) with status and project-type filter chips.

- Transcripts: `%USERPROFILE%\.claude\projects\<sanitized-cwd>\<uuid>.jsonl`; only the last ~96 KB of each file is parsed, and results are cached by size + mtime.
- Titles, branch, PRs (with state), archived flag and the `claude://claude.ai/epitaxy/local_<id>` link come from the desktop app's `claude-code-sessions\**\local_<id>.json`, matched on `cliSessionId` = transcript uuid. The packaged (MSIX) app stores these under `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude`; both that and `%APPDATA%\Claude` are scanned. Without metadata, **Go** opens the Claude app and copies the title.
- Effort ceiling comes from the tables in `%USERPROFILE%\.claude\CLAUDE.md`; project type from `Project type:` in the project's `CLAUDE.md` (default `dev`).
- Status: **Needs you** (pending AskUserQuestion/ExitPlanMode, a tool call stalled > 5 min, or a final message ending in a question / waiting for input or a manual test), **Working** (written in the last 60 s or a tool running < 5 min), **Done** (reports done/merged, or all its PRs merged), otherwise **Idle**. Sessions archived or idle > 3 days are hidden unless toggled on.
- Refresh: file watcher (debounced 1 s) + 10 s timer. The Needs-you count shows as a badge on the Sessions nav button and as a taskbar overlay.

#### Audit card

Top of the Sessions tab. Read-only view of the reports written by the `workflow-audit` Claude scheduled task in `%USERPROFILE%\.claude\effort-board\audits\` (override with the `JUTILITY_AUDITS_DIR` environment variable for fixtures). Power Ops never writes there.

- Latest `<YYYY-MM-DD>-<matin|soir>.md` (soir after matin on the same day). Status badge from the worst 🟢/🟠/🔴 in **Résumé** (teal OK, amber warning, red critical), the first 3 Résumé lines, the number of top-level items (or `###` headings) under **Problèmes détectés** ("Aucun" counts as 0), and time since the file was written. **STALE** is shown after 14 h. The card tooltip shows the last line of `log.md`.
- **Open report** renders the markdown in a viewer window, with **Open in default app** (falls back to Notepad).
- **Checkup** opens `claude://claude.ai/epitaxy/scheduled/workflow-audit`. The Claude desktop app (checked on 2.9939.2.0) has no URL or CLI that runs a scheduled task now: `run_scheduled_task` is only available inside the app. So the button opens the task page and shows the tooltip "Scheduled → workflow-audit → Run now". It never calls the Anthropic API.

## Navigation model

```text
Power Ops
├─ Dashboard
├─ Repository Hub
│  ├─ All
│  ├─ Foil
│  │  ├─ Core
│  │  ├─ Extensions
│  │  └─ Experiments
│  ├─ Atlas
│  ├─ Datapass
│  ├─ Fabric
│  └─ ...
├─ Portal Launcher
│  ├─ Cloud & Data
│  ├─ Deploy & Hosting
│  ├─ Development
│  ├─ Personal Sites
│  └─ Work / Social
├─ Resource Hub
│  ├─ GitHub
│  ├─ Google Drive
│  ├─ Dropbox
│  ├─ OneDrive / SharePoint
│  ├─ Notion
│  └─ Other
├─ Capture
│  ├─ Inbox
│  ├─ To-do
│  ├─ Quick notes
│  ├─ Bookmarks
│  ├─ Read later
│  └─ Transcripts
├─ Clipboard
│  ├─ Text
│  └─ Images & clips
├─ Prompt Builder
└─ Settings
```

## Delivery order

The preserved takeover branch reported a compile blocker and unresolved storage reliability work. Expansion must not hide those issues.

1. restore a green Windows build;
2. keep workspace migrations backward-compatible and non-destructive;
3. add explicit Repository Hub link fields;
4. add Portal Launcher model + UI;
5. evolve Notes into Capture without discarding existing notes;
6. add quick Clipboard snippets while retaining Prompt Builder;
7. reshape shell/navigation and dashboard;
8. run Windows build/smoke checks and isolated persistence tests.

The accidental VizLens Power Ops PR is not part of this product and must remain unmerged.
