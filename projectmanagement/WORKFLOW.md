# Delivery workflow

## Responsibilities

The lead decides the product boundaries, architecture, acceptance criteria, and next sprint, and reviews the actual code logic at the end. The developer implements the approved design and tests needed to build it responsibly. The light tester independently verifies it, supports review with reproductions and focused code inspection, and maintains project records. Passing tests alone never substitutes for lead acceptance.

## Work cadence

- One sprint is one coherent outcome across several substantial passes. A pass is a useful implementation milestone, not a five-minute task or a separate request for permission.
- The developer continues through every approved pass in the current assignment. Do not stop after each file, commit, passing test, or checkpoint to ask for a new pass.
- Hours of work are acceptable when the scope justifies them. Do not pad work to meet an hour target or invent unrelated features to keep running.
- Use internal checkpoints after each pass and before context exhaustion: progress, exact next step, current tests, unresolved decisions. Commit cohesive work when repository authorization permits; otherwise record the uncommitted state precisely.
- Independent testing normally begins after all development passes. The developer still builds and runs relevant regression checks during implementation.
- If execution limits force the turn to end, write `PAUSED_RESUMABLE` with the underlying phase and a one-line continuation prompt. This is a runtime limitation, not a new approval gate. Do not promise that work will continue while no agent is running.
- At the first sprint review, record whether the user had to intervene too often. Adjust future batch size using completed outcomes and actual interruption count, not arbitrary token estimates.

## State gates

`PLANNED -> DEVELOPING -> READY_FOR_TEST -> TESTING -> READY_FOR_LEAD -> ACCEPTED`

- `READY_FOR_TEST`: all approved passes implemented, developer checks complete, handoff identifies code revision and known gaps. If essential checks cannot run, use `BLOCKED` with evidence instead of claiming readiness.
- `TESTING -> REWORK_REQUIRED -> DEVELOPING`: tester found routine implementation defects with clear expected behavior. Developer fixes them in one bounded batch; tester reruns affected checks and relevant regressions against the new revision.
- `READY_FOR_LEAD`: independent report is complete and contains no unresolved mandatory failed/blocked checks. The tester recommends acceptance; only the lead accepts.
- `BLOCKED`: required environment/input prevents progress; name the exact dependency. Work on unrelated authorized items where possible.
- `LEAD_DECISION_REQUIRED`: conflicting requirements, architecture uncertainty, or unresolved serious defect. Bring the smallest concrete decision and supporting evidence.
- `PAUSED_RESUMABLE`: save the underlying phase, completed work, and next step so a continuation can resume directly.
- `ACCEPTED` does not mean published or merged. Record integration separately. Lead must write the next sprint before it starts.

## When to call the lead

Call the lead at the end of every sprint, before a new sprint starts. Call earlier for:

1. A new risk of losing user data, an unclear migration/recovery policy, or a proposed change to an architecture invariant that the sprint has not already resolved.
2. A new dependency, storage format, background service, network feature, or cross-cutting rewrite outside approved scope.
3. A failed defect after two materially different repair attempts, or a developer/tester disagreement about expected behavior.
4. A change that makes mandatory acceptance criteria infeasible or requires dropping scope.

Routine compile errors, straightforward test failures, formatting, and implementation choices already covered by the sprint return to the developer. The tester must include reproduction steps and expected/actual behavior rather than requesting an open-ended review.

## Branches and evidence

- Default to one development branch per sprint, named `codex/sNN-short-description`. Avoid a branch for every pass.
- Inspect local changes before switching branches. Preserve user work and planning changes; do not discard or overwrite unrelated files.
- Tester uses the candidate revision or an isolated checkout. Never switch a checkout beneath an active developer.
- Record base/head SHA, dirty paths, report, test outcome, and integration state in BRANCHES.md. Inventory local and known remote refs; label stale/offline information. Inspect remote branches/PRs only when available; never invent their status.
- Evidence applies to the recorded code. A subsequent product change invalidates affected results. Markdown-only report changes can follow testing if explicitly identified.
- No automatic push, merge, deployment, release, or messages to other people are authorized by this workflow.

## Required handoff endings

Developer: `READY_FOR_TEST — all S01 development passes complete. Run the light testing model with the prompt below.`

Tester with routine failures: `REWORK_REQUIRED — send this defect batch to the development model.`

Tester after verification: `READY_FOR_LEAD — independent verification complete. Ask the technical lead to audit the code and decide the next sprint.`

Use these endings only when the corresponding gate is actually met. Otherwise name the blocker or lead decision.
