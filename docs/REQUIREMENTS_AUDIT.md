# Power Ops requirements audit

Updated: 2026-09-21

This is the concrete checklist for the PowerToy / Power Ops requirements discussed during the September 21 development sessions. It distinguishes implemented behavior from user-maintained content and native Windows acceptance work.

## Coverage matrix

| Requirement | Status | Current implementation |
| --- | --- | --- |
| One Power Ops shell rather than separate GitHub/portal/storage utilities | Implemented | Dashboard, Repository Hub, Portal Launcher, Resource Hub, Capture, Clipboard, Prompt Builder and Settings share one local-first workspace. |
| GitHub cockpit with project families/subcategories on the left | Implemented | Repository Hub uses the family/subcategory tree plus top multi-family filters. |
| Scrollable center repository workspace | Implemented | Repository Hub DataGrid remains the central editable surface. |
| Repository URL + Website + Server/backend + ChatGPT conversation columns | Implemented | All four are first-class project fields with separate include/copy controls. |
| Multi-repository selection and Copy/Open workflows | Implemented | Row selection, per-link inclusion, Copy All/column copy, Open selected and saved lists are present. |
| GitHub repository discovery | Implemented | Authenticated local gh CLI is preferred; public API is the fallback. |
| GitHub refresh must preserve manually maintained cockpit metadata | Implemented + regression tested | Site, Server, ChatGPT, custom category/subcategory and notes are preserved; an empty site can be filled from the GitHub homepage. |
| Separate Portal Launcher | Implemented | Portal workflow is separate from Repository Hub and has no multi-select copy workflow. |
| Portal main URL opens directly | Implemented | Open actions and pinned quick-ribbon items launch the main URL. |
| Project-specific portal sub-URLs added manually | Implemented | Each portal supports named project sub-links with Open/Copy actions. |
| Portal categories on the left filter the main surface | Implemented | All/Favorites/Pinned/category filters drive the center cards. |
| Common cloud/deploy/work portals available quickly | Implemented | Starter Pack adds GitHub, Azure, Fabric, Databricks, Vercel, Netlify, Cloudflare, ChatGPT, LinkedIn, Google Drive, Dropbox, OneDrive and Notion without overwriting existing entries. |
| Personal sites such as AtlasNote / AtlasCode / portfolio | Supported; user-maintained | Add them with + Portal or as project-specific sub-links. They are deliberately not hard-coded because these URLs are user/project state. |
| Quick links to GitHub / Google Drive / Dropbox / OneDrive / SharePoint / Notion | Implemented | Resource Hub stores exact destinations with provider, kind, group, notes, pin/favorite and Open/Copy. |
| Bring Repository Hub repos into the quick-link catalog | Implemented | Import repos updates GitHub resources idempotently while preserving custom labels/notes/pins. |
| Automatically browse every Drive/Dropbox/OneDrive account item | Not part of current contract | Resource Hub is a local quick-link catalog, not a connected cloud-file browser. Exact destinations are added/imported explicitly. |
| Capture / Stickies with URL, category, text and transcript | Implemented | Inbox, To-do, Quick note, Bookmark, Read later and Transcript share one model. |
| Capture export | Implemented | Single and bulk JSON/Markdown export with project context. |
| Quick clipboard library | Implemented | Categorized/tagged snippets with ribbon pins and keyboard copy. |
| Prompt manager | Implemented | Ordered modules, project variables, arbitrary variables, preview/copy and recent prompts. |
| Dashboard summary | Implemented | Capture board, curated portals/resources, saved repository lists, counts and quick ribbon. Full usage-history analytics were discussed as optional enrichment, not a required blocker. |
| Expanded / Compact / Sidebar modes | Implemented | Separate saved placement/size per layout. |
| Summon-style fast access | Implemented; native acceptance pending | Mouse 4/5, middle-click variants, hide-on-focus-loss, near-cursor placement, modal protection and failed-save protection are implemented. |
| Sidebar stays fast rather than becoming another full dashboard | Implemented + regression tested | Sidebar now shows curated pinned/favorite portals/resources, pinned snippets and active captures only. Manage actions transition to Compact editors instead of creating invisible items. |
| Keyboard/focus access | Implemented; native acceptance pending | Ctrl+K/Escape and Enter/Ctrl+C list policies are tested; custom focus visuals and automation labels are present. |
| Local-first persistence and safe import/export | Implemented + heavily regression tested | Atomic save/export, backup recovery, invalid/future schema guards, two-phase import and isolated data directories are covered. |
| Self-contained Windows package | Implemented; clean-machine acceptance pending | win-x64/win-arm64 publish script and CI x64 artifact path exist. |

## Remaining acceptance, not missing architecture

The remaining work before calling Power Ops a final Windows release is native validation rather than a missing module:

1. summon/hide behavior across real mouse buttons, modal dialogs, focus transitions and pinning;
2. mixed-DPI / multi-monitor visual validation;
3. keyboard focus order and Narrator/screen-reader validation in Expanded, Compact and Sidebar;
4. clean-machine validation of the self-contained Windows package;
5. final Fluent-style spacing/visual polish;
6. optional deeper disk-fault injection such as out-of-space or permissions changing mid-write.

## Scope note

Power Ops is intentionally a launcher/workspace companion. It does not need to become a live GitHub clone, browser automation tool, or authenticated Drive/Dropbox explorer. Repository discovery is automated where it materially helps; portal/resource URLs remain explicit local workspace data.
