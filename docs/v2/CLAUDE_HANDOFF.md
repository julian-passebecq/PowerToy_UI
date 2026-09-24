# Claude takeover handoff - Power Ops V2/V2.1

Updated: 2026-09-24.

## Mission

Take over **Power Ops / J Utility Palette** as the implementation/review agent for the V2 preview and the next V2.1 Quick Actions increment.

The user wants a configurable, lightweight Windows companion for project context, launch/resume, capture, Git/AI activity, environment inventory, optional monitoring and fast mouse/keyboard access. It is **not** a Microsoft PowerToys fork and should not become a replacement IDE, browser, Git client, email client, monitoring stack or password vault.

Repository:
`https://github.com/julian-passebecq/PowerToy_UI`

Work branch:
`codex/power-ops-v2-workspaces`

Draft PR:
`#4 - V2 foundation: saved workspaces, tabs, launchpad and selective JSON export`

Do **not** work directly on `main` or `codex/to-be-tested-v1`. Do not merge.

## Starting state

The V2 line started from the Explorer/schema-v7 checkpoint:
`df3aa1626fee3c96f6008793214852b273595ffc`

Key V2 implementation checkpoint:
`d151051d9b40c4cde2982e2e0cffc7821b6083ca`

Later evidence/handoff checkpoint:
`e14e2f89b28e38226796a7c48e96ae8d1e29a26e`

Quick Actions architecture was added after that. **Fetch and verify the current remote head before doing any work; do not assume the SHA above is still current.**

Known CI evidence:
- Windows CI #314 passed the implementation checkpoint.
- Windows CI #315 passed the later documentation checkpoint.
- The gate includes full Release WPF build, inherited smoke/regression checks and the new workspace/export regression executable.
- New workspace/export suite was 21/21 PASS at the implementation checkpoint.

Native V2 Windows UI acceptance is still outstanding. A green source/CI gate is not native interaction proof.

## Read first

1. `AGENTS.md`
2. `docs/v2/ARCHITECTURE.md`
3. `docs/v2/QUICK_ACTIONS_MX_MASTER.md`
4. `docs/v2/DELIVERY.md`
5. `docs/v2/CODEX_HANDOFF.md`
6. current diff against the V1 base

Only inspect AtlasNote or Mongoku when needed for integration contracts. Do not modify those repositories as part of this takeover.

## What is already implemented

The V2 preview adds:
- named workspace views and five starter presets;
- per-workspace module visibility;
- grouped navigation;
- module tabs with saved search/filter state;
- close/reopen/next/previous/select-tab keyboard behavior outside text editors;
- saved view bookmarks;
- per-workspace inherited Expanded/Compact/Sidebar mode and quick-ribbon visibility;
- separate bounded `shell-workspaces.json`;
- all/current/selected content review JSON export;
- separate layout JSON export/import with validation and preview;
- saved-destination Launchpad, no embedded browser fleet;
- manual/on-demand PATH inventory that does not execute discovered programs;
- in-app feature catalog;
- package-free workspace/export regression tests.

Business data remains in the inherited schema-v7 workspace. Workspace views are presentation/context state, not copies of business data and not authorization boundaries.

Known limitation: inherited V1 WPF editors are still eagerly constructed. Hiding a module does not unload it.

## Approved next direction: Quick Actions

Build one shared action architecture:

```text
                  Power Ops action catalog
        /             |              |             \
 Full UI         Quick Ring      Quick Shelf     shortcuts/MX
```

Every surface invokes the same typed action. Do not duplicate execution logic.

Recommended implementation order:
1. typed action catalog and safe built-in actions;
2. configurable global summon/hotkey abstraction;
3. Quick Shelf;
4. Quick Ring;
5. per-workspace action selection/order;
6. Interaction settings + MX Master/Logi Options+ mapping guide;
7. native acceptance and performance measurements.

Initial actions:
- show/hide/open Power Ops;
- screenshot/region capture;
- Quick Capture;
- Clipboard Library;
- Downloads;
- configured Explorer folder;
- configured terminal;
- Resume current workspace;
- next/previous workspace;
- next/previous tab.

Do not implement Gmail, cloud billing, hardware telemetry and agent hooks all at once with this framework.

## MX Master principle

Power Ops must not require Logitech hardware.

Preferred path:
`MX Master -> Logi Options+ -> shortcut/Smart Action -> Power Ops action`.

Do not write a Logitech driver. Avoid having Options+ and Power Ops global mouse hooks consume the same physical button.

Provide a setup guide with copyable shortcuts. Existing direct mouse summon may remain optional.

## Daily UX target

The product should help answer:
- What context/project am I in?
- What needs my attention?
- What should I resume?
- Can I capture/copy/open it in one action?

Longer-term surfaces include:
- Resume card;
- Attention Center;
- Git + CI + deployment evidence;
- Codex/Claude event summaries;
- AtlasNote task/capture bridge;
- Mongoku read-only saved-report summary;
- selected cloud allowance/Grafana observations;
- optional CPU/RAM/battery/screen-time providers.

These are planned adapters, not current V2 claims.

## Gmail / verification codes

Future unless the user explicitly promotes it into the current sprint.

Do not build a full mail client. Target an opt-in Mail Peek:
- sender/service, subject, age;
- likely short verification code;
- Copy code / Open email;
- code held in memory only and expires quickly;
- never save/export/log OTP values;
- manual refresh first, later low-frequency provider refresh.

Use a supported authenticated provider contract. Do not scrape browser sessions or plaintext credentials.

## Git / GitHub / GitHub Desktop / GitLens

Power Ops should show lightweight evidence and launch the owning tool, not clone GitHub Desktop/GitLens.

Future summary:
- repository;
- branch;
- clean/dirty;
- local last commit;
- ahead/behind with freshness/source;
- PR/CI/deployment observations;
- open GitHub / GitHub Desktop / VS Code / terminal / folder.

Never infer push time or agent identity from unsupported evidence.

## Agent activity

Keep separate:
- process running;
- session/turn ended;
- commit exists;
- branch pushed;
- tests passed;
- CI passed;
- deployment succeeded.

A completed turn is not proof all work is done. Do not persist private conversation transcripts by default.

## Performance architecture

The application may have many features but disabled features must be cheap.

Rules:
- disabled provider = no timer/socket/child process;
- hidden provider normally stops unless explicitly allowed in background;
- one provider can feed several widgets;
- observations have source, observedAt and freshness;
- missing data = Unknown, not zero/healthy;
- continuous telemetry does not go sample-by-sample into workspace JSON;
- no full-disk scans or running discovered executables at startup;
- no permanent browser/webview fleet;
- network adapters opt-in and back off.

True module/provider lifecycle management remains a next architectural requirement. Do not describe navigation visibility as runtime unloading.

## Security and destructive actions

No plaintext password or `.env` vault in workspace JSON.

Prefer OS/external credential references. Never export secret values in review JSON.

System power, process force-close, cleanup/delete, cloud writes and dependency updates require explicit scope, preview where practical, confirmation and rollback/quarantine where possible.

Quick Ring must not place destructive actions in its default gesture path.

## JSON contracts

Keep distinct:
- `workspace.json`: domain/business state;
- `shell-workspaces.json`: view/session state;
- content review JSON: scoped review/share format, not recovery;
- future full backup: separate manifest/assets/hashes/recovery workflow.

AI-generated config is data, not code: validate -> preview -> recovery point -> apply.

## Development procedure

Before editing:

```powershell
git status
git branch --show-current
git log -1 --oneline
git fetch origin
```

Confirm branch:
`codex/power-ops-v2-workspaces`

Run:
```powershell
.\scripts\build.ps1
```

Use small focused commits. Push only to the V2 branch. Keep PR #4 draft.

Do not:
- merge;
- rewrite V1 acceptance history;
- touch personal test data;
- claim unavailable native tests passed;
- silently install software;
- alter PATH/registry/services/firewall;
- introduce arbitrary shell execution through imported JSON.

## Native testing

Use a new isolated `--data-dir` and a separate V2 worktree/clone if the user's V1 tree is still active.

If desktop control is unavailable, use manual-assisted tests one exact scenario at a time and record the observer.

For Quick Actions test:
- global hotkey collision/cleanup;
- summon/hide with another foreground app;
- every available monitor;
- negative coordinates/mixed DPI where available;
- focus restoration;
- Escape/outside click;
- keyboard accessibility;
- Shelf pin/auto-hide;
- per-workspace action persistence;
- Downloads;
- screenshot;
- text-editing shortcut conflicts;
- no duplicate mouse event with Options+;
- idle CPU/RAM/disk/network before/after overlays.

## First Claude takeover deliverable

1. Audit current branch/diff and architecture for concrete inconsistencies.
2. Fix any build/test defect before feature work.
3. Implement only the first coherent Quick Actions slice that can be tested safely.
4. Add regression tests.
5. Run the full build gate.
6. Update `docs/v2/DELIVERY.md` with exact SHA, evidence and limitations.
7. If native interaction cannot be tested, write a small user-assisted checklist rather than claiming success.
8. Commit and push to `codex/power-ops-v2-workspaces`.
9. Leave PR #4 draft.
10. Report final SHA, tests, changed architecture, remaining V2.1 work and next recommended slice.

Do not jump directly to every V3 provider. Preserve the lightweight architecture while growing the product.
