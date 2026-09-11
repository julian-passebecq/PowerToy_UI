# Pro AI handover — 2026-09-11

## Compact takeover brief

The user is stopping the Codex/multiple-agent workflow because its token cost is unsustainable. Take over as one Pro AI, choose your own implementation, and keep concise checkpoints. Do not restart the old developer/tester/lead ceremony or spawn agents unless requested. Data safety and honest acceptance evidence still apply.

**Product:** J Utility Palette, a small local Windows companion for project links, reusable prompts, temporary notes, and mouse summon/hide while another app is in use. Rewritten as a standalone app after a prototype in the PowerToys_J fork. The user confirmed reliability and polish before expansion on September 8. No account, cloud or PowerToys installation is required.

**Actual status:** prototype source exists, but this is not an accepted release. Application code remains at `d0a5ff8`, already on GitHub main. S01 reliability work was planned, never implemented. This branch preserves the uncommitted planning/audit package and adds this handover; no production fixes were made. No sprint is accepted.

## Implemented surfaces and why

- WPF Sidebar/Compact/Expanded layouts, Normal/Always on top/Summon modes, mouse triggers, focus-loss hiding, cursor-relative placement and temporary Keep open make the companion accessible while working elsewhere.
- Project links support explicit Open/Copy, optional Extra, inclusion switches and Copy All to reduce repetitive gathering of links.
- Prompt modules, ordering, built-in project variables and recent history reduce repetitive instruction writing. Notes store temporary text and pin/archive flags.
- Local JSON, backup, import/export keep data local. Seven package-free Core smoke checks cover a narrow happy-path baseline.

These are implemented surfaces, not guarantees of correct end-to-end behavior. Core services/models are in `src/JUtility.Core`; WPF/view model/Windows services in `src/JUtility.App`; existing checks in `tests/JUtility.SmokeTests`. Never use the live `%LOCALAPPDATA%/JUtilityPalette` workspace as a destructive test fixture.

## Broken and unfinished

| Area | Current evidence and consequence |
| --- | --- |
| Build | Reconfirmed September 11: CS0136, duplicate `button` declaration at `src/JUtility.App/Services/GlobalMouseSummonService.cs:100`. Blocks fresh WPF execution and UI verification. |
| Scripts | September 8 audit reproduced exit 0 after failed app build. Core green is insufficient. Not rerun for this handover. |
| Data | September 8 isolated reproductions: recovery overwrites good backup with corrupt primary; future schema loses unknown data on downgrade; null collection entries crash; `{}` import replaces useful data with empty state. Source unchanged, all unresolved. |
| Editing/session | Inspection found unreliable edit/save propagation, silent close-save failure, possible stale import selections/preview and no single-writer ownership. Native UI/multiprocess reproductions outstanding. |
| Platform | Clipboard/launch failures lack complete handling. Mouse consumption, modal/pin/focus transitions, hidden-app recovery and mixed DPI are not accepted. |
| Completion | Archive views, note association/search, safe delete/link policy, arbitrary prompt variables, history recall and release packaging remain incomplete. |

See [baseline audit](../projectmanagement/reports/2026-09-08-baseline-audit.md) for F01–F12, reproduction recipes, and the distinction between reproduced and inspection-only findings. There is no identified credential blocker to coding; the immediate blocker is compilation.

## Remaining outcomes for a complete app — not implementation instructions

1. **Reliable foundation, B01–B09:** full Windows build succeeds and scripts fail honestly; supported data validates/migrates safely; failed save/import/recovery preserves useful data and evidence; future files remain intact; one writer owns a workspace; edits save predictably with visible/recoverable failure; replacement is explicit; imported UI state is consistent; clipboard/launch failure is contained; isolated regression tests cover failure paths.
2. **Reliable access, B10–B13:** predictable summon/hide, modal/pin behavior, hidden-app recovery and second launch; retained per-view size/position; off-screen and mixed-DPI recovery; usable small layouts, keyboard and focus.
3. **Project/note completion, B14–B16:** active/archive views and pinned ordering; validated editing and deliberate deletion with a note-link policy; usable associations, labels and search.
4. **Prompt completion, B17–B19:** arbitrary variable inputs and unresolved-placeholder feedback; predictable preview/copy contract; history recall/copy; stable module lifecycle and ordering.
5. **Release, B20–B21:** self-contained Windows package, clean-machine run, upgrade/recovery documentation, measured startup/responsiveness and accessibility checks. Full build, storage tests and native interaction evidence must agree before release.

These describe the current completion target, subject to user prioritization. Optional GitHub status and global keyboard shortcuts (B22–B23) are later experiments, not release requirements. Cloud sync, monitoring, feeds, browser automation and PowerToys integration are out of scope.

## Read only what is needed

Start with this file and [delivery audit](DELIVERY_AUDIT.md). Use [backlog](../projectmanagement/BACKLOG.md) for IDs, [test strategy](../projectmanagement/TEST_STRATEGY.md) for acceptance cases, and baseline audit for concrete failures. Read relevant source before changes. Historical [S01](../projectmanagement/sprints/S01-reliable-foundation.md) contains prior detailed implementation suggestions; preserve safety outcomes but choose your own coding approach. Do not reread all role documents/chat logs merely to restart.

## Ready-to-paste prompt

> Take over J Utility Palette from `codex/pro-ai-handover-2026-09-11` in `julian-passebecq/PowerToy_UI`. Read `handover/README.md` and `handover/DELIVERY_AUDIT.md` first. The user wants a complete reliable local Windows app with minimal token overhead and one Pro AI. The app currently fails to build; seven passing Core checks do not establish readiness. S01 was planned only. Prioritize the remaining outcomes, choose your implementation, preserve personal data, and keep compact revision-specific evidence. Read deeper source/documents only as needed. Do not restart multiple-model handoffs. Establish the reliable foundation before feature expansion and verify native behavior and packaging before calling it a final release.
