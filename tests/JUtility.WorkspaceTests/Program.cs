using System.Text.Json;
using System.Text.Json.Nodes;
using JUtility.Core.Actions;
using System.Net;
using JUtility.Core.Models;
using JUtility.Core.Reports;
using JUtility.Core.Workspaces;
using JUtility.Core.Capture;
using JUtility.Core.Credentials;

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
    Check(QuickRingModel.Build(s, Guid.NewGuid(), _ => null, _ => null).Select(x => x.Id).SequenceEqual(QuickActionLayouts.DefaultRing), "inherits default");
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
