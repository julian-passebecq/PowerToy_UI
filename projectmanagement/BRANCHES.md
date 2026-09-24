# Branch and integration register

## 2026-09-11 handover

`codex/pro-ai-handover-2026-09-11` preserves previously uncommitted planning and the Pro AI handover. Application base remains `d0a5ff8`, confirmed on `origin/main` by fetch. No other local branches, stashes, unpushed commits or worktrees were found before handover. See [delivery audit](../handover/DELIVERY_AUDIT.md). The inventory below is historical.

Inventory captured locally on 2026-09-08. No remote fetch, PR lookup, push or merge was performed. Remote-tracking refs below may be stale.

| Branch/ref | Observed head/base | Purpose | Test status | Integration |
| --- | --- | --- | --- | --- |
| `main` | `d0a5ff8` | Current application baseline | Full build fails; 7 core smoke checks pass | Current checkout; initially clean |
| `origin/main` | `d0a5ff8` locally observed | Known remote-tracking baseline | Remote CI not inspected | Matches local main at inventory time |
| `origin/HEAD` | symbolic to `origin/main` | Known remote default | Not a separate test candidate | Local metadata only |
| `codex/s01-reliable-foundation` | Planned base `d0a5ff8` plus planning docs | S01 implementation | Not started | Branch not created yet |

Planning task changes: root AGENTS.md, projectmanagement documents, and documentation pointers. They are uncommitted at this inventory; no production code was changed.

## Update per candidate

Record branch, exact base/head SHA, dirty production paths, pass/sprint, developer handoff, test report, known failures, PR URL if verified, CI result if verified, and accepted/merged status separately. Include other branches encountered rather than treating them as accepted or obsolete by assumption. Never delete branches or worktrees as bookkeeping without authorization.
