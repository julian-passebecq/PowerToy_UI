# Credentials & IDs + Scratchpad pass (V2.2)

Date: 2026-09-25. Source: `GALAXY_NEXT_PASS_HANDOFFS_2026-09-25` → `01_POWER_OPS_HANDOFF.md`.
Branch: `codex/power-ops-v2-workspaces` (PR #4, draft, not merged or retargeted).

Power Ops stays the local Windows entry point. This pass adds friction/safety features on top of existing modules; it is not a new platform, a password manager or a sync engine.

## What was added

| Area | Behaviour | Where |
| --- | --- | --- |
| Credentials & IDs module | One list of records, three projections: **by service**, **by project**, **by type**. Each record appears exactly once per view. | `MainWindow.Credentials.cs`, `Core/Credentials/CredentialCatalog.cs` |
| Non-secret IDs | Label, value, "used for", source/admin URL, optional project and service, one-click **Copy**. Private by default; only records marked *Shareable* export their value. | same |
| Service sets | Cloudflare, MongoDB Atlas, Azure/Fabric, GitHub, Databricks, Vercel, Netlify starter rows with empty values. Idempotent per service + project. Nothing is fetched from any service. | `CredentialTemplates` |
| Secrets | Types Password / Token / Secret. The value is entered in a `PasswordBox` and written to **Windows Credential Manager** at an opaque target `PowerOps/Secret/{dataFolderScope}/{recordId}`. The metadata file never holds it: validation rejects a secret-type record that has a value. | `ISecretVault`, `WindowsCredentialVault` |
| Secret copy | Uses the documented clipboard formats `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory=0` and `CanUploadToCloudClipboard=0`. The clipboard is cleared after 30 s if still unchanged (checked by clipboard sequence number; the value is not kept in memory for the timer) and on exit. | `Services/SecretClipboard.cs` |
| Reveal | Explicit, 10 s, read-only box with the fixed automation name "Revealed secret value". It hides when the selection changes. | editor |
| Mistyped secret | If a secret is typed into a plain ID field and the type is then changed to Token/Secret, Save moves the value into the vault and saves twice, so the prior-generation `.backup` no longer contains it. A non-blocking hint appears when a plain value *looks* secret; Power Ops never reclassifies anything automatically. | `SaveCredentialEdits`, `SaveAndRotateBackup` |
| Delete | For a secret row: Yes = also remove it from the vault (vault first; if that fails, the row stays), No = keep the secret (listed as orphaned), Cancel. `.env` links to the row are cleared. | `DeleteCredential` |
| Vault check | Counts rows vs. vault entries without reading any value, and lists orphans **only within this data folder's scope**. Another `--data-dir`'s entries are counted but never offered for removal. | `VaultCheckView`, `CredentialRules.OrphanTargets` |
| .env registry | Register a file, or all `.env*` files directly inside a folder. It stores the path, project, environment label and key **names** with Present / Empty / Missing / Unknown state and an observed time. Actions: Copy key name, mark as expected, link to a record (opaque id), and **Copy KEY=value** from the linked record (secret copies use the history-excluded clipboard). Re-scan runs on demand only. Power Ops never edits `.env` files. | `Core/Credentials/EnvRegistry.cs` |
| Quick capture `+` | Small unowned topmost window: Note / Link / To-do / Read later / Clipboard image. A lone URL suggests Link. Project and labels are optional. Ctrl+Enter saves, Esc cancels. It creates ordinary Capture entries (business schema v7 unchanged) or routes an image through the existing managed media path, with the same rollback on save failure. It warns (never blocks) when the text looks like a secret. Opened from the toolbar `+ Quick capture` and from the existing `capture.quick` action (Ring / Shelf / global shortcut). | `MainWindow.QuickCapture.cs`, `Core/Capture/QuickCapture.cs` |
| AtlasNote handoff | File → *Export captures for AtlasNote (review)*. Checkbox list; items that look like they hold a secret start unchecked. Writes `powerops.atlasnote-handoff/1` with `sourceApp`, `sourceObjectId`, `sourceRevision`, `observedAt`, `authority`, `visibility`, `freshness: snapshot`, `projectRef`. No credential values, `.env` data or media binaries. Originals are kept. No live sync. | `AtlasNoteHandoff` |
| Content export | The `credentials` module exports metadata only: shareable IDs with their value, private IDs without it, and secrets as `credentialRef` (`powerops-credential:{id}`). `.env` paths are included only with local details. | `PortableExport` |

## Storage boundaries

- `credentials-ids.json`: format `powerops-credentials`, schema 1, 1 MiB cap, at most 2000 records / 200 `.env` files / 500 keys per file. Same store pattern as `quick-actions.json`: validated before every write, atomic replace, prior generation kept in `.backup`, and a missing file means an empty catalog (nothing written).
- Malformed or future files **fail closed**. The module shows the error and a Retry button. No save overwrites the bytes; the rest of the app keeps working.
- `workspace.json` (schema v7) and `shell-workspaces.json` (schema 1) are unchanged. Quick captures are normal `StickyNoteEntry` rows.
- A new `credentials` module id was added to the catalog. Existing saved workspace views do not list it until the user opens it from **View → Credentials & IDs**, the nav button (default views) or **Customize**. Opening it adds it to the current view, as for other modules.

## Evidence (2026-09-25)

Automated: `.\scripts\build.ps1` passed (full Release solution, inherited smoke suite, workspace tests **80/80**, 14 of them new). The new tests use synthetic secrets only. They cover:

- secret values in no metadata file, backup or export;
- private IDs exported without their value;
- backup rotation after moving a secret out of metadata;
- opaque, per-data-folder vault targets and orphan scoping;
- corrupt, future, dangling or invalid metadata failing closed with bytes preserved;
- `.env` parsing (comments, `export`, quotes, multi-line values, duplicates) returning names/state only, with no value in the result or its warnings;
- the registry keeping expected/linked keys as Missing and never rewriting the `.env` file;
- `KEY=value` quoting;
- the heuristics (long hex IDs and GUIDs are not flagged);
- quick-capture classification;
- the AtlasNote envelope;
- the module registration being compatible with existing shell files.

Native (driven through Windows UI Automation by Claude on this machine, isolated `--data-dir` under the session scratchpad, synthetic values only):

| Check | Result |
| --- | --- |
| Cloudflare set added; Account ID edited, saved and copied (clipboard = exact value) | PASS |
| Token set via PasswordBox → Credential Manager target `PowerOps/Secret/{scope}/{id}`, user `PowerOps` | PASS |
| Secret copy: clipboard holds the value and the three exclusion formats; cleared after 30 s | PASS |
| Reveal shows the value, then collapses after 10 s | PASS |
| No UIA name or help text contains the secret or the `.env` value (full tree scan after reveal) | PASS |
| No data-folder file contains the secret or the `.env` value | PASS |
| `.env` re-scan: Present / Present / Empty; copy key name; link to token; Copy KEY=value (secret) | PASS |
| Quick capture: URL auto-selects Link, saved as Bookmark with URL and title; To-do saved with status Open | PASS (after the fix below) |
| Vault check counts; deleting a secret row with Yes removes the vault entry and clears the `.env` link | PASS |
| Corrupt `credentials-ids.json`: module shows the error, `+ ID` and Retry do not change the bytes, other modules work | PASS |

Defects found and fixed during the native pass:

1. The quick-capture kind buttons and the `.env` "Expected" checkbox reacted only to `Click`. The UIA Toggle pattern used by assistive technology changed the visual state without changing the saved kind. They now use `Checked`/`Unchecked`.
2. WPF removes the first `_` from the automation name of a text button (access-key handling), so `FOO_API_TOKEN` was announced as `FOOAPI_TOKEN`. These names are now escaped.

**Not verified / BLOCKED**, not claimed as passed:

- Windows clipboard history (Win+V) actually omitting the copied secret. The formats are set, but the history UI was not inspected (BLOCKED: needs a manual look).
- Registering a `.env` through the Windows *Open file* / *Open folder* dialogs: the file-name box is not exposed to UI Automation. The entry was seeded while the app was closed, then re-scanned through the UI. Manual check needed.
- Content export and AtlasNote export Save dialogs, native. The JSON content is covered by the automated tests only.
- Quick capture from a global shortcut / Quick Ring while another app is focused, the clipboard-image path, DPI and multi-monitor placement.
- Screen reader walkthrough.

## Explicit non-goals kept

No CPU/fan telemetry, cloud or password sync, custom cryptography, Git-backed secrets, Mongo writes, background polling, automatic `.env` writing or bidirectional AtlasNote sync. Mongoku report cards are unchanged.
