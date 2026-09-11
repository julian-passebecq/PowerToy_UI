# S01 — Reliable foundation

State: PLANNED. Owner: development model, then independent light tester, then technical lead. Starting code: `d0a5ff8`. Branch: `codex/s01-reliable-foundation` (planned, not yet created).

## Outcome

The application builds, saves edits predictably, and cannot silently replace useful workspace data with a corrupt, unrelated, or unsupported file. Failures are visible and recoverable. This is the prerequisite for adding more features to an app containing personal notes and reusable text.

Execute A, B, C continuously. Each is a substantial engineering pass with implementation, focused regression checks, and a checkpoint. The developer does not wait for the light model between passes. Independent testing starts after C.

## Pass A — Build gate and trustworthy data input

Scope: B01, B02, B04, initial B09. Likely files: GlobalMouseSummonService.cs, scripts, WorkspaceState.cs, WorkspaceStore.cs, new focused validation/result types in Core, test runner/fixtures.

1. Fix the CS0136 collision with distinct down/up button names. Preserve hook matching and suppression behavior; this pass is not a hook rewrite.
2. Make build/run scripts inspect native exit status immediately and stop with nonzero on failure. No smoke execution after failed restore/build. Keep CI exercising an equivalent verified path; demonstrate failure propagation with a controlled failing command/stub in isolation.
3. Separate parsing/validation/migration from disk publication. Use a structured result or typed exceptions distinguishing missing, malformed, unsupported, inaccessible and valid data. Avoid catch-all fallback-to-seed behavior.
4. Accept supported explicit v1/v2 files and valid explicitly versioned empty workspaces. Missing version may migrate as v1 only when at least one recognized collection has the correct shape. Reject `{}`, unrelated JSON, wrong collection types, null list entries, unsupported/invalid versions, unknown enum values, and duplicate/empty IDs. Missing optional collections/preferences receive defaults. Missing item IDs may receive generated IDs through legacy migration; explicitly invalid IDs must not silently remap note associations.
5. Normalize optional nullable strings to empty and label fields to documented defaults. Null collection properties may normalize to empty for compatible legacy files; null entries must be rejected with a useful field path. Cap/deduplicate recent history consistently at the existing 30-entry policy. Preserve supported content and IDs through migration. A dangling note ProjectId is retained with a validation warning, not deleted or silently reassigned.
6. Keep validation detached from caller-owned state. Save/export validation cannot mutate live objects. Existing URL text is preserved in this sprint; do not make older workspaces unloadable merely because a stored URL is invalid. Open remains constrained to web URLs. Full editor URL feedback is B15.
7. Introduce an explicit `--data-dir <path>` app option and injectable store/session dependencies for isolated integration testing. Reject malformed options visibly. Default directory remains unchanged. This option is also used to key writer ownership in B.

Milestone A: full solution builds; legacy/good/bad-input tests pass; scripts demonstrably fail correctly; input policy documented. Checkpoint, then continue to B.

## Pass B — Durable workspace transactions and ownership

Scope: B03, B05, B09. Likely files: Core persistence, App startup/composition, a small workspace session/coordinator, tests.

1. Acquire one-writer ownership per canonical data directory before any load/seed/write. A second app instance for the same directory exits gracefully with a visible explanation. A different data directory is allowed. Release ownership on normal exit and process failure. Existing-window activation is deferred to S02.
2. Use validated detached snapshots, unique same-directory temporary files, appropriate flush/close, and a replacement operation. Normal saves retain a last-known-good previous primary as backup. Avoid rotating a corrupt/unsupported primary over a valid backup. Test actual Windows filesystem behavior in addition to any injected failures.
3. Classify startup outcomes: both files absent => seed; valid primary => load; corrupt/missing primary plus valid supported backup => report recoverable state and offer recovery; both unusable => preserve files and offer choosing a valid import or exiting. Unsupported future primary and access failures must not silently fall back or seed. No automatic destructive reset flow in S01.
4. Recovery preserves corrupt evidence to a unique recovery file before replacing the primary, keeps the valid backup, and reports what happened. If preservation/publication fails, keep source files and present a retryable error. Ignore stale temporary files as authoritative input; clean only files demonstrably created by the current operation.
5. Separate import preparation from commitment. Read and validate into a candidate without changing live state or workspace files. Commit a confirmed replacement transactionally, retaining the previous valid workspace as backup; failed commit keeps the old session. The selected import source must not be changed during preparation.
6. Export a validated snapshot safely. Reject exports targeting the active primary, backup, recovery files, or managed temp paths; use canonical comparison appropriate to Windows. Same-target replacement of an ordinary export should not leave a truncated file on failure. Do not delete or alter the user-selected import source except when that same managed path is explicitly part of the confirmed recovery operation.
7. Build a narrow filesystem seam for failures at write, backup/preservation, and publication. Keep the real file path covered by integration tests. Do not introduce a general storage framework.

Milestone B: isolated tests prove good data survives failed saves/imports/recovery; second-instance ownership is exercised; startup results are explicit. Checkpoint, then continue to C.

## Pass C — Reliable WPF editing and complete developer verification

Scope: B06, B07, B08, remaining B09. Likely files: view models/editor wrappers, MainWindow.xaml/.cs, startup and Windows adapters, tests.

1. Route editable project/module/note/preference fields through observable editor/session state. Edits, toggles, reorder, add/remove, and archive actions mark dirty; update UpdatedUtc on real persisted edits. All presentations of an item update consistently. Remove subscriptions on import/removal.
2. Debounce ordinary autosave at approximately 750 ms after the last valid edit. Commit pending WPF edits before explicit Save, export, import preparation, hide, and close. Route hide/close persistence through the UI/session coordinator, never the native callback. Serialize writes through one coordinator and track snapshot generation so an older result cannot clear newer edits. Do not block the UI on long disk work.
3. Surface Saving / Saved / Unsaved changes / Save failed distinctly. Autosave errors do not trigger modal loops or busy retries. Preserve edits for explicit retry or export. If close flush fails, offer retry, export, cancel close, or explicit discard; do not silently close and lose changes. A failed hide flush may leave the app visible with an actionable error.
4. Import first resolves pending edits, shows candidate counts/migration warnings and makes replacement explicit. Cancel is a no-op to current state/files. Confirmed commit replaces collections as one session operation, clears stale note/project references and prompt preview, and reapplies preferences under an event/save suppression guard. Do not trigger intermediate preference saves while rebinding imported data.
5. Catch expected clipboard/process launch failures through small adapters and report actionable errors. Record prompt history only after successful clipboard transfer. Preserve Open vs Copy behavior and existing URL normalization. Unexpected exceptions must retain useful diagnostics instead of being silently treated as success.
6. Protect programmatic preference initialization/import from incidental change-event saves. Handle startup storage errors with a visible recovery path; constructing a default MainViewModel must not be the sole unguarded storage entry point.
7. Retain project/module/note functionality and window behavior while refactoring. Avoid implementing archive filters, variable editors, placement persistence, UI redesign, or new network features here.
8. Complete developer T01-T18 verification appropriate to available tools, update architecture implementation notes and user-facing storage/run docs, and write a compact developer handoff listing contracts, changed areas, evidence, known limits, and any manual checks still required from the tester.

Milestone C: all S01 acceptance behavior implemented and developer build/automated checks pass. Set READY_FOR_TEST only with a complete handoff and no known mandatory automated failures. Windows interaction checks may be explicitly assigned to the independent tester, but cannot be skipped for lead acceptance.

## Acceptance and scope control

- Mandatory matrix: [T01-T18](../TEST_STRATEGY.md). Native/window cases are a baseline regression subset; full hook/DPI hardening is S02.
- All P0/P1 defects introduced by S01, and all S01 backlog items, must be resolved and independently verified.
- Core still has no WPF/Win32 dependency; no broad rewrite or new production package without lead decision.
- Preserve v1 AlwaysOnTop migration and normal/topmost/summon preferences; retain Extra data, clipboard toggles, composition ordering, and note content.
- Do not proceed to S02 after developer completion. Tester first, then lead audits full changed logic, recovery ordering, session transitions, and tests before acceptance.

If a policy above proves technically inconsistent, bring the lead the exact conflict and a proposed alternative. Do not silently relax data preservation to meet the sprint deadline.
