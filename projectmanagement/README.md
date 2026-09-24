# Project management

**2026-09-11:** Start at [Pro AI handover](../handover/README.md). The user requested one Pro AI to reduce token cost; the multi-model prompts below are historical.

This folder is the durable source for delivery planning, technical decisions, and handoffs. Product direction confirmed by the user on 2026-09-08: make the small Windows companion for projects, prompts, notes, and summon access reliable and polished first.

## Start here

1. Read [STATUS.md](STATUS.md) for the active sprint and next action.
2. Read [VISION_ARCHITECTURE.md](VISION_ARCHITECTURE.md) for the intended product and engineering boundaries.
3. Read the active [Sprint S01](sprints/S01-reliable-foundation.md) and your role brief.
4. Keep the [backlog](BACKLOG.md), [branch register](BRANCHES.md), and report evidence consistent with actual work.

| Document | Purpose | Primary owner |
| --- | --- | --- |
| [WORKFLOW.md](WORKFLOW.md) | Pass cadence, status gates, escalation, resumption | Lead |
| [VISION_ARCHITECTURE.md](VISION_ARCHITECTURE.md) | Why features exist and how the system should fit together | Lead |
| [BACKLOG.md](BACKLOG.md) | Feature sequence, defects, dependencies, release gates | Lead prioritizes; tester maintains |
| [TEST_STRATEGY.md](TEST_STRATEGY.md) | Independent verification and acceptance cases | Lead defines; tester executes |
| [BRANCHES.md](BRANCHES.md) | Local branch and tested revision inventory | Tester |
| [Baseline audit](reports/2026-09-08-baseline-audit.md) | Starting evidence and known defects | Lead |
| [REPORT_TEMPLATE.md](REPORT_TEMPLATE.md) | Compact handoff and verification record | Current worker |

## One prompt per role handoff

**Start development:**

> Act as the development model. Read AGENTS.md, projectmanagement/STATUS.md, projectmanagement/roles/DEVELOPER.md, and the active sprint. Execute all authorized development passes in order, continuing between milestones without asking me for a new pass. Perform developer verification as you work and keep resumable checkpoints. Stop only at READY_FOR_TEST or a documented escalation condition. At completion, give me the prompt for the light testing model.

**Start independent testing:**

> Act as the light testing and project bookkeeping model. Read AGENTS.md, projectmanagement/STATUS.md, projectmanagement/roles/TESTER.md, the active sprint, and its developer handoff. Independently execute the required test plan, inspect the changed code for concrete defects, and record exact evidence. Maintain the backlog, branch register, and a concise report for the lead. Do not mark unavailable Windows UI or hardware tests passed. Tell me whether to return to development or call the technical lead, and give me the prompt.

**Return to the lead:**

> Act as technical lead. Read AGENTS.md and projectmanagement/STATUS.md, then the sprint, developer handoff, independent test report, and actual code diff. Audit the implementation logic and architecture, not just test results. Resolve acceptance or request focused rework. If the sprint is accepted, update the vision/backlog as needed and write the next substantial sprint with development passes and explicit test criteria.

The user chooses the actual medium/light models. Role names here express responsibility, not a dependency on a particular model name. No development agent has been started by this planning task.
