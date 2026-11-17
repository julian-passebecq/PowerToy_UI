# Power Ops / J Utility Palette (PowerToy_UI)

A standalone Windows productivity shell (WPF, .NET 8): links, portals, captures, clipboard, prompts, Quick Ring and Quick Shelf, credentials and IDs, file tray. Not a PowerToys fork. Power Ring lives in its own repo (`powertoy-ring`) and is not built here. Context: `projectmanagement/STATUS.md`, `handoff/RESUME.md`, `docs/v2/`.

## Build and test
- `.\scripts\build.ps1` (Release solution, smoke suite, workspace tests). Native release gate: `tests/native/release-acceptance.ps1` and the feature suites in `tests/native/`; `tests/native/package-lifecycle.ps1` for a package.
- CI: GitHub Actions `windows-ci.yml` (public repo). No GitLab mirror.

## What to protect
- The native tests drive the real desktop: run them with a new isolated `--data-dir` on a machine nobody is using, never against personal data or the real Downloads folder.
- Business data keeps workspace schema v7; UI sessions live separately in `shell-workspaces.json`. Malformed or newer bytes are kept, never silently reset.
- Secrets live only in Windows Credential Manager; the .env registry stores paths and key names, never values. Tests use synthetic secrets.
- No new polling services, external writes, automatic updates or stored credentials.
- Unexecuted Windows, mouse or clipboard checks are NOT RUN, never PASS.

How work flows: the fleet guideline every session loads (`~/.claude/CLAUDE.md`, `~/.codex/AGENTS.md`). CI recipe and runners: datapass-fleet-facade `AGENTS.md`.
