# J Utility Palette

**Current checkpoint:** [Pro AI handover](handover/README.md) records preserved work, failures and remaining release outcomes. The full app currently fails compilation; the feature list below is not release acceptance.

A clean, lightweight Windows productivity companion for reusable prompts, project links, and temporary notes.

This repository intentionally starts from scratch. It is **not** a fork of Microsoft PowerToys.

## Development coordination

See [projectmanagement](projectmanagement/README.md) for the product vision, current sprint, technical audit, backlog, and development/test/lead handoffs. Start with [current status](projectmanagement/STATUS.md); the feature list below describes implemented scope, not verified release readiness.

## What V1 does

- Sidebar, Compact, and Expanded layouts from one app/data model.
- Three explicit window modes:
  - **Normal** — standard Windows window.
  - **Always on top** — remains above other applications.
  - **Summon / hide** — stays out of the way until a configured global mouse action shows it, then the same action hides it again.
- Summon bindings: Mouse Button 4, Mouse Button 5, middle click, or Ctrl + middle click.
- Optional hide-on-focus-loss and cursor-relative placement on the active monitor.
- Temporary **Keep open** pin without changing the saved window mode.
- Project Clipboard: Repo + Site + optional Extra, explicit Open/Copy actions, per-field copy switches, Copy All.
- Modular prompt composer with project variables and recent prompt history.
- Sticky notes with pinned/archive state.
- Local JSON workspace with backup plus import/export.
- No GitHub token, cloud account, Node/Electron runtime, or PowerToys source tree required.

## Summon mode

Open **Settings → Window behavior → Summon / hide**, then choose a mouse trigger. The default is **Mouse Button 5**. While Summon mode is active the configured mouse action is consumed by J Utility, preventing side-button Back/Forward navigation or middle-click side effects in the foreground application.

For mice without side buttons, **Ctrl + middle click** is the safest fallback because ordinary middle click remains untouched.

If `Open near the mouse cursor` is enabled, the window is positioned in native screen coordinates and clamped to the working area of the monitor under the cursor.

## Run on Windows

Prerequisite: .NET 8 SDK.

```powershell
.\scripts\run.ps1
```

Or:

```powershell
dotnet run --project .\src\JUtility.App\JUtility.App.csproj
```

The first launch creates:

```text
%LOCALAPPDATA%\JUtilityPalette\workspace.json
```

## Build and smoke-test

```powershell
.\scripts\build.ps1
```

GitHub Actions runs the same build/test path on `windows-latest`.

## Why the rewrite

The earlier prototype was developed inside `julian-passebecq/PowerToys_J`, a full Microsoft PowerToys fork. That was useful for learning Command Palette integration, but it is the wrong maintenance surface for this small personal app.

Useful behaviors were retained and rewritten in a much smaller architecture. See `docs/ARCHITECTURE.md` and `docs/ROADMAP.md`.
