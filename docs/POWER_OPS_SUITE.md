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

### 3. Capture / Stickies

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

### 4. Clipboard / Prompt Library

Purpose: copy reusable text instantly.

Two surfaces share the same module:

- **Snippets** — one-click copy for premade prompts, commands, URLs, signatures, fragments, etc.
- **Prompt Builder** — the existing modular prompt composer with variables and project context.

Snippet fields:

- title
- category
- text
- pinned/favorite
- sort order
- optional tags

The top ribbon can expose pinned snippets for one-click copy.

### 5. Dashboard

Dashboard is summary/navigation, not another editor.

Useful content:

- recently used portals
- recently updated repository rows
- pinned captures
- due to-do items
- recent clipboard items/prompts
- saved repository lists
- counts by project/category

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
├─ Capture
│  ├─ Inbox
│  ├─ To-do
│  ├─ Quick notes
│  ├─ Bookmarks
│  ├─ Read later
│  └─ Transcripts
├─ Clipboard
│  ├─ Snippets
│  └─ Prompt Builder
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
