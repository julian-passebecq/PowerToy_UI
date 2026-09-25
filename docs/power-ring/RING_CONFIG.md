# Power Ring: ring.json guide

Power Ring is a light radial launcher. It opens at the mouse pointer with a hotkey (Ctrl+Alt+Shift+R by default), for example from the MX Master Sense Panel through Logi Options+. Everything is set in `ring.json`, next to this file (`%APPDATA%\PowerRing`). Save the file and the ring reloads. If the file has a mistake, a notice names the line or the field, and the previous version stays active.

`ring.schema.json` describes every field, so VS Code gives autocompletion. To change the ring with an AI assistant, give it this guide and your `ring.json`, and ask for example "add Figma behind Canva in my Work workspace" or "make the ring smaller".

## Structure

```jsonc
{
  "version": 1,
  "hotkey": "Ctrl+Alt+Shift+R",
  "startProfile": "home",
  "appearance": { "scale": 1.0, "spacing": 6 },   // sizes, colours, theme (see below)
  "profiles": [                                     // 1 to 6 workspaces
    { "id": "home", "name": "Home", "icon": "home",
      "items": [ ... ] }                            // circle 1: 1 to 10 buttons, clockwise from the top
  ]
}
```

A button:

```jsonc
{ "label": "VS Code", "action": "run", "target": "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe" }
```

### Three circles, all visible

Give a button `items` and its children appear **right behind it** on the next circle (up to 4 visible), and their children on a third circle. Clicking the button runs its action; clicking a child runs the child.

```jsonc
{ "label": "ChatGPT", "action": "url", "target": "https://chatgpt.com/", "items": [
  { "label": "Claude", "action": "url", "target": "https://claude.ai/new" },
  { "label": "Codex", "action": "url", "target": "https://chatgpt.com/codex" }
] }
```

A button **without** an action but with `items` is a group: its first children show behind it, and clicking it opens its full circle (the centre, Esc or a right click go back). At most 3 circles in total.

### Workspaces

`kind` picks how a workspace looks:

- `ring` (default): circles of buttons, with `items`.
- `board`: tables switched with the ‹ › arrows (or Left/Right), with `tables`:
  - `clipboard`: the last copied texts (kept in memory only, never written to disk; copies that password managers mark as private are skipped);
  - `images`: the last copied images (memory only);
  - `notes`: quick notes, typed in the box and saved in `notes.json`;
  - `links`: any buttons you list (`items`); right-click copies the address.
  Each table has a `title`, `columns` (1-3) and, for clipboard/images, `keep` (how many).
- `gallery`: an app library, with `sections` (1-6), each a `title`, an optional `icon` and `items` shown as tiles in rows.

`"enabled": false` hides a workspace without deleting it (also from the tray icon > Workspaces). The ring's centre shows the current workspace with the previous and next ones as small icons on its sides; board and gallery show all workspaces at the bottom.

## Actions

| action | target | example |
| --- | --- | --- |
| `run` | program or .exe (plus `args`, `workingDirectory`) | `"target": "code"`, `"args": "D:\\PROJ\\app"` |
| `url` | http(s)://, mailto: or ms-settings: address | `"target": "https://mail.google.com/"` |
| `folder` | a path, or `downloads`, `desktop`, `documents`, `pictures`, `videos`, `music`, `home` | `"target": "D:\\PROJ"` |
| `keys` | key combination sent to the window that was in front | `"Win+Tab"`, `"Win+D"`, `"Win+Left"`, `"Win+Shift+R"` |
| `text` | text copied to the clipboard | `"target": "julian@example.com"` |
| `screenshot` | none: Windows region snip | |
| `screen-to-clipboard` | none: the whole screen under the pointer, copied | |
| `powerops` | path to `JUtilityPalette.exe` | shows Power Ops |
| `ring-settings` | `edit`, `folder`, `reload` or `guide` | Power Ring's own settings, from the ring |
| `group` | none: give `items` | sub-circle |

Keys: letters, digits, F1-F24, Tab, Esc, Enter, Space, Backspace, Delete, Insert, Home, End, PageUp, PageDown, Left, Right, Up, Down, PrintScreen, VolumeUp, VolumeDown, VolumeMute, MediaPlayPause, MediaNext, MediaPrevious, with Ctrl, Alt, Shift, Win.

## Icons

Without an `"icon"`, programs and folders show their real Windows icon and web sites their own icon (fetched once from the site, cached in the `icons` folder; `"webIcons": false` turns this off). Otherwise `"icon"` is:

- a name: app, back, bolt, book, bug, calendar, camera, chat, clipboard, cloud, code, copy, database, desktop, dev, document, download, edit, explorer, favorite, folder, game, globe, heart, home, keyboard, link, lock, mail, map, music, note, person, phone, photo, pin, play, power, powerops, refresh, screen, screenshot, search, settings, share, shop, snap-left, snap-right, star, task-view, terminal, text, tools, video, volume, web, windows, work;
- a Segoe Fluent Icons code such as `"E943"`;
- a path: `.png`, `.ico`, `.jpg`, an `.exe`, or any file or folder (its Windows icon).

## Appearance

| field | default | meaning |
| --- | --- | --- |
| `scale` | 1.0 | everything bigger (1.2) or smaller (0.85): the easiest knob |
| `spacing` | 6 | gap between the centre, the circles and the buttons |
| `slotSize`, `satelliteSize`, `thirdSize` | 42, 34, 24 | button size on circles 1, 2 and 3 |
| `centerSize` | 60 | centre button |
| `iconSize`, `satelliteIconSize`, `thirdIconSize` | 20, 18, 12 | |
| `showSatellites`, `showThirdRing` | true | show circles 2 and 3 |
| `ringSize` | computed | disc diameter |
| `slotRadius` | computed | minimum distance of circle 1 |
| `theme` | `system` | `system`, `dark` or `light` |
| `accent` | Windows accent | hover colour; a workspace's `accent` wins |
| `background`, `border`, `slot`, `slotHover`, `icon`, `text` | theme | `#RRGGBB` or `#AARRGGBB` |
| `opacity` | 1 | disc background opacity (0.3-1) |
| `shadow`, `showLabels` | true | |
| `showNumbers` | false | small numbers next to the main buttons |
| `webIcons` | true | site icons for web buttons |
| `boardWidth`, `boardHeight` | 560, 420 | size of board and gallery workspaces |
| `animationMs` | 120 | 0 turns animations off |

A button can have its own `"color"` (background).

## Layouts

Put complete alternative `ring.json` files in `layouts\` next to this guide. Tray icon > Layouts makes one active (it is checked first; the current `ring.json` is kept as `ring.json.bak`). Useful to compare versions.

## Using the ring

- Click a button, or press its number 1-9. Arrows move between the main buttons, Enter runs.
- Workspaces: the small icons beside the centre, Tab / Shift+Tab, Ctrl+1-6, F1-F6 or the mouse wheel.
- Esc, Backspace or a right click go back; Esc on the first circle, the centre, or a click outside close the ring.
- Board: ‹ › or Left/Right change table, click copies (or opens a link), 1-9 pick a card.
- Tray icon: open the ring, pick the workspace, show/hide workspaces, add a preset, switch layout, edit ring.json, reload, start with Windows, exit.
- `PowerRing.exe --show` opens the ring (for tools that cannot send a hotkey), `--profile N` picks a workspace, `--exit` closes it.
