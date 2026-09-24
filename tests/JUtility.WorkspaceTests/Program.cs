using System.Text.Json;
using System.Text.Json.Nodes;
using JUtility.Core.Actions;
using JUtility.Core.Models;
using JUtility.Core.Workspaces;

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
int failures = 0;
foreach (var test in tests)
{
    try { test.Test(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"Workspace tests: {tests.Count - failures}/{tests.Count} passed; {failures} failed.");
return failures == 0 ? 0 : 1;
