# Roadmap

Current delivery priorities, acceptance gates, and the active sprint are maintained in [projectmanagement/BACKLOG.md](../projectmanagement/BACKLOG.md) and [STATUS.md](../projectmanagement/STATUS.md). The list below records the original V1 scope and candidate ideas; it does not supersede the current sprint plan.

## V1 - implemented in the clean repo

- adaptive Sidebar / Compact / Expanded window sizes;
- app-native Always On Top toggle;
- project clipboard with field-level copy flags and Copy All;
- explicit Open vs Copy actions;
- optional Extra column;
- modular prompt composer with deterministic ordering;
- built-in `{{project}}`, `{{repo}}`, `{{site}}`, `{{extra}}` variables;
- recent composed prompts;
- sticky notes with pin/archive state;
- local JSON persistence, backup, import, and export;
- Windows CI plus package-free smoke tests.

## Next after V1 is green

1. Add archive filters and safer project editing validation in the UI.
2. Add dynamic editors for arbitrary `{{variable}}` placeholders.
3. Remember per-view window position/size with off-screen recovery.
4. Add a global hotkey only if it proves more useful than pinning the app.
5. Add optional GitHub latest-commit status with manual refresh and no token in JSON.
6. Package a self-contained Windows release.

## Still intentionally out of scope

- CPU/GPU monitoring;
- power-plan management;
- f.lux replacement;
- news/stocks feeds;
- browser automation;
- cloud synchronization;
- rewriting Microsoft PowerToys core.
