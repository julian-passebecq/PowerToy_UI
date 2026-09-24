# Codex live acceptance test v1 — current priority

This branch, `codex/to-be-tested-v1`, exists specifically for a **live Windows acceptance pass of Power Ops / J Utility Palette**.

## Model policy

Default model/reasoning for this acceptance pass:

**GPT-6 Luna — Medium**

Computer-control / desktop automation does **not** by itself require High reasoning. Stay on Luna Medium for the normal test matrix, routine code changes, build/test commands, and straightforward bug fixes.

Escalate the session to **GPT-6 Luna — High** only when a confirmed defect is genuinely difficult, for example:

- the same failure persists after one careful reproduce/fix/retest cycle;
- the root cause crosses multiple layers or is still ambiguous after inspection;
- WPF focus, summon/hide, window placement, mixed DPI, native interop, concurrency, persistence/recovery, or other stateful behavior needs deeper reasoning;
- a proposed fix is risky enough that broader architectural reasoning is warranted.

Do not switch to High merely to click through the UI or control the Windows desktop. Do not downgrade the acceptance pass to Light just to save tokens if doing so would reduce reliability.

Before doing anything else, read and follow:

`handover/CODEX_LIVE_TEST_V1.md`

The priority for this branch is not another source-only review. **Run the application on the user's Windows computer and test it interactively using the available computer-control / desktop-automation capability.**

Use an isolated `--data-dir` for all write/delete/import/export/media scenarios. Do not use personal files as destructive fixtures. If computer-control or specific hardware (for example Mouse Button 4/5 or a second monitor) is unavailable, mark those scenarios BLOCKED instead of claiming success.

Confirmed defects may be fixed on this branch only. Keep changes focused, add regression coverage when practical, rerun `.\scripts\build.ps1`, retest the live scenario, and record results in:

`handover/CODEX_LIVE_TEST_V1_RESULTS.md`

Do not merge this branch and do not push live-acceptance fixes directly to `main`.

---

# Working in this repository

## Current user direction — 2026-09-24

The Power Ops implementation has been promoted to `main`. The dedicated live Windows acceptance pass remains isolated on `codex/to-be-tested-v1`.

For this acceptance branch, the live-test instructions above override older project-management handoff text when there is any conflict.

Read `projectmanagement/README.md` and `projectmanagement/STATUS.md` for durable project context.

- Do not assume a successful core test run means the WPF app works interactively. Run the real app and record native command exit codes.
- Use isolated fixture data for destructive, import, recovery, and failure tests. Never use the user's live workspace as a test fixture.
- Keep durable evidence in the results ledger; identify the exact tested revision and any uncommitted changes.
- Never label unexecuted checks as passed.
- Do not start unrelated implementation work during this acceptance pass.
- This workflow does not automatically start another model or create background jobs. If High reasoning is needed, explicitly escalate only for the difficult defect and continue the same acceptance objective.
