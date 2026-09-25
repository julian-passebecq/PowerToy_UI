# Power Ops V2 foundation branch

Current task, 2026-09-24: the user authorized a broader modular-workspace V2/V3 design and implementation of a useful V2 foundation. This supersedes the inherited V1 acceptance-only restriction on THIS branch.

Branch: `codex/power-ops-v2-workspaces`.
Base: `df3aa1626fee3c96f6008793214852b273595ffc` (Explorer checkpoint, schema v7, Windows CI #312 passed).

Do not modify `codex/to-be-tested-v1` or `main` for this work, and do not merge. V1 native acceptance remains separate and incomplete. Existing source success is not native desktop evidence.

## Scope and architecture

Read `docs/v2/ARCHITECTURE.md`, `docs/v2/QUICK_ACTIONS_MX_MASTER.md`, `docs/v2/CLAUDE_HANDOFF.md`, and `docs/v2/DELIVERY.md` when available. This is an incremental WPF shell, not an Electron rewrite, GitHub Desktop clone, browser fleet, monitoring server or credential vault.

Share the existing business data. Save UI sessions separately in `shell-workspaces.json` (format powerops-shell, version 1); preserve business workspace schema v7. Workspaces are named VIEWS, not access-control boundaries. Saved tabs currently preserve module/search/filter state, not editor undo or every control's selection/scroll.

No new polling services, subprocess inventory probes, external writes, automatic updates or credentials. Navigation visibility does NOT yet unload the inherited eager WPF editors. State that limitation explicitly.

## Verification

Run `.\scripts\build.ps1` (full Release solution, existing smoke suite, new package-free workspace tests). Record exact commit and run evidence. Use a NEW isolated `--data-dir` for native tests. Never use personal data as a destructive fixture. Do not call the inherited V1-only launcher, which asserts a different branch.

Do not label unexecuted Windows/monitor/mouse/clipboard checks PASS. If computer control is unavailable, use user-assisted steps and identify who observed each result.

## Delivery

Keep changes focused; no production deployments or main merge. Secret-bearing values, user data and machine paths must not enter public logs or exports unnoticed. Content exports are review formats, not full backups; layout imports must validate and preview before replacement. Preserve malformed or future-state bytes instead of silently resetting them.

Model choice is the user's session setting; this file does not configure or launch an agent. Use the selected efficient model for ordinary work and request escalation only for a reproducible difficult defect.


## V2.1 continuation

The next approved direction is the shared Quick Actions layer: typed action catalog -> configurable global summon -> Quick Shelf -> Quick Ring -> per-workspace action selection -> MX Master/Logi Options+ setup guidance. Power Ops must remain usable without Logitech hardware. Do not write Logitech-specific drivers. Keep Gmail/Attention Center, cloud usage, telemetry and other external providers as later opt-in adapters until the action/provider lifecycle is stable.

## V2.2 Credentials & IDs + Scratchpad (2026-09-25)

Galaxy next-pass handoff for Power Ops: local Credentials & IDs (secret values only in Windows Credential Manager, opaque per-data-folder targets), .env registry (paths and key names only, never values, never edits files), quick capture `+` window reusing Capture/Clipboard, and a reviewed one-way `powerops.atlasnote-handoff/1` export. Read `docs/v2/CREDENTIALS_AND_SCRATCHPAD.md` for boundaries, evidence and the BLOCKED manual checks. Tests use synthetic secrets only; never put a real secret in a fixture, log or commit.
