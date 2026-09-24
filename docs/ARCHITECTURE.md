# Architecture

This page describes the original implementation. The target boundaries, reliability invariants, and phased changes are maintained in [projectmanagement/VISION_ARCHITECTURE.md](../projectmanagement/VISION_ARCHITECTURE.md).

## Why this repository is separate

The previous prototype lived inside a full fork of Microsoft PowerToys. That made a very small personal utility expensive to clone, build, review, and maintain.

`PowerToy_UI` therefore starts from an empty repository and keeps only the product ideas that were useful.

## V1 structure

- `JUtility.Core` - models, JSON persistence, project clipboard formatting, URL normalization, prompt composition.
- `JUtility.App` - a dependency-free WPF desktop shell for Windows.
- `JUtility.SmokeTests` - executable smoke tests with no test-framework package dependency.

The UI does not depend on PowerToys internals or Command Palette packages.

## Deliberate choices

### WPF instead of the PowerToys fork

The current requirement is a lightweight personal Windows companion, not a PowerToys contribution. WPF gives the app a real standalone window, native clipboard/browser integration, `Topmost`, and simple JSON-backed state with no dependency on PowerToys release churn.

A future Command Palette extension can be added as a thin launcher over `JUtility.Core` if it becomes useful again.

### Local-first JSON

Data is stored in `%LOCALAPPDATA%\JUtilityPalette\workspace.json`. Before replacing an existing workspace, the store copies it to `workspace.backup.json`.

### Explicit interaction semantics

- Open buttons open links.
- Copy buttons copy text.
- Hiding the Extra column does not delete Extra data.
- Notes can be archived/restored without deletion.
