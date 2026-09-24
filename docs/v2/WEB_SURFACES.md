# Power Ops - web surfaces decision (Mongoku, Grafana, AI chats)

Date: 2026-09-24. Decided by Claude Code at the user's request ("decide"). Extends `ARCHITECTURE.md` and `QUICK_ACTIONS_MX_MASTER.md`.

## Decision

1. **Never embed or fork `chromium/chromium`.** If Power Ops ever renders a page itself, it uses Microsoft **WebView2**. WebView2 is the Chromium engine Windows already ships and services (Evergreen runtime 153 is installed on the user's laptop).
2. **Ship web apps as launch targets first (done in this slice).** Each web app is a user-configured `web:` quick action, so it works from the Actions menu, the Quick Shelf, global shortcuts and later the Ring and MX Master. It can open in two ways:
   - **App window:** Chrome/Edge `--app=URL` gives a dedicated window with no tabs or address bar, using the user's existing browser profile and sign-ins.
   - **Default browser.**

   Cost to Power Ops: zero while idle; nothing starts until clicked.
3. **AI chats (Gemini, ChatGPT, Claude) stay in app windows, never embedded.** They depend on the user's Google/OpenAI/Anthropic sessions. Google blocks OAuth sign-in inside embedded webviews, and a second browser profile inside Power Ops would mean signing in again and holding more cookies. Chrome is the default browser here, so app windows reuse the existing signed-in profile.
4. **An embedded "Web workspace" tab is a later, optional, measured slice, targeted at local tools such as Mongoku.** It is justified only for localhost/self-hosted tools where being next to the project context helps. Conditions:
   - lazy: no WebView2 environment until the first embedded open; disposed when the tab closes;
   - `WebView2CompositionControl`, to avoid airspace problems with the Shelf, Ring, menus and Summon;
   - one persistent user-data folder per Power Ops data dir, because Mongoku keeps its tabs in `localStorage`;
   - `NewWindowRequested` → open in an app window or the default browser; downloads prompt; no host objects or script bridge;
   - an allow-list: only web apps the user marked "embedded";
   - measure working set, processes and startup before and after, and keep it only if the numbers are acceptable.

   This adds the first NuGet dependency (`Microsoft.Web.WebView2`), which CI must restore.
5. **Native read-only summary cards come after that, and only where an API exists.** Mongoku qualifies; Grafana does not yet.

## Mongoku facts (source: `julian-passebecq/Mongoku-datapass`, branch `datapass/control-plane-v1`)

- **Runtime:** SvelteKit/Node web app. `pnpm dev` → `http://localhost:3100`; `mongoku` CLI default port 3100; Docker sets `MONGOKU_SERVER_ORIGIN=http://localhost:3100`. Vercel adapter present. **No deployed URL is recorded**, so Power Ops assumes nothing beyond the localhost preset.
- **Auth:** off by default. Optional basic auth (`MONGOKU_AUTH_BASIC`, which also applies to API calls) or generic OIDC with an httpOnly SameSite=Lax session cookie. An embedded view is fine with no auth, basic auth or a non-Google OIDC provider. With a Google IdP, use the app window.
- **Headers:** no X-Frame-Options or CSP frame-ancestors (irrelevant to WebView2 top-level navigation).
- **Embedding caveats:**
  - `window.open`/`target=_blank` usage needs `NewWindowRequested` handling;
  - Blob downloads and a file picker need testing;
  - clipboard is used;
  - `localStorage` persists workspace tabs.
- **Deep links** (usable as web-app URLs today):
  - `/servers/{hostKey}/databases/{db}/collections/{coll}/documents?query=...`
  - `/queries?query={savedQueryId}&project={id}`
  - `/foil/report/{FOIL_*}`
  - `/projects?project={id}`
- **Cheap health:** `GET /api/health` returns `{status, mode, controlDisabled, readOnly, writesEnabled, commit}` without touching MongoDB. It is behind auth when auth is on.
- **Summary card candidate:** `GET /api/datapass/reports/{reportId}` is read-only and returns sections with `meta.returnedRows`, capped at 500 rows and 5 s. Examples: `FOIL_STATUS_NOW`, `FOIL_NEXT`, `GLOBAL_PROJECTS`.
  - There is no "list reports" endpoint.
  - `POST /api/datapass/query/:id` needs the control DB, so it fails in the default mode.
  - A future card must be on demand only, show source/age, and keep any credential in Windows Credential Manager/DPAPI, never in Power Ops JSON.
- The branch is an open PR, not yet merged into `master`; its handoff asks for a connected UI smoke test first.

## Grafana facts (source: `julian-passebecq/grafana`)

The repository is an empty placeholder (README only, created 2026-09-24). There is no server, dashboards, auth or URL yet. Power Ops therefore offers only a **Grafana preset pointing at `http://localhost:3000/`** that the user edits to the real stack URL. Embedding or status cards wait until the deployment exists. Embedding would need `allow_embedding`, cookie SameSite settings and a non-Google login; cards would need a read-only viewer service-account token stored outside Power Ops JSON.

## Not doing

- Chromium or CEF bundles, Electron conversion, or a browser fleet of always-on webviews.
- Scraping another browser's cookies or local storage.
- Background health polling of web apps.
- Exposing Power Ops commands to arbitrary web pages.
