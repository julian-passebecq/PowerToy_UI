# Power Ring: ring.json guide

Power Ring is a light radial launcher. It opens at the mouse pointer with a hotkey (by default Ctrl+Alt+Shift+R), for example from the MX Master Sense Panel through Logi Options+. Everything is set in `ring.json`, next to this file (`%APPDATA%\PowerRing`). Save the file and the ring reloads. If the file has a mistake, a notification names the line or the field, and the previous version stays active.

`ring.schema.json` describes every field, so VS Code gives autocompletion. To change the ring with an AI assistant, give it this guide and your `ring.json`, and ask for example "add Figma to my Dev profile" or "make the ring bigger with green buttons".

## Structure

```jsonc
{
  "version": 1,
  "hotkey": "Ctrl+Alt+Shift+R",
  "startProfile": "dev",
  "appearance": { ... },          // sizes, colours, theme
  "profiles": [                   // 1 to 5; buttons 1-5 above the ring
    { "id": "dev", "name": "Dev", "icon": "code", "accent": "#3B82F6",
      "items": [ ... ] }          // one circle: 1 to 8 buttons, clockwise from the top
  ]
}
```

An item is a button:

```jsonc
{ "label": "VS Code", "icon": "code", "action": "run", "target": "code" }
```

A sub-circle is an item with `items` instead of an action (at most 3 levels: ring › circle › circle):

```jsonc
{ "label": "Folders", "icon": "folder", "items": [
  { "label": "Downloads", "action": "folder", "target": "downloads" }
] }
```

## Actions

| action | target | example |
| --- | --- | --- |
| `run` | program or .exe (plus `args`, `workingDirectory`) | `"target": "code", "args": "D:\\PROJ\\app"` |
| `url` | http(s):// or mailto: address | `"target": "https://mail.google.com/"` |
| `folder` | a path, or `downloads`, `desktop`, `documents`, `pictures`, `videos`, `music`, `home` | `"target": "D:\\PROJ"` |
| `keys` | key combination sent to the window that was in front | `"target": "Win+Tab"`, `"Win+D"`, `"Win+Left"`, `"Ctrl+Shift+Esc"` |
| `text` | text copied to the clipboard | `"target": "julian@example.com"` |
| `screenshot` | none: Windows region snip | |
| `screen-to-clipboard` | none: the whole screen under the pointer, copied | |
| `powerops` | optional path to `JUtilityPalette.exe` | shows Power Ops; without a path it uses the running one |
| `group` | none: give `items` | sub-circle |

Keys: letters, digits, F1-F24, Tab, Esc, Enter, Space, Backspace, Delete, Insert, Home, End, PageUp, PageDown, Left, Right, Up, Down, PrintScreen, VolumeUp, VolumeDown, VolumeMute, MediaPlayPause, MediaNext, MediaPrevious, with Ctrl, Alt, Shift, Win.

## Icons

`"icon"` is one of:

- a name: app, back, bolt, book, bug, calendar, camera, chat, clipboard, cloud, code, copy, database, desktop, dev, document, download, edit, explorer, favorite, folder, game, globe, heart, home, keyboard, link, lock, mail, map, music, note, person, phone, photo, pin, play, power, powerops, refresh, screen, screenshot, search, settings, share, shop, snap-left, snap-right, star, task-view, terminal, text, tools, video, volume, web, windows, work;
- a Segoe Fluent Icons code such as `"E943"`;
- a file: `.png`, `.ico`, `.jpg`, or an `.exe` (its own icon, for example `"C:\\Users\\me\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe"`).

Without an icon, one is chosen from the action.

## Appearance

| field | default | meaning |
| --- | --- | --- |
| `theme` | `system` | `system`, `dark` or `light` |
| `ringSize` | 340 | disc diameter |
| `slotSize` | 60 | button diameter (less than half of ringSize) |
| `centerSize` | 78 | centre button diameter |
| `slotRadius` | computed | distance from the centre to the buttons |
| `iconSize`, `fontSize` | 24, 12 | |
| `accent` | Windows accent | hover colour; a profile's `accent` wins |
| `background`, `border`, `slot`, `slotHover`, `icon`, `text` | theme | `#RRGGBB` or `#AARRGGBB` |
| `opacity` | 0.94 | disc background opacity (0.3-1) |
| `shadow`, `showNumbers`, `showLabels` | true | |
| `animationMs` | 120 | 0 turns animations off |

An item can have its own `"color"` (button background).

## Using the ring

- Click a button, or press its number 1-8. Arrows move, Enter runs.
- A button with a small › opens its sub-circle; the centre, Esc, Backspace or a right click go back. Esc on the first circle closes the ring, and so does a click outside.
- Profile buttons 1-5 above the ring, Tab / Shift+Tab, or the mouse wheel switch profile.
- The tray icon: open the ring, pick the profile, edit ring.json, reload, start with Windows, exit.
- `PowerRing.exe --show` opens the ring (for tools that cannot send a hotkey), `--exit` closes it.
