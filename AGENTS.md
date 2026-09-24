# Working in this repository

## Current user direction — 2026-09-11

Start with `handover/README.md` and `handover/DELIVERY_AUDIT.md`. The user stopped the expensive multi-agent workflow and requested one Pro AI. Historical role instructions below do not require additional agents or repeated model handoffs. Preserve data-safety and evidence standards. This handover task authorizes preservation/publishing, not further implementation.

Read `projectmanagement/README.md` and `projectmanagement/STATUS.md` before starting work. They identify the active sprint, role instructions, and next handoff. The user's latest instructions take precedence.

- Development role: follow `projectmanagement/roles/DEVELOPER.md` and finish the active sprint's authorized development passes without asking for permission between passes.
- Test / audit support role: follow `projectmanagement/roles/TESTER.md`; independently verify behavior and maintain evidence, defects, and branch records.
- Technical lead role: follow `projectmanagement/roles/TECH_LEAD.md`; own architecture, scope, logic review, acceptance, and the next sprint.
- Do not assume that a successful core test run means the WPF app builds or works. Check the full solution and record native command exit codes.
- Use isolated fixture data for destructive, import, recovery, and failure tests. Never use the user's live workspace as a test fixture.
- Keep durable checkpoints in `projectmanagement`; identify the exact tested revision and any uncommitted changes. Never label unexecuted checks as passed.
- Do not start a future sprint merely because development of the current one is complete. Independent verification and lead acceptance come first.

This workflow does not automatically start another model or create background jobs. When a handoff is required, provide the appropriate ready-to-paste prompt and persist the checkpoint first.
