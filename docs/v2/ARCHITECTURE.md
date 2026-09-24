# Power Ops V2/V3 - contextual Windows workspace

Design date: 2026-09-24. This document distinguishes the implemented V2 foundation from the target architecture. A design entry is not a shipped integration or a native acceptance result.

## 1. Product decision

Continue beyond the utility palette, but build a configurable companion rather than a replacement operating system, browser, IDE, Git client, task database or monitoring stack.

The recurring user requirement is **resume the right context with little effort**: a project, relevant applications, saved tabs, documentation, commands, tasks and recent evidence. It is not simply a request for more shortcuts. This is an interpretation of the user's explicit workflows, not a psychological assessment.

The September 23 requests already included a Developer Environment Explorer, runtimes/CLI/extensions, computer telemetry, AI usage and organized clipboard media. The current System/Cheat Sheet covers only part of that inventory vision. Do not call the earlier planning fully implemented.

AtlasNote provides the design precedent: separate reading sessions, panes, views/history, filters, bookmarks and saved states, with underlying library content shared. Saved reading states explicitly exclude document bytes. Power Ops should reuse that pattern, not copy AtlasNote's React UI into WPF.

## 2. Verified starting points

- Power Ops: `julian-passebecq/PowerToy_UI`, standalone WPF/.NET, existing business schema v7 at `df3aa1626fee3c96f6008793214852b273595ffc`. Explorer folders and shortcuts are inherited from this checkpoint.
- New work is isolated on `codex/power-ops-v2-workspaces`. Do not alter the ongoing `codex/to-be-tested-v1` laptop session or merge to main.
- AtlasNote main README identifies a React/Vite V2.3 Cloudflare migration candidate, not a currently certified production release. Its content model includes five independent sessions, bookmarks, saved states and local assets. A supported task-sync endpoint was not established by this inspection.
- Mongoku-datapass `datapass/control-plane-v1` documents saved workspaces, reviewed ChangeSets and read-only saved-query APIs. Its FOIL integration deliberately resolves the Project Management resource registry first and keeps authoritative source databases separate. This is a source capability, not proof that a configured live deployment is available.

## 3. The model: data, views and runtime are different things

| Concept | Meaning | Example |
| --- | --- | --- |
| Data store / instance | Durable domain data and credential boundary | Personal vs work; separate `--data-dir` |
| Project context | Typed identity and relations, not a fixed-depth folder tree | Foil company -> IT Dev -> simulation; related business work |
| Workspace view | Named composition over shared data | Foil operations, Datapass study, Device settings |
| Tab | Module route plus instance-specific view state | Git for Foil vs Git for Datapass |
| Widget instance | Small projection of a module, with context binding | Read-only AtlasNote tasks in a side panel |
| Module | UI and operations for a domain | Repository Hub, Capture, Inventory |
| Provider | Adapter that supplies observations or explicit writes | Local Git, Claude hooks, AWS usage |
| Action | Typed command with inputs, risk and confirmation | Open terminal at an approved repo path |
| Secret reference | Identifier resolved by the OS/external vault | Credential Manager reference; never a plaintext password |

Saving a view must not duplicate repositories, screenshots or cloud data. Restoring a view must not roll back domain edits. Launching a workspace must not silently run every registered application or cloud operation. Workspace module visibility is not access control.

Project identities should have stable IDs, typed parent/related relations, aliases, ownership and external resource references. Do not require every project to have identical company/project/subproject/task depth. Reuse the FOIL resource registry rather than invent a competing authoritative registry in Power Ops.

## 4. Target navigation and UX

The target is six top-level groups, with modules beneath them. The V2 preview currently groups its available modules into four simpler sections; do not render empty future groups by default.

| Group | Modules / subcategories | Primary interaction |
| --- | --- | --- |
| Start & spaces | Launchpad, Resume, saved views, recent evidence, project overview | Choose context and resume |
| Projects & tools | Repositories, Git activity, agent activity, terminals, runtime inventory, IDE extensions | Inspect locally; open the owning tool |
| Knowledge & capture | Inbox, notes, tasks, bookmarks, read-later, snippets, screenshots/clips, AtlasNote links | Capture immediately; edit in the authority app |
| Cloud & services | Portals, websites/health, free-tier usage, Grafana links, VM profiles, Mongoku summaries | Read-only status; deliberate navigation/actions |
| Device & settings | Hardware, battery, usage, OS shortcuts, shortcuts, module management, export/backup | Inspect and configure safely |
| Personal | RSS, optional market watch, personal folders/links | Quiet, opt-in summaries |

### Shell anatomy

- Thin top menu: File / Workspace / Tabs / View, later Tools / Help.
- Workspace selector and explicit Save view / Restore view.
- Session tabs: multiple routes into the same module without duplicating its data.
- Left rail: module groups. Collapsible in Compact mode.
- Optional second left pane: project/category/filter tree, not a second global navigation.
- Center: main task. Prefer an informative table or one dominant card over equal-sized dashboard tiles.
- Optional right pane: selected item details, provenance/freshness, quick actions, or a pinned companion widget.
- Collapsible contextual ribbon, not a permanent wall of commands.
- Status footer: store/instance, offline/connected, last observation, pending unsaved changes.

Right-pane settings components should be reusable widgets in other workspaces. A Device workspace can gather all settings; a Foil workspace can pin only battery mode or one terminal profile. That should reference one settings model, not duplicate settings data.

### Separate configuration axes

1. **Density:** Minimal / Efficient / Advanced changes visible chrome and detail.
2. **Layout:** Expanded / Compact / Sidebar; later portrait, landscape, bottom-square, borderless focus, attached companion.
3. **Runtime policy:** Quiet / Balanced / Observe changes refresh and provider lifetimes.
4. **Context:** active project, account, environment and selected module routes.

Advanced does not mean every integration runs. Minimal does not delete data. Quiet must not hide an already-known important failure; show cached evidence with its age.

### Proposed workspace recipes

- **Datapass build:** repositories, Git state, Mosaic/VS Code launcher, pinned commands and docs, last agent outcome.
- **Foil operations:** business and IT contexts side by side; resource-registry links, Mongoku saved report, Oracle portal/SSH profile and explicitly enabled cost card.
- **Knowledge:** AtlasNote, useful syntax snippets, screenshots, reading queue and a small capture area.
- **Device & settings:** installations, environment versions, mouse mapping reference, battery and explicit maintenance previews.
- **Personal quiet:** bookmarks, RSS, personal capture, optional delayed market watch. No developer monitors running merely because they exist.

## 5. What the V2 foundation actually implements

- Named workspace views, five editable starter presets, add/rename/delete views.
- Per-workspace visible-module selection and grouped left navigation.
- Module tabs with per-tab search/filter state; close, reopen and standard Ctrl+T/Ctrl+W/Ctrl+Shift+T/Ctrl+Tab/Ctrl+Shift+Tab/Ctrl+1..9 commands when not editing text.
- Saved view bookmarks as deep copies of UI state; restore without rolling back business data.
- Per-workspace inherited Expanded/Compact/Sidebar choice and quick-ribbon visibility.
- File menu for all/current/selected-module review JSON and separate layout export/import with preview.
- Launchpad built from the existing project website, portal and resource catalogs; up to 150 distinct HTTP(S) destinations; no webviews or health polling.
- On-demand local PATH inventory for 29 common CLI/runtime/editor/agent names. Reports executable file metadata, not guaranteed runtime versions. Never executes discovered programs.
- In-app catalog listing available modules and planned integrations.
- Separate `shell-workspaces.json`, schema 1, bounded input, validation, atomic replacement and prior-generation backup; malformed/future content is not silently reset.
- Package-free workspace regression suite added to the existing full solution build/smoke gate.

This is a preview. It does not implement an arbitrary drag-and-drop widget designer, task synchronization, runtime update checks, live Git/agent status, multi-window companions, full-screen layout engine, monitoring providers, or complete lazy unloading of legacy editors. Native WPF behavior requires a separate Windows pass.

Existing V1 views are still eagerly constructed by the inherited XAML. Hiding their navigation is NOT the same as unloading their visuals or providers. The added inventory runs only on demand, and the shell autosave timer is a one-shot debounce after changes, not an ongoing monitor.

Tabs currently preserve module, search, main filter and project/provider/subject selections. They do not yet preserve every editor selection, scroll position, undo stack or in-progress modal dialog. Those are the next view-state contracts, not implied guarantees.

## 6. Feature portfolio and priority

Effort labels are relative implementation risk, not calendar estimates. P = implemented preview; N = next V2 increment; V3 = optional later adapter; X = deliberately delegate/reject.

| Feature | Stage | Effort | Boundary / reason |
| --- | --- | --- | --- |
| Saved workspace views and module tabs | P | Medium | Reuses existing data, no browser instances |
| All/current/selected JSON export | P | Medium | Review format, not media-inclusive recovery |
| Website front page | P | Low | Saved links only; no false live badges |
| Module feature catalog | P | Low | Actual vs planned is visible |
| CLI locations and file versions | P | Low-medium | Local PATH only, explicitly incomplete |
| Lazy module view/provider lifecycle | N | Medium-high | Prerequisite for meaningful global disable |
| Contextual project launch profiles | N | Medium | Paths/accounts/VS Code profiles, explicit launch confirmation |
| Read-only Git status and recent commits | N | Medium | No automatic fetch/commit/push/reset |
| GitHub PR/CI/deployment summary | N | Medium | Verified credentials, bounded requests, source links |
| Full Python/Conda/WSL/runtime inventory | N | Medium | Separate adapters and scopes; don't mix environments |
| VS Code extensions by profile | N | Medium | Query actual editor profile; no auto-install/update |
| Snippet language tags and copy variables | N | Low-medium | Extend Clipboard/Prompt Builder; don't build another editor |
| Export review/diff and schema-assisted AI layouts | N | Medium | Parse data, never execute JSON as code |
| Light website health checks | N/V3 | Medium | Allowlist URLs, explicit interval, auth errors != down |
| AtlasNote read-only task/capture bridge | N/V3 | Medium-high | Requires explicit source contract; browser local data not silently readable |
| Mongoku saved-report summary | N/V3 | Medium | Narrow read-only aggregate, registry-aware, no Mongo secret in Power Ops config |
| Agent run/turn event summary | V3 | Medium | Explicit adapters; finished != tested != pushed |
| Cloud free-tier consumption | V3 | High | Provider-specific units, windows, availability and billing permissions |
| Grafana summaries/deep links | V3 | Medium | Reuse existing server; no bundled monitoring stack |
| CPU/RAM/battery | V3 | Medium | Native low-frequency sampling only while requested |
| GPU and detailed disk/process telemetry | V3 | Medium-high | Hardware-dependent counters; disabled by default |
| Screen/app time | V3 | Medium | Opt-in local event aggregates; no keystrokes/content/screenshots |
| Power mode / OS Settings shortcuts | N/V3 | Low-medium | Open supported settings first; changes require confirmation |
| Close/restart a selected app | V3 | Medium-high | Exact process identity, graceful close first, unsaved-work warning |
| Multi-monitor companion windows | V3 | High | Shared process/model, per-monitor placement, no duplicate providers |
| Attached side panel tracking another app | V3 | High | Focus/DPI/z-order/fullscreen edge cases; optional |
| MX Master button bindings | N | Low-medium | Standard shortcuts + Logi Options+, not custom hardware drivers |
| RSS/news card | V3 | Low-medium | Plain text, user feeds, no script execution or constant refresh |
| Market watch | V3 | Medium | Authorized data provider and timestamp; no undocumented Yahoo scraping |
| Downloads cleanup recipes | V3 | Medium-high | Dry run, selection, quarantine and undo; not blind deletion |
| Password manager / plaintext .env vault | X | High risk | Link to established vault; store secret references only |
| GitHub Desktop/GitLens clone | X | High | Existing tools own branching/merges/history |
| Embedded browser for every site | X | High runtime cost | Open external app/browser; optional single lazy webview only after measurement |
| Full IDE/notebook/cloud orchestrator | X | Very high | Datapass/Mosaic and cloud apps already own execution |
| Automatic dependency updates/VM shutdown | X default | High risk | Explicit reviewed action only, never hidden background behavior |

## 7. Adapter contracts and truthful status

A module consumes an observation with source identity, scope, observedAt, validity/freshness, data, and error/permission state. Missing data is **Unknown**, not zero and not healthy.

### Git and AI coding

Local `git log` proves commit history, not when or whether a push occurred. Ahead/behind is relative to the last fetched remote-tracking reference; show the fetch age. Remote freshness requires an explicit fetch or API observation. A commit author is not reliable proof of which agent produced it.

Use separate badges: working tree clean/dirty; local commit; remote synchronized as-of; CI; deployment. Never combine them into an unsupported green 'done'.

For Codex use supported App Server/thread/turn events where the installed integration permits them. For Claude Code use supported hooks such as Stop/SessionEnd. A turn ending is not proof all tasks, tests or pushes succeeded. Keep only minimal opt-in run metadata, not private conversation transcripts. Process presence is at most 'process running'; it does not prove an agent is busy.

A useful event contract: provider, sessionId, repositoryId, branch, startedAt, observedAt, state, commitSha if verified, evidence URI. States include unknown, running, awaiting approval, turn-ended, failed and interrupted. Add Git/CI evidence separately.

### Inventory

Distinguish installed version, PATH-selected executable, runtime/environment version, per-repo interpreter, VS Code profile extensions, and remote/WSL installations. 'Not on PATH' is not 'not installed'.

The next version can run bounded allowlisted probes after user approval, using ProcessStartInfo.ArgumentList, exact executable locations, timeouts/output limits, cancellation, and a clear scope. Do not run a project-supplied executable merely because its filename matches a known tool. No full-disk recursive scan on startup.

Version/update providers must say where the result comes from: winget, a package manager, release API, or unknown. Do not call a tool obsolete because a lexicographic version comparison is wrong or the network is unavailable. Updates are separate reviewed actions.

### AtlasNote and Mongoku

AtlasNote owns knowledge/reading state; Power Ops owns local quick capture only unless the user explicitly chooses a task authority. Begin with deep links and reviewed export/import. A bidirectional bridge needs stable IDs, origin identity, revisions/ETags, tombstones, conflict handling and explicit permissions. Never synchronize by copying another browser's live IndexedDB files.

Mongoku owns its saved-query/report layer and follows authoritative FOIL resources. Power Ops should request a narrow read-only summary and show source/age/link, not connect to every FOIL database or create a second project management database. Its source branch documents a read-only saved-query API; endpoint availability/auth must still be verified on the selected deployment.

### Cloud usage and Grafana

Grafana renders data from adapters; it cannot invent missing provider billing/quota data. Start with the user's three most important providers, not a universal connector suite.

Record provider, account/project, service/metric, used, limit, unit, allowance type (ongoing free tier vs promotional credit), periodStart/periodEnd/reset timezone, source, observedAt, forecast and freshness. Different services count requests, bytes, active hours, compute or spend; don't sum unlike units into one percent.

The AWS Free Tier API exposes usage/forecast/limit fields. Azure budget alerts do not stop consumption and billing observations can lag. Therefore Power Ops is an awareness tool, not a spending guarantee. Do not auto-stop VMs based on inferred costs. Missing provider APIs get a portal link or explicitly manual reading.

Use the existing Grafana server or a thin protected aggregate endpoint. Do not bundle Docker/Prometheus/Grafana solely to display a few status cards.

## 8. Runtime performance contract

These are proposed acceptance targets, not measured claims for this build.

| Activity | Default policy | Retention |
| --- | --- | --- |
| Saved links / snippets / configuration | No network, no periodic scan | Local domain data |
| Shell state | Debounced after actual changes and on close | Current + previous valid generation |
| Inventory | Manual, bounded, cancellable; no execution in preview | Session result only in preview |
| Local Git | On entering view/manual; short cache | Latest observation + limited recent events |
| Remote Git/CI/health | Explicit opt-in; backoff; visible refresh | Timestamped cache |
| CPU/RAM | Off; when visible approximately every 2-5 seconds | Bounded ring buffer, no JSON write per sample |
| Battery | Native events or slow sampling | Latest state; optional summaries |
| Cloud billing/free-tier | On demand or provider-appropriate 15-60+ minute interval | Billing-period aggregates |
| RSS / market watch | Off until configured; slow refresh | Capped items; timestamps |
| Screen time | Separate explicit opt-in | Daily local aggregates, retention limit |

One provider subscription can serve multiple widgets/windows. Hiding a live module cancels its work unless the user explicitly pins a background alert. Globally disabled providers have no timers, child processes or sockets. Implement unload/cancellation tests, not only a checkbox.

Runtime scheduler design: maximum two low-priority concurrent refreshes initially, per-provider timeout, backoff with jitter, network/power awareness, deduplicated requests and unsubscribe disposal. Preserve the last observation on error but label it stale.

Measure the existing WPF baseline on the same laptop before setting hard budgets. A useful initial goal is less than 0.5% average idle CPU over five minutes after warm-up with monitoring off and no repeated disk/network traffic. The preview has not proven that goal. Do not advertise a tiny RAM number: the previously observed application was already roughly 200+ MiB, and hidden legacy editors still exist.

Future telemetry should use a bounded in-memory buffer and, only when history is enabled, a small separate local database with batched writes. Never stuff continuous time series or image bytes into the configuration JSON.

## 9. Windows, monitors and mouse

Existing Always on top applies to Power Ops itself. Existing Sidebar/Compact/Expanded are retained. Target portrait/landscape/square presets should respond to available dimensions, not device-name guesses. Store monitor identity, logical placement and last-known DPI with recovery to a visible work area when displays disappear.

Multiple companion windows for one data store require a shared in-process coordinator and shared providers. Do not bypass the current one-writer-per-data-directory mutex by launching independent writers. A companion on every display should be lightweight projections, not several complete app copies.

PowerToys Workspaces already launches applications into layouts; FancyZones and Always On Top remain useful external utilities. Integrate through verified shortcuts/actions rather than fork the entire suite. Attaching a panel to arbitrary external windows is substantially harder than a fixed bottom-right square and should wait for native DPI/focus QA.

For MX Master 4, Logi Options+ supports app-specific button behavior and is needed for Actions Ring. Keep proprietary gestures/haptics in Logitech's software. Map supported keys or a future registered global summon chord to Power Ops commands. Ctrl+T/Ctrl+W/Ctrl+Tab are in-app shortcuts, not global controls. Avoid simultaneously intercepting Back/Forward mouse buttons globally and mapping them through Options+. The existing Button4/5 hook should remain explicit and optional.

## 10. Safety, secrets and maintenance

Keep secrets in Windows Credential Manager or an established external vault. Export only references where needed. Do not read and export all `.env` values. An environment checklist may show key names, presence and source path only after permission; even names/paths can be private.

Configuration imported from AI is data, not executable code. Validate schemas, reject unknown modules/unsupported versions, preview scope, create a recovery point and obtain confirmation. Future executable recipes require an allowlisted action registry, separate permissions and visible arguments, not arbitrary shell text embedded in a bookmark.

Downloads cleanup: scan only user-approved roots; show candidate files/reasons; preview; select; move to a quarantine folder or recycle bin; log; support restore. No irreversible delete-on-click. Opening Codex with a prepared prompt is useful, but actual automatic submission depends on supported launch/integration contracts. A deterministic local cleanup helper is preferable to spending model tokens for routine file enumeration.

Power controls: open supported Windows settings first. For app restart, identify exact PID/path/start time, request graceful close and warn about unsaved work before force termination. Sleep/restart/logoff and cloud resource changes always require explicit confirmation. Never act because a dashboard card was restored.

RSS content must be treated as untrusted: plain text or sanitized rendering, no scripts or commands from feeds. Market data requires an authorized provider, timestamp and delay indication; a Yahoo link is easy, a licensed reliable real-time feed is a different feature.

## 11. JSON, backups and portability

Current formats:

- `workspace.json`: inherited business schema v7, existing domain import/export behavior.
- `shell-workspaces.json`: `Format=powerops-shell`, `SchemaVersion=1`, view state only, 2 MiB cap, up to 20 workspaces/16 tabs each/20 bookmarks.
- `powerops-content-export`: review/share envelope, selected domain sections, version 1. All-content export includes shell state; current/selected-module exports do not leak unrelated shell state. Launcher commands, arguments, machine paths/preferences are omitted in the UI path. User-entered text and URLs still require review.

Media exports contain metadata and relative references, not image/video bytes. These content exports are deliberately not accepted as a complete workspace restore format. Do not call them complete backups.

Next: a separate full recovery bundle with manifest, hashes, business JSON, shell JSON and selected media; portable environment-path mapping; selected-content merge/import with ID collision previews and rollback. Future caches, device-specific preferences and secret references should have explicit include/exclude controls. Cross-computer tabs can be portable; monitor IDs and absolute executable paths require remapping.

## 12. Delivery gates and deferred choices

Ship the V2 foundation behind this branch's preview status. Run the full WPF build, inherited smoke tests and new regression suite, then native tests on a new isolated directory. Do not interrupt or rewrite the current V1 laptop evidence.

Next V2 acceptance gates: old schema-v7 data loads unchanged, independent tabs/filters restore, navigation stays accessible in small windows, import cancellation is side-effect-free, malformed state is preserved, close-save failures are visible, inventory does not execute discovered scripts, and disabled/hidden states are represented truthfully.

Only after shell stability, implement one adapter at a time: contextual launch profiles -> local Git -> richer inventory -> one read-only external summary -> optional monitoring. Do not mix five live services into one unreviewable change.

Four decisions would make the next increment precise: the authoritative task system; the first three cloud providers/accounts; exact Windows/WSL/editor/terminal launch targets; preferred monitor placement and thumb-button behavior. None blocks using the V2 preview foundation.

## Sources inspected

Repository sources:
- https://github.com/julian-passebecq/PowerToy_UI/tree/df3aa1626fee3c96f6008793214852b273595ffc
- https://github.com/julian-passebecq/atlasnote/blob/main/README.md
- https://github.com/julian-passebecq/atlasnote/blob/main/src/core/model.ts
- https://github.com/julian-passebecq/atlasnote/blob/main/src/core/workspace-slots.ts
- https://github.com/julian-passebecq/atlasnote/blob/main/src/core/saved-states-types.ts
- https://github.com/julian-passebecq/Mongoku-datapass/blob/datapass/control-plane-v1/README.md

External primary references (capabilities must still be checked against installed versions):
- https://code.visualstudio.com/docs/configure/command-line
- https://learn.microsoft.com/en-us/windows/terminal/command-line-arguments
- https://learn.microsoft.com/en-us/windows/package-manager/winget/list
- https://learn.microsoft.com/en-us/windows/powertoys/workspaces
- https://learn.microsoft.com/en-us/windows/powertoys/always-on-top
- https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getsystempowerstatus
- https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew
- https://support.logi.com/hc/en-ca/articles/28321445268247-Getting-Started-MX-Master-4
- https://openai.com/index/unlocking-the-codex-harness/
- https://developers.openai.com/codex/app-server/
- https://code.claude.com/docs/en/hooks
- https://docs.aws.amazon.com/aws-cost-management/latest/APIReference/API_freetier_GetFreeTierUsage.html
- https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/tutorial-acm-create-budgets
- https://grafana.com/docs/grafana-cloud/platform/pricing-and-usage/
