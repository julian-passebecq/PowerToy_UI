# Codex live test v1 results

Status: **NOT RUN YET**

This file is the durable result ledger for the live Windows acceptance pass defined in [CODEX_LIVE_TEST_V1.md](CODEX_LIVE_TEST_V1.md).

Date:
Machine:
Windows:
Architecture:
Branch: `codex/to-be-tested-v1`
Commit tested:
Tester/model: Codex / GPT-6 Luna Medium (High only if escalated for a difficult defect)

## Automated gate

- `scripts/build.ps1`: NOT RUN
- smoke/regression tests: NOT RUN
- publish win-x64: NOT RUN
- publish win-arm64: NOT RUN / N/A

## Live matrix

| Area | Result | Evidence / observation | Fix commit |
| --- | --- | --- | --- |
| Startup / shell | NOT RUN | | |
| Repository Hub | NOT RUN | | |
| Portal Launcher | NOT RUN | | |
| Tool Launcher | NOT RUN | | |
| System / Cheat Sheet | NOT RUN | | |
| Resource Hub | NOT RUN | | |
| Capture | NOT RUN | | |
| Clipboard text | NOT RUN | | |
| Clipboard screenshot/image | NOT RUN | | |
| Clipboard video | NOT RUN | | |
| Media path safety | NOT RUN | | |
| Prompt Builder | NOT RUN | | |
| Settings | NOT RUN | | |
| Summon/hide | NOT RUN | | |
| Multi-monitor/DPI | NOT RUN | | |
| Self-contained package | NOT RUN | | |
| Performance sanity | NOT RUN | | |

Allowed result values: **PASS**, **FAIL**, **BLOCKED**, **N/A**.

## Defects found

None recorded yet.

Use one section per confirmed defect:

```markdown
### BUG-001 — short title

- Reproduction:
- Expected:
- Actual:
- Root cause:
- Fix:
- Regression test:
- Live retest:
- Fix commit:
```

## Blocked tests

None recorded yet.

## Final status

**NOT TESTED**

Replace with exactly one of:

- **READY FOR USER TEST**
- **NOT READY — remaining confirmed defects**
