# Power Ops / J Utility Palette — current repository direction

## Current live acceptance workflow — 2026-09-24

The current Power Ops implementation has been promoted to `main`.

Current promoted main revision:

`96fdaf580ff9556dbc7330ae8c7831fabf0cb8ae`

Windows CI **#304** passed the implementation before promotion, and Windows CI **#305** passed again on `main` after promotion.

### Live Windows testing does not happen on main

For the current native Windows acceptance pass, switch to:

`codex/to-be-tested-v1`

Then read that branch's:

- `AGENTS.md`
- `handover/CODEX_LIVE_TEST_V1.md`
- `handover/CODEX_LIVE_TEST_V1_RESULTS.md`

Do not commit live-acceptance fixes directly to `main`. Reproduce, fix, test, and commit them on `codex/to-be-tested-v1` first.

### Model policy for the live pass

Default:

**GPT-6 Luna — Medium**

Computer-control / desktop automation does **not** require High reasoning.

Stay on Luna Medium for normal UI navigation, live test execution, build/test commands, routine code changes, and straightforward bug fixes.

Escalate to **GPT-6 Luna — High** only when a confirmed defect is genuinely difficult, such as:

- a failure that persists after one careful reproduce/fix/retest cycle;
- an unclear root cause spanning multiple layers;
- difficult WPF focus, summon/hide, window placement, DPI, native interop, concurrency, persistence, or recovery behavior;
- a risky cross-cutting fix that needs deeper architectural reasoning.

Do not switch to High just because the task controls the Windows desktop.

Use an isolated `--data-dir` for destructive/stateful live tests. Never use the user's live workspace as a destructive fixture. Any untestable physical capability must be recorded as **BLOCKED**, not passed.

---

# Working in this repository

Read `projectmanagement/README.md` and `projectmanagement/STATUS.md` for durable project context. The latest user instruction takes precedence over older handoff material.

- Do not assume a successful core test run means the WPF app builds or behaves correctly. Check the full solution and, when doing acceptance, the real Windows UI.
- Use isolated fixture data for destructive, import, recovery, and failure tests.
- Keep durable checkpoints and identify the exact tested revision.
- Never label unexecuted checks as passed.
- Keep implementation changes focused on confirmed requirements or reproduced defects.
- A handoff does not automatically start another model or background job.
