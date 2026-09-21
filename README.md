# J Utility Palette · Power Ops

A local-first Windows productivity companion for project links, service portals, cross-service resource shortcuts, quick captures, reusable clipboard text, and prompt composition.

This repository is intentionally standalone. It is **not** a fork of Microsoft PowerToys. The older `PowerToys_J` repository remains historical experimentation only.

> Current development direction: [Power Ops suite](docs/POWER_OPS_SUITE.md). The preserved September 11 handover remains available in [handover/README.md](handover/README.md), but its original compile blocker has now been fixed on the active Power Ops branch.

## Power Ops modules

### Dashboard

A summary surface rather than another editor:

- repository / portal / resource / capture / clipboard counts;
- quick-launch ribbon;
- capture board for Inbox, To-do, Quick notes, Bookmarks, Read later, and Transcript;
- saved repository lists;
- direct-open portal shortcuts.

### Repository Hub

A development-project row can hold:

- GitHub repository URL;
- website URL;
- server / hosting / backend URL;
- ChatGPT conversation URL;
- project family + subcategory;
- lightweight GitHub metadata.

Repository selection is multi-row. Selecting a row includes every available primary URL by default, while each URL cell has its own inclusion checkbox. Bulk actions copy all selected URLs or only GitHub / website / server / ChatGPT links.

The top project-family ribbon supports multi-select filters. The second left panel is a collapsible family/subcategory tree. Saved lists preserve both repository membership and per-link inclusion choices.

**GitHub sync:** Repository Hub first tries the locally authenticated `gh` CLI, allowing private repositories without placing a token in the workspace. If `gh` is unavailable or not authenticated, it falls back to GitHub's public repository API.

### Portal Launcher

For direct access to Fabric, Azure, Databricks, Vercel, Netlify, Cloudflare, GitHub, LinkedIn, personal sites, or any other frequently used portal.

Each portal has:

- main URL;
- category and short icon key;
- optional top-ribbon pin;
- zero or more project-specific sub-links.

There is deliberately **no multi-selection workflow** here: open the main portal directly or open one of its saved sub-links.

### Resource Hub

A cross-service quick-link surface for exact destinations rather than service home pages.

Typical providers:

- GitHub repositories;
- Google Drive folders/documents;
- Dropbox folders;
- OneDrive / SharePoint locations;
- Notion pages;
- any other HTTPS resource.

Resource Hub has provider filters in the top ribbon and second left panel, Favorites / Pinned views, one-click Open / Copy, clipboard-URL capture, and a repository import action that mirrors active GitHub repositories from Repository Hub without duplicating repository-management logic.

**Portal Launcher vs Resource Hub:** Portal Launcher is for service entry points such as the Vercel or Fabric home page. Resource Hub is for exact destinations such as a specific project repository, Drive folder, Dropbox folder, document, or dashboard.

### Capture

One local capture model supports:

- Inbox
- To-do
- Quick note
- Bookmark
- Read later
- Transcript

Captures support subject/category, source URL, labels, status, priority, pin/completed/archive state, and long-form text. A single capture or the complete active capture collection can be exported as JSON or Markdown.

### Clipboard

A one-click library for premade prompts, commands, URLs, signatures, fragments, and other reusable text. Pinned snippets can appear in the quick ribbon.

### Prompt Builder

The existing modular prompt composer remains separate from the quick Clipboard surface. It supports ordered modules, recent prompt history, and built-in project variables including:

`{{project}}`, `{{repo}}`, `{{site}}`, `{{server}}`, `{{chatgpt}}`, and `{{extra}}`.

Any other placeholder such as `{{environment}}` or `{{region}}` is detected automatically and exposed as an input in Prompt Builder before preview/copy.

## Window modes

The same local workspace supports three layouts:

- **Expanded** — full Power Ops navigation, secondary tree/categories, quick ribbon, and center workspace.
- **Compact** — focused module view with less navigation chrome.
- **Sidebar** — fast summon-style access to pinned/favorite resources, portals, clipboard snippets, and captures.

Window behavior can be Normal, Always on top, or Summon / hide. Summon bindings support Mouse Button 4, Mouse Button 5, middle click, or Ctrl + middle click.

## Local-first storage

Workspace data is stored at:

```text
%LOCALAPPDATA%\JUtilityPalette\workspace.json
```

The store maintains a backup and supports explicit JSON import/export. Current schema handling rejects unsupported future versions rather than silently rewriting them. Corrupt primary bytes are preserved as recovery evidence before a valid backup is restored; malformed workspaces without a valid backup are never silently reset.

Workspace import is two-phase in the UI: the selected file is validated and summarized first, and the current workspace is replaced only after explicit confirmation. Export uses a same-directory temporary file and cannot target the active primary or backup.

No GitHub token is written to the workspace.

## Run on Windows

Prerequisite: .NET 8 SDK.

```powershell
.\scripts\run.ps1
```

Or:

```powershell
dotnet run --project .\src\JUtility.App\JUtility.App.csproj
```

For isolated testing or a separate local workspace:

```powershell
.\artifacts\PowerOps-win-x64\JUtilityPalette.exe --data-dir "D:\PowerOps-Test"
```

Single-instance ownership is scoped to the canonical data directory: the same workspace cannot have two writers, while deliberately separate `--data-dir` workspaces can run independently.

## Build and smoke-test

```powershell
.\scripts\build.ps1
```

GitHub Actions builds the full WPF solution on `windows-latest` and runs package-free smoke/regression checks.

## Self-contained Windows package

Create a portable Windows x64 package that does not require a preinstalled .NET runtime:

```powershell
.\scripts\publish.ps1 -Runtime win-x64
```

Output:

```text
artifacts\PowerOps-win-x64\
artifacts\PowerOps-win-x64.zip
```

Run `JUtilityPalette.exe` from the extracted folder. The package does not embed the user's workspace or credentials; the workspace remains under `%LOCALAPPDATA%\JUtilityPalette`.

The Windows CI workflow also supports a manual **workflow_dispatch** run that builds and uploads the same x64 ZIP as an artifact.

## Architecture

- `JUtility.Core` — models, local workspace persistence, URL normalization, project clipboard formatting, prompt composition.
- `JUtility.App` — WPF shell, Windows integration, summon behavior, GitHub discovery, browser/clipboard actions.
- `JUtility.SmokeTests` — isolated workspace and core behavior regression checks.

See [docs/POWER_OPS_SUITE.md](docs/POWER_OPS_SUITE.md) for the current product boundary and navigation model.
