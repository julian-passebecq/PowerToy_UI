using System.Text.Json;
using System.Text.Json.Nodes;
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
int failures = 0;
foreach (var test in tests)
{
    try { test.Test(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"Workspace tests: {tests.Count - failures}/{tests.Count} passed; {failures} failed.");
return failures == 0 ? 0 : 1;
