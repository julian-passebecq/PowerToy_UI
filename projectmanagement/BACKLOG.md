# Backlog

Updated 2026-09-08. Priority: P0 blocks the current build or threatens basic data preservation; P1 essential reliability/usability; P2 useful completion; P3 optional experiment. Status starts as Planned, Deferred, or Needs verification; later use In progress / Ready for test / Rework / Accepted.

| ID | Priority | Item and purpose | Planned sprint | Dependency | Status / evidence |
| --- | --- | --- | --- | --- | --- |
| B01 | P0 | Fix hook variable scope collision so WPF compiles | S01 A | None | Planned; F01 |
| B02 | P1 | Make scripts fail immediately on native command failure | S01 A | None | Planned; F02 |
| B03 | P0 | Preserve valid backup and corrupt evidence during recovery | S01 B | B04 | Planned; F03 |
| B04 | P1 | Validate file shape, versions, entries, strings, enums and identities before use | S01 A | None | Planned; F04/F05 |
| B05 | P0 | Transactional save/import with failure isolation and single writer | S01 B | B04 | Planned; F06/F09 |
| B06 | P1 | Dirty tracking, edit propagation, save feedback and safe shutdown | S01 C | B05 | Planned; F07 |
| B07 | P1 | Import preview/replace decision and reset every stale UI selection | S01 C | B04/B05 | Planned; F06/F08 |
| B08 | P1 | Handle clipboard/launch failures without crashing or recording false success | S01 C | None | Planned; F10 |
| B09 | P1 | Test isolation, fault injection, independent regression gate | S01 A-C | None | Planned; T01-T18 |
| B10 | P1 | Summon state transitions, modal/pin behavior, native hook lifecycle | S02 | S01 accepted | Deferred; native verification absent |
| B11 | P1 | Recover hidden app access and validate second-launch experience | S02 | B05/B10 | Deferred; recovery route design needed |
| B12 | P1 | Per-view placement/size, off-screen recovery and mixed DPI | S02 | B10 | Deferred; current fixed sizes |
| B13 | P1 | Small-window layout and keyboard/focus usability | S02 | S01 accepted | Deferred; visual audit needed |
| B14 | P1 | Active/archived/all filters for projects and notes; pinned note ordering | S03 | B06 | Deferred; current flags lack complete view behavior |
| B15 | P1 | Safer project editors and URL feedback; explicit delete with note-link policy | S03 | B04/B06 | Deferred; F11 |
| B16 | P2 | Note project association, labels and search | S03 | B14/B15 | Deferred; model has fields but UI incomplete |
| B17 | P1 | Dynamic prompt variable inputs and unresolved-variable feedback | S04 | B06 | Deferred; FindVariables exists |
| B18 | P1 | Clear preview editing/copy contract; recall and copy recent prompts | S04 | B17 | Deferred; F12 |
| B19 | P2 | Prompt module lifecycle and stable ordering UX | S04 | B17 | Deferred |
| B20 | P1 | Self-contained Windows package, clean-machine run, upgrade/recovery guide | S05 | S01-S04 accepted | Deferred |
| B21 | P2 | Measured startup, responsiveness, keyboard/accessibility release checks | S05 | B13/B20 | Deferred; establish measurements first |
| B22 | P3 | Optional manual GitHub commit status without credentials in JSON | Later | Stable local release | Deferred; user value must justify |
| B23 | P3 | Global keyboard shortcut or launcher integration | Later | B10/B11 | Deferred; only if needed beyond mouse access |

## Maintenance rules

Tester adds concrete new defects with a stable ID, severity, reproduction, affected revision, and evidence link. Do not close a defect because code changed; close after its regression passes. Lead owns priority changes, sprint scope, architecture decisions, and final acceptance. Map fixes/tests to IDs without maintaining duplicate full descriptions here.

Later sprints are not promises to implement every item. Review usage value and remaining risk at each lead gate. Merge/PR/remote branch status belongs in BRANCHES.md.
