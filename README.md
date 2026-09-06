# J Utility Palette

A clean, lightweight Windows productivity companion for the workflows that were useful in the earlier PowerToys experiment: reusable prompts, paired project links, and temporary notes.

This repository intentionally starts from scratch. It is **not** a fork of Microsoft PowerToys.

## What V1 does

- Sidebar, Compact, and Expanded layouts from one app/data model.
- App-native **Always On Top** toggle.
- Project Clipboard: Repo + Site + optional Extra, explicit Open/Copy actions, per-field copy switches, Copy All.
- Modular prompt composer with project variables and recent prompt history.
- Sticky notes with pinned/archive state.
- Local JSON workspace with backup plus import/export.
- No GitHub token, cloud account, Node/Electron runtime, or PowerToys source tree required.

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
