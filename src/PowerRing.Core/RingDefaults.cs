namespace PowerRing.Core;

/// <summary>
/// The ring.json written on first start, kept as JSON text (with comments) so it reads like the file the user edits.
/// Its workspaces are also the presets offered by the tray menu (Workspaces > Add).
/// </summary>
public static class RingDefaults
{
    public const string Json = """
{
  // Power Ring settings. Edit and save: the ring reloads by itself. A mistake is reported and the last good version stays active.
  // Every field is described in ring.schema.json (autocompletion in VS Code) and RING_CONFIG.md.
  // Children ("items") of a button are shown right behind it on the next circle; up to 3 circles.
  // Buttons without an "icon" show the real Windows icon of their program or folder.
  // Hide a workspace with "enabled": false (or tray icon > Workspaces).
  "$schema": "./ring.schema.json",
  "version": 1,
  "hotkey": "Ctrl+Alt+Shift+R",
  "startProfile": "home",
  "appearance": {
    "theme": "system",
    "scale": 1.0,        // everything bigger (1.2) or smaller (0.85)
    "spacing": 6,        // gap between the circles
    "slotSize": 42,      // circle 1
    "satelliteSize": 34, // circle 2
    "thirdSize": 24,     // circle 3
    "centerSize": 60,
    "iconSize": 20,
    "satelliteIconSize": 18,
    "showSatellites": true,
    "showThirdRing": true,
    "animationMs": 120
  },
  "profiles": [
    {
      "id": "home", "name": "Home", "icon": "home",
      "items": [
        { "label": "VS Code", "action": "run", "target": "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe", "items": [
          { "label": "New window", "icon": "code", "action": "run", "target": "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe", "args": "--new-window" }
        ] },
        { "label": "Text editor", "action": "run", "target": "notepad.exe", "items": [
          { "label": "Notepad++", "action": "run", "target": "C:\\Program Files\\Notepad++\\notepad++.exe" }
        ] },
        { "label": "ChatGPT", "icon": "chat", "action": "url", "target": "https://chatgpt.com/", "items": [
          { "label": "Claude", "icon": "chat", "action": "url", "target": "https://claude.ai/new" },
          { "label": "Codex", "icon": "code", "action": "url", "target": "https://chatgpt.com/codex" }
        ] },
        { "label": "Fabric", "icon": "database", "action": "url", "target": "https://app.fabric.microsoft.com/", "items": [
          { "label": "Azure", "icon": "cloud", "action": "url", "target": "https://portal.azure.com/" },
          { "label": "Power BI", "icon": "bolt", "action": "url", "target": "https://app.powerbi.com/" }
        ] },
        { "label": "Databricks", "icon": "database", "action": "url", "target": "https://accounts.cloud.databricks.com/", "items": [
          { "label": "BigQuery", "icon": "search", "action": "url", "target": "https://console.cloud.google.com/bigquery" },
          { "label": "Oracle Cloud", "icon": "cloud", "action": "url", "target": "https://cloud.oracle.com/" }
        ] },
        { "label": "GitHub", "icon": "code", "action": "url", "target": "https://github.com/", "items": [
          { "label": "MongoDB Atlas", "icon": "database", "action": "url", "target": "https://cloud.mongodb.com/" },
          { "label": "Streamlit", "icon": "play", "action": "url", "target": "https://share.streamlit.io/" }
        ] },
        { "label": "Cloudflare", "icon": "cloud", "action": "url", "target": "https://dash.cloudflare.com/", "items": [
          { "label": "Netlify", "icon": "globe", "action": "url", "target": "https://app.netlify.com/" },
          { "label": "Vercel", "icon": "bolt", "action": "url", "target": "https://vercel.com/dashboard" }
        ] },
        { "label": "Explorer", "action": "run", "target": "explorer.exe", "items": [
          { "label": "Downloads", "action": "folder", "target": "downloads" },
          { "label": "Desktop", "action": "folder", "target": "desktop" },
          { "label": "Documents", "action": "folder", "target": "documents" }
        ] },
        { "label": "Screenshot", "action": "screenshot", "items": [
          { "label": "Whole screen", "action": "screen-to-clipboard" },
          { "label": "Video", "icon": "video", "action": "keys", "target": "Win+Shift+R" }
        ] }
      ]
    },
    {
      "id": "code", "name": "Code", "icon": "code", "accent": "#3B82F6",
      "items": [
        { "label": "VS Code", "action": "run", "target": "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe", "items": [
          { "label": "New window", "icon": "code", "action": "run", "target": "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe", "args": "--new-window" }
        ] },
        { "label": "GitHub Desktop", "action": "run", "target": "%LOCALAPPDATA%\\GitHubDesktop\\GitHubDesktop.exe", "items": [
          { "label": "Git Bash", "action": "run", "target": "C:\\Program Files\\Git\\git-bash.exe" },
          { "label": "GitHub", "icon": "code", "action": "url", "target": "https://github.com/" }
        ] },
        { "label": "Terminal", "action": "run", "target": "wt.exe", "items": [
          { "label": "PowerShell", "action": "run", "target": "powershell.exe" },
          { "label": "Linux", "action": "run", "target": "wsl.exe" },
          { "label": "Command prompt", "action": "run", "target": "cmd.exe" }
        ] },
        { "label": "Claude", "icon": "chat", "action": "url", "target": "https://claude.ai/new", "items": [
          { "label": "Claude Code", "icon": "code", "action": "url", "target": "https://claude.ai/code" }
        ] },
        { "label": "Explorer", "action": "run", "target": "explorer.exe", "items": [
          { "label": "Downloads", "action": "folder", "target": "downloads" },
          { "label": "Home", "action": "folder", "target": "home" }
        ] }
      ]
    },
    {
      "id": "manage", "name": "Manage", "icon": "settings", "accent": "#8B5CF6",
      "items": [
        { "label": "All windows", "icon": "task-view", "action": "keys", "target": "Win+Tab", "items": [
          { "label": "Left half", "icon": "snap-left", "action": "keys", "target": "Win+Left" },
          { "label": "Right half", "icon": "snap-right", "action": "keys", "target": "Win+Right" },
          { "label": "Desktop", "icon": "desktop", "action": "keys", "target": "Win+D" }
        ] },
        { "label": "Task Manager", "action": "run", "target": "taskmgr.exe", "items": [
          { "label": "Resource Monitor", "action": "run", "target": "resmon.exe" },
          { "label": "Services", "icon": "tools", "action": "run", "target": "services.msc" }
        ] },
        { "label": "Apps", "icon": "apps", "action": "url", "target": "ms-settings:appsfeatures", "items": [
          { "label": "Startup apps", "icon": "power", "action": "url", "target": "ms-settings:startupapps" },
          { "label": "Storage", "icon": "database", "action": "url", "target": "ms-settings:storagesense" }
        ] },
        { "label": "Environment variables", "icon": "keyboard", "action": "run", "target": "rundll32.exe", "args": "sysdm.cpl,EditEnvironmentVariables" },
        { "label": "Clipboard history", "icon": "clipboard", "action": "keys", "target": "Win+V" },
        { "label": "Windows settings", "icon": "settings", "action": "url", "target": "ms-settings:" },
        { "label": "Power Ring settings", "icon": "edit", "action": "ring-settings", "target": "edit", "items": [
          { "label": "Settings folder", "icon": "folder", "action": "ring-settings", "target": "folder" },
          { "label": "Reload", "icon": "refresh", "action": "ring-settings", "target": "reload" },
          { "label": "Guide", "icon": "book", "action": "ring-settings", "target": "guide" }
        ] }
      ]
    },
    {
      "id": "clip", "name": "Clipboard", "icon": "clipboard", "kind": "board", "accent": "#10B981",
      "tables": [
        { "title": "Last copied", "kind": "clipboard", "columns": 2, "keep": 20 },
        { "title": "Images", "kind": "images", "columns": 3, "keep": 9 },
        { "title": "Notes", "kind": "notes", "columns": 2 },
        { "title": "Links", "kind": "links", "columns": 3, "items": [
          { "label": "GitHub", "action": "url", "target": "https://github.com/" },
          { "label": "ChatGPT", "action": "url", "target": "https://chatgpt.com/" },
          { "label": "Claude", "action": "url", "target": "https://claude.ai/new" }
        ] }
      ]
    }
  ]
}
""";

    public static RingConfig Create() => RingConfigs.Parse(Json);

    /// <summary>Built-in workspaces the tray menu can add back to a ring.json that lacks them.</summary>
    public static IReadOnlyList<RingProfile> Presets() => Create().Profiles;
}
