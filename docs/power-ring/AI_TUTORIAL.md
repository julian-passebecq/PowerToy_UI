# Power Ring: tutorial for an AI that edits ring.json

You are asked to change Julian's Power Ring, a radial launcher for Windows. Everything the ring shows is in **one file, `ring.json`**. You edit that file and nothing else. This page is all you need; `ring.schema.json` (same folder) is the exact contract and `RING_CONFIG.md` is the shorter user guide.

## 1. Files and how a change goes live

| what | where |
| --- | --- |
| active config | `%APPDATA%\PowerRing\ring.json` |
| schema | `%APPDATA%\PowerRing\ring.schema.json` |
| saved alternative layouts | `%APPDATA%\PowerRing\layouts\*.json` (complete ring.json files) |
| previous version | `%APPDATA%\PowerRing\ring.json.bak` (written when a layout is switched from the tray) |
| cached web icons | `%APPDATA%\PowerRing\icons\` |
| quick notes (board) | `%APPDATA%\PowerRing\notes.json` |
| program | `%LOCALAPPDATA%\Programs\PowerRing\PowerRing.exe` |

- Saving `ring.json` reloads the ring at once. If the file is invalid, a notice names the line or the field path (for example `profiles[2].items[4].target: ...`) and **the previous version stays active**, so a mistake never breaks the ring.
- Comments (`//`) and trailing commas are accepted. Keep `"$schema": "./ring.schema.json"` at the top so editors validate.
- Unknown fields are ignored by the program (the schema flags them). Do not invent fields: if it is not in this page, it does not exist.
- To see the result without opening the ring, render a workspace to a PNG:
  `PowerRing.exe --config "<path>\ring.json" --snapshot out.png --profile 2` (profile number starts at 1; no window, no hotkey, exits afterwards).
- To keep the user's version, work on a copy in `layouts\` and let the user switch to it from the tray icon > Layouts.

## 2. Julian's mental model (keep it)

- The ring opens at the mouse pointer (hotkey `Ctrl+Alt+Shift+R`, mapped to a mouse button).
- It has **up to 6 workspaces** (`profiles`). On any ring workspace he sees the **centre** plus **4 small buttons on the centre's rim**: the 4 other ring workspaces. Clicking the centre opens the **clipboard board**. So: 1 current + 4 small buttons + the board = 6.
- Current layout (V1): Accueil, Travail, Code, Outils & news, Gestion (all `ring`), and Presse-papier (`board`).
- The small buttons list the next workspaces in order; when `centerClick` is `board` or `toggle`, board workspaces are left out of them (the centre already opens the board).
- If you add or remove a workspace, keep this shape: 5 ring workspaces + 1 board, unless the user asks otherwise.

## 3. Structure

```jsonc
{
  "$schema": "./ring.schema.json",
  "version": 1,                         // always 1
  "hotkey": "Ctrl+Alt+Shift+R",         // needs Ctrl, Alt or Win
  "startProfile": "home",               // id of the workspace shown first (optional)
  "appearance": { ... },                // sizes and colours, section 4
  "profiles": [                         // 1 to 6 workspaces
    { "id": "home", "name": "Accueil", "icon": "home", "accent": "#0078D4",
      "items": [ ... ] }                // circle 1: 1 to 10 buttons, clockwise from the top
  ]
}
```

### Workspaces (`profiles[]`)

| field | rule |
| --- | --- |
| `id` | 1-30 letters, digits or `-`, unique |
| `name` | 1-30 characters, shown to the user |
| `icon` | see section 6 |
| `accent` | hover colour while this workspace is shown |
| `enabled` | `false` hides it without deleting it; at least one must stay enabled |
| `kind` | `ring` (default), `board` or `gallery` |
| `items` | `ring` only: circle 1 |
| `tables` | `board` only: 1-8 tables |
| `sections` | `gallery` only: 1-6 sections |

### Buttons (`items[]`) and the three circles

```jsonc
{ "label": "ChatGPT", "action": "url", "target": "https://chatgpt.com/", "icon": "chat",
  "items": [                                            // children: shown right behind it on circle 2
    { "label": "Claude", "action": "run", "target": "%LOCALAPPDATA%\\Microsoft\\WindowsApps\\claude-desktop.exe" },
    { "label": "Codex", "action": "url", "target": "https://chatgpt.com/codex" }
  ] }
```

| field | rule |
| --- | --- |
| `label` | required, 1-40 characters (shown under the centre on hover) |
| `action`, `target`, `args`, `workingDirectory` | section 5 |
| `icon` | optional, section 6 |
| `color` | background of this button only |
| `items` | children |
| `delay` | `screen-to-clipboard` only, 0-10 s |

- Circle 1 holds 1-10 buttons per workspace, clockwise from the top.
- A button **with an action** can have **1 to 4 children**. They sit right behind it on circle 2; their own children sit on circle 3. Clicking the parent runs its action, clicking a child runs the child.
- A button **without an action** (or `"action": "group"`) and with `items` (1-10) is a **group**: its first children show behind it, and clicking it opens its full circle. The centre, Esc or a right click go back.
- **At most 3 circles** in total. A 4th level is refused.

### Boards (`kind: "board"`)

```jsonc
{ "id": "clip", "name": "Presse-papier", "icon": "clipboard", "kind": "board", "tables": [
  { "title": "Textes copiés", "kind": "clipboard", "columns": 2, "keep": 20 },
  { "title": "Images", "kind": "images", "keep": 8 },
  { "title": "Notes", "kind": "notes" },
  { "title": "Liens", "kind": "links", "columns": 3, "items": [ { "label": "Mongoku", "action": "url", "target": "http://localhost:3100/" } ] }
] }
```

Table kinds: `clipboard` (last copied texts), `images` (last copied images), `notes` (typed notes, saved in notes.json), `links` (1-60 buttons you list, no children). `columns` 1-3 (default 2); `keep` 1-50 for clipboard/images. Tables are switched with ‹ › or Left/Right.

### Galleries (`kind: "gallery"`)

```jsonc
{ "id": "apps", "name": "Galerie", "icon": "apps", "kind": "gallery", "sections": [
  { "title": "IA", "icon": "chat", "items": [ { "label": "Claude", "action": "url", "target": "https://claude.ai/new" } ] }
] }
```

1-6 sections (1-4 show as 2 columns, 5-6 as 3), each with a `title`, an optional `icon` and 1-24 tiles. Tiles cannot have children.

## 4. Sizes and colours (`appearance`)

Sizes are Windows display units (scaled with the monitor). **To make everything bigger or smaller, change `scale` first** (0.5-2.5; 0.85 = smaller, 1.2 = bigger).

| field | default | range | meaning |
| --- | --- | --- | --- |
| `scale` | 1 | 0.5-2.5 | multiplies every size |
| `spacing` | 6 | 0-60 | gap between centre, circles and buttons |
| `slotSize` | 42 | 28-160 | circle 1 button diameter |
| `satelliteSize` | 34 | 16-100 | circle 2 button diameter |
| `thirdSize` | 24 | 12-80 | circle 3 button diameter |
| `centerSize` | 50 | 28-200 | centre button |
| `iconSize` / `satelliteIconSize` / `thirdIconSize` | 20 / 18 / 12 | 10-96 / 8-48 / 6-40 | icon sizes per circle |
| `fontSize` | 12 | 8-32 | label text |
| `ringSize` | computed | 200-900 | disc diameter; omit it (it fits every circle). If set, `slotSize` must be under half of it |
| `slotRadius` | computed | 40-400 | minimum distance of circle 1 from the centre |
| `showSatellites`, `showThirdRing` | true | | show circles 2 and 3 |
| `showLabels` | true | | name of the hovered button under the centre |
| `showNumbers` | false | | small 1-10 numbers beside circle 1 |
| `workspaceButtons` | 4 | 0-4 | small workspace buttons on the centre's rim |
| `centerClick` | `board` | `board`, `home`, `toggle`, `close` | what a click on the centre does |
| `boardWidth`, `boardHeight` | 560, 420 | 300-1400, 200-1000 | size of boards and galleries |
| `animationMs` | 120 | 0-1000 | 0 = no animation |
| `webIcons` | true | | web buttons fetch the site's icon once |
| `theme` | `system` | `system`, `dark`, `light` | follows the Windows app theme by default |
| `opacity` | 1 | 0.3-1 | disc background opacity |
| `shadow` | true | | |
| `accent` | Windows accent | colour | hover colour; a workspace `accent` wins |
| `background`, `border`, `slot`, `slotHover`, `icon`, `text` | from theme | colour | disc, outline, buttons, hovered button, icons, labels |

Colours are `"#RRGGBB"` or `"#AARRGGBB"` (AA = opacity). Nothing else (no names, no `rgb()`). Prefer leaving colours to the theme, so dark and light mode both work; set a workspace `accent` to tell workspaces apart.

## 5. Actions

| action | target | notes |
| --- | --- | --- |
| `run` | program name or full `.exe` path | `args`, `workingDirectory` optional. `%LOCALAPPDATA%` etc. work. JSON paths need `\\` |
| `url` | `http(s)://`, `mailto:` or `ms-settings:` | never a user name or password in the address (refused) |
| `folder` | a path, or `downloads`, `desktop`, `documents`, `pictures`, `videos`, `music`, `home` | |
| `keys` | a key combination sent to the window that was in front | `"Win+Tab"`, `"Win+Shift+S"`, `"Ctrl+Shift+Esc"` |
| `text` | text copied to the clipboard (up to 4000 characters) | |
| `screenshot` | none | Windows region snip |
| `screen-to-clipboard` | none | whole screen under the pointer; `"delay": 3` counts down first |
| `powerops` | path to `JUtilityPalette.exe` | shows Power Ops |
| `ring-settings` | `edit`, `folder`, `reload` or `guide` | the ring's own settings |
| `power-mode` | `efficiency`, `balanced` or `performance` | Windows power mode |
| `close-apps` | 1-40 program names, comma separated (`"chrome, msedge"`) | asks each to close like clicking its X (it may still ask to save). Power Ring, Explorer and Claude are never closed, and listing them is refused |
| `group` | none | give `items` (a sub-circle) |

Keys for `keys` and `hotkey`: letters, digits, F1-F24, Tab, Esc, Enter, Space, Backspace, Delete, Insert, Home, End, PageUp, PageDown, Left, Right, Up, Down, PrintScreen, VolumeUp, VolumeDown, VolumeMute, MediaPlayPause, MediaNext, MediaPrevious, with Ctrl, Alt, Shift, Win.

Store apps: `"action": "run", "target": "explorer.exe", "args": "shell:AppsFolder\\<AppUserModelID>"` (for example Snipping Tool: `Microsoft.ScreenSketch_8wekyb3d8bbwe!App`).

## 6. Icons

Omit `icon` whenever possible: programs, files and folders show their real Windows icon, and web sites their own icon. Otherwise `icon` is one of:

- a name: app, apps, back, battery, bolt, book, bug, calendar, camera, chat, clipboard, cloud, code, copy, database, desktop, dev, document, download, downloads, edit, explorer, favorite, folder, game, globe, heart, home, keyboard, link, lock, mail, map, moon, music, note, person, phone, photo, pin, play, power, powerops, refresh, screen, screenshot, search, settings, share, shop, snap-left, snap-right, star, task-view, terminal, text, timer, tools, video, volume, web, windows, work;
- a Segoe Fluent Icons code point, 4-5 hex digits (`"E943"`);
- a path to a `.png`, `.ico`, `.jpg` or `.exe`, or any file or folder (its Windows icon).

## 7. Recipes

- **Add an app behind another one**: append to that button's `items` (max 4 children when the parent has an action). If it already has 4, turn a child into the new parent or use a group.
- **Add a workspace**: add a profile (max 6 in total). A 7th is refused: disable or merge one instead.
- **Rename / reorder**: change `name` or `label`; circle 1 order is clockwise from the top.
- **Smaller ring**: `"scale": 0.85`. Tighter: lower `spacing`. Fewer visible circles: `showThirdRing: false`.
- **Night mode button**: a group with `close-apps` (browsers, editors, never Claude) and `power-mode` `efficiency`.
- **Capture row**: `screenshot`, `screen-to-clipboard`, and `screen-to-clipboard` with `"delay": 3`.

## 8. What is not configurable

Do not promise these; they are fixed in the program:

- More than 6 workspaces, 10 buttons per circle, 4 children per action button, or 3 circles.
- The geometry: circle 1 starts at the top and goes clockwise; children sit behind their parent; the 4 workspace buttons sit on the centre's diagonals.
- Per-button size or font; fonts (Segoe UI Variable / Segoe Fluent Icons); per-workspace colours other than `accent`.
- Keyboard and mouse handling inside the ring (1-9, arrows, Enter, Tab, Ctrl+1-6, F1-F6, wheel, Esc/Backspace/right click, Back/Forward).
- The tray menu, the notice for invalid files, the layout switcher.
- Clipboard and image history are **kept in memory only**, never on disk, and copies marked private by password managers are skipped. There is no setting to persist them.
- `close-apps` never force-kills and never closes Power Ring, Explorer or Claude.
- Actions other than the list in section 5 (no scripts, no PowerShell commands, no chained actions). For a script, `run` `pwsh.exe` with `args`.

## 9. Before you hand the file back

1. Valid JSON, `"version": 1`, 1-6 profiles, unique ids, `startProfile` is one of them.
2. Every button has a `label`; every action has the target it needs; no credentials in URLs; paths use `\\`.
3. Limits: 10 per circle, 4 children per action button, 3 circles, gallery tiles and links without children.
4. Colours are `#RRGGBB` / `#AARRGGBB`; icon names are from section 6.
5. Julian's shape is kept (5 ring workspaces + the clipboard board, centre opens the board) unless he asked to change it.
6. If you can run it: `PowerRing.exe --config ring.json --snapshot check.png --profile N` for each changed workspace, and look at the PNG.
