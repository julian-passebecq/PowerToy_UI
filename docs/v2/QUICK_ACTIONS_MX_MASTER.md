# Power Ops V2.1 - Quick Actions, MX Master and daily companion UX

Design update: 2026-09-24.

This is an implementation specification, not a claim that these features already ship. It extends `docs/v2/ARCHITECTURE.md`.

## Product decision

Power Ops should expose one shared set of useful actions through several surfaces:

- full workspace UI;
- compact Quick Shelf;
- transient Quick Ring;
- keyboard shortcuts / global summon;
- Logitech MX Master mappings through Logi Options+;
- later command search and external automation adapters.

Do not implement each surface as an independent launcher. Build one typed action catalog with shared execution, availability checks, confirmation rules and tests.

Power Ops must remain fully usable without Logitech hardware. MX Master is an optional invocation layer, never a dependency.

## Action catalog

Target contract:

```text
PowerOpsAction
  id
  label
  category
  description
  icon/glyph
  defaultShortcut?
  globalAllowed
  requiresConfirmation
  risk: safe / caution / destructive
  availability
  execute
```

Initial candidates:

| ID | Action | Notes |
| --- | --- | --- |
| `app.toggle` | Show/hide Power Ops | Global summon target |
| `app.open` | Open full Power Ops | Ring center |
| `capture.region` | Screenshot / region capture | Prefer supported Windows capture |
| `capture.quick` | Quick Capture | Existing Capture module |
| `clipboard.open` | Clipboard Library | Existing module |
| `folder.downloads` | Open Downloads | First-class quick destination |
| `folder.explorer` | Open configured Explorer folder | Reuse Explorer-folder model |
| `terminal.open` | Open configured terminal | Explicit profile/path only |
| `workspace.resume` | Resume current workspace | Preview external launches first |
| `workspace.next/previous` | Cycle saved workspaces | No domain-data mutation |
| `tab.next/previous` | Cycle Power Ops tabs | Existing tab model |
| `mail.latestCode` | Copy latest verification code | Future provider only |

Destructive/system power actions are not initial ring actions.

## Interaction modes

Settings > Interaction should eventually expose:

| Mode | Behavior |
| --- | --- |
| Off | No overlay |
| Quick Shelf | Small pinnable strip |
| Quick Ring | Transient radial actions |
| MX Master guide | Shortcut mapping guidance only |
| Hybrid | MX/shortcut summons Quick Ring; another command opens full Power Ops |

Recommended user setup after native validation: **Hybrid**.

Fresh installs must not silently register global hooks. Enabling a global summon is explicit.

## Quick Ring

Requirements:
- near pointer or configured anchor;
- maximum 6-8 primary actions;
- center opens full Power Ops;
- Escape/outside-click dismisses;
- keyboard accessible;
- restores focus correctly;
- monitor/DPI aware;
- per-workspace action overrides;
- showing the ring must not start heavy providers.

Suggested general default:
1. Screenshot
2. Downloads
3. Quick Capture
4. Clipboard
5. Explorer/project folder
6. Terminal
7. Resume workspace
8. Latest verification code only when a future mail provider has a fresh code

Project-specific actions bind through workspace context; do not hard-code Foil/Datapass logic into the global action catalog.

## Quick Shelf

Persistent/pinnable strip:
- 5-10 configured actions;
- horizontal or vertical;
- optional always-on-top/auto-hide;
- icon-first with accessible labels/tooltips;
- per-workspace ordering;
- same action catalog as Ring/full UI.

Do not begin with an arbitrary drag-and-drop dashboard designer.

## MX Master 4

Preferred architecture:

```text
MX Master 4
  -> Logi Options+
  -> keyboard shortcut / Smart Action
  -> Power Ops action
```

Do not write a Logitech driver or require proprietary hardware APIs.

Provide an MX Master setup guide with copyable recommended shortcuts. Logi Options+ owns app-specific mappings.

Suggested mappings:
- gesture/thumb press -> summon Quick Ring;
- Back/Forward -> previous/next Power Ops tab while focused;
- thumb wheel -> cycle workspace/tabs only when explicitly configured;
- gesture + up -> Quick Capture;
- gesture + down -> Clipboard.

Avoid double interception: do not have Logi Options+ and the Power Ops global mouse hook consume the same physical button simultaneously. Existing Button4/Button5/middle-click summon remains an alternative.

## Global summon

Add a configurable global hotkey abstraction before relying on mouse-specific behavior.

Candidate default: `Ctrl+Alt+Space` only if conflict-free. Registration failure must be visible and leave the app usable.

Global invocation and in-app shortcuts are separate. Tab shortcuts remain in-app only.

## Downloads

V2.1:
- Open Downloads;
- Open latest downloaded file by explicit click;
- Copy latest file path;
- Reveal latest file in Explorer.

Later:
- move selected download to project;
- cleanup preview;
- quarantine/recycle with undo/logging.

Never implement blind deletion from the Ring.

## Screenshot / capture

Initial:
- invoke supported Windows region-capture flow.

Later after native validation:
- screenshot -> Power Ops Clipboard Media / Capture Inbox;
- optional project/category prompt;
- short clip remains explicit.

Do not rebuild Snipping Tool just to duplicate Windows selection UX.

## Attention Center and Gmail

Future provider; do not mix into the first Quick Actions framework diff.

Power Ops should not become an email client. A future opt-in Mail Peek may expose sender/service, subject, age, likely 4-8 digit verification code, Copy and Open email.

Security:
- supported authenticated provider;
- code memory-only by default;
- short expiry;
- never save/export/log OTP values;
- no full message body by default;
- manual refresh initially; later low-frequency refresh only when enabled/needed.

Future Attention Center may aggregate truthful observations from Git/CI, agent events, mail, AtlasNote and cloud allowances. Never collapse these into one unsupported “done” or “healthy” state.

## Performance

Quick Actions itself should be nearly idle:
- no polling because Ring/Shelf is enabled;
- no provider startup for hidden actions;
- shared action/provider instances;
- overlays release transient resources;
- per-workspace configuration is data only.

Measure startup, overlay latency, idle CPU, working set, handles, disk and network before making performance claims.

## Native tests

At minimum:
- global hotkey register/collision/unregister;
- summon/hide from another foreground app;
- every available monitor, negative coordinates and mixed DPI where available;
- focus restoration;
- Escape/outside-click/keyboard navigation;
- no text-input theft after dismissal;
- Shelf pin/auto-hide;
- action ordering/persistence per workspace;
- truthful unavailable/disabled actions;
- Downloads;
- screenshot launch;
- text editor shortcut conflicts;
- no duplicate mouse event with Options+;
- idle CPU/network/disk sanity.

## Implementation sequence

1. Typed action catalog + safe built-ins + tests.
2. Configurable global summon.
3. Quick Shelf.
4. Quick Ring.
5. Per-workspace overrides + Interaction settings + MX Master guide.
6. Native Windows acceptance/performance pass.
7. Only then external-provider actions such as Gmail code, Git/agent activity, AtlasNote or cloud observations.

Preserve existing V2 workspace/data contracts and keep work isolated on the V2 branch until reviewed.

## Implementation status (2026-09-24, V2.1 slices 1-6)

Implemented on `codex/power-ops-v2-workspaces` (see `DELIVERY.md` for evidence): typed catalog + dispatcher, opt-in global shortcuts (RegisterHotKey, no hooks), Quick Shelf, Quick Ring, web apps as actions, and **Actions > Interaction settings**:

- five modes (Off, Quick Shelf, Quick Ring, MX Master guide, Hybrid) with explanations;
- "Add recommended shortcuts" per mode: Ctrl+Alt+Shift+letter suggestions with fallbacks, each checked against Windows before it is offered; a binding already owned by another program is replaced (the default Ctrl+Alt+Space is taken on the user's laptop, so Show/hide becomes Ctrl+Alt+Shift+P there);
- an MX Master / Logi Options+ guide generated from the shortcuts actually configured (gesture press -> Quick Ring, gesture up/down -> Quick Capture/Clipboard, a spare button -> show/hide, app-specific Back/Forward -> Ctrl+Shift+Tab/Ctrl+Tab), with Copy buttons; steps for unbound actions are flagged instead of promised;
- the Summon mouse-hook double-interception warning with a one-click switch to Ctrl + middle click.

Not yet verified with a physical MX Master and Logi Options+.
