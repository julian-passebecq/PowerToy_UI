using System.Text.Json;
using System.Text.Json.Nodes;
using JUtility.Core.Actions;
using System.Net;
using JUtility.Core.Models;
using JUtility.Core.Reports;
using JUtility.Core.Workspaces;
using JUtility.Core.Capture;
using JUtility.Core.Credentials;
using JUtility.Core.Files;

var tests = new List<(string Name, Action Test)>();
void Test(string name, Action test) => tests.Add((name, test));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action action)
{
    try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException or InvalidOperationException) { return; }
    throw new Exception("Expected validation rejection");
}
void Temporary(Action<string> test)
{
    string directory = Path.Combine(Path.GetTempPath(), "PowerOpsV2Tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { test(directory); } finally { Directory.Delete(directory, true); }
}
Test("Default presets are valid and independently owned", () =>
{
    var state = WorkspaceSessions.Defaults(); WorkspaceSessions.Validate(state);
    Check(state.Workspaces.Count == 5);
    WorkspaceSessions.ActiveTab(state.Workspaces[0]).Search = "Foil";
    Check(WorkspaceSessions.ActiveTab(state.Workspaces[1]).Search == "");
});
Test("Tabs preserve independent search and filter state", () =>
{
    var p = WorkspaceSessions.NewProfile("Test");
    var a = WorkspaceSessions.AddTab(p, "repositories"); a.Search = "first"; a.Families.Add("Foil");
    var b = WorkspaceSessions.AddTab(p, "repositories"); b.Search = "second";
    Check(a.Search == "first" && b.Families.Count == 0);
});
Test("Closing last tab retains a usable Launchpad", () =>
{
    var p = WorkspaceSessions.NewProfile("Test"); WorkspaceSessions.CloseTab(p);
    Check(p.Tabs.Count == 1 && WorkspaceSessions.ActiveTab(p).ModuleId == "home");
});
Test("Reopen preserves closed tab state without aliasing", () =>
{
    var p = WorkspaceSessions.NewProfile("Test"); var tab = WorkspaceSessions.AddTab(p, "capture"); tab.Search = "one";
    WorkspaceSessions.CloseTab(p); tab.Search = "mutated"; WorkspaceSessions.ReopenTab(p);
    Check(WorkspaceSessions.ActiveTab(p).Search == "one");
});
Test("Tab limit is enforced", () =>
{
    var p = WorkspaceSessions.NewProfile("Test");
    while (p.Tabs.Count < WorkspaceSessions.MaxTabs) WorkspaceSessions.AddTab(p, "home");
    Reject(() => WorkspaceSessions.AddTab(p, "home"));
});
Test("Hidden module tabs recover to home and settings cannot disappear", () =>
{
    var p = WorkspaceSessions.NewProfile("Test"); WorkspaceSessions.AddTab(p, "repositories").Search = "sensitive";
    WorkspaceSessions.SetVisibleModules(p, []);
    Check(p.VisibleModules.Contains("home") && p.VisibleModules.Contains("settings"));
    Check(WorkspaceSessions.ActiveTab(p).ModuleId == "home" && WorkspaceSessions.ActiveTab(p).Search == "");
    Reject(() => WorkspaceSessions.AddTab(p, "repositories"));
});
Test("Saved view is an immutable deep copy", () =>
{
    var state = WorkspaceSessions.Defaults(); var p = WorkspaceSessions.Active(state);
    WorkspaceSessions.AddTab(p, "repositories").Search = "saved";
    WorkspaceSessions.SaveView(state, "Checkpoint");
    WorkspaceSessions.ActiveTab(p).Search = "later";
    Check(WorkspaceSessions.ActiveTab(state.Bookmarks[0].View).Search == "saved");
    WorkspaceSessions.RestoreView(state, state.Bookmarks[0].Id);
    WorkspaceSessions.ActiveTab(WorkspaceSessions.Active(state)).Search = "restored then changed";
    Check(WorkspaceSessions.ActiveTab(state.Bookmarks[0].View).Search == "saved");
});
Test("Deleting a view does not prevent restoring its saved bookmark", () =>
{
    var state = WorkspaceSessions.Defaults(); var id = state.ActiveWorkspaceId; WorkspaceSessions.SaveView(state, "Restore me");
    state.Workspaces.Remove(WorkspaceSessions.Active(state)); state.ActiveWorkspaceId = state.Workspaces[0].Id;
    WorkspaceSessions.RestoreView(state, state.Bookmarks[0].Id); WorkspaceSessions.Validate(state); Check(state.ActiveWorkspaceId == id);
});
Test("Unknown module, null collection and missing active tab are rejected", () =>
{
    var state = WorkspaceSessions.Defaults(); WorkspaceSessions.ActiveTab(WorkspaceSessions.Active(state)).ModuleId = "run arbitrary shell";
    Reject(() => WorkspaceSessions.Validate(state));
    state = WorkspaceSessions.Defaults(); state.Workspaces[0].Tabs = null!; Reject(() => WorkspaceSessions.Validate(state));
    state = WorkspaceSessions.Defaults(); state.Workspaces[0].ActiveTabId = Guid.NewGuid(); Reject(() => WorkspaceSessions.Validate(state));
});
Test("Duplicate and empty IDs are rejected", () =>
{
    var s = WorkspaceSessions.Defaults(); s.Workspaces[1].Id = s.Workspaces[0].Id; Reject(() => WorkspaceSessions.Validate(s));
    s = WorkspaceSessions.Defaults(); s.Workspaces[0].Tabs[0].Id = Guid.Empty; Reject(() => WorkspaceSessions.Validate(s));
});
Test("Oversized fields and invalid enums are rejected", () =>
{
    var s = WorkspaceSessions.Defaults(); s.Workspaces[0].Name = new string('x', 101); Reject(() => WorkspaceSessions.Validate(s));
    s = WorkspaceSessions.Defaults(); s.Workspaces[0].Layout = (WorkspaceViewMode)999; Reject(() => WorkspaceSessions.Validate(s));
});
Test("Store roundtrip preserves tabs and bookmarks and backs up previous generation", () => Temporary(dir =>
{
    var store = new ShellStateStore(dir); var state = store.Load(); store.Save(state);
    string first = File.ReadAllText(store.FilePath); state.Workspaces[0].Name = "Changed"; WorkspaceSessions.SaveView(state, "Saved"); store.Save(state);
    Check(store.Load().Workspaces[0].Name == "Changed" && store.Load().Bookmarks.Count == 1);
    Check(File.ReadAllText(store.FilePath + ".backup") == first);
    Check(Directory.GetFiles(dir, "*.tmp").Length == 0);
}));
Test("Future shell schema is not reset or overwritten", () => Temporary(dir =>
{
    var store = new ShellStateStore(dir); var s = WorkspaceSessions.Defaults(); s.SchemaVersion = 99;
    string json = JsonSerializer.Serialize(s, WorkspaceSessions.Json); File.WriteAllText(store.FilePath, json);
    Reject(() => store.Load()); Reject(() => store.Save(WorkspaceSessions.Defaults())); Check(File.ReadAllText(store.FilePath) == json);
}));
Test("Malformed primary is preserved", () => Temporary(dir =>
{
    var store = new ShellStateStore(dir); File.WriteAllText(store.FilePath, "{broken");
    Reject(() => store.Load()); Reject(() => store.Save(WorkspaceSessions.Defaults())); Check(File.ReadAllText(store.FilePath) == "{broken");
}));
Test("Import requires an explicit format and version", () => Temporary(dir =>
{
    string path = Path.Combine(dir, "missing.json");
    var node = JsonSerializer.SerializeToNode(WorkspaceSessions.Defaults(), WorkspaceSessions.Json)!.AsObject();
    node.Remove("SchemaVersion"); File.WriteAllText(path, node.ToJsonString()); Reject(() => ShellStateStore.Read(path));
}));
Test("Oversized import is rejected", () => Temporary(dir =>
{
    string path = Path.Combine(dir, "large.json"); File.WriteAllBytes(path, new byte[ShellStateStore.MaxBytes + 1]); Reject(() => ShellStateStore.Read(path));
}));
Test("Business workspace remains untouched when changing shell views", () => Temporary(dir =>
{
    var domain = new WorkspaceStore(dir); var data = domain.Load(); domain.Save(data); string before = File.ReadAllText(domain.DataFilePath);
    var shell = new ShellStateStore(dir); var s = shell.Load(); s.Workspaces[0].Name = "New name"; shell.Save(s);
    Check(File.ReadAllText(domain.DataFilePath) == before);
}));
Test("Selected export includes only requested content and no unrelated shell state", () =>
{
    var data = new WorkspaceState(); data.Projects.Add(new ProjectEntry { Name = "Should not export" }); data.Notes.Add(new StickyNoteEntry { Title = "Capture" });
    var shell = WorkspaceSessions.Defaults(); shell.Workspaces[0].Name = "Private workspace name";
    string json = PortableExport.Create(data, shell, ["capture"]); var node = JsonNode.Parse(json)!;
    Check(node["modules"]!.AsObject().Count == 1 && node["modules"]!["capture"] is not null);
    Check(node["shell"] is null && !json.Contains("Should not export") && !json.Contains("Private workspace name"));
});
Test("Export omits launcher arguments, paths and resolved media paths by default", () =>
{
    var data = new WorkspaceState(); data.Tools.Add(new ToolLauncherEntry { Name = "Tool", Command = "PRIVATE-COMMAND", Arguments = "PRIVATE-TOKEN", WorkingDirectory = "PRIVATE-DIR" });
    data.ExplorerFolders.Add(new ExplorerFolderEntry { Name = "Folder", Path = "PRIVATE-PATH" });
    data.ClipboardMedia.Add(new ClipboardMediaEntry { RelativePath = "media/fixture.png", ResolvedPath = "PRIVATE-RESOLVED" });
    string json = PortableExport.Create(data, WorkspaceSessions.Defaults(), ["tools", "settings", "clipboard"]);
    Check(!json.Contains("PRIVATE-") && json.Contains("media/fixture.png"));
    Check(JsonNode.Parse(json)!["containsMediaBytes"]!.GetValue<bool>() == false);
});
Test("Full export can explicitly include shell without mutating live data", () =>
{
    var data = new WorkspaceState(); var state = WorkspaceSessions.Defaults(); string before = JsonSerializer.Serialize(state, WorkspaceSessions.Json);
    string json = PortableExport.Create(data, state, ModuleCatalog.All.Select(x => x.Id), includeShell: true);
    Check(JsonNode.Parse(json)!["shell"] is not null); Check(JsonSerializer.Serialize(state, WorkspaceSessions.Json) == before);
});
Test("Export rejects empty or unknown module selection", () =>
{
    Reject(() => PortableExport.Create(new WorkspaceState(), WorkspaceSessions.Defaults(), []));
    Reject(() => PortableExport.Create(new WorkspaceState(), WorkspaceSessions.Defaults(), ["unknown"]));
});
// ---- V2.1 Quick Actions: one typed catalog behind every surface ----
QuickActionDispatcher CountingDispatcher(Dictionary<string, int> runs, Dictionary<string, int> probes)
{
    var d = new QuickActionDispatcher();
    foreach (var a in QuickActionCatalog.All)
    {
        string id = a.Id;
        d.Register(id, new QuickActionHandler(
            () => runs[id] = runs.GetValueOrDefault(id) + 1,
            () => { probes[id] = probes.GetValueOrDefault(id) + 1; return null; }));
    }
    return d;
}
Test("Action catalog has stable unique IDs, complete metadata and no destructive built-ins", () =>
{
    var ids = QuickActionCatalog.All.Select(x => x.Id).ToList();
    Check(ids.Distinct().Count() == ids.Count);
    foreach (string id in new[] { "app.toggle", "app.open", "capture.region", "capture.quick", "clipboard.open", "folder.downloads",
        "folder.explorer", "terminal.open", "workspace.resume", "workspace.next", "workspace.previous", "tab.next", "tab.previous" })
        Check(ids.Contains(id), "missing " + id);
    Check(!ids.Contains("mail.latestCode"), "provider-backed actions must not ship in this slice");
    Check(QuickActionCatalog.All.All(x => x.Label.Length > 0 && x.Description.Length > 0 && x.Category.Length > 0 && x.Glyph.Length == 4));
    Check(QuickActionCatalog.All.All(x => x.Risk == ActionRisk.Safe));
});
Test("Each action has exactly one implementation shared by every surface", () =>
{
    var runs = new Dictionary<string, int>(); var d = CountingDispatcher(runs, new());
    Reject(() => d.Register("capture.region", new QuickActionHandler(() => { })));
    Reject(() => d.Register("unknown.action", new QuickActionHandler(() => { })));
    foreach (var surface in new[] { ActionSurface.FullUi, ActionSurface.QuickRing, ActionSurface.QuickShelf, ActionSurface.GlobalShortcut, ActionSurface.InAppShortcut })
        Check(d.Invoke("capture.region", surface).Succeeded);
    Check(runs["capture.region"] == 5);
});
Test("Focused-only tab actions are refused from global surfaces without running", () =>
{
    var runs = new Dictionary<string, int>(); var d = CountingDispatcher(runs, new());
    foreach (var surface in new[] { ActionSurface.GlobalShortcut, ActionSurface.QuickRing, ActionSurface.QuickShelf })
        Check(d.Invoke("tab.next", surface).Outcome == QuickActionOutcome.NotAllowed);
    Check(!runs.ContainsKey("tab.next"));
    Check(d.Invoke("tab.next", ActionSurface.InAppShortcut).Succeeded && runs["tab.next"] == 1);
});
Test("Quick Actions never probe availability on registration or layout resolution", () =>
{
    var probes = new Dictionary<string, int>(); var d = CountingDispatcher(new(), probes);
    var s = QuickActionLayouts.Defaults(); _ = QuickActionLayouts.ResolveRing(s, Guid.NewGuid()); _ = QuickActionLayouts.ResolveShelf(s, Guid.NewGuid());
    Check(probes.Count == 0, "no background polling");
    Check(d.UnavailableReason("terminal.open") is null && probes["terminal.open"] == 1);
});
Test("Unavailable, unregistered, unknown and throwing actions report instead of crashing", () =>
{
    var d = new QuickActionDispatcher(); bool ran = false;
    d.Register("terminal.open", new QuickActionHandler(() => ran = true, () => "Choose a terminal in Settings first."));
    d.Register("folder.downloads", new QuickActionHandler(() => throw new IOException("boom")));
    var unavailable = d.Invoke("terminal.open", ActionSurface.QuickRing);
    Check(unavailable.Outcome == QuickActionOutcome.Unavailable && unavailable.Message.Contains("Settings") && !ran);
    Check(d.Invoke("clipboard.open", ActionSurface.QuickRing).Outcome == QuickActionOutcome.Unavailable);
    Check(d.Invoke("does.not.exist", ActionSurface.GlobalShortcut).Outcome == QuickActionOutcome.NotAllowed);
    var failed = d.Invoke("folder.downloads", ActionSurface.QuickShelf);
    Check(failed.Outcome == QuickActionOutcome.Failed && failed.Message.Contains("boom"));
});
Test("Quick Actions defaults are opt-in, bounded and valid", () =>
{
    var s = QuickActionLayouts.Defaults(); QuickActionLayouts.Validate(s);
    Check(s.Mode == InteractionMode.Off && !s.GlobalShortcutsEnabled);
    Check(s.Ring.Count <= QuickActionLayouts.MaxRing && s.Shelf.Count <= QuickActionLayouts.MaxShelf);
    Check(!s.Ring.Contains("app.open"), "ring centre opens Power Ops; not a slot");
    s.Ring.Add("workspace.next"); Check(!QuickActionLayouts.DefaultRing.Contains("workspace.next"), "defaults are not aliased");
});
Test("Ring and Shelf layouts reject oversize, duplicate, unknown, focused-only and self entries", () =>
{
    Reject(() => QuickActionLayouts.ValidateLayout([], ActionSurface.QuickRing));
    Reject(() => QuickActionLayouts.ValidateLayout(QuickActionCatalog.All.Where(x => x.GlobalAllowed && x.Id != "ring.show").Select(x => x.Id).Take(9).ToList(), ActionSurface.QuickRing));
    Reject(() => QuickActionLayouts.ValidateLayout(["capture.region", "capture.region"], ActionSurface.QuickShelf));
    Reject(() => QuickActionLayouts.ValidateLayout(["nope"], ActionSurface.QuickShelf));
    Reject(() => QuickActionLayouts.ValidateLayout(["tab.next"], ActionSurface.QuickRing));
    Reject(() => QuickActionLayouts.ValidateLayout(["ring.show"], ActionSurface.QuickRing));
    Reject(() => QuickActionLayouts.ValidateLayout(["shelf.toggle"], ActionSurface.QuickShelf));
    QuickActionLayouts.ValidateLayout(["ring.show", "capture.region"], ActionSurface.QuickShelf);
});
Test("Per-workspace ring/shelf overrides resolve, fall back and clear", () =>
{
    var s = QuickActionLayouts.Defaults(); var work = Guid.NewGuid(); var other = Guid.NewGuid();
    QuickActionLayouts.SetWorkspaceRing(s, work, ["terminal.open", "folder.explorer"]);
    Check(QuickActionLayouts.ResolveRing(s, work).SequenceEqual(new[] { "terminal.open", "folder.explorer" }));
    Check(QuickActionLayouts.ResolveRing(s, other).SequenceEqual(s.Ring));
    Check(QuickActionLayouts.ResolveShelf(s, work).SequenceEqual(s.Shelf), "shelf still inherits");
    Reject(() => QuickActionLayouts.SetWorkspaceRing(s, work, ["tab.next"]));
    Check(QuickActionLayouts.ResolveRing(s, work).Count == 2, "failed update leaves previous override");
    QuickActionLayouts.SetWorkspaceRing(s, work, null);
    Check(s.WorkspaceOverrides.Count == 0 && QuickActionLayouts.ResolveRing(s, work).SequenceEqual(s.Ring));
    Reject(() => QuickActionLayouts.SetWorkspaceRing(s, Guid.Empty, ["capture.region"]));
});
Test("Global hotkeys parse canonically to RegisterHotKey arguments", () =>
{
    var g = HotkeyGesture.Parse(" alt + ctrl + space ");
    Check(g.ToString() == "Ctrl+Alt+Space" && g.VirtualKey == 0x20);
    Check(g.NativeModifiers == (0x1 | 0x2 | 0x4000));
    Check(HotkeyGesture.Parse("Ctrl+Alt+Shift+F9").VirtualKey == 0x78 && HotkeyGesture.Parse("Win+Ctrl+k").ToString() == "Ctrl+Win+K");
    Check(HotkeyGesture.GlobalConflict(g) is null);
});
Test("Global hotkeys reject typing capture, OS-reserved and in-app shortcuts", () =>
{
    foreach (string bad in new[] { "", "Space", "Shift+A", "Ctrl+Alt", "Ctrl+Alt+A+B", "Ctrl+Ctrl+A", "Ctrl+Alt+Delete", "Ctrl+Alt+Tab" })
        Reject(() => HotkeyGesture.Parse(bad));
    foreach (string reserved in new[] { "Ctrl+C", "Ctrl+T", "Ctrl+1", "Alt+F4", "Alt+Space", "Win+L", "Ctrl+Shift+E", "Ctrl+Shift+T" })
        Check(HotkeyGesture.GlobalConflict(HotkeyGesture.Parse(reserved)) is not null, reserved + " must conflict");
});
Test("Global shortcut bindings reject duplicates and focused-only actions", () =>
{
    var s = QuickActionLayouts.Defaults();
    s.GlobalShortcuts.Add(new ShortcutBinding { Gesture = "alt+ctrl+SPACE", ActionId = "ring.show" });
    Reject(() => QuickActionLayouts.Validate(s));
    s = QuickActionLayouts.Defaults(); s.GlobalShortcuts.Add(new ShortcutBinding { Gesture = "Ctrl+Alt+F10", ActionId = "tab.next" });
    Reject(() => QuickActionLayouts.Validate(s));
    s = QuickActionLayouts.Defaults(); s.GlobalShortcuts.Add(new ShortcutBinding { Gesture = "Ctrl+Alt+Shift+R", ActionId = "ring.show" });
    QuickActionLayouts.Validate(s);
});
Test("MX Master guidance warns about double interception with the Summon mouse hook", () =>
{
    Check(QuickActionLayouts.MouseDoubleInterceptionWarning(WindowBehaviorMode.Summon, SummonMouseBinding.MouseButton5, InteractionMode.Hybrid) is not null);
    Check(QuickActionLayouts.MouseDoubleInterceptionWarning(WindowBehaviorMode.Summon, SummonMouseBinding.MouseButton4, InteractionMode.MxMasterGuide) is not null);
    Check(QuickActionLayouts.MouseDoubleInterceptionWarning(WindowBehaviorMode.Normal, SummonMouseBinding.MouseButton5, InteractionMode.Hybrid) is null);
    Check(QuickActionLayouts.MouseDoubleInterceptionWarning(WindowBehaviorMode.Summon, SummonMouseBinding.CtrlMiddleClick, InteractionMode.Hybrid) is null);
    Check(QuickActionLayouts.MouseDoubleInterceptionWarning(WindowBehaviorMode.Summon, SummonMouseBinding.MouseButton5, InteractionMode.QuickRing) is null);
});
Test("Quick actions store round-trips, writes nothing on load and preserves corrupt/future bytes", () => Temporary(dir =>
{
    var store = new QuickActionSettingsStore(dir);
    var loaded = store.Load(); Check(!File.Exists(store.FilePath), "load must not create files");
    loaded.Mode = InteractionMode.Hybrid; QuickActionLayouts.SetWorkspaceShelf(loaded, Guid.NewGuid(), ["capture.quick"]);
    store.Save(loaded);
    var again = store.Load(); Check(again.Mode == InteractionMode.Hybrid && again.WorkspaceOverrides.Count == 1);
    Check(File.ReadAllText(store.FilePath).Contains("\"Hybrid\""), "enums persist as names");

    File.WriteAllText(store.FilePath, "{ not json");
    Reject(() => store.Load()); Reject(() => store.Save(QuickActionLayouts.Defaults()));
    Check(File.ReadAllText(store.FilePath) == "{ not json", "corrupt bytes preserved");

    var future = JsonNode.Parse(JsonSerializer.Serialize(QuickActionLayouts.Defaults()))!; future["SchemaVersion"] = 2;
    File.WriteAllText(store.FilePath, future.ToJsonString());
    Reject(() => store.Load());
    var badMode = JsonNode.Parse(JsonSerializer.Serialize(QuickActionLayouts.Defaults()))!; badMode["Mode"] = 99;
    File.WriteAllText(store.FilePath, badMode.ToJsonString());
    Reject(() => store.Load());
    File.WriteAllText(store.FilePath, new string(' ', QuickActionSettingsStore.MaxBytes + 1));
    Reject(() => store.Load());
    Check(Directory.GetFiles(dir, "*.tmp").Length == 0);
}));
// ---- V2.1 Quick Actions slice 2: app wiring helpers ----
Test("Tab cycling wraps both ways and reports when there is nothing to switch", () =>
{
    var p = WorkspaceSessions.NewProfile("T");
    Check(!WorkspaceSessions.CycleTab(p, 1), "single tab cannot cycle");
    var first = p.Tabs[0].Id; WorkspaceSessions.AddTab(p, "capture"); WorkspaceSessions.AddTab(p, "clipboard");
    var last = p.ActiveTabId;
    Check(WorkspaceSessions.CycleTab(p, 1) && p.ActiveTabId == first, "next wraps to first");
    Check(WorkspaceSessions.CycleTab(p, -1) && p.ActiveTabId == last, "previous wraps to last");
    Check(WorkspaceSessions.CycleTab(p, -1) && p.ActiveTabId == p.Tabs[1].Id);
});
Test("Workspace cycling wraps, changes only the active view and needs two views", () =>
{
    var s = WorkspaceSessions.Defaults(); var before = JsonSerializer.Serialize(s.Workspaces, WorkspaceSessions.Json);
    Check(WorkspaceSessions.CycleWorkspace(s, -1) && s.ActiveWorkspaceId == s.Workspaces[^1].Id);
    Check(WorkspaceSessions.CycleWorkspace(s, 1) && s.ActiveWorkspaceId == s.Workspaces[0].Id);
    Check(JsonSerializer.Serialize(s.Workspaces, WorkspaceSessions.Json) == before, "profiles are not mutated");
    var single = new ShellState { Workspaces = [WorkspaceSessions.NewProfile("Only")] }; single.ActiveWorkspaceId = single.Workspaces[0].Id;
    Check(!WorkspaceSessions.CycleWorkspace(single, 1));
});
Test("Global hotkey plan is empty until explicitly enabled and maps bindings to stable IDs", () =>
{
    var s = QuickActionLayouts.Defaults();
    Check(QuickActionHotkeys.Plan(s).Count == 0, "fresh install registers nothing");
    s.GlobalShortcutsEnabled = true;
    s.GlobalShortcuts.Add(new ShortcutBinding { Gesture = "ctrl+alt+shift+r", ActionId = "capture.region" });
    var plan = QuickActionHotkeys.Plan(s);
    Check(plan.Count == 2 && plan[0].Id == QuickActionHotkeys.FirstId && plan[1].Id == QuickActionHotkeys.FirstId + 1);
    Check(plan[0].ActionId == "app.toggle" && plan[0].Gesture.VirtualKey == 0x20);
    Check(plan[1].Gesture.ToString() == "Ctrl+Alt+Shift+R" && plan[1].Gesture.NativeModifiers == (0x1 | 0x2 | 0x4 | 0x4000));
    Check(plan.All(x => x.Id is >= 0 and <= 0xBFFF), "application hotkey ID range");
    s.GlobalShortcuts.Add(new ShortcutBinding { Gesture = "Ctrl+Alt+F11", ActionId = "tab.next" });
    Reject(() => QuickActionHotkeys.Plan(s));
});
Test("Hotkey registration failures produce visible, specific messages", () =>
{
    var g = HotkeyGesture.Parse("Ctrl+Alt+Space");
    Check(QuickActionHotkeys.DescribeRegistrationFailure(g, 1409).Contains("already used"));
    string other = QuickActionHotkeys.DescribeRegistrationFailure(g, 5);
    Check(other.Contains("error 5") && other.Contains("Ctrl+Alt+Space"));
});
Test("Terminal action uses only an explicitly configured Tool Launcher entry", () =>
{
    (string Name, string Command)[] Tools(params (string, string)[] items) => items;
    string? Pick(params (string, string)[] items) =>
        QuickActionTargets.PickTerminal(Tools(items).Select(x => new[] { x.Name, x.Command }), x => x[0], x => x[1])?[0];
    Check(Pick(("VS Code", "code"), ("Shell", "pwsh"), ("Windows Terminal", "wt")) == "Windows Terminal", "named entry wins");
    Check(Pick(("VS Code", "code"), ("My shell", "\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\"")) == "My shell", "known shell by path");
    Check(Pick(("Windows Terminal", " "), ("Cmd", "cmd.exe")) == "Cmd", "blank command is not configured");
    Check(Pick(("VS Code", "code"), ("Notes", "notepad")) is null, "never guesses an executable");
});
// ---- V2.1 Quick Actions slice 3: Quick Shelf ----
Test("Quick Shelf renders the active workspace layout and probes each button once on demand", () =>
{
    var s = QuickActionLayouts.Defaults(); var work = Guid.NewGuid();
    QuickActionLayouts.SetWorkspaceShelf(s, work, ["terminal.open", "ring.show", "folder.downloads"]);
    var probes = new List<string>();
    var items = QuickShelfModel.Build(s, work,
        id => { probes.Add(id); return id == "terminal.open" ? "Add a terminal first." : null; },
        id => id == "folder.downloads" ? "Ctrl+Alt+Shift+D" : null);
    Check(items.Select(x => x.Id).SequenceEqual(new[] { "terminal.open", "ring.show", "folder.downloads" }), "workspace order");
    Check(probes.SequenceEqual(items.Select(x => x.Id)), "one probe per button");
    Check(!items[0].IsAvailable && items[0].ToolTip.Contains("Unavailable: Add a terminal first."), "reason is shown, action kept");
    Check(items[2].IsAvailable && items[2].ToolTip.StartsWith("Downloads (Ctrl+Alt+Shift+D)\n"), "gesture in tooltip");
    Check(items.All(x => x.GlyphText.Length == 1 && x.GlyphText[0] >= ''), "private-use icon glyphs");
    Check(QuickShelfModel.Build(s, Guid.NewGuid(), _ => null, _ => null).Select(x => x.Id).SequenceEqual(s.Shelf), "other workspaces inherit");
});
Test("Quick Shelf eligibility matches layout validation", () =>
{
    var shelf = QuickActionLayouts.Eligible(ActionSurface.QuickShelf).Select(x => x.Id).ToList();
    var ring = QuickActionLayouts.Eligible(ActionSurface.QuickRing).Select(x => x.Id).ToList();
    Check(!shelf.Contains("shelf.toggle") && shelf.Contains("ring.show") && !shelf.Any(x => x.StartsWith("tab.")));
    Check(!ring.Contains("ring.show") && ring.Contains("shelf.toggle"));
    foreach (string id in shelf) QuickActionLayouts.ValidateLayout([id], ActionSurface.QuickShelf);
    foreach (string id in ring) QuickActionLayouts.ValidateLayout([id], ActionSurface.QuickRing);
});
Test("Quick Shelf is shown at startup only in Quick Shelf mode", () =>
{
    var s = QuickActionLayouts.Defaults(); Check(!QuickShelfModel.ShowAtStartup(s), "off by default");
    foreach (InteractionMode mode in Enum.GetValues<InteractionMode>()) { s.Mode = mode; Check(QuickShelfModel.ShowAtStartup(s) == (mode == InteractionMode.QuickShelf)); }
});
Test("Settings copy is independent and shelf position is validated and persisted", () => Temporary(dir =>
{
    var s = QuickActionLayouts.Defaults(); s.ShelfLeft = -1920.5; s.ShelfTop = 12;
    var copy = QuickActionSettingsStore.Copy(s); copy.Shelf.Add("workspace.next"); copy.ShelfLeft = 5;
    Check(!s.Shelf.Contains("workspace.next") && s.ShelfLeft == -1920.5, "copy does not alias");
    var store = new QuickActionSettingsStore(dir); store.Save(s);
    var loaded = store.Load(); Check(loaded.ShelfLeft == -1920.5 && loaded.ShelfTop == 12, "negative-coordinate monitors persist");
    foreach (double bad in new[] { double.NaN, double.PositiveInfinity, 1e9 }) { s.ShelfTop = bad; Reject(() => QuickActionLayouts.Validate(s)); }
    s.ShelfTop = null; s.ShelfLeft = null; QuickActionLayouts.Validate(s);
    var legacy = JsonNode.Parse(File.ReadAllText(store.FilePath))!.AsObject(); legacy.Remove("ShelfLeft"); legacy.Remove("ShelfTop");
    File.WriteAllText(store.FilePath, legacy.ToJsonString()); Check(store.Load().ShelfLeft is null, "slice-1 files without a position still load");
}));
// ---- V2.1 web apps (Mongoku, Grafana, Gemini...) as quick actions ----
Test("Web apps are opt-in and only accept plain http(s) addresses without credentials", () =>
{
    var s = QuickActionLayouts.Defaults(); Check(s.WebApps.Count == 0, "nothing configured by default");
    foreach (var preset in QuickWebApps.Presets) { s.WebApps.Add(QuickWebApps.FromPreset(preset)); }
    QuickActionLayouts.Validate(s);
    Check(QuickWebApps.Presets.Single(x => x.Name == "Mongoku").Url == "http://localhost:3100/", "Mongoku default port");
    foreach (string bad in new[] { "", "localhost:3100", "file:///C:/Windows", "javascript:alert(1)", "ftp://host/", "https://user:secret@grafana.example.com/", "https://" + new string('a', 2100) })
    {
        var t = QuickActionLayouts.Defaults(); t.WebApps.Add(new WebAppEntry { Name = "X", Url = bad });
        Reject(() => QuickActionLayouts.Validate(t));
    }
    var dup = QuickActionLayouts.Defaults(); var app = new WebAppEntry { Name = "A", Url = "https://a.example/" };
    dup.WebApps.Add(app); dup.WebApps.Add(new WebAppEntry { Id = app.Id, Name = "B", Url = "https://b.example/" });
    Reject(() => QuickActionLayouts.Validate(dup));
    var blank = QuickActionLayouts.Defaults(); blank.WebApps.Add(new WebAppEntry { Name = "  ", Url = "https://a.example/" });
    Reject(() => QuickActionLayouts.Validate(blank));
});
Test("Web apps work on the Shelf, Ring and global shortcuts, and removal cleans every reference", () =>
{
    var s = QuickActionLayouts.Defaults(); var mongoku = QuickWebApps.FromPreset(QuickWebApps.Presets[0]); s.WebApps.Add(mongoku);
    string id = QuickWebApps.ActionId(mongoku.Id); var work = Guid.NewGuid();
    s.Shelf.Add(id); s.Ring.Add(id); s.GlobalShortcutsEnabled = true;
    s.GlobalShortcuts.Add(new ShortcutBinding { Gesture = "Ctrl+Alt+Shift+M", ActionId = id });
    QuickActionLayouts.SetWorkspaceShelf(s, work, [id]);
    QuickActionLayouts.Validate(s);
    Check(QuickActionLayouts.Eligible(ActionSurface.QuickShelf, s).Any(x => x.Id == id), "offered in the Shelf editor");
    Check(QuickShelfModel.Build(s, work, _ => null, _ => null).Single().Label == "Mongoku");
    Check(QuickActionHotkeys.Plan(s).Any(x => x.ActionId == id));
    var orphan = QuickActionSettingsStore.Copy(s); orphan.WebApps.Clear();
    Reject(() => QuickActionLayouts.Validate(orphan));
    QuickWebApps.RemoveWebApp(s, mongoku.Id);
    QuickActionLayouts.Validate(s);
    Check(!s.Shelf.Contains(id) && !s.Ring.Contains(id) && s.GlobalShortcuts.All(x => x.ActionId != id) && s.WorkspaceOverrides.Count == 0, "all references removed");
});
Test("Dispatcher hosts user-defined web actions without shadowing built-ins", () =>
{
    var d = new QuickActionDispatcher(); var app = new WebAppEntry { Name = "Grafana", Url = "http://localhost:3000/" };
    var def = QuickWebApps.Definition(app); int runs = 0;
    d.ReplaceDynamic(QuickWebApps.Prefix, [(def, new QuickActionHandler(() => runs++))]);
    Check(d.Invoke(def.Id, ActionSurface.GlobalShortcut).Succeeded && d.Invoke(def.Id, ActionSurface.QuickShelf).Succeeded && runs == 2);
    Check(d.Definitions.Any(x => x.Id == def.Id) && d.Definition(def.Id)!.Category == "Web apps");
    Reject(() => d.ReplaceDynamic(QuickWebApps.Prefix, [(QuickActionCatalog.Get("app.open"), new QuickActionHandler(() => { }))]));
    Reject(() => d.ReplaceDynamic("web", []));
    Reject(() => d.ReplaceDynamic(QuickWebApps.Prefix, [(def, new QuickActionHandler(() => { })), (def, new QuickActionHandler(() => { }))]));
    d.ReplaceDynamic(QuickWebApps.Prefix, []);
    Check(d.Invoke(def.Id, ActionSurface.FullUi).Outcome == QuickActionOutcome.NotAllowed && !d.IsRegistered(def.Id), "replace removes old actions");
    d.Register("app.open", new QuickActionHandler(() => { }));
    Check(d.IsRegistered("app.open"), "built-ins unaffected");
});
Test("App-window browser follows default Chrome profile, else Edge", () =>
{
    const string chrome = @"C:\Chrome\chrome.exe", edge = @"C:\Edge\msedge.exe";
    Check(QuickWebApps.ChooseAppBrowser(WebBrowserChoice.Auto, "ChromeHTML", chrome, edge) == chrome);
    Check(QuickWebApps.ChooseAppBrowser(WebBrowserChoice.Auto, "MSEdgeHTM", chrome, edge) == edge);
    Check(QuickWebApps.ChooseAppBrowser(WebBrowserChoice.Auto, "FirefoxURL-308046B0AF4A39CB", chrome, null) == chrome);
    Check(QuickWebApps.ChooseAppBrowser(WebBrowserChoice.Edge, "ChromeHTML", chrome, null) is null, "explicit choice is never substituted");
    Check(QuickWebApps.ChooseAppBrowser(WebBrowserChoice.Auto, null, null, null) is null);
});
// ---- V2.1 Quick Ring ----
Test("Quick Ring slots start at the top and go clockwise", () =>
{
    var four = Enumerable.Range(0, 4).Select(i => QuickRingModel.SlotOffset(i, 4, 100)).ToList();
    Check(four[0] == (0, -100) && four[1] == (100, 0) && four[2] == (0, 100) && four[3] == (-100, 0), string.Join(" ", four));
    var eight = Enumerable.Range(0, 8).Select(i => QuickRingModel.SlotOffset(i, 8, 112)).ToList();
    Check(eight.All(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - 112) < 1e-3), "all slots on the circle");
    Check(eight.Select(p => (Math.Round(p.X), Math.Round(p.Y))).Distinct().Count() == 8, "no overlapping slots");
    Check(QuickRingModel.SlotOffset(0, 1, 50) == (0, -50));
    Reject(() => { try { QuickRingModel.SlotOffset(8, 8, 1); } catch (ArgumentOutOfRangeException ex) { throw new InvalidOperationException(ex.Message); } });
});
Test("Quick Ring keyboard: arrows wrap, digits pick existing slots only", () =>
{
    Check(QuickRingModel.Move(-1, 7, 1) == 0 && QuickRingModel.Move(-1, 7, -1) == 6, "entry from the centre");
    Check(QuickRingModel.Move(6, 7, 1) == 0 && QuickRingModel.Move(0, 7, -1) == 6, "wrap-around");
    Check(QuickRingModel.Move(0, 0, 1) == -1, "empty ring");
    Check(QuickRingModel.SlotForDigit(1, 7) == 0 && QuickRingModel.SlotForDigit(7, 7) == 6);
    Check(QuickRingModel.SlotForDigit(8, 7) is null && QuickRingModel.SlotForDigit(0, 7) is null && QuickRingModel.SlotForDigit(9, 8) is null);
});
Test("Quick Ring uses the workspace ring, web apps and never lists itself", () =>
{
    var s = QuickActionLayouts.Defaults(); var app = new WebAppEntry { Name = "Mongoku", Url = "http://localhost:3100/" }; s.WebApps.Add(app);
    var work = Guid.NewGuid(); string web = QuickWebApps.ActionId(app.Id);
    QuickActionLayouts.SetWorkspaceRing(s, work, [web, "capture.region", "shelf.toggle"]);
    var probes = 0;
    var items = QuickRingModel.Build(s, work, _ => { probes++; return null; }, _ => null);
    Check(items.Select(x => x.Label).SequenceEqual(new[] { "Mongoku", "Screenshot (region)", "Quick Shelf" }) && probes == 3);
    Check(QuickRingModel.Build(s, Guid.NewGuid(), _ => null, _ => null).Select(x => x.Id).SequenceEqual(s.Ring), "inherits default");
    Check(QuickActionLayouts.DefaultRing.Count <= QuickActionLayouts.MaxRing && !QuickActionLayouts.DefaultRing.Contains("app.open"), "centre is Open Power Ops");
    Reject(() => QuickActionLayouts.SetWorkspaceRing(s, work, ["ring.show"]));
});
Test("Ring placement centres on the pointer and stays inside the monitor work area", () =>
{
    var mid = JUtility.Core.Services.WindowPlacementMath.CenterOn(960, 500, 480, 480, 0, 0, 1920, 1032);
    Check(mid.Left == 720 && mid.Top == 260 && mid.Width == 480);
    var corner = JUtility.Core.Services.WindowPlacementMath.CenterOn(3, 4, 480, 480, 0, 0, 1920, 1032);
    Check(corner.Left == 0 && corner.Top == 0, "top-left corner clamps");
    var negative = JUtility.Core.Services.WindowPlacementMath.CenterOn(-1915, 1070, 480, 480, -1920, 0, 0, 1080);
    Check(negative.Left == -1920 && negative.Top == 600, "left monitor with negative coordinates, bottom edge");
});
// ---- V2.1 Interaction settings + MX Master guide ----
Test("Recommended shortcuts per mode are valid, typeable and conflict-free", () =>
{
    Check(InteractionGuide.Recommended(InteractionMode.Off).Count == 0);
    Check(InteractionGuide.Recommended(InteractionMode.Hybrid).Select(x => x.ActionId).SequenceEqual(new[] { "ring.show", "app.toggle", "capture.quick", "clipboard.open" }));
    Check(InteractionGuide.Recommended(InteractionMode.QuickShelf).Any(x => x.ActionId == "shelf.toggle"));
    foreach (InteractionMode mode in Enum.GetValues<InteractionMode>())
    {
        Check(InteractionGuide.Describe(mode).Length > 0, "every mode explained");
        foreach (var r in InteractionGuide.Recommended(mode))
            foreach (string c in r.Candidates)
            {
                var g = HotkeyGesture.Parse(c);
                Check(HotkeyGesture.GlobalConflict(g) is null && !g.Modifiers.HasFlag(HotkeyModifiers.Windows), c);
                Check(QuickActionCatalog.Get(r.ActionId).GlobalAllowed, r.ActionId);
            }
    }
});
Test("Adding recommended shortcuts keeps user choices, skips taken combinations and is idempotent", () =>
{
    var s = QuickActionLayouts.Defaults(); // default binding: Ctrl+Alt+Space -> app.toggle, shortcuts disabled
    s.GlobalShortcuts.Add(new ShortcutBinding { Gesture = "Ctrl+Alt+Shift+N", ActionId = "clipboard.open" });
    var probed = new List<string>();
    bool Free(HotkeyGesture g) { probed.Add(g.ToString()); return g.ToString() != "Ctrl+Alt+Shift+R"; } // R owned by another app
    var lines = InteractionGuide.AddRecommended(s, InteractionMode.Hybrid, Free);
    Check(lines.Single(x => x.ActionId == "ring.show").Gesture == "Ctrl+Alt+Shift+Q", "falls back when Windows reports R taken");
    Check(lines.Single(x => x.ActionId == "app.toggle").Outcome == ShortcutPlanOutcome.AlreadyBound, "user binding kept");
    Check(lines.Single(x => x.ActionId == "clipboard.open").Outcome == ShortcutPlanOutcome.AlreadyBound);
    Check(lines.Single(x => x.ActionId == "capture.quick").Gesture == "Ctrl+Alt+Shift+F10", "N is already used by clipboard.open in settings");
    Check(probed.Count(x => x == "Ctrl+Alt+Shift+N") == 1, "an existing binding is checked once, never re-offered as a candidate");
    Check(s.GlobalShortcutsEnabled, "enabled once something was added");
    QuickActionLayouts.Validate(s);
    int count = s.GlobalShortcuts.Count;
    Check(InteractionGuide.AddRecommended(s, InteractionMode.Hybrid, _ => true).All(x => x.Outcome == ShortcutPlanOutcome.AlreadyBound) && s.GlobalShortcuts.Count == count, "idempotent");
    var taken = QuickActionLayouts.Defaults(); // Ctrl+Alt+Space owned by another program, as on the user's laptop
    var replaced = InteractionGuide.AddRecommended(taken, InteractionMode.QuickRing, g => g.ToString() != "Ctrl+Alt+Space");
    Check(replaced.Single(x => x.ActionId == "app.toggle").Outcome == ShortcutPlanOutcome.Replaced
        && taken.GlobalShortcuts.Single(x => x.ActionId == "app.toggle").Gesture == "Ctrl+Alt+Shift+P", "unusable binding replaced");
    Check(taken.GlobalShortcuts.Count == 2 && taken.GlobalShortcuts.All(x => x.Gesture != "Ctrl+Alt+Space"));
    QuickActionLayouts.Validate(taken);
    var none = QuickActionLayouts.Defaults(); none.GlobalShortcuts.Clear();
    var blocked = InteractionGuide.AddRecommended(none, InteractionMode.QuickRing, _ => false);
    Check(blocked.All(x => x.Outcome == ShortcutPlanOutcome.NoFreeCandidate) && none.GlobalShortcuts.Count == 0 && !none.GlobalShortcutsEnabled, "nothing enabled when nothing is free");
});
Test("MX Master guide reflects configured shortcuts and warns about double interception", () =>
{
    var s = QuickActionLayouts.Defaults(); s.GlobalShortcuts.Clear();
    var steps = InteractionGuide.MxMasterSteps(s, null);
    var ring = steps.Single(x => x.Title.Contains("Quick Ring"));
    Check(ring.IsWarning && ring.CopyText is null, "missing binding is flagged, never promised");
    Check(steps.Any(x => x.Title.StartsWith("Back") && x.CopyText == "Ctrl+Shift+Tab") && steps.Any(x => x.Title.StartsWith("Forward") && x.CopyText == "Ctrl+Tab"), "in-app tab shortcuts");
    InteractionGuide.AddRecommended(s, InteractionMode.Hybrid, _ => true);
    steps = InteractionGuide.MxMasterSteps(s, "hook conflict");
    Check(steps.Single(x => x.Title.Contains("Quick Ring")).CopyText == "Ctrl+Alt+Shift+R");
    Check(steps.Any(x => x.IsWarning && x.Detail == "hook conflict"), "summon hook warning included");
    s.GlobalShortcutsEnabled = false;
    Check(InteractionGuide.MxMasterSteps(s, null).Single(x => x.Title.Contains("Quick Ring")).IsWarning, "disabled shortcuts are not promised");
    Check(QuickActionLayouts.MouseDoubleInterceptionWarning(WindowBehaviorMode.Summon, SummonMouseBinding.CtrlMiddleClick, InteractionMode.Hybrid) is null, "the offered fix clears the warning");
});
// ---- V2.1 embedded web apps (WebView2 "Web" module) ----
Test("Embedded web apps open in a reusable Web tab and keep shell state valid", () =>
{
    var shell = WorkspaceSessions.Defaults(); var ws = WorkspaceSessions.Active(shell);
    ws.VisibleModules.Remove("web");
    string mongoku = QuickWebApps.ActionId(Guid.NewGuid()), grafana = QuickWebApps.ActionId(Guid.NewGuid());
    var tab = EmbeddedWebPolicy.ShowInWorkspace(ws, mongoku);
    Check(ws.VisibleModules.Contains("web") && tab.ModuleId == "web" && tab.Filter == mongoku && ws.ActiveTabId == tab.Id);
    WorkspaceSessions.Validate(shell);
    int tabs = ws.Tabs.Count;
    Check(EmbeddedWebPolicy.ShowInWorkspace(ws, mongoku).Id == tab.Id && ws.Tabs.Count == tabs, "existing tab reused");
    EmbeddedWebPolicy.ShowInWorkspace(ws, grafana); Check(ws.Tabs.Count == tabs + 1);
    while (ws.Tabs.Count < WorkspaceSessions.MaxTabs) WorkspaceSessions.AddTab(ws, "capture");
    string third = QuickWebApps.ActionId(Guid.NewGuid());
    var retarget = EmbeddedWebPolicy.ShowInWorkspace(ws, third);
    Check(ws.Tabs.Count == WorkspaceSessions.MaxTabs && retarget.Filter == third && retarget.ModuleId == "web", "tab limit retargets the active tab");
    WorkspaceSessions.Validate(shell);
    var store = shell; Check(JsonSerializer.Serialize(store, WorkspaceSessions.Json).Contains(third), "persisted in the shell file, not a new schema");
    Reject(() => EmbeddedWebPolicy.ShowInWorkspace(ws, "capture.quick"));
    Check(ModuleCatalog.Get("web").Header == "Web");
});
Test("Only embedded apps open in the current workspace keep a live web view", () =>
{
    var s = QuickActionLayouts.Defaults();
    var mongoku = new WebAppEntry { Name = "Mongoku", Url = "http://localhost:3100/", OpenMode = WebOpenMode.Embedded };
    var gemini = new WebAppEntry { Name = "Gemini", Url = "https://gemini.google.com/app", OpenMode = WebOpenMode.AppWindow };
    s.WebApps.AddRange([mongoku, gemini]);
    var ws = WorkspaceSessions.NewProfile("W");
    EmbeddedWebPolicy.ShowInWorkspace(ws, QuickWebApps.ActionId(mongoku.Id));
    EmbeddedWebPolicy.ShowInWorkspace(ws, QuickWebApps.ActionId(gemini.Id));
    EmbeddedWebPolicy.ShowInWorkspace(ws, QuickWebApps.ActionId(Guid.NewGuid())); // deleted app
    var live = EmbeddedWebPolicy.LiveViews(ws, s);
    Check(live.Count == 1 && live.Contains(QuickWebApps.ActionId(mongoku.Id)), string.Join(",", live));
    WorkspaceSessions.CloseTab(ws); WorkspaceSessions.CloseTab(ws); WorkspaceSessions.CloseTab(ws);
    Check(EmbeddedWebPolicy.LiveViews(ws, s).Count == 0, "closing the tab frees the view");
    Check(EmbeddedWebPolicy.LiveViews(WorkspaceSessions.NewProfile("Other"), s).Count == 0, "other workspaces hold nothing");
    Check(QuickWebApps.Presets.Single(x => x.Name == "Mongoku").OpenMode == WebOpenMode.Embedded
        && QuickWebApps.Presets.Where(x => x.Name is "Gemini" or "ChatGPT" or "Claude").All(x => x.OpenMode == WebOpenMode.AppWindow), "chats keep the browser sign-in");
});
Test("Embedded navigation allows only http(s); pop-ups go to the default browser", () =>
{
    Check(EmbeddedWebPolicy.AllowNavigation("http://localhost:3100/servers") && EmbeddedWebPolicy.AllowNavigation("https://idp.example.com/authorize?x=1"));
    foreach (string blocked in new[] { "file:///C:/Users", "javascript:alert(1)", "ms-settings:privacy", "about:blank", "", null! })
        Check(!EmbeddedWebPolicy.AllowNavigation(blocked) && EmbeddedWebPolicy.ExternalTarget(blocked) is null, blocked ?? "null");
    Check(EmbeddedWebPolicy.ExternalTarget("https://github.com/huggingface/Mongoku")!.Host == "github.com");
    Check(EmbeddedWebPolicy.UserDataFolder(@"C:\data\PowerOps").EndsWith(@"PowerOps\webview2"), "profile lives in the Power Ops data folder");
    var export = PortableExport.Create(new WorkspaceState(), WorkspaceSessions.Defaults(), ["web"]);
    Check(!export.Contains("webview2") && export.Contains("No runtime snapshot"), "content export never includes browser data");
});
// ---- V2.1 Mongoku report cards ----
const string FoilStatusSample = """
{"reportId":"FOIL_STATUS_NOW","title":"FOIL status now","description":"PM scorecards","presentation":{"kind":"cards"},"generatedAt":"2026-09-24T23:12:53.000Z","readOnly":true,
 "sections":[{"id":"a","label":"PM scorecards","authority":"FOIL Project Management","sourceId":"x","rows":[],"trace":{},"meta":{"state":"SOURCE_UNBOUND","returnedRows":0,"responseBytes":0,"truncated":false}},
             {"id":"b","label":"Recent events","authority":"DATAPASSCONTROL","rows":[{"secret":"never shown"}],"meta":{"state":"TRUNCATED","returnedRows":30,"truncated":true,"futureField":1}},
             {"id":"c","label":"Projects","meta":{"state":"OK","returnedRows":31}}]}
""";
Test("Report cards parse Mongoku reports into section states and counts only", () =>
{
    var s = ReportCards.Parse(FoilStatusSample, DateTimeOffset.Now);
    Check(s.ReportId == "FOIL_STATUS_NOW" && s.Title == "FOIL status now" && s.ReadOnly == true && s.GeneratedAt?.Year == 2026);
    Check(s.Sections.Count == 3 && s.Sections[0].Health == SectionHealth.Unavailable && s.Sections[0].Explanation.Contains("not bound"));
    Check(s.Sections[1].Health == SectionHealth.Partial && s.Sections[1].ReturnedRows == 30 && s.Sections[1].Truncated);
    Check(s.Sections[2].Health == SectionHealth.Ok && s.Sections[2].Authority is null);
    Check(s.Overall == SectionHealth.Unavailable, "worst section wins");
    Check(!JsonSerializer.Serialize(s).Contains("never shown"), "row contents are never kept");
    var empty = ReportCards.Parse("""{"reportId":"X","sections":[]}""", DateTimeOffset.Now);
    Check(empty.Overall == SectionHealth.Unknown && empty.Title == "X" && empty.ReadOnly is null && empty.GeneratedAt is null, "missing data stays unknown, never OK");
    var noMeta = ReportCards.Parse("""{"sections":[{"label":"L"}]}""", DateTimeOffset.Now);
    Check(noMeta.Sections[0].Health == SectionHealth.Unknown && noMeta.Sections[0].ReturnedRows is null);
    Reject(() => ReportCards.Parse("[1,2]", DateTimeOffset.Now));
});
Test("Report card settings validate ids and addresses and never store credentials", () => Temporary(dir =>
{
    var store = new ReportCardStore(dir);
    Check(store.Load().Cards.Count == 0 && !File.Exists(store.FilePath), "load writes nothing");
    var ok = new ReportCardSettings { Cards = [new ReportCard { SourceUrl = "http://localhost:3100", ReportId = "FOIL_STATUS_NOW" }] };
    store.Save(ok); Check(store.Load().Cards.Single().ReportId == "FOIL_STATUS_NOW");
    foreach (var bad in new[] { ("http://localhost:3100/", "foil_status"), ("http://localhost:3100/", "../../api"), ("http://localhost:3100/", ""), ("file:///C:/x", "FOIL_NEXT"), ("http://admin:pw@localhost:3100/", "FOIL_NEXT") })
        Reject(() => ReportCards.Validate(new ReportCardSettings { Cards = [new ReportCard { SourceUrl = bad.Item1, ReportId = bad.Item2 }] }));
    Reject(() => ReportCards.Validate(new ReportCardSettings { Cards = Enumerable.Range(0, 9).Select(_ => new ReportCard { ReportId = "FOIL_NEXT" }).ToList() }));
    File.WriteAllText(store.FilePath, "{ corrupt"); Reject(() => store.Load()); Reject(() => store.Save(ok));
    Check(File.ReadAllText(store.FilePath) == "{ corrupt", "corrupt bytes preserved");
}));
Test("Report card addresses: API URL, and deep links into Mongoku", () =>
{
    var foil = new ReportCard { SourceUrl = "http://localhost:3100", ReportId = "FOIL_NEXT" };
    Check(ReportCards.ReportUri(foil).AbsoluteUri == "http://localhost:3100/api/datapass/reports/FOIL_NEXT");
    Check(ReportCards.DeepLink(foil).AbsoluteUri == "http://localhost:3100/foil/report/FOIL_NEXT");
    Check(ReportCards.DeepLink(new ReportCard { SourceUrl = "http://localhost:3100/", ReportId = "GLOBAL_PROJECTS" }).AbsoluteUri == "http://localhost:3100/projects");
    Check(ReportCards.DeepLink(new ReportCard { SourceUrl = "https://mongoku.example.com/base/", ReportId = "SOURCE_INVENTORY" }).AbsoluteUri == "https://mongoku.example.com/base/foil/report/SOURCE_INVENTORY", "every report but GLOBAL_PROJECTS has a /foil/report page");
    Check(ReportCards.WorkspaceUri("https://mongoku.example.com/base").AbsoluteUri == "https://mongoku.example.com/base/api/datapass/workspace", "base path kept");
});
Test("Report card fetch turns every failure into a readable message (fake server)", () =>
{
    var card = new ReportCard { SourceUrl = "http://localhost:3100/", ReportId = "FOIL_STATUS_NOW" };
    ReportFetchResult Run(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        ReportCards.FetchAsync(card, new FakeHandler(respond)).GetAwaiter().GetResult();
    HttpResponseMessage Json(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    HttpRequestMessage? seen = null;
    var ok = Run(r => { seen = r; return Json(HttpStatusCode.OK, FoilStatusSample); });
    Check(ok.Succeeded && ok.Summary!.Sections.Count == 3);
    Check(seen!.Method == HttpMethod.Get && seen.RequestUri!.AbsolutePath == "/api/datapass/reports/FOIL_STATUS_NOW" && seen.Headers.Accept.ToString().Contains("application/json"), "read-only GET");
    Check(Run(_ => Json(HttpStatusCode.BadRequest, """{"ok":false,"error":"Unknown report: FOIL_STATUS_NOW"}""")).Error!.Contains("Unknown report"));
    Check(Run(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)).Error!.Contains("uses web sign-in (OIDC)"), "401 without a Basic challenge = Mongoku OIDC");
    Check(Run(_ => new HttpResponseMessage(HttpStatusCode.Redirect)).Error!.Contains("redirected"));
    Check(Run(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).Error!.Contains("no Mongoku report API"));
    Check(Run(_ => throw new HttpRequestException("refused", new System.Net.Sockets.SocketException(10061))).Error!.Contains("not reachable"));
    Check(Run(_ => throw new TaskCanceledException("timeout")).Error!.Contains("did not answer"));
    Check(Run(_ => Json(HttpStatusCode.OK, "{ not json")).Error!.Contains("could not read"));
    var huge = new string('x', ReportCards.MaxResponseBytes + 10);
    Check(Run(_ => Json(HttpStatusCode.OK, huge)).Error!.Contains("larger than"), "size cap with Content-Length");
    Check(Run(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(ReportCards.MaxResponseBytes + 10)) }).Error!.Contains("larger than"), "size cap without Content-Length");
});
Test("Report list comes from the Mongoku workspace (ids, titles, descriptions only)", () =>
{
    const string workspace = """{"schemaVersion":3,"projects":[{"name":"private"}],"reports":[{"id":"GLOBAL_PROJECTS","title":"Global projects","description":"Portfolio"},{"id":"FOIL_NEXT","title":"FOIL next"},{"id":"bad id"},{"title":"no id"}]}""";
    var (list, error) = ReportCards.ListReportsAsync("http://localhost:3100/", new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(workspace) })).GetAwaiter().GetResult();
    Check(error is null && list.Select(x => x.Id).SequenceEqual(new[] { "GLOBAL_PROJECTS", "FOIL_NEXT" }) && list[0].Description == "Portfolio");
    var (none, noneError) = ReportCards.ListReportsAsync("http://localhost:3100/", new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"reports":[]}""") })).GetAwaiter().GetResult();
    Check(none.Count == 0 && noneError!.Contains("no saved reports"));
    Reject(() => ReportCards.ListReportsAsync("javascript:alert(1)", new FakeHandler(_ => throw new InvalidOperationException("must not be called"))).GetAwaiter().GetResult());
});
// ---- V2.1 protected Mongoku (basic auth, Windows Credential Manager) ----
Test("Mongoku sign-in: one entry per origin, sent only over https or to this computer", () =>
{
    Check(ReportAuth.Target("HTTP://LocalHost:3100/some/path") == "PowerOps/Mongoku/http://localhost:3100");
    Check(ReportAuth.Target("https://mongoku.example.com/") == "PowerOps/Mongoku/https://mongoku.example.com", "default port folded");
    foreach (string ok in new[] { "https://mongoku.example.com/", "http://localhost:3100/", "http://127.0.0.1:3100/", "http://[::1]:3100/" })
        Check(ReportAuth.RefusalToSend(new Uri(ok)) is null, ok);
    Check(ReportAuth.RefusalToSend(new Uri("http://192.168.1.20:3100/"))!.Contains("plain http"), "no passwords over LAN http");
    foreach (var bad in new[] { new BasicCredential("", "p"), new BasicCredential("a:b", "p"), new BasicCredential("user", ""), new BasicCredential("user", "p\u0001"), new BasicCredential(new string('u', 129), "p") })
        Reject(() => ReportAuth.Validate(bad));
    ReportAuth.Validate(new BasicCredential("report-reader", "pässwörd with spaces")); // a Mongoku HTTP sign-in, never a database user
    var secret = new BasicCredential("u", "TopSecret-123");
    Check(!secret.ToString().Contains("TopSecret") && !$"{secret}".Contains("TopSecret"), "password never printed");
    Check(ReportAuth.Header(new BasicCredential("user", "pässword")).Parameter == Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user:pässword")));
});
Test("Report cards sign in to a basic-auth Mongoku and explain each 401 (fake server)", () =>
{
    var good = new BasicCredential("reader", "right-password");
    string expected = ReportAuth.Header(good).ToString();
    var headers = new List<string?>();
    HttpResponseMessage Server(HttpRequestMessage r)
    {
        headers.Add(r.Headers.Authorization?.ToString());
        if (r.Headers.Authorization?.ToString() == expected)
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FoilStatusSample) };
        var denied = new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("Unauthorized") };
        denied.Headers.WwwAuthenticate.ParseAdd("Basic");
        return denied;
    }
    var card = new ReportCard { SourceUrl = "http://localhost:3100/", ReportId = "FOIL_STATUS_NOW" };
    var none = ReportCards.FetchAsync(card, new FakeHandler(Server)).GetAwaiter().GetResult();
    Check(none.Error!.Contains("asks for a user name and password") && headers[^1] is null, "no header without a saved sign-in");
    var wrong = ReportCards.FetchAsync(card, new FakeHandler(Server), new BasicCredential("reader", "nope")).GetAwaiter().GetResult();
    Check(wrong.Error!.Contains("rejected the saved user name or password"));
    var ok = ReportCards.FetchAsync(card, new FakeHandler(Server), good).GetAwaiter().GetResult();
    Check(ok.Succeeded && headers[^1] == expected, "signed in");
    Check(!(wrong.Error + none.Error).Contains("right-password") && !(wrong.Error).Contains("nope"), "messages never echo passwords");
    var (list, _) = ReportCards.ListReportsAsync("http://localhost:3100/", new FakeHandler(r => { headers.Add(r.Headers.Authorization?.ToString()); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"reports":[{"id":"FOIL_NEXT","title":"FOIL next"}]}""") }; }), good).GetAwaiter().GetResult();
    Check(list.Count == 1 && headers[^1] == expected, "report list uses the same sign-in");
    int calls = headers.Count;
    var lan = ReportCards.FetchAsync(new ReportCard { SourceUrl = "http://192.168.1.20:3100/", ReportId = "FOIL_NEXT" }, new FakeHandler(Server), good).GetAwaiter().GetResult();
    Check(lan.Error!.Contains("plain http") && headers.Count == calls, "password never sent over LAN http: no request at all");
    var oidc = ReportCards.FetchAsync(card, new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("""{"message":"Session expired"}""") }), good).GetAwaiter().GetResult();
    Check(oidc.Error!.Contains("OIDC"), "Mongoku web sign-in is recognised");
});
Test("Source inventory report: new states and per-source resolution are understood", () =>
{
    const string inventory = """
    {"reportId":"SOURCE_INVENTORY","title":"Source inventory","readOnly":true,"sections":[
      {"sourceId":"DATAPROJECTS_GLOBAL","trace":{"resolved":true,"operation":"inventory"},"meta":{"state":"OK","returnedRows":7}},
      {"sourceId":"FOIL_FABRIC","trace":{"resolved":true},"meta":{"state":"EMPTY","returnedRows":0}},
      {"sourceId":"FOIL_FRONT","trace":{"resolved":false},"meta":{"state":"REGISTERED_UNBOUND","returnedRows":0}},
      {"sourceId":"FOIL_CORE","trace":{"resolved":true},"meta":{"state":"SOURCE_ERROR","returnedRows":0}},
      {"label":"No trace","meta":{"state":"OK","returnedRows":1}}]}
    """;
    var s = ReportCards.Parse(inventory, DateTimeOffset.Now);
    Check(s.Sections[0].Label == "DATAPROJECTS_GLOBAL", "sourceId used when a section has no label");
    Check(s.Sections[1].Health == SectionHealth.Ok && s.Sections[1].Explanation.Contains("nothing to list"), "EMPTY is healthy");
    Check(s.Sections[2].Health == SectionHealth.Unavailable && s.Sections[2].Explanation.Contains("registered"), "REGISTERED_UNBOUND");
    Check(s.Sections[3].Health == SectionHealth.Unavailable && s.Sections[3].Explanation.Contains("error"), "SOURCE_ERROR");
    Check(s.Sections.Count(x => x.Resolved == true) == 3 && s.Sections.Count(x => x.Resolved is not null) == 4 && s.Sections[4].Resolved is null);
    Check(s.Overall == SectionHealth.Unavailable);
});
Test("Non-authoritative reports are flagged from Mongoku's row markers; descriptions carry caveats", () =>
{
    const string ai = """
    {"reportId":"FOIL_AI_REASONING_RECENT","title":"FOIL AI Reasoning — recent (non-authoritative)","description":"Non-authoritative AI reasoning: hypotheses. Never FOIL Core Truth.","readOnly":true,
     "sections":[{"id":"reasoning","label":"Recent reasoning records","rows":[{"title":"h1","summary":"private hypothesis text","authorityBoundary":"NON_AUTHORITATIVE_AI_REASONING","promotionState":"candidate"},{"title":"h2","authorityBoundary":"NON_AUTHORITATIVE_AI_REASONING"}],"meta":{"state":"OK","returnedRows":2}},
                 {"id":"boundary","label":"Authority boundary","rows":[{"title":"rule"}],"meta":{"state":"OK","returnedRows":1}}]}
    """;
    var s = ReportCards.Parse(ai, DateTimeOffset.Now);
    Check(s.NonAuthoritative && s.AuthorityBoundaries!.SequenceEqual(new[] { "NON_AUTHORITATIVE_AI_REASONING" }), "boundary marker read once");
    Check(s.Description!.Contains("Never FOIL Core Truth"), "Mongoku's caveat kept for display");
    Check(!JsonSerializer.Serialize(s).Contains("private hypothesis text"), "only the marker is kept, never row text");
    Check(!ReportCards.Parse(FoilStatusSample, DateTimeOffset.Now).NonAuthoritative, "ordinary reports are not flagged");
    var authoritative = ReportCards.Parse("""{"sections":[{"rows":[{"authorityBoundary":"AUTHORITATIVE_PM"}],"meta":{"state":"OK"}}]}""", DateTimeOffset.Now);
    Check(!authoritative.NonAuthoritative && authoritative.AuthorityBoundaries!.Count == 1);
});
// ---- Credentials & IDs, .env registry, quick capture and AtlasNote handoff (synthetic secrets only) ----
const string SyntheticSecret = "SYNTH-S3CRET-ZqX7wV9kL2mN4pR8tY1uI3oP5aS6dF";
CredentialCatalog SampleCatalog(out CredentialRecord secret, out CredentialRecord id)
{
    var catalog = new CredentialCatalog();
    CredentialTemplates.Apply(catalog, CredentialTemplates.All.Single(x => x.Service == "Cloudflare"), "Foil");
    id = catalog.Records.Single(x => x.Label == "Account ID"); id.Value = "0123456789abcdef0123456789abcdef";
    secret = catalog.Records.Single(x => x.Label == "API token");
    return catalog;
}
Test("Cloudflare/Mongo templates give labelled copyable rows and secrets without stored values", () =>
{
    var catalog = new CredentialCatalog();
    var cloudflare = CredentialTemplates.Apply(catalog, CredentialTemplates.All.Single(x => x.Service == "Cloudflare"), "Foil");
    Check(cloudflare.Select(x => x.Label).Take(4).SequenceEqual(["Account ID", "Zone ID", "Access Application ID", "Service Token Client ID"]));
    var mongo = CredentialTemplates.Apply(catalog, CredentialTemplates.All.Single(x => x.Service == "MongoDB Atlas"), "Foil");
    Check(mongo.Any(x => x.Label == "Project ID" && x.Kind == CredentialKind.Id));
    Check(mongo.Any(x => x.Kind == CredentialKind.Password) && catalog.Records.Where(x => CredentialRules.IsSecret(x.Kind)).All(x => x.Value == ""));
    Check(CredentialTemplates.Apply(catalog, CredentialTemplates.All.Single(x => x.Service == "Cloudflare"), "foil").Count == 0, "Template is idempotent per service+project");
    Check(CredentialTemplates.Apply(catalog, CredentialTemplates.All.Single(x => x.Service == "Cloudflare"), "Datapass").Count == cloudflare.Count);
    CredentialRules.Validate(catalog);
});
Test("Project, service and type views are projections of the same records", () =>
{
    var catalog = SampleCatalog(out _, out var id);
    CredentialTemplates.Apply(catalog, CredentialTemplates.All.Single(x => x.Service == "MongoDB Atlas"), "");
    foreach (var view in Enum.GetValues<CredentialView>())
    {
        var groups = CredentialRules.Group(catalog.Records, view);
        var all = groups.SelectMany(g => g.Records).ToList();
        Check(all.Count == catalog.Records.Count && all.Distinct().Count() == all.Count && all.All(r => catalog.Records.Any(x => ReferenceEquals(x, r))), $"{view} duplicates or copies records");
    }
    Check(CredentialRules.Group(catalog.Records, CredentialView.Project).Last().Name == CredentialRules.NoProject);
    Check(CredentialRules.Group(catalog.Records, CredentialView.Type).Any(g => g.Name == "Token"));
    Check(CredentialRules.Group(catalog.Records, CredentialView.Service, "0123456789abcdef").Single().Records.Single() == id, "Search finds a non-secret value");
});
Test("Secret kinds refuse a value in metadata and cannot be shareable", () =>
{
    var catalog = SampleCatalog(out var secret, out _);
    secret.Value = SyntheticSecret; Reject(() => CredentialRules.Validate(catalog));
    secret.Value = ""; secret.IsShareable = true; Reject(() => CredentialRules.Validate(catalog));
    secret.IsShareable = false; CredentialRules.Validate(catalog);
    var url = new CredentialRecord { Label = "Portal", Kind = CredentialKind.Url, Value = "javascript:alert(1)" };
    catalog.Records.Add(url); Reject(() => CredentialRules.Validate(catalog));
});
Test("Secret values reach only the vault: metadata file, backup and export stay clean", () => Temporary(directory =>
{
    var vault = new MemoryVault();
    var catalog = SampleCatalog(out var secret, out var id);
    vault.WriteSecret(CredentialRules.VaultTarget(catalog, secret.Id), SyntheticSecret, "", "test");
    var store = new CredentialCatalogStore(directory);
    store.Save(catalog); store.Save(catalog);
    foreach (string file in Directory.GetFiles(directory)) Check(!File.ReadAllText(file).Contains(SyntheticSecret), "Secret leaked into " + Path.GetFileName(file));
    Check(store.Load().Records.Count == catalog.Records.Count);
    string export = PortableExport.Create(new WorkspaceState(), WorkspaceSessions.Defaults(), ModuleCatalog.All.Select(x => x.Id), includeLocalDetails: true, includeShell: true, credentials: catalog);
    Check(!export.Contains(SyntheticSecret), "Secret leaked into export");
    Check(!export.Contains(id.Value), "Private ID value exported");
    Check(export.Contains(CredentialRules.CredentialRef(secret.Id)), "Secret exported as opaque reference");
    id.IsShareable = true;
    Check(PortableExport.Create(new WorkspaceState(), WorkspaceSessions.Defaults(), ["credentials"], credentials: catalog).Contains(id.Value), "Shareable ID exported");
    Check(vault.ReadSecret(CredentialRules.VaultTarget(catalog, secret.Id)) == SyntheticSecret);
}));
Test("Moving a mistyped secret out of metadata also rotates it out of the backup", () => Temporary(directory =>
{
    var store = new CredentialCatalogStore(directory);
    var catalog = SampleCatalog(out _, out var id);
    id.Value = SyntheticSecret; store.Save(catalog); store.Save(catalog);
    id.Value = ""; id.Kind = CredentialKind.Token; store.SaveAndRotateBackup(catalog);
    foreach (string file in Directory.GetFiles(directory)) Check(!File.ReadAllText(file).Contains(SyntheticSecret), "Stale copy in " + Path.GetFileName(file));
}));
Test("Vault targets are opaque and isolated per data folder; orphans are scoped", () =>
{
    var a = SampleCatalog(out var secretA, out _); var b = SampleCatalog(out var secretB, out _);
    string targetA = CredentialRules.VaultTarget(a, secretA.Id);
    Check(!targetA.Contains("Cloudflare") && !targetA.Contains("API") && targetA.StartsWith(CredentialRules.VaultPrefix));
    Check(!CredentialRules.ScopePrefix(a).Equals(CredentialRules.ScopePrefix(b)));
    var vault = new MemoryVault();
    vault.WriteSecret(targetA, SyntheticSecret, "", ""); vault.WriteSecret(CredentialRules.VaultTarget(b, secretB.Id), SyntheticSecret, "", "");
    string orphan = CredentialRules.ScopePrefix(a) + Guid.NewGuid().ToString("N"); vault.WriteSecret(orphan, SyntheticSecret, "", "");
    Check(CredentialRules.OrphanTargets(a, vault.Targets(CredentialRules.VaultPrefix)).SequenceEqual([orphan]), "Other data folder's secrets are never orphans here");
    var envFile = new EnvFileEntry { Path = @"C:\fixture\.env", Keys = [new EnvKeyEntry { Name = "CLOUDFLARE_API_TOKEN", CredentialRef = secretA.Id }] };
    a.EnvFiles.Add(envFile);
    Check(CredentialRules.RemoveRecord(a, secretA.Id) && envFile.Keys[0].CredentialRef is null, "Deleting a record clears its .env links");
    CredentialRules.Validate(a);
    Check(CredentialRules.OrphanTargets(a, vault.Targets(CredentialRules.VaultPrefix)).Contains(targetA), "A deleted record's kept secret is reported as orphaned");
});
Test("Corrupt, future or dangling credential metadata fails closed and is never overwritten", () => Temporary(directory =>
{
    var store = new CredentialCatalogStore(directory);
    Check(store.Load().Records.Count == 0 && !File.Exists(store.FilePath), "Missing file = empty catalog, nothing written");
    File.WriteAllText(store.FilePath, "{ not json");
    Reject(() => store.Load()); Reject(() => store.Save(new CredentialCatalog()));
    Check(File.ReadAllText(store.FilePath) == "{ not json", "Corrupt bytes preserved");
    File.WriteAllText(store.FilePath, "{\"Format\":\"powerops-credentials\",\"SchemaVersion\":2,\"VaultScope\":\"" + Guid.NewGuid() + "\"}");
    Reject(() => store.Load());
    var catalog = new CredentialCatalog();
    catalog.EnvFiles.Add(new EnvFileEntry { Path = @"C:\fixture\.env", Keys = [new EnvKeyEntry { Name = "X", CredentialRef = Guid.NewGuid() }] });
    Reject(() => CredentialRules.Validate(catalog));
    catalog.EnvFiles[0] = new EnvFileEntry { Path = "relative\\.env" }; Reject(() => CredentialRules.Validate(catalog));
    catalog.EnvFiles[0] = new EnvFileEntry { Path = @"C:\fixture\.env", Keys = [new EnvKeyEntry { Name = "BAD NAME" }] }; Reject(() => CredentialRules.Validate(catalog));
}));
Test(".env inspection returns key names and present/empty state only, never values", () =>
{
    string content = string.Join("\n",
        "# comment with " + SyntheticSecret,
        "CLOUDFLARE_API_TOKEN=" + SyntheticSecret,
        "export MONGODB_URI=\"mongodb+srv://user:" + SyntheticSecret + "@cluster0.example.net/db\"",
        "EMPTY_ONE=",
        "QUOTED_EMPTY=''",
        "INLINE=  # just a comment",
        "PRIVATE_KEY=\"-----BEGIN KEY-----",
        SyntheticSecret,
        "NOT_A_KEY=still inside the quoted value",
        "-----END KEY-----\"",
        "garbage line " + SyntheticSecret,
        "CLOUDFLARE_API_TOKEN=second");
    var inspection = EnvRegistry.Parse(new StringReader(content));
    Check(inspection.Keys.Select(k => k.Name).SequenceEqual(["CLOUDFLARE_API_TOKEN", "MONGODB_URI", "EMPTY_ONE", "QUOTED_EMPTY", "INLINE", "PRIVATE_KEY"]), string.Join(",", inspection.Keys.Select(k => k.Name)));
    Check(inspection.Keys.Where(k => k.Name is "EMPTY_ONE" or "QUOTED_EMPTY" or "INLINE").All(k => !k.HasValue));
    Check(inspection.Keys.Where(k => k.Name is "CLOUDFLARE_API_TOKEN" or "MONGODB_URI" or "PRIVATE_KEY").All(k => k.HasValue));
    string everything = JsonSerializer.Serialize(inspection);
    Check(!everything.Contains("SYNTH") && !everything.Contains("mongodb+srv") && !everything.Contains("second"), "Inspection result holds a value");
    Check(inspection.Warnings.Count == 2 && inspection.Warnings.All(w => !w.Contains("SYNTH") && !w.Contains("garbage")));
});
Test(".env registry keeps expected and linked keys as Missing, drops vanished observations, never persists values", () => Temporary(directory =>
{
    string envPath = Path.Combine(directory, ".env.preview");
    File.WriteAllText(envPath, "FOO_API_TOKEN=" + SyntheticSecret + "\nOLD_KEY=x\nBLANK=\n");
    var catalog = SampleCatalog(out var secret, out _);
    var entry = new EnvFileEntry { Path = envPath, Environment = EnvRegistry.SuggestEnvironment(envPath), Project = "Foil" };
    catalog.EnvFiles.Add(entry);
    EnvRegistry.Apply(entry, EnvRegistry.Inspect(envPath), DateTimeOffset.UtcNow);
    Check(entry.Environment == "preview" && entry.Keys.Single(k => k.Name == "FOO_API_TOKEN").State == EnvKeyState.Present && entry.Keys.Single(k => k.Name == "BLANK").State == EnvKeyState.Empty);
    entry.Keys.Single(k => k.Name == "FOO_API_TOKEN").CredentialRef = secret.Id;
    entry.Keys.Add(new EnvKeyEntry { Name = "EXPECTED_ONLY", Expected = true });
    File.WriteAllText(envPath, "BLANK=now-set\n");
    EnvRegistry.Apply(entry, EnvRegistry.Inspect(envPath), DateTimeOffset.UtcNow);
    Check(entry.Keys.Single(k => k.Name == "FOO_API_TOKEN").State == EnvKeyState.Missing);
    Check(entry.Keys.Single(k => k.Name == "EXPECTED_ONLY").State == EnvKeyState.Missing);
    Check(entry.Keys.All(k => k.Name != "OLD_KEY") && entry.Keys.Single(k => k.Name == "BLANK").State == EnvKeyState.Present);
    var store = new CredentialCatalogStore(Path.Combine(directory, "data")); store.Save(catalog);
    Check(!File.ReadAllText(store.FilePath).Contains("SYNTH") && !File.ReadAllText(store.FilePath).Contains("now-set"));
    Check(File.ReadAllText(envPath) == "BLANK=now-set\n", "The .env file is never rewritten");
    Check(EnvRegistry.IsEnvFileName(".env") && EnvRegistry.IsEnvFileName(".env.local") && EnvRegistry.IsEnvFileName("prod.env") && !EnvRegistry.IsEnvFileName("env.txt") && !EnvRegistry.IsEnvFileName(".envrc"));
}));
Test("KEY=value clipboard lines quote and escape only when needed", () =>
{
    Check(EnvRegistry.FormatAssignment("ZONE_ID", "0123abcd") == "ZONE_ID=0123abcd");
    Check(EnvRegistry.FormatAssignment("A", "has space") == "A=\"has space\"");
    Check(EnvRegistry.FormatAssignment("A", "q\"uo\\te#") == "A=\"q\\\"uo\\\\te#\"");
    Check(EnvRegistry.FormatAssignment("A", "line1\nline2") == "A=\"line1\\nline2\"");
    Check(EnvRegistry.FormatAssignment("A", "") == "A=\"\"");
    Reject(() => EnvRegistry.FormatAssignment("BAD NAME", "x"));
});
Test("Secret heuristics warn on token shapes but not on long hex IDs", () =>
{
    Check(SecretHeuristics.Reason("0123456789abcdef0123456789abcdef") is null, "Cloudflare account id is not secret-shaped");
    Check(SecretHeuristics.Reason("3fa85f64-5717-4562-b3fc-2c963f66afa6") is null);
    Check(SecretHeuristics.Reason("ghp_" + "A1b2C3d4E5f6G7h8I9j0") is not null);
    Check(SecretHeuristics.Reason("mongodb+srv://app:pw123@cluster0.example.net") is not null);
    Check(SecretHeuristics.Reason("password = hunter2") is not null);
    Check(SecretHeuristics.Reason(SyntheticSecret) is { } reason && !reason.Contains("SYNTH"));
    Check(SecretHeuristics.Reason("Read the Cloudflare docs about Zero Trust tomorrow") is null);
});
Test("Quick capture: URL suggests Link, text suggests Note, project optional", () =>
{
    Check(QuickCaptureRules.Suggest("https://developers.cloudflare.com/workers/") == CaptureKind.Bookmark);
    Check(QuickCaptureRules.Suggest("remember to rotate the token") == CaptureKind.QuickNote);
    Check(QuickCaptureRules.Suggest("see https://example.com later") == CaptureKind.QuickNote);
    var now = DateTimeOffset.UtcNow;
    var link = QuickCaptureRules.Create(CaptureKind.Bookmark, "  https://developers.cloudflare.com/workers/ ", null, "", now);
    Check(link.Url == "https://developers.cloudflare.com/workers/" && link.Title == "developers.cloudflare.com/workers" && link.Text == "" && link.ProjectId is null);
    var project = Guid.NewGuid();
    var todo = QuickCaptureRules.Create(CaptureKind.Todo, "Rotate CF token\nbefore Friday", project, "foil", now);
    Check(todo.Title == "Rotate CF token" && todo.Text.Contains("before Friday") && todo.Status == "Open" && todo.ProjectId == project && todo.Labels == "foil");
    var note = QuickCaptureRules.Create(CaptureKind.QuickNote, "short", null, null, now);
    Check(note.Title == "short" && note.Text == "");
    Check(QuickCaptureRules.Create(CaptureKind.QuickNote, new string('x', 400), null, null, now).Title.Length <= QuickCaptureRules.MaxTitle);
    Reject(() => QuickCaptureRules.Create(CaptureKind.QuickNote, "   ", null, null, now));
    Reject(() => QuickCaptureRules.Create(CaptureKind.Transcript, "x", null, null, now));
});
Test("AtlasNote handoff is non-secret, stable-id, snapshot-labelled and flags secret-shaped captures", () =>
{
    var project = Guid.NewGuid();
    var clean = QuickCaptureRules.Create(CaptureKind.ReadLater, "https://example.com/article", project, "reading, foil", DateTimeOffset.UtcNow);
    var risky = QuickCaptureRules.Create(CaptureKind.QuickNote, "token=" + SyntheticSecret, null, null, DateTimeOffset.UtcNow);
    var warnings = AtlasNoteHandoff.Review([clean, risky]);
    Check(warnings.Count == 1 && warnings[0].NoteId == risky.Id && !warnings[0].Reason.Contains("SYNTH"));
    string json = AtlasNoteHandoff.Create([clean], new Dictionary<Guid, string> { [project] = "Foil" }, DateTimeOffset.UtcNow);
    var root = JsonNode.Parse(json)!;
    Check(root["format"]!.GetValue<string>() == "powerops.atlasnote-handoff" && root["version"]!.GetValue<int>() == 1);
    Check(root["containsCredentialValues"]!.GetValue<bool>() == false && root["containsMediaBinaries"]!.GetValue<bool>() == false);
    var item = root["items"]![0]!;
    Check(item["sourceObjectId"]!.GetValue<string>() == clean.Id.ToString("D") && item["freshness"]!.GetValue<string>() == "snapshot" && item["kind"]!.GetValue<string>() == "readLater");
    Check(item["projectName"]!.GetValue<string>() == "Foil" && item["labels"]!.AsArray().Count == 2 && item["observedAt"] is not null);
    Check(!json.Contains("SYNTH"));
    Reject(() => AtlasNoteHandoff.Create([], new Dictionary<Guid, string>(), DateTimeOffset.UtcNow));
});
Test("Claude usage card parses the Effort Board usage.json read-only shape", () =>
{
    string json = """
    {"generated":"2026-09-25T14:40","today":"2026-09-25",
     "plan":{"name":"Pro","asOf":"2026-09-25T12:39Z","windows":[
       {"label":"5-hour limit","percentUsed":7,"resetsAt":"2026-09-25T16:59:59Z"},
       {"label":"Weekly Ã‚Â· all models","percentUsed":123,"resetsAt":"2026-10-01T11:59:59Z"},
       {"label":"no numbers"}]},
     "days":[{"day":"2026-09-25","tokens":5}],
     "projects":[{"name":"small","week":12300,"today":0},{"name":"datapass","week":1511041581,"today":905514715},{"week":5},"junk"]}
    """;
    var usage = ClaudeUsage.Parse(json);
    Check(usage.PlanName == "Pro" && usage.Generated == "2026-09-25T14:40" && usage.Windows.Count == 3);
    Check(usage.Windows[0].PercentUsed == 7 && usage.Windows[0].ResetsAt == DateTimeOffset.Parse("2026-09-25T16:59:59Z"));
    Check(usage.Windows[1].Label == "Weekly · all models", "Double-encoded label repaired: " + usage.Windows[1].Label);
    Check(usage.Windows[1].PercentUsed == 100 && double.IsNaN(usage.Windows[2].PercentUsed) && usage.Windows[2].ResetsAt is null);
    Check(usage.Projects.Select(p => p.Name).SequenceEqual(["datapass", "small"]), "Sorted by week, nameless/invalid skipped");
    Check(ClaudeUsage.Tokens(1511041581) == "1.51 B" && ClaudeUsage.Tokens(905514715) == "906 M" && ClaudeUsage.Tokens(12300) == "12.3 K" && ClaudeUsage.Tokens(7) == "7");
    Check(ClaudeUsage.RepairMojibake("Weekly · Fable") == "Weekly · Fable" && ClaudeUsage.RepairMojibake("Café") == "Café");
    Check(ClaudeUsage.Parse("{}").Windows.Count == 0);
    Reject(() => ClaudeUsage.Parse("[1,2]"));
    Reject(() => ClaudeUsage.Parse("{ broken"));
});
Test("Effort Board preset opens with the browser sign-in and carries no private link", () =>
{
    var preset = QuickWebApps.Presets.Single(x => x.Name == "Effort Board");
    Check(preset.OpenMode == WebOpenMode.AppWindow && !preset.Url.Contains("/artifact/"));
    QuickWebApps.Validate([QuickWebApps.FromPreset(preset)]);
});
Test("Credentials module is registered without breaking existing shell files", () =>
{
    Check(ModuleCatalog.Get("credentials").Header == "Credentials & IDs");
    var state = WorkspaceSessions.Defaults(); WorkspaceSessions.Validate(state);
    var old = WorkspaceSessions.NewProfile("Old", "repositories"); old.VisibleModules.Remove("credentials");
    state.Workspaces.Add(old); WorkspaceSessions.Validate(state);
});
// Trimmed from the live Mongoku MAINTENANCE response (2026-09-25); extra row fields must never be kept.
const string MaintenanceSample = """
{"reportId":"MAINTENANCE","title":"Portfolio maintenance","description":"What needs attention, from recorded metadata only.","generatedAt":"2026-09-25T13:59:10.361Z","readOnly":true,"sections":[
 {"id":"summary","label":"Summary","trace":{"resolved":true},"meta":{"state":"OK","returnedRows":1},"rows":[{"_id":"summary","title":"Maintenance summary","sourcesReachable":"10/10","projectsNeedingAction":8,"headsToReconcile":0,"auditsToReview":4,"projectionsNeedingAction":1,"reconciliationFindings":0,"backups":"NOT_RECORDED","summary":"10/10 sources reachable; 8 project(s) need attention.","actionKind":"verify","nextAction":"Atlas family: Record a verification at the next review"}]},
 {"id":"sources","label":"Source reachability","trace":{"resolved":true},"meta":{"state":"OK","returnedRows":1},"rows":[{"_id":"FOIL_PM","title":"FOIL PM","actionKind":"none","nextAction":"No action"}]},
 {"id":"reconciliation","label":"Needs reconciliation","trace":{"resolved":true},"meta":{"state":"EMPTY","returnedRows":0},"rows":[]},
 {"id":"projects","label":"Project verification freshness","trace":{"resolved":true},"meta":{"state":"OK","returnedRows":3},"rows":[
   {"_id":"atlas","title":"Atlas family","repo":"julian-passebecq/atlasnote-private-marker","openUri":"/?project=atlas","summary":"Active but never verified.","actionKind":"verify","nextAction":"Record a verification at the next review"},
   {"_id":"evil","title":"Elsewhere","openUri":"https://evil.example/steal","actionKind":"review_record","nextAction":"Review the record"},
   {"_id":"done","title":"Done project","openUri":"/?project=done","actionKind":"none","nextAction":"No action"}]},
 {"id":"heads","label":"Recorded heads (GitHub not read)","trace":{"resolved":true},"meta":{"state":"OK","returnedRows":1},"rows":[{"_id":"a","title":"AtlasNote","recordedHead":"215d3c962736bbac44c3f2e3602fa1f80cee77c8","actionKind":"none","nextAction":"No action"}]},
 {"id":"projections","label":"Galaxy projections","trace":{"resolved":true},"meta":{"state":"OK","returnedRows":1},"rows":[{"_id":"atlasnote","title":"AtlasNote projection","actionKind":"export_projection","nextAction":"No projection published yet"}]},
 {"id":"audits","label":"Audit runs","trace":{"resolved":true},"meta":{"state":"OK","returnedRows":1},"rows":[{"_id":"AUDIT-1","title":"AUDIT-1","actionKind":"review_audit","nextAction":"Review its recorded next actions and coverage"}]}]}
""";
Test("Maintenance card parses the summary line, next action, counts and rows to act on", () =>
{
    var root = new Uri("http://localhost:3100/");
    var s = ReportCards.Parse(MaintenanceSample, DateTimeOffset.Now, root);
    var m = s.Maintenance!;
    Check(m.SummaryLine!.StartsWith("10/10 sources reachable") && m.NextAction == "Atlas family: Record a verification at the next review");
    Check(m.SourcesReachable == "10/10" && m.ProjectsNeedingAction == 8 && m.HeadsToReconcile == 0 && m.AuditsToReview == 4 && m.ProjectionsNeedingAction == 1 && m.ReconciliationFindings == 0 && m.Backups == "NOT_RECORDED");
    Check(!m.SummaryUnavailable && !m.UnavailableSections.Any() && m.Sections.Count == 7);
    Check(MaintenanceReport.CountsLine(m) == "Sources 10/10 · projects 8 · heads 0 · projections 1 · audits 4 · reconciliation 0 · backups not recorded", MaintenanceReport.CountsLine(m));
    Check(m.Actions.Select(x => x.Title).SequenceEqual(["Atlas family", "Elsewhere", "AtlasNote projection", "AUDIT-1"]), "actionKind none rows are skipped");
    Check(m.Actions[0].OpenUri!.AbsoluteUri == "http://localhost:3100/?project=atlas" && m.Actions[0].Summary == "Active but never verified.");
    Check(m.Actions[1].OpenUri is null, "cross-origin openUri is dropped (card falls back to the maintenance page)");
    string kept = JsonSerializer.Serialize(s);
    Check(!kept.Contains("atlasnote-private-marker") && !kept.Contains("215d3c9"), "only title/summary/next action/link are kept from rows");
    Check(ReportCards.DeepLink(new ReportCard { SourceUrl = "http://localhost:3100", ReportId = "MAINTENANCE" }).AbsoluteUri == "http://localhost:3100/maintenance");
    Check(ReportCards.Parse(FoilStatusSample, DateTimeOffset.Now, root).Maintenance is null, "other reports are unchanged");
});
Test("Maintenance row links stay inside the card's Mongoku", () =>
{
    var root = new Uri("http://localhost:3100/");
    Check(MaintenanceReport.ResolveOpenUri(root, "/?project=a_b")!.AbsoluteUri == "http://localhost:3100/?project=a_b");
    Check(MaintenanceReport.ResolveOpenUri(new Uri("https://m.example.com/base/"), "/?project=x")!.AbsoluteUri == "https://m.example.com/base/?project=x", "relative to the Mongoku base address");
    foreach (string? bad in new[] { null, "", "?project=x", "//evil.example/x", "/\\evil", "javascript:alert(1)", "https://evil.example/", "file:///C:/x", "/\nx" })
        Check(MaintenanceReport.ResolveOpenUri(root, bad) is null, "rejected: " + bad);
    Check(MaintenanceReport.ResolveOpenUri(null, "/?project=x") is null);
});
Test("Maintenance card shows unresolved sections as unavailable, never substitute numbers", () =>
{
    string auditsDown = MaintenanceSample.Replace("""{"id":"audits","label":"Audit runs","trace":{"resolved":true}""", """{"id":"audits","label":"Audit runs","trace":{"resolved":false}""");
    var m = ReportCards.Parse(auditsDown, DateTimeOffset.Now, new Uri("http://localhost:3100/")).Maintenance!;
    Check(m.UnavailableSections.Single().Id == "audits" && !m.SummaryUnavailable);
    Check(m.CountFor("audits", m.AuditsToReview) is null && MaintenanceReport.CountsLine(m).Contains("audits unavailable") && MaintenanceReport.CountsLine(m).Contains("projects 8"));
    Check(m.Actions.All(x => x.Section != "audits"), "rows of an unresolved section are not shown");

    string summaryDown = MaintenanceSample.Replace("""{"id":"summary","label":"Summary","trace":{"resolved":true},"meta":{"state":"OK",""", """{"id":"summary","label":"Summary","trace":{"resolved":false},"meta":{"state":"SOURCE_UNAVAILABLE",""");
    var d = ReportCards.Parse(summaryDown, DateTimeOffset.Now, new Uri("http://localhost:3100/")).Maintenance!;
    Check(d.SummaryUnavailable && d.SummaryLine is null && d.NextAction is null);
    Check(MaintenanceReport.CountsLine(d) == "Sources unavailable · projects unavailable · heads unavailable · projections unavailable · audits unavailable · reconciliation unavailable · backups unavailable", MaintenanceReport.CountsLine(d));

    var bare = ReportCards.Parse("""{"reportId":"MAINTENANCE","sections":[]}""", DateTimeOffset.Now).Maintenance!;
    Check(bare.SummaryUnavailable && bare.Actions.Count == 0, "a report without a summary section is unavailable, not all-clear");
    var odd = ReportCards.Parse("""{"reportId":"MAINTENANCE","sections":[{"id":"summary","rows":[{"projectsNeedingAction":"8","auditsToReview":-1}]},{"id":"projects","rows":"x"},7]}""", DateTimeOffset.Now).Maintenance!;
    Check(odd.ProjectsNeedingAction is null && odd.AuditsToReview is null && odd.Actions.Count == 0, "wrong types stay unknown");
});
Test("Maintenance card: Mongoku down is a readable failure, never an exception (fake server)", () =>
{
    var card = new ReportCard { SourceUrl = "http://localhost:3100/", ReportId = "MAINTENANCE" };
    var down = ReportCards.FetchAsync(card, new FakeHandler(_ => throw new HttpRequestException("refused", new System.Net.Sockets.SocketException(10061)))).GetAwaiter().GetResult();
    Check(!down.Succeeded && down.Error!.Contains("not reachable"));
    HttpRequestMessage? seen = null;
    var ok = ReportCards.FetchAsync(card, new FakeHandler(r => { seen = r; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MaintenanceSample) }; })).GetAwaiter().GetResult();
    Check(ok.Summary!.Maintenance!.Actions[0].OpenUri!.AbsoluteUri == "http://localhost:3100/?project=atlas", "links resolved against the card's address");
    Check(seen!.Method == HttpMethod.Get && seen.RequestUri!.AbsolutePath == "/api/datapass/reports/MAINTENANCE" && seen.Headers.Authorization is null, "read-only GET, no credential");
});
// ---- V2.3 File tray: recently received files for AI chats ----
var trayEpoch = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
FileTrayEntry TrayEntry(string name, int minutesAgo, long length = 100, string folder = "C:\\Tray", int writeMinutesAgo = -1) =>
    new(Path.Combine(folder, name), folder, "Downloads", FileTrayFilter.KindOf(name) ?? FileTrayKind.Text, length,
        trayEpoch.AddMinutes(-(writeMinutesAgo < 0 ? minutesAgo : writeMinutesAgo)), trayEpoch.AddMinutes(-minutesAgo));
bool WaitFor(Func<bool> condition, int milliseconds = 6000)
{
    var clock = System.Diagnostics.Stopwatch.StartNew();
    while (clock.ElapsedMilliseconds < milliseconds) { if (condition()) return true; Thread.Sleep(50); }
    return condition();
}
void Write(string path, string text, DateTime? writeUtc = null)
{
    File.WriteAllText(path, text);
    if (writeUtc is DateTime time) { File.SetCreationTimeUtc(path, time); File.SetLastWriteTimeUtc(path, time); }
}
Test("File tray filter keeps chat file types and ignores partial downloads", () =>
{
    foreach (string name in new[] { "a.pdf", "B.PNG", "c.jpg", "d.JPEG", "e.webp", "f.gif", "IMG_1.HEIC", "g.docx", "h.txt", "WhatsApp Image 2026-09-25 at 10.02.11.jpeg" })
        Check(FileTrayFilter.IsCandidate(name), name);
    foreach (string name in new[] { "a.pdf.crdownload", "Unconfirmed 123456.crdownload", "b.pdf.part", "c.jpg.partial", "d.tmp", "e.pdf.download", "f.opdownload",
        "~$report.docx", ".~lock.report.docx#", "setup.exe", "archive.zip", "notes.md", "photo.jpg.exe", "noextension", "g.doc" })
        Check(!FileTrayFilter.IsCandidate(name), name);
    Check(FileTrayFilter.IsPartial("x.pdf.crdownload") && FileTrayFilter.IsPartial("~$x.docx") && !FileTrayFilter.IsPartial("x.pdf"));
    Check(FileTrayFilter.KindOf("x.pdf") == FileTrayKind.Pdf && FileTrayFilter.KindOf("x.heic") == FileTrayKind.Image
        && FileTrayFilter.KindOf("x.docx") == FileTrayKind.Document && FileTrayFilter.KindOf("x.txt") == FileTrayKind.Text && FileTrayFilter.KindOf("x.zip") is null);
});
Test("File tray orders newest arrival first, one entry per file, bounded to N", () =>
{
    var list = new FileTrayList(3);
    Check(list.Upsert(TrayEntry("old.pdf", 30)) && list.Upsert(TrayEntry("new.png", 1)) && list.Upsert(TrayEntry("mid.txt", 10)));
    Check(string.Join(",", list.Items.Select(x => x.Name)) == "new.png,mid.txt,old.pdf");
    Check(list.Upsert(TrayEntry("newest.docx", 0)), "a newer file enters");
    Check(list.Items.Count == 3 && list.Items[0].Name == "newest.docx" && list.Items.All(x => x.Name != "old.pdf"), "oldest trimmed");
    Check(!list.Upsert(TrayEntry("ancient.pdf", 500)), "older than everything kept: not added");
    Check(!list.Upsert(TrayEntry("x.zip", 0)) && !list.Upsert(TrayEntry("y.pdf.crdownload", 0)) && !list.Upsert(TrayEntry("empty.pdf", 0, length: 0)), "never unsupported, partial or empty");
    Check(list.Latest!.Name == "newest.docx");
    // Same arrival time: newer write time first, then name.
    var ties = new FileTrayList();
    ties.Upsert(TrayEntry("b.pdf", 5, writeMinutesAgo: 9)); ties.Upsert(TrayEntry("a.pdf", 5, writeMinutesAgo: 9)); ties.Upsert(TrayEntry("c.pdf", 5, writeMinutesAgo: 2));
    Check(string.Join(",", ties.Items.Select(x => x.Name)) == "c.pdf,a.pdf,b.pdf");
    Reject(() => new FileTrayList(0)); Reject(() => new FileTrayList(101));
    Check(list.SetCapacity(1) && list.Items.Count == 1 && list.Items[0].Name == "newest.docx");
});
Test("File tray dedupe: repeated events and path spellings stay one entry; unchanged files keep their place", () =>
{
    var list = new FileTrayList();
    list.Upsert(TrayEntry("report.pdf", 20)); list.Upsert(TrayEntry("photo.jpg", 5));
    var again = TrayEntry("report.pdf", 0, folder: "c:\\tray\\sub\\..") with { LastWriteUtc = trayEpoch.AddMinutes(-20) };
    Check(!list.Upsert(again), "same size and write time: a spurious event does not move it");
    Check(list.Items.Count == 2 && list.Items[0].Name == "photo.jpg");
    Check(list.Upsert(TrayEntry("REPORT.PDF", 0, length: 200)), "rewritten file moves to the top");
    Check(list.Items.Count == 2 && list.Items[0].Name == "REPORT.PDF" && list.Items.Count(x => x.Key == FileTrayFilter.Key("C:\\Tray\\report.pdf")) == 1);
    Check(list.Dismiss("c:\\TRAY\\report.pdf") && list.Items.Count == 1, "dismiss by any spelling");
    Check(!list.Upsert(TrayEntry("REPORT.PDF", 0, length: 200)), "dismissed stays hidden while unchanged");
    Check(list.Upsert(TrayEntry("REPORT.PDF", 0, length: 300)), "comes back when written again");
    Check(list.Dismiss("C:\\Tray\\photo.jpg") && !new FileTrayList(20, list.DismissedKeys).Upsert(TrayEntry("photo.jpg", 5)), "dismissals survive a watcher rebuild");
    Check(list.Relocate("C:\\Tray\\REPORT.PDF", "D:\\Projects\\Foil\\REPORT.PDF", "Moved to Foil") && list.Items[0].SourceLabel == "Moved to Foil"
        && list.Items[0].Path == "D:\\Projects\\Foil\\REPORT.PDF" && list.Items[0].ArrivedUtc == trayEpoch, "moved file keeps its place");
    Check(!list.Relocate("C:\\Tray\\missing.pdf", "D:\\x.pdf", "x") && !list.Remove("C:\\Tray\\missing.pdf"));
    Check(list.Prune(_ => false) && list.Items.Count == 0);
});
Test("File readiness waits for locked and empty files with a bounded one-shot schedule", () =>
{
    Temporary(dir =>
    {
        string file = Path.Combine(dir, "incoming.pdf");
        Check(FileTrayReadiness.Probe(file) == FileReadiness.Missing);
        File.WriteAllBytes(file, []);
        Check(FileTrayReadiness.Probe(file) == FileReadiness.Empty, "Firefox-style placeholder");
        using (var writer = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            writer.WriteByte(1); writer.Flush();
            Check(FileTrayReadiness.Probe(file) == FileReadiness.Locked, "still being written");
        }
        Check(FileTrayReadiness.Probe(file) == FileReadiness.Ready);
        Check(FileTrayReadiness.Probe(Path.Combine(dir, "x.pdf.crdownload")) == FileReadiness.NotCandidate);
    });
    Check(FileTrayReadiness.RetryDelay(0) == TimeSpan.FromMilliseconds(250) && FileTrayReadiness.RetryDelay(1) == TimeSpan.FromMilliseconds(500));
    double total = 0; int attempts = 0;
    for (int i = 0; FileTrayReadiness.RetryDelay(i) is TimeSpan delay; i++) { Check(delay <= TimeSpan.FromSeconds(8)); total += delay.TotalSeconds; attempts++; }
    Check(total <= 120 && total > 100 && attempts < 25, $"gives up after about two minutes ({total} s, {attempts} checks)");
});
Test("File tray watcher: new file appears, partial download only after rename, locked file after unlock, delete disappears", () =>
{
    Temporary(dir =>
    {
        Write(Path.Combine(dir, "old.txt"), "old", DateTime.UtcNow.AddDays(-2));
        using var service = new FileTrayService([new WatchedFolder(dir, "Test")], 3);
        int changes = 0; service.Changed += (_, _) => Interlocked.Increment(ref changes);
        Check(!service.IsStarted && service.Items.Count == 0, "nothing runs before first use");
        service.Start();
        Check(service.Items.Count == 1 && service.Items[0].Name == "old.txt" && service.PendingCount == 0);

        Write(Path.Combine(dir, "invoice.pdf"), "%PDF-1.4 fake");
        Check(WaitFor(() => service.Latest?.Name == "invoice.pdf"), "dropped file appears on top");
        Check(service.Items[0].SourceLabel == "Test" && service.Items[0].Kind == FileTrayKind.Pdf);

        string partial = Path.Combine(dir, "scan.pdf.crdownload");
        Write(partial, "downloading");
        Thread.Sleep(800);
        Check(service.Items.All(x => !x.Name.Contains("crdownload") && x.Name != "scan.pdf") && service.PendingCount == 0, "partial download ignored without disk access");
        File.Move(partial, Path.Combine(dir, "scan.pdf"));
        Check(WaitFor(() => service.Latest?.Name == "scan.pdf"), "appears after the browser renames it");

        // Firefox: empty placeholder, then the .part file replaces it.
        File.WriteAllBytes(Path.Combine(dir, "ff.png"), []);
        Write(Path.Combine(dir, "ff.png.part"), "pixels");
        Thread.Sleep(600);
        Check(service.Items.All(x => x.Name != "ff.png"), "empty placeholder not shown");
        File.Move(Path.Combine(dir, "ff.png.part"), Path.Combine(dir, "ff.png"), overwrite: true);
        Check(WaitFor(() => service.Latest?.Name == "ff.png"), "Firefox-style download appears after the rename");

        string locked = Path.Combine(dir, "voice-note.txt");
        using (var writer = new FileStream(locked, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            writer.Write(new byte[] { 65, 66, 67 }); writer.Flush();
            Thread.Sleep(1200);
            Check(service.Items.All(x => x.Name != "voice-note.txt") && service.PendingCount == 1, "not shown while another app is writing it");
        }
        Check(WaitFor(() => service.Latest?.Name == "voice-note.txt"), "shown once the writer lets go");
        Check(service.Items.Count == 3 && service.Items.All(x => x.Name != "old.txt"), "bounded to N");

        File.Delete(Path.Combine(dir, "scan.pdf"));
        Check(WaitFor(() => service.Items.All(x => x.Name != "scan.pdf")), "deleted file leaves the tray");
        Check(WaitFor(() => service.PendingCount == 0), "nothing pending when idle");
        Check(Volatile.Read(ref changes) >= 4);
    });
});
Test("File tray first scan: newest N per folder, skips hidden, empty, partial and unsupported files, dedupes folders", () =>
{
    Temporary(dir =>
    {
        string a = Directory.CreateDirectory(Path.Combine(dir, "Downloads")).FullName, b = Directory.CreateDirectory(Path.Combine(dir, "WhatsApp")).FullName;
        for (int i = 0; i < 5; i++) Write(Path.Combine(a, $"d{i}.pdf"), "x", DateTime.UtcNow.AddHours(-10 - i));
        Write(Path.Combine(b, "wa.jpeg"), "x", DateTime.UtcNow.AddHours(-1));
        Write(Path.Combine(a, "hidden.pdf"), "x"); File.SetAttributes(Path.Combine(a, "hidden.pdf"), FileAttributes.Hidden);
        File.WriteAllBytes(Path.Combine(a, "empty.pdf"), []);
        Write(Path.Combine(a, "tool.exe"), "x"); Write(Path.Combine(a, "big.pdf.crdownload"), "x");
        Directory.CreateDirectory(Path.Combine(a, "sub")); Write(Path.Combine(a, "sub", "nested.pdf"), "x");
        var settings = new FileTraySettings { MaxItems = 3, ExtraFolders = [new FileTrayFolder { Path = b, Label = "WhatsApp" }, new FileTrayFolder { Path = a + Path.DirectorySeparatorChar, Label = "again" }] };
        var folders = settings.ResolveFolders(a);
        Check(folders.Count == 2 && folders[0].Label == "Downloads" && folders[1].Label == "WhatsApp", "Downloads listed once even when added again");
        using var service = new FileTrayService(folders, settings.MaxItems);
        service.Start();
        Check(string.Join(",", service.Items.Select(x => x.Name)) == "wa.jpeg,d0.pdf,d1.pdf", string.Join(",", service.Items.Select(x => x.Name)));
        Check(service.Items[0].SourceLabel == "WhatsApp" && service.Problems.Count == 0);
        using var missing = new FileTrayService([new WatchedFolder(Path.Combine(dir, "gone"), "Gone")], 5);
        missing.Start();
        Check(missing.Items.Count == 0 && missing.Problems.Single().Contains("not found"), "a missing folder is reported, not thrown");
    });
});
Test("File tray settings: defaults, bounds, folder validation, fail closed without overwriting", () =>
{
    Temporary(dir =>
    {
        var store = new FileTraySettingsStore(dir);
        var loaded = store.Load();
        Check(!File.Exists(store.FilePath), "loading never writes");
        Check(loaded.Enabled && loaded.WatchDownloads && loaded.MaxItems == 20 && loaded.ExtraFolders.Count == 0);
        loaded.ExtraFolders.Add(new FileTrayFolder { Path = dir, Label = "WhatsApp" });
        loaded.RememberProjectFolder(Guid.NewGuid(), dir);
        store.Save(loaded);
        var round = store.Load();
        Check(round.ExtraFolders.Single().Label == "WhatsApp" && round.ProjectFolders.Count == 1);
        string json = File.ReadAllText(store.FilePath);
        Check(json.Contains("powerops-file-tray") && !json.Contains("\"Items\"") && !json.Contains(".pdf"), "settings only, no file list");
        foreach (int bad in new[] { 0, 101 }) { var s = FileTraySettingsStore.Copy(round); s.MaxItems = bad; Reject(() => FileTraySettings.Validate(s)); }
        var dup = FileTraySettingsStore.Copy(round); dup.ExtraFolders.Add(new FileTrayFolder { Path = dir.ToUpperInvariant() + Path.DirectorySeparatorChar }); Reject(() => FileTraySettings.Validate(dup));
        foreach (string bad in new[] { "relative\\folder", "https://example.com/files", "", "   " })
        { var s = FileTraySettingsStore.Copy(round); s.ExtraFolders = [new FileTrayFolder { Path = bad }]; Reject(() => FileTraySettings.Validate(s)); }
        var many = FileTraySettingsStore.Copy(round); many.ExtraFolders = Enumerable.Range(0, 9).Select(i => new FileTrayFolder { Path = Path.Combine(dir, "f" + i) }).ToList(); Reject(() => FileTraySettings.Validate(many));
        File.WriteAllText(store.FilePath, "{ \"Format\": \"powerops-file-tray\", \"SchemaVersion\": 2 }");
        byte[] future = File.ReadAllBytes(store.FilePath);
        Reject(() => store.Load());
        Reject(() => store.Save(new FileTraySettings()));
        Check(File.ReadAllBytes(store.FilePath).SequenceEqual(future), "future/corrupt bytes preserved");
    });
});
Test("File tray text: Word paragraphs, tabs and breaks; plain text encodings; truncation; no DTD", () =>
{
    Temporary(dir =>
    {
        string docx = Path.Combine(dir, "brief.docx");
        using (var zip = System.IO.Compression.ZipFile.Open(docx, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            writer.Write("<?xml version=\"1.0\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
                + "<w:p><w:r><w:t>Invoice</w:t><w:tab/><w:t xml:space=\"preserve\">42 </w:t></w:r></w:p>"
                + "<w:p><w:r><w:t>Line one</w:t><w:br/><w:t>Line two</w:t></w:r></w:p><w:p/><w:p/>"
                + "<w:p><w:r><w:t>Caf\u00e9 &amp; end</w:t></w:r></w:p></w:body></w:document>");
        string text = FileTrayText.ExtractDocx(docx);
        Check(text == "Invoice\t42\nLine one\nLine two\n\nCaf\u00e9 & end", text.Replace("\n", "\\n").Replace("\t", "\\t"));
        string dtd = Path.Combine(dir, "dtd.docx");
        using (var zip = System.IO.Compression.ZipFile.Open(dtd, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            writer.Write("<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY e \"boom\">]><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:t>&e;</w:t></w:p></w:document>");
        bool refused = false;
        try { FileTrayText.ExtractDocx(dtd); } catch (System.Xml.XmlException) { refused = true; }
        Check(refused, "DTDs are refused");
        string utf16 = Path.Combine(dir, "u16.txt"); File.WriteAllText(utf16, "h\u00e9llo  \r\n\r\n\r\nworld", System.Text.Encoding.Unicode);
        Check(FileTrayText.ReadPlainText(utf16) == "h\u00e9llo\n\nworld");
        string utf8 = Path.Combine(dir, "u8.txt"); File.WriteAllText(utf8, new string('x', 40));
        Check(FileTrayText.ReadPlainText(utf8, 10) == new string('x', 10) + "\n\n[... truncated by Power Ops]");
        Check(FileTrayText.HasText("Invoice number 4821") && !FileTrayText.HasText("  a b \n c  "), "scanned-PDF threshold");
    });
});
Test("Prompt Builder {{file}} and {{file_text}} use the chosen tray file; unknown values stay visible", () =>
{
    var modules = new List<PromptModuleEntry> { new() { Title = "Attach", Body = "Summarize {{file}}:\n{{file_text}}" } };
    Check(FileTrayPrompt.NeedsText(modules) && FileTrayPrompt.UsesFile(modules));
    Check(!FileTrayPrompt.NeedsText([new PromptModuleEntry { Body = "About {{ file }}" }]) && FileTrayPrompt.UsesFile([new PromptModuleEntry { Body = "About {{ file }}" }]));
    var entry = TrayEntry("contract.pdf", 1);
    string composed = PromptComposer.Compose(modules, variables: FileTrayPrompt.Variables(entry, "Party A pays Party B."));
    Check(composed == "Summarize contract.pdf:\nParty A pays Party B.", composed);
    Check(PromptComposer.Compose(modules, variables: FileTrayPrompt.Variables(null, null)).Contains("{{file}}"), "no tray file: placeholder kept");
    Check(FileTrayPrompt.Placeholder(FileTrayPrompt.TextVariable) == "{{file_text}}");
});
Test("File tray is a registered module, never exported, and older shell files still load", () =>
{
    Check(ModuleCatalog.Get("tray").Header == "File tray");
    var older = WorkspaceSessions.Defaults();
    foreach (var profile in older.Workspaces) profile.VisibleModules.Remove("tray");
    WorkspaceSessions.Validate(older);
    WorkspaceSessions.Active(older).VisibleModules.Add("tray"); WorkspaceSessions.AddTab(WorkspaceSessions.Active(older), "tray");
    WorkspaceSessions.Validate(older);
    string export = PortableExport.Create(new WorkspaceState(), older, ModuleCatalog.All.Select(x => x.Id), includeLocalDetails: true, includeShell: true);
    var tray = JsonNode.Parse(export)!["modules"]!["tray"]!;
    Check(tray.ToJsonString().Contains("never exported") && tray.AsObject().Count == 1, "only an observation");
    Check(!export.Contains("file-tray") && !export.Contains("ExtraFolders") && !export.Contains("ProjectFolders"), "tray settings are not in any export");
});
Test("File tray actions share the dispatcher and can go on the Shelf, the Ring and a global shortcut", () =>
{
    string[] tray = [QuickActionCatalog.TrayShow, QuickActionCatalog.TrayCopyLatest, QuickActionCatalog.TrayCopyFile, QuickActionCatalog.TrayCopyText,
        QuickActionCatalog.TrayCopyImage, QuickActionCatalog.TrayOpen, QuickActionCatalog.TrayReveal, QuickActionCatalog.TrayMove, QuickActionCatalog.TrayRemove];
    foreach (string id in tray)
    {
        var action = QuickActionCatalog.Get(id);
        Check(action.GlobalAllowed && action.Risk == ActionRisk.Safe && action.Category == "File tray", id);
    }
    Check(QuickActionCatalog.Get(QuickActionCatalog.TrayRemove).Description.Contains("never deleted"), "remove is not delete");
    QuickActionLayouts.ValidateLayout([QuickActionCatalog.TrayShow, QuickActionCatalog.TrayCopyLatest], ActionSurface.QuickShelf);
    QuickActionLayouts.ValidateLayout([QuickActionCatalog.TrayShow, QuickActionCatalog.TrayCopyText], ActionSurface.QuickRing);
    QuickActionLayouts.ValidateGlobalShortcuts([new ShortcutBinding { Gesture = "Ctrl+Alt+Shift+F", ActionId = QuickActionCatalog.TrayCopyLatest }]);
    Check(QuickActionLayouts.DefaultShelf.Contains(QuickActionCatalog.TrayShow) && QuickActionLayouts.DefaultShelf.Count <= QuickActionLayouts.MaxShelf, "the tray is a default Shelf entry");
    var runs = new Dictionary<string, int>(); var d = CountingDispatcher(runs, new());
    Check(d.Invoke(QuickActionCatalog.TrayCopyLatest, ActionSurface.GlobalShortcut).Succeeded && runs[QuickActionCatalog.TrayCopyLatest] == 1);
});
Test("Claude Control is off by default and adds nothing", () =>
{
    var settings = QuickActionLayouts.Defaults();
    QuickActionLayouts.Validate(settings);
    Check(settings.ClaudeControl is null && ClaudeControl.WebApp(settings) is null && QuickWebApps.All(settings).Count == 0);
});
Test("Claude Control becomes an embedded tab with a fixed identity and survives a save", () => Temporary(directory =>
{
    var store = new QuickActionSettingsStore(directory);
    var settings = QuickActionLayouts.Defaults();
    settings.WebApps.Add(new WebAppEntry { Name = "Claude Home", Url = "https://claude.ai/new" });
    settings.ClaudeControl = new ClaudeControlSettings { Url = "http://127.0.0.1:7430/home.html", StartCommand = @"C:\tools\start-control.cmd" };
    store.Save(settings);
    var loaded = store.Load();
    string id = QuickWebApps.ActionId(ClaudeControl.WebAppId);
    Check(QuickWebApps.All(loaded).Select(x => x.Name).SequenceEqual(["Claude Home", "Claude Control"]), "user web apps first, Claude Control last");
    var tab = QuickWebApps.Find(loaded, id)!;
    Check(tab.OpenMode == WebOpenMode.Embedded && tab.Url == "http://127.0.0.1:7430/home.html");
    Check(QuickActionLayouts.Describe(loaded, id).Label == "Claude Control");
    Check(loaded.WebApps.Count == 1, "the synthesized entry is never written into WebApps");
    Check(ClaudeControl.HealthUri(loaded.ClaudeControl!).AbsoluteUri == "http://127.0.0.1:7430/api/health");
    Check(ClaudeControl.StatusUri(loaded.ClaudeControl!).AbsoluteUri == "http://127.0.0.1:7430/api/status");
    var workspace = WorkspaceSessions.NewProfile("Claude");
    EmbeddedWebPolicy.ShowInWorkspace(workspace, id);
    Check(EmbeddedWebPolicy.LiveViews(workspace, loaded).Contains(id));
    QuickActionLayouts.SetWorkspaceShelf(loaded, workspace.Id, [id]);
    loaded.ClaudeControl = null;
    Reject(() => QuickActionLayouts.Validate(loaded)); // a shelf that still points at the removed tab fails closed
}));
Test("Claude Control settings reject remote addresses, odd start commands and the reserved id", () =>
{
    QuickActionSettings With(string url, string? start = null)
    {
        var s = QuickActionLayouts.Defaults();
        s.ClaudeControl = new ClaudeControlSettings { Url = url, StartCommand = start };
        return s;
    }
    QuickActionLayouts.Validate(With("http://localhost:7430/home.html"));
    QuickActionLayouts.Validate(With("http://[::1]:7430/"));
    Reject(() => QuickActionLayouts.Validate(With("https://example.com/home.html")));
    Reject(() => QuickActionLayouts.Validate(With("file:///C:/home.html")));
    Reject(() => QuickActionLayouts.Validate(With("http://user:pw@127.0.0.1:7430/")));
    Reject(() => QuickActionLayouts.Validate(With("http://127.0.0.1:7430/", @"tools\start-control.cmd")));
    Reject(() => QuickActionLayouts.Validate(With("http://127.0.0.1:7430/", @"C:\tools\start.ps1")));
    var clash = With("http://127.0.0.1:7430/");
    clash.WebApps.Add(new WebAppEntry { Id = ClaudeControl.WebAppId, Name = "Fake", Url = "https://example.com/" });
    Reject(() => QuickActionLayouts.Validate(clash));
});
Test("Claude Control status parses the documented contract defensively", () =>
{
    string json = """
    {"generated": "2026-09-25T18:36", "project": null,
     "plan": {"five_hour": 44, "week": 133, "sampled": "2026-09-25T18:10"},
     "sessions": {"open": 12, "running": 3, "needs_you": 1},
     "todo": 8, "open_prs": 2, "checks_to_act": -2,
     "urgent": [{"time": "2026-09-25T16:12", "level": "urgent", "source": "session", "project": "Mongoku", "text": "Waiting on you", "link": "claude://claude.ai/epitaxy/x"},
                {"text": "bad link", "link": "javascript:alert(1)"}, {"level": "no text"}, 7],
     "latest_audit": {"name": "2026-09-25-systeme", "status": "🟠"},
     "links": {"home": "https://claude.ai/artifact/x"}}
    """;
    var status = ClaudeControl.ParseStatus(json);
    Check(status.Generated == "2026-09-25T18:36" && status.Plan!.FiveHour == 44 && status.Plan.Week == 100 && status.Plan.Sampled == "2026-09-25T18:10");
    Check(status.Sessions == new ClaudeControlSessions(12, 3, 1) && status.Todo == 8 && status.OpenPrs == 2 && status.ChecksToAct == 0);
    Check(status.Urgent.Count == 2 && status.Urgent[0].Link!.Scheme == "claude" && status.Urgent[0].Project == "Mongoku" && status.Urgent[1].Link is null);
    Check(status.AuditName == "2026-09-25-systeme" && status.AuditStatus == "🟠");
    var empty = ClaudeControl.ParseStatus("{}");
    Check(empty.Plan is null && empty.Sessions is null && empty.Todo is null && empty.Urgent.Count == 0);
    Reject(() => ClaudeControl.ParseStatus("[1]"));
    Check(ClaudeControl.Friendly("2026-09-25T18:10", new DateTime(2026, 9, 25, 20, 0, 0)) == "18:10");
    Check(ClaudeControl.Friendly("2026-09-24T18:10", new DateTime(2026, 9, 25, 20, 0, 0)) == "2026-09-24 18:10");
});
Test("Claude Control is only read with GET and reports an absent server", () =>
{
    var control = new ClaudeControlSettings { Url = "http://127.0.0.1:7430/home.html" };
    var seen = new List<HttpRequestMessage>();
    HttpResponseMessage Answer(HttpRequestMessage request, string body, HttpStatusCode code = HttpStatusCode.OK)
    {
        seen.Add(request);
        return new HttpResponseMessage(code) { Content = new StringContent(body) };
    }
    Check(ClaudeControl.IsHealthyAsync(control, new FakeHandler(r => Answer(r, """{"ok": true, "time": "x"}"""))).GetAwaiter().GetResult());
    Check(!ClaudeControl.IsHealthyAsync(control, new FakeHandler(r => Answer(r, """{"ok": false}"""))).GetAwaiter().GetResult());
    Check(!ClaudeControl.IsHealthyAsync(control, new FakeHandler(r => Answer(r, "oops", HttpStatusCode.InternalServerError))).GetAwaiter().GetResult());
    var (status, error) = ClaudeControl.FetchStatusAsync(control, new FakeHandler(r => Answer(r, """{"todo": 3}"""))).GetAwaiter().GetResult();
    Check(status!.Todo == 3 && error is null);
    Check(seen.All(r => r.Method == HttpMethod.Get && r.Content is null && r.Headers.Authorization is null), "GET only, no body, no credential");
    Check(seen.Select(r => r.RequestUri!.AbsolutePath).Distinct().SequenceEqual(["/api/health", "/api/status"]));
    var (none, why) = ClaudeControl.FetchStatusAsync(control, new FakeHandler(_ => throw new HttpRequestException("refused"))).GetAwaiter().GetResult();
    Check(none is null && why!.Contains("not running"), why ?? "");
    Check(!ClaudeControl.IsHealthyAsync(control, new FakeHandler(_ => throw new HttpRequestException("refused"))).GetAwaiter().GetResult());
});
// ---- V2.4 Quick Ring sub-rings, tool actions, whole-screen capture ----
Test("Fresh settings use the two-level ring: Folders and Apps sub-rings, all valid", () =>
{
    var s = QuickActionLayouts.Defaults(); QuickActionLayouts.Validate(s);
    string folders = QuickRingGroups.ActionId(QuickRingGroups.FoldersId), apps = QuickRingGroups.ActionId(QuickRingGroups.AppsId);
    Check(s.Ring.Contains("capture.screen") && s.Ring.Contains(folders) && s.Ring.Contains(apps) && s.Ring.Count <= QuickActionLayouts.MaxRing);
    Check(QuickRingGroups.Find(s, folders)!.Items.SequenceEqual(new[] { "folder.downloads", "folder.desktop", "folder.explorer", "tray.show" }));
    var items = QuickRingModel.Build(s, Guid.NewGuid(), _ => null, _ => null);
    Check(items.Single(x => x.Id == folders).Label == "Folders ›", "sub-ring slots are marked");
    Check(QuickRingModel.BuildGroup(s, apps, _ => null, _ => null)!.Select(x => x.Id).SequenceEqual(new[] { "terminal.open" }));
    Check(QuickRingModel.BuildGroup(s, "group:" + Guid.NewGuid().ToString("N"), _ => null, _ => null) is null, "a missing group opens nothing");
});
Test("Older quick-actions files without sub-rings load unchanged", () => Temporary(dir =>
{
    var old = JsonNode.Parse(JsonSerializer.Serialize(new QuickActionSettings()))!.AsObject(); old.Remove("RingGroups");
    File.WriteAllText(Path.Combine(dir, "quick-actions.json"), old.ToJsonString());
    var s = new QuickActionSettingsStore(dir).Load();
    Check(s.RingGroups.Count == 0 && s.Ring.SequenceEqual(QuickActionLayouts.DefaultRing), "flat ring kept, nothing invented");
}));
Test("Sub-rings are validated: no nesting, no empty or oversized group, sane name and icon", () =>
{
    QuickActionSettings With(Action<RingGroup> change)
    {
        var s = QuickActionLayouts.Defaults(); change(s.RingGroups[0]); return s;
    }
    Reject(() => QuickActionLayouts.Validate(With(g => g.Items = [QuickRingGroups.ActionId(QuickRingGroups.AppsId)])));
    Reject(() => QuickActionLayouts.Validate(With(g => g.Items = [])));
    Reject(() => QuickActionLayouts.Validate(With(g => g.Items = ["folder.downloads", "folder.downloads"])));
    Reject(() => QuickActionLayouts.Validate(With(g => g.Items = QuickActionCatalog.All.Where(x => x.GlobalAllowed && x.Id != "ring.show").Take(9).Select(x => x.Id).ToList())));
    Reject(() => QuickActionLayouts.Validate(With(g => g.Items = ["tab.next"])));
    Reject(() => QuickActionLayouts.Validate(With(g => g.Name = " ")));
    Reject(() => QuickActionLayouts.Validate(With(g => g.Name = new string('x', 41))));
    Reject(() => QuickActionLayouts.Validate(With(g => g.Glyph = "0041")));
    Reject(() => QuickActionLayouts.Validate(With(g => g.Id = QuickRingGroups.AppsId)));
    var dangling = QuickActionLayouts.Defaults(); dangling.Ring.Add("group:" + Guid.NewGuid().ToString("N"));
    try { QuickActionLayouts.Validate(dangling); throw new Exception("accepted"); }
    catch (InvalidDataException ex) { Check(ex.Message.Contains("group that no longer exists"), ex.Message); }
});
Test("A sub-ring can be bound to a shortcut or the Shelf, and deleting it cleans every reference", () =>
{
    var s = QuickActionLayouts.Defaults(); var work = Guid.NewGuid();
    string apps = QuickRingGroups.ActionId(QuickRingGroups.AppsId);
    s.Shelf.Add(apps); s.GlobalShortcuts.Add(new ShortcutBinding { Gesture = "Ctrl+Alt+Shift+A", ActionId = apps });
    QuickActionLayouts.SetWorkspaceRing(s, work, [apps]);
    QuickActionLayouts.Validate(s);
    QuickRingGroups.Remove(s, QuickRingGroups.AppsId);
    QuickActionLayouts.Validate(s);
    Check(!s.Ring.Contains(apps) && !s.Shelf.Contains(apps) && s.GlobalShortcuts.All(x => x.ActionId != apps) && s.WorkspaceOverrides.Count == 0);
    var only = QuickActionLayouts.Defaults(); only.Ring = [QuickRingGroups.ActionId(QuickRingGroups.FoldersId)];
    QuickRingGroups.Remove(only, QuickRingGroups.FoldersId);
    Check(only.Ring.SequenceEqual(QuickActionLayouts.DefaultRing), "an emptied ring falls back to the flat default");
});
Test("Removing a web app also removes it from sub-rings; an emptied sub-ring disappears", () =>
{
    var s = QuickActionLayouts.Defaults(); var app = new WebAppEntry { Name = "Gmail", Url = "https://mail.google.com/" }; s.WebApps.Add(app);
    QuickRingGroups.ApplySuggested(s); QuickActionLayouts.Validate(s);
    string web = QuickWebApps.ActionId(app.Id);
    Check(QuickRingGroups.Find(s, QuickRingGroups.ActionId(QuickRingGroups.AppsId))!.Items.SequenceEqual(new[] { "terminal.open", web }), "suggested Apps holds web apps");
    var mail = new RingGroup { Id = Guid.NewGuid(), Name = "Mail", Items = [web] };
    s.RingGroups.Add(mail); s.Ring.Add(QuickRingGroups.ActionId(mail.Id));
    QuickActionLayouts.Validate(s);
    QuickWebApps.RemoveWebApp(s, app.Id); QuickActionLayouts.Validate(s);
    Check(s.RingGroups.All(g => !g.Items.Contains(web)) && s.RingGroups.All(g => g.Id != mail.Id) && !s.Ring.Contains(QuickRingGroups.ActionId(mail.Id)));
});
Test("Suggested layout is idempotent and keeps the user's other sub-rings", () =>
{
    var s = QuickActionLayouts.Defaults(); var mine = new RingGroup { Id = Guid.NewGuid(), Name = "Projects", Items = ["workspace.next"] };
    s.RingGroups.Add(mine);
    QuickRingGroups.ApplySuggested(s); QuickRingGroups.ApplySuggested(s); QuickActionLayouts.Validate(s);
    Check(s.RingGroups.Count == 3 && s.RingGroups.Count(x => x.Id == mine.Id) == 1, "own group kept once");
});
Test("Tool Launcher entries become tool: actions; a removed tool is shown, never breaks the file", () =>
{
    var toolId = Guid.NewGuid(); string id = QuickToolActions.ActionId(toolId);
    Check(QuickToolActions.ToolId(id) == toolId && QuickToolActions.ToolId("tool:nope") is null);
    var s = QuickActionLayouts.Defaults(); s.RingGroups[1].Items.Add(id); s.Shelf.Add(id);
    QuickActionLayouts.Validate(s);
    Check(QuickActionLayouts.Describe(s, id).Label == "Tool (not found)");
    var code = QuickToolActions.Definition(toolId, "VS Code", "code");
    Check(code.Label == "VS Code" && code.Glyph == "E943" && code.GlobalAllowed && code.Risk == ActionRisk.Safe);
    var items = QuickRingModel.BuildGroup(s, QuickRingGroups.ActionId(QuickRingGroups.AppsId), _ => null, _ => null, x => x == id ? code : null)!;
    Check(items.Any(x => x.Label == "VS Code"), "the live tool name wins over the placeholder");
    var bad = QuickActionLayouts.Defaults(); bad.Ring.Add("tool:not-a-guid"); Reject(() => QuickActionLayouts.Validate(bad));
    var dispatcher = new QuickActionDispatcher(); var ran = 0;
    dispatcher.ReplaceDynamic(QuickToolActions.Prefix, [(code, new QuickActionHandler(() => ran++))]);
    Check(dispatcher.Invoke(id, ActionSurface.QuickRing).Succeeded && ran == 1);
});
Test("Whole-screen capture and Desktop folder are safe out-of-app catalog actions", () =>
{
    foreach (string id in new[] { "capture.screen", "folder.desktop" })
    {
        var action = QuickActionCatalog.Get(id);
        Check(action.GlobalAllowed && action.Risk == ActionRisk.Safe, id);
    }
    Check(QuickActionCatalog.All.Select(x => x.Id).Distinct().Count() == QuickActionCatalog.All.Count, "IDs stay unique");
});
Test("MX guide: Sense Panel opens the ring, moves are Windows commands, Back/Forward need no setup", () =>
{
    var s = QuickActionLayouts.Defaults(); InteractionGuide.AddRecommended(s, InteractionMode.Hybrid, _ => true);
    var steps = InteractionGuide.MxMasterSteps(s, null);
    Check(steps.Single(x => x.Title.Contains("Quick Ring")).Detail.Contains("Sense Panel"));
    Check(steps.Any(x => x.CopyText == "Win+Tab") && steps.Any(x => x.CopyText == "Win+D"));
    Check(steps.Single(x => x.Title.StartsWith("Back")).Detail.StartsWith("Nothing to set up"), "no Options+ app-specific setting");
});
int failures = 0;
foreach (var test in tests)
{
    try { test.Test(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"Workspace tests: {tests.Count - failures}/{tests.Count} passed; {failures} failed.");
return failures == 0 ? 0 : 1;

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
}

/// <summary>Chunked-style body: no Content-Length, so only the streaming size cap can stop it.</summary>
sealed class UnknownLengthStream(long length) : Stream
{
    private long _position;
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = (int)Math.Min(count, length - _position); Array.Fill(buffer, (byte)'x', offset, n); _position += n; return n;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
sealed class MemoryVault : ISecretVault
{
    private readonly Dictionary<string, string> _items = new(StringComparer.OrdinalIgnoreCase);
    public bool Exists(string target) => _items.ContainsKey(target);
    public string? ReadSecret(string target) => _items.GetValueOrDefault(target);
    public void WriteSecret(string target, string secret, string userName, string comment) { SecretValues.Validate(secret); _items[target] = secret; }
    public bool Delete(string target) => _items.Remove(target);
    public IReadOnlyList<string> Targets(string prefix) => _items.Keys.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
}
