namespace PowerRing.Core;

/// <summary>The ring.json written on first start. Kept as JSON text (with comments) so it reads like the file the user edits.</summary>
public static class RingDefaults
{
    public const string Json = """
{
  // Power Ring settings. Edit and save: the ring reloads by itself. A mistake is reported and the last good version stays active.
  // Every field is described in ring.schema.json (autocompletion in VS Code) and RING_CONFIG.md.
  "$schema": "./ring.schema.json",
  "version": 1,
  "hotkey": "Ctrl+Alt+Shift+R",
  "startProfile": "dev",
  "appearance": {
    "theme": "system",
    "ringSize": 340,
    "slotSize": 60,
    "centerSize": 78,
    "iconSize": 24,
    "fontSize": 12,
    "opacity": 0.94,
    "shadow": true,
    "showNumbers": true,
    "showLabels": true,
    "animationMs": 120
  },
  "profiles": [
    {
      "id": "dev", "name": "Dev", "icon": "code", "accent": "#3B82F6",
      "items": [
        { "label": "VS Code", "icon": "code", "action": "run", "target": "code" },
        { "label": "Terminal", "icon": "terminal", "action": "run", "target": "wt" },
        { "label": "Screenshot", "action": "screenshot" },
        { "label": "Whole screen", "action": "screen-to-clipboard" },
        { "label": "Folders", "icon": "folder", "items": [
          { "label": "Downloads", "action": "folder", "target": "downloads" },
          { "label": "Desktop", "action": "folder", "target": "desktop" },
          { "label": "Documents", "action": "folder", "target": "documents" },
          { "label": "Home", "icon": "home", "action": "folder", "target": "home" }
        ] },
        { "label": "Web", "icon": "web", "items": [
          { "label": "GitHub", "icon": "code", "action": "url", "target": "https://github.com/" },
          { "label": "Claude", "icon": "chat", "action": "url", "target": "https://claude.ai/" },
          { "label": "Local", "icon": "database", "items": [
            { "label": "Mongoku", "icon": "database", "action": "url", "target": "http://localhost:3100/" },
            { "label": "Claude Control", "icon": "settings", "action": "url", "target": "http://127.0.0.1:7430/" }
          ] }
        ] },
        { "label": "Windows", "icon": "windows", "items": [
          { "label": "All windows", "icon": "task-view", "action": "keys", "target": "Win+Tab" },
          { "label": "Desktop", "icon": "desktop", "action": "keys", "target": "Win+D" },
          { "label": "Left half", "icon": "snap-left", "action": "keys", "target": "Win+Left" },
          { "label": "Right half", "icon": "snap-right", "action": "keys", "target": "Win+Right" },
          { "label": "Task Manager", "icon": "tools", "action": "keys", "target": "Ctrl+Shift+Esc" }
        ] },
        { "label": "Power Ops", "action": "powerops" }
      ]
    },
    {
      "id": "perso", "name": "Perso", "icon": "person", "accent": "#22C55E",
      "items": [
        { "label": "Gmail", "icon": "mail", "action": "url", "target": "https://mail.google.com/" },
        { "label": "WhatsApp", "icon": "chat", "action": "url", "target": "https://web.whatsapp.com/" },
        { "label": "Screenshot", "action": "screenshot" },
        { "label": "Whole screen", "action": "screen-to-clipboard" },
        { "label": "Downloads", "action": "folder", "target": "downloads" },
        { "label": "Calendar", "icon": "calendar", "action": "url", "target": "https://calendar.google.com/" },
        { "label": "YouTube", "icon": "video", "action": "url", "target": "https://www.youtube.com/" },
        { "label": "Power Ops", "action": "powerops" }
      ]
    },
    {
      "id": "work", "name": "Work", "icon": "work", "accent": "#F97316",
      "items": [
        { "label": "Outlook", "icon": "mail", "action": "url", "target": "https://outlook.office.com/" },
        { "label": "Teams", "icon": "chat", "action": "url", "target": "https://teams.microsoft.com/" },
        { "label": "Screenshot", "action": "screenshot" },
        { "label": "Whole screen", "action": "screen-to-clipboard" },
        { "label": "Folders", "icon": "folder", "items": [
          { "label": "Downloads", "action": "folder", "target": "downloads" },
          { "label": "Documents", "action": "folder", "target": "documents" }
        ] },
        { "label": "Windows", "icon": "windows", "items": [
          { "label": "All windows", "icon": "task-view", "action": "keys", "target": "Win+Tab" },
          { "label": "Desktop", "icon": "desktop", "action": "keys", "target": "Win+D" },
          { "label": "Left half", "icon": "snap-left", "action": "keys", "target": "Win+Left" },
          { "label": "Right half", "icon": "snap-right", "action": "keys", "target": "Win+Right" }
        ] },
        { "label": "Power Ops", "action": "powerops" }
      ]
    }
  ]
}
""";

    public static RingConfig Create() => RingConfigs.Parse(Json);
}
