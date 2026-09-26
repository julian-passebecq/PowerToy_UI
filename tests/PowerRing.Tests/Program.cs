using System.Text.Json.Nodes;
using PowerRing.Core;

// Package-free regression tests for Power Ring's config, validation, layout, navigation, clipboard history and notes.
// "--validate <file>..." only checks ring.json-style files and prints the first error of each.
if (args.Length > 1 && args[0] == "--validate")
{
    int bad = 0;
    foreach (string file in args.Skip(1))
    {
        try { RingConfigs.Parse(File.ReadAllText(file)); Console.WriteLine("OK   " + file); }
        catch (RingConfigException ex) { bad++; Console.WriteLine("FAIL " + file + ": " + ex.Message); }
    }
    return bad == 0 ? 0 : 1;
}

var tests = new List<(string Name, Action Test)>();
void Test(string name, Action test) => tests.Add((name, test));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
string Rejected(Func<object?> action)
{
    try { action(); } catch (Exception ex) when (ex is RingConfigException or FormatException) { return ex.Message; }
    throw new Exception("Expected a rejection");
}
string WithChange(Action<JsonObject> change)
{
    var node = JsonNode.Parse(RingDefaults.Json, documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!.AsObject();
    change(node);
    return node.ToJsonString();
}
JsonObject Item(JsonObject root, int profile, int index) => root["profiles"]![profile]!["items"]![index]!.AsObject();
string One(string item) => $$"""{ "version": 1, "profiles": [ { "id": "a", "name": "A", "items": [ {{item}} ] } ] }""";
void Temp(Action<string> test)
{
    string dir = Path.Combine(Path.GetTempPath(), "PowerRingTests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try { test(dir); } finally { Directory.Delete(dir, true); }
}

Test("Default ring.json: Home, Code, Manage rings and a Clipboard board, all valid", () =>
{
    RingConfig config = RingDefaults.Create();
    Check(config.Profiles.Select(x => x.Id).SequenceEqual(new[] { "home", "code", "manage", "clip" }));
    Check(config.Profiles.Where(p => !p.IsPanel).All(p => p.Items.Count is >= 1 and <= RingConfigs.MaxItems));
    Check(config.Profiles.Single(p => p.Id == "clip").IsBoard);
    RingItem code = config.Profiles[0].Items.Single(x => x.Label == "VS Code");
    Check(!code.IsGroup && code.Items!.Count == 1, "an action with children (shown behind it)");
    Check(RingDefaults.Presets().Count == 4);
});
Test("Comments and trailing commas are accepted (hand and AI edits)", () =>
{
    RingConfig config = RingConfigs.Parse("""
        { // comment
          "version": 1, "hotkey": "Ctrl+Alt+Space",
          "profiles": [ { "id": "a", "name": "A", "items": [ { "label": "X", "action": "screenshot" }, ], }, ],
        }
        """);
    Check(config.Profiles[0].Items[0].Label == "X" && config.Appearance.SlotSize == 42 && config.Appearance.RingSize is null, "appearance defaults when omitted");
});
Test("Round trip keeps every field", () =>
{
    RingConfig config = RingDefaults.Create();
    config.Appearance.Accent = "#FF112233"; config.Appearance.Scale = 1.2; config.Profiles[0].Items[0].Color = "#22C55E"; config.Profiles[1].Enabled = false;
    RingConfig again = RingConfigs.Parse(RingConfigs.Serialize(config));
    Check(again.Appearance.Accent == "#FF112233" && again.Appearance.Scale == 1.2 && again.Profiles[0].Items[0].Color == "#22C55E" && !again.Profiles[1].Enabled);
    Check(again.Profiles[3].Tables!.Count == 4);
});
Test("Bad JSON reports the line", () =>
{
    string message = Rejected(() => RingConfigs.Parse("{\n  \"version\": 1,\n  \"profiles\": [ oops ]\n}"));
    Check(message.Contains("line 3"), message);
});
Test("Errors name the exact field path", () =>
{
    string message = Rejected(() => RingConfigs.Parse(WithChange(r => Item(r, 0, 1)["action"] = "launch")));
    Check(message.StartsWith("profiles[0].items[1].action") && message.Contains("screen-to-clipboard"), message);
    message = Rejected(() => RingConfigs.Parse(WithChange(r => Item(r, 0, 2)["target"] = "javascript:alert(1)")));
    Check(message.StartsWith("profiles[0].items[2].target"), message);
    message = Rejected(() => RingConfigs.Parse(WithChange(r => r["appearance"]!["scale"] = 9)));
    Check(message.StartsWith("appearance.scale") && message.Contains("between"), message);
    message = Rejected(() => RingConfigs.Parse(WithChange(r => Item(r, 0, 0)["icon"] = "rocket-ship")));
    Check(message.Contains("icon") && message.Contains("code"), "unknown icon lists the names: " + message);
});
Test("Limits: profiles, 10 items, 4 children, 3 circles, ids, colours, hotkey, one enabled workspace", () =>
{
    Rejected(() => RingConfigs.Parse(WithChange(r => r["profiles"] = new JsonArray())));
    Rejected(() => RingConfigs.Parse(WithChange(r => { var items = r["profiles"]![0]!["items"]!.AsArray(); items.Add(JsonNode.Parse("""{ "label": "10", "action": "screenshot" }""")); items.Add(JsonNode.Parse("""{ "label": "11", "action": "screenshot" }""")); })));
    Rejected(() => RingConfigs.Parse(WithChange(r => r["profiles"]![1]!["id"] = "home")));
    Rejected(() => RingConfigs.Parse(WithChange(r => r["profiles"]![0]!["accent"] = "blue")));
    Rejected(() => RingConfigs.Parse(WithChange(r => r["hotkey"] = "R")));
    Rejected(() => RingConfigs.Parse(WithChange(r => r["startProfile"] = "nope")));
    Rejected(() => RingConfigs.Parse(WithChange(r => r["version"] = 2)));
    Rejected(() => RingConfigs.Parse(WithChange(r => { foreach (JsonNode? p in r["profiles"]!.AsArray()) p!["enabled"] = false; })));
    string tooMany = Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "screenshot", "items": [ {"label":"1","action":"screenshot"},{"label":"2","action":"screenshot"},{"label":"3","action":"screenshot"},{"label":"4","action":"screenshot"},{"label":"5","action":"screenshot"} ] }""")));
    Check(tooMany.Contains("1 to 4 children"), tooMany);
    RingConfigs.Parse(One("""{ "label": "a", "action": "screenshot", "items": [ { "label": "b", "action": "screenshot", "items": [ { "label": "c", "action": "screenshot" } ] } ] }"""));
    string deep = Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "screenshot", "items": [ { "label": "b", "action": "screenshot", "items": [ { "label": "c", "action": "screenshot", "items": [ { "label": "d", "action": "screenshot" } ] } ] } ] }""")));
    Check(deep.Contains("at most 3 circles"), deep);
    Rejected(() => RingConfigs.Parse(WithChange(r => Item(r, 0, 2)["target"] = "https://user:pw@mail.example/")));
});
Test("Every action validates its target", () =>
{
    RingConfigs.Parse(One("""{ "label": "a", "action": "run", "target": "code", "args": "." }"""));
    RingConfigs.Parse(One("""{ "label": "a", "action": "folder", "target": "downloads" }"""));
    RingConfigs.Parse(One("""{ "label": "a", "action": "keys", "target": "Win+Tab" }"""));
    RingConfigs.Parse(One("""{ "label": "a", "action": "text", "target": "hello" }"""));
    RingConfigs.Parse(One("""{ "label": "a", "action": "url", "target": "mailto:me@example.com" }"""));
    RingConfigs.Parse(One("""{ "label": "a", "action": "url", "target": "ms-settings:display" }"""));
    RingConfigs.Parse(One("""{ "label": "a", "action": "powerops" }"""));
    RingConfigs.Parse(One("""{ "label": "a", "action": "ring-settings", "target": "reload" }"""));
    RingConfigs.Parse(One("""{ "label": "a", "icon": "C:\\Tools\\app.exe", "action": "run", "target": "C:\\Tools\\app.exe" }"""));
    RingConfigs.Parse(One("""{ "label": "a", "icon": "%USERPROFILE%\\Downloads", "action": "folder", "target": "downloads" }"""));
    Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "run" }""")));
    Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "keys", "target": "Win+Nope" }""")));
    Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "text" }""")));
    Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "url", "target": "www.example.com" }""")));
    Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "ring-settings", "target": "delete" }""")));
    Rejected(() => RingConfigs.Parse(One("""{ "label": "", "action": "screenshot" }""")));
});
Test("Night mode actions: power mode, close-apps list with protected names, screen delay", () =>
{
    RingConfigs.Parse(One("""{ "label": "a", "action": "power-mode", "target": "efficiency" }"""));
    RingConfigs.Parse(One("""{ "label": "a", "action": "close-apps", "target": "chrome, msedge.exe; opera" }"""));
    RingConfigs.Parse(One("""{ "label": "a", "action": "screen-to-clipboard", "delay": 3 }"""));
    Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "power-mode", "target": "turbo" }""")));
    Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "close-apps" }""")));
    string claude = Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "close-apps", "target": "chrome, Claude.exe" }""")));
    Check(claude.Contains("never closed"), claude);
    Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "close-apps", "target": "explorer" }""")));
    Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "screen-to-clipboard", "delay": 30 }""")));
    Rejected(() => RingConfigs.Parse(One("""{ "label": "a", "action": "screenshot", "delay": 3 }""")));
    Check(RingActions.ProcessNames("chrome, msedge.exe; ,opera, chrome").SequenceEqual(new[] { "chrome", "msedge", "opera" }));
});
Test("Boards and galleries are validated", () =>
{
    string Board(string tables) => $$"""{ "version": 1, "profiles": [ { "id": "a", "name": "A", "items": [ { "label": "x", "action": "screenshot" } ] }, { "id": "b", "name": "B", "kind": "board", "tables": [ {{tables}} ] } ] }""";
    RingConfigs.Parse(Board("""{ "title": "T", "kind": "clipboard", "keep": 10 }, { "title": "L", "kind": "links", "columns": 3, "items": [ { "label": "g", "action": "url", "target": "https://github.com/" } ] }"""));
    Rejected(() => RingConfigs.Parse(Board("""{ "title": "T", "kind": "clipboard", "columns": 4 }""")));
    Rejected(() => RingConfigs.Parse(Board("""{ "title": "T", "kind": "history" }""")));
    Rejected(() => RingConfigs.Parse(Board("""{ "title": "T", "kind": "links" }""")));
    Rejected(() => RingConfigs.Parse(Board("""{ "title": "T", "kind": "notes", "items": [ { "label": "g", "action": "screenshot" } ] }""")));
    string Gallery(string sections) => $$"""{ "version": 1, "profiles": [ { "id": "g", "name": "G", "kind": "gallery", "sections": [ {{sections}} ] } ] }""";
    RingConfig gallery = RingConfigs.Parse(Gallery("""{ "title": "Cloud", "icon": "cloud", "items": [ { "label": "Azure", "action": "url", "target": "https://portal.azure.com/" } ] }"""));
    Check(gallery.Profiles[0].IsGallery && gallery.Profiles[0].IsPanel);
    Rejected(() => RingConfigs.Parse(Gallery("""{ "title": "", "items": [ { "label": "x", "action": "screenshot" } ] }""")));
    Rejected(() => RingConfigs.Parse(Gallery("""{ "title": "S", "items": [ { "label": "x", "action": "screenshot", "items": [ { "label": "y", "action": "screenshot" } ] } ] }""")));
    Rejected(() => RingConfigs.Parse("""{ "version": 1, "profiles": [ { "id": "g", "name": "G", "kind": "grid", "items": [ { "label": "x", "action": "screenshot" } ] } ] }"""));
});
Test("Key combos parse to virtual keys in press order", () =>
{
    KeyCombo tab = KeyCombo.Parse("Win+Tab");
    Check(tab.VirtualKey == 0x09 && tab.ModifierKeys().SequenceEqual(new[] { 0x5B }) && tab.ToString() == "Win+Tab");
    KeyCombo hot = KeyCombo.ParseHotkey("shift+alt+ctrl+r");
    Check(hot.ToString() == "Ctrl+Alt+Shift+R" && hot.VirtualKey == 'R' && hot.ModifierKeys().SequenceEqual(new[] { 0x11, 0x12, 0x10 }));
    Check(KeyCombo.Parse("Ctrl+Shift+Esc").VirtualKey == 0x1B && KeyCombo.Parse("F12").VirtualKey == 0x7B && KeyCombo.Parse("Win+Shift+R").VirtualKey == 'R');
    Rejected(() => KeyCombo.Parse("Ctrl+Ctrl+A"));
    Rejected(() => KeyCombo.Parse("A+B"));
    Rejected(() => KeyCombo.ParseHotkey("Shift+R"));
});
Test("Navigator: enabled workspaces, groups, back and breadcrumb", () =>
{
    RingConfig config = RingConfigs.Parse("""
        { "version": 1, "profiles": [
          { "id": "a", "name": "A", "items": [ { "label": "Folders", "items": [ { "label": "Sub", "items": [ { "label": "Leaf", "action": "screenshot" } ] } ] }, { "label": "Shot", "action": "screenshot" } ] },
          { "id": "b", "name": "B", "enabled": false, "items": [ { "label": "x", "action": "screenshot" } ] },
          { "id": "c", "name": "C", "items": [ { "label": "y", "action": "screenshot" } ] } ] }
        """);
    var nav = new RingNavigator(config);
    Check(nav.Profiles.Select(x => x.Id).SequenceEqual(new[] { "a", "c" }), "hidden workspace skipped");
    RingItem folders = nav.Items[0];
    nav.Open(folders);
    Check(nav.Depth == 2 && nav.Breadcrumb == "A › Folders");
    nav.Open(nav.Items.Single(x => x.Label == "Sub"));
    Check(nav.Breadcrumb == "A › Folders › Sub" && nav.Items.Single().Label == "Leaf");
    Check(nav.Back() && nav.Back() && !nav.Back() && nav.AtRoot);
    nav.Open(folders.Items![0]);   // a group shown behind the first circle can be opened directly
    Check(nav.Breadcrumb == "A › Sub");
    bool refused = false;
    nav.Home();
    try { nav.Open(nav.Items.Single(x => x.Label == "Shot")); } catch (InvalidOperationException) { refused = true; }
    Check(refused, "only groups open");
    nav.CycleProfile(1); Check(nav.Profile.Id == "c" && nav.AtRoot);
    nav.CycleProfile(1); Check(nav.Profile.Id == "a", "wraps over enabled workspaces only");
});
Test("Navigator reload keeps the profile when it still exists", () =>
{
    var nav = new RingNavigator(RingDefaults.Create());
    nav.SetProfile(2);
    RingConfig next = RingDefaults.Create(); next.Profiles.RemoveAt(0);
    nav.Reload(next); Check(nav.Profile.Id == "manage");
    RingConfig hidden = RingDefaults.Create(); hidden.Profiles[2].Enabled = false;
    nav.Reload(hidden); Check(nav.Profile.Id == "home");
});
Test("Layout: every default ring fits, nothing overlaps, children sit behind their parent", () =>
{
    foreach (RingProfile profile in RingDefaults.Create().Profiles.Where(p => !p.IsPanel))
    {
        var a = new RingAppearance();
        RingLayout.Result layout = RingLayout.Compute(profile.Items, a);
        Check(!RingLayout.Overlaps(layout.Nodes, 0), profile.Id + ": overlap");
        Check(layout.Nodes.All(n => Math.Sqrt(n.X * n.X + n.Y * n.Y) + n.Size / 2 <= layout.DiscSize / 2), profile.Id + ": outside the disc");
        Check(layout.Nodes.Where(n => n.Level == 1).All(n => Math.Sqrt(n.X * n.X + n.Y * n.Y) - n.Size / 2 >= layout.CenterSize / 2), profile.Id + ": over the centre");
        foreach (RingNode child in layout.Nodes.Where(n => n.Parent is not null))
        {
            double parentAngle = child.Parent!.Angle, delta = Math.Abs(Math.IEEERemainder(child.Angle - parentAngle, 2 * Math.PI));
            Check(delta < Math.PI / 3, $"{profile.Id}: {child.Item.Label} strays from {child.Parent.Item.Label}");
            Check(Math.Sqrt(child.X * child.X + child.Y * child.Y) > Math.Sqrt(child.Parent.X * child.Parent.X + child.Parent.Y * child.Parent.Y), "behind = further out");
        }
        Check(layout.Nodes.Count(n => n.Level == 1) == profile.Items.Count);
    }
    var first = RingNavigator.SlotOffset(0, 8, 100);
    Check(Math.Abs(first.X) < 1e-6 && first.Y == -100, "slot 1 at the top");
});
Test("Layout settings: scale grows everything, hiding circles removes them, a full 10 x 4 ring still fits", () =>
{
    List<RingItem> items = RingDefaults.Create().Profiles[0].Items;
    var normal = RingLayout.Compute(items, new RingAppearance());
    var big = RingLayout.Compute(items, new RingAppearance { Scale = 1.5 });
    Check(big.DiscSize > normal.DiscSize * 1.3);
    Check(RingLayout.Compute(items, new RingAppearance { ShowSatellites = false }).Nodes.All(n => n.Level == 1));
    var full = Enumerable.Range(0, 10).Select(i => new RingItem { Label = "p" + i, Action = "screenshot", Items = Enumerable.Range(0, 4).Select(c => new RingItem { Label = "c" + c, Action = "screenshot", Items = [new RingItem { Label = "g", Action = "screenshot" }, new RingItem { Label = "h", Action = "screenshot" }] }).ToList() }).ToList();
    var dense = RingLayout.Compute(full, new RingAppearance());
    Check(!RingLayout.Overlaps(dense.Nodes, 0) && dense.Nodes.Count == 10 + 40 + 80, "dense ring without overlap");
    Check(RingLayout.Compute(full, new RingAppearance { ShowThirdRing = false }).Nodes.Count == 50);
});
Test("Clipboard history: newest first, deduplicated, bounded, memory only", () =>
{
    var history = new ClipHistory(3);
    var t = DateTimeOffset.Now;
    Check(history.Add("a", t) && history.Add("b", t) && history.Add("c", t) && history.Add("a", t));
    Check(history.Items.Select(x => x.Text).SequenceEqual(new[] { "a", "c", "b" }), "copying again moves to the top");
    history.Add("d", t);
    Check(history.Items.Count == 3 && history.Items[^1].Text == "c");
    Check(!history.Add("   ", t) && !history.Add(new string('x', ClipHistory.MaxTextLength + 1), t));
    Check(ClipHistory.Preview("one\r\ntwo\tthree") == "one two three" && ClipHistory.Preview(new string('y', 300), 10).Length == 10);
    Check(ClipHistory.Age(t.AddMinutes(-5), t) == "5 min" && ClipHistory.Age(t, t) == "now");
});
Test("Notes: saved next to ring.json, newest first, deduplicated; a corrupt file is kept aside", () => Temp(dir =>
{
    var notes = new RingNotesStore(dir);
    var t = DateTimeOffset.Now;
    notes.Add("first", t); notes.Add("second", t.AddSeconds(1)); notes.Add("first", t.AddSeconds(2));
    List<RingNote> all = notes.Load();
    Check(all.Select(x => x.Text).SequenceEqual(new[] { "first", "second" }));
    notes.Remove(all[1]);
    Check(notes.Load().Single().Text == "first");
    File.WriteAllText(notes.FilePath, "{ broken");
    Check(notes.Load().Count == 0);
    notes.Add("after", t);
    Check(Directory.GetFiles(dir, "notes.json.broken-*").Length == 1 && notes.Load().Single().Text == "after");
}));
Test("Icons: names, codes, files and defaults by action", () =>
{
    Check(RingIcons.Glyph("code") == "\uE943" && RingIcons.Glyph("E715") == "\uE715" && RingIcons.Glyph("x.png") is null);
    Check(RingIcons.IsFile(@"C:\apps\Code.exe") && RingIcons.IsFile(@"%USERPROFILE%\Downloads") && !RingIcons.IsFile("code"));
    Check(RingIcons.Default(new RingItem { Label = "a", Action = "run", Target = "code" }) == "code");
    Check(RingIcons.Default(new RingItem { Label = "a", Action = "url", Target = "https://mail.google.com/" }) == "mail");
    Check(RingIcons.Default(new RingItem { Label = "a", Items = [] }) == "folder");
});
Test("Schema lists every action, icon name and appearance field", () =>
{
    string schema = RingConfigStore.Resource(RingConfigStore.SchemaFileName);
    var root = JsonNode.Parse(schema)!;
    var actions = root["definitions"]!["item"]!["properties"]!["action"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>());
    Check(actions.ToHashSet().SetEquals(RingActions.All), "schema actions = code actions");
    var icons = root["definitions"]!["icon"]!["anyOf"]![0]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet();
    Check(icons.SetEquals(RingIcons.Names), "schema icon names = code icon names");
    var fields = root["definitions"]!["appearance"]!["properties"]!.AsObject().Select(x => x.Key).ToHashSet();
    var code = typeof(RingAppearance).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).ToHashSet();
    Check(fields.SetEquals(code), "schema appearance = code appearance: " + string.Join(",", code.Except(fields).Concat(fields.Except(code))));
    var profileFields = root["definitions"]!["profile"]!["properties"]!.AsObject().Select(x => x.Key).ToHashSet();
    Check(new[] { "kind", "enabled", "tables", "sections" }.All(profileFields.Contains), "schema profile fields");
    string guide = RingConfigStore.Resource(RingConfigStore.GuideFileName);
    Check(RingActions.All.All(a => guide.Contains($"`{a}`")), "guide documents every action");
});
Test("Store: first start writes ring.json, schema and guide; tray saves and layouts keep ring.json.bak", () => Temp(dir =>
{
    var store = new RingConfigStore(Path.Combine(dir, "ring.json"));
    store.EnsureFiles();
    Check(File.Exists(store.FilePath) && File.Exists(Path.Combine(dir, "ring.schema.json")) && File.Exists(Path.Combine(dir, "RING_CONFIG.md")));
    RingConfig config = store.Load();
    Check(config.Profiles.Count == 4);
    config.Profiles[1].Enabled = false;
    store.Save(config);
    Check(!store.Load().Profiles[1].Enabled && File.ReadAllText(store.FilePath + ".bak").Contains("// Power Ring settings"), "comments kept in the backup");
    Directory.CreateDirectory(store.LayoutsDirectory);
    string layout = Path.Combine(store.LayoutsDirectory, "Mine.json");
    File.WriteAllText(layout, "// mine\n" + One("""{ "label": "only", "action": "screenshot" }""").Replace("\"version\": 1,", "\"$schema\": \"../ring.schema.json\", \"version\": 1,"));
    Check(store.Layouts().Single() == layout);
    store.ApplyLayout(layout);
    Check(store.Load().Profiles.Single().Items.Single().Label == "only" && File.ReadAllText(store.FilePath).Contains("\"./ring.schema.json\""));
    File.WriteAllText(layout, "{ broken");
    Rejected(() => { store.ApplyLayout(layout); return null; });
    Check(store.Load().Profiles.Single().Items.Single().Label == "only", "a broken layout never replaces ring.json");
    File.WriteAllText(store.FilePath, "{ broken");
    store.EnsureFiles();
    Check(File.ReadAllText(store.FilePath) == "{ broken", "user file kept");
}));
Test("Settings fields: fluent theme, rim sizes, satellite gap, quick buttons, ring-settings open", () =>
{
    RingConfig config = RingConfigs.Parse(WithChange(root =>
    {
        root["appearance"] = JsonNode.Parse("""{ "theme": "fluent", "workspaceButtonSize": 30, "rimIconSize": 14, "satelliteGap": 2, "workspaceButtons": 3 }""");
        root["quickButtons"] = JsonNode.Parse("""[ { "label": "ChatGPT", "action": "url", "target": "https://chatgpt.com/" }, { "label": "Réglages", "action": "ring-settings", "target": "open" } ]""");
    }));
    Check(config.Appearance.Theme == "fluent" && config.Appearance.WorkspaceButtonSize == 30 && config.Appearance.RimIconSize == 14 && config.Appearance.SatelliteGap == 2);
    Check(config.QuickButtons!.Count == 2 && RingConfigs.Parse(RingConfigs.Serialize(config)).QuickButtons![0].Label == "ChatGPT", "round trip");
    Check(RingConfigs.Parse(RingDefaults.Json).QuickButtons is null && !RingConfigs.Serialize(RingConfigs.Parse(RingDefaults.Json)).Contains("satelliteGap"), "absent by default");
    string Bad(string appearance) => Rejected(() => RingConfigs.Parse(WithChange(r => r["appearance"] = JsonNode.Parse(appearance))));
    Check(Bad("""{ "theme": "neon" }""").Contains("fluent"));
    Check(Bad("""{ "workspaceButtonSize": 90 }""").StartsWith("appearance.workspaceButtonSize"));
    Check(Bad("""{ "workspaceButtonSize": 10 }""").StartsWith("appearance.workspaceButtonSize"));
    Check(Bad("""{ "rimIconSize": 60 }""").StartsWith("appearance.rimIconSize"));
    Check(Bad("""{ "satelliteGap": -1 }""").StartsWith("appearance.satelliteGap"));
    string Quick(string items) => Rejected(() => RingConfigs.Parse(WithChange(r => r["quickButtons"] = JsonNode.Parse(items))));
    string one = """{ "label": "Q", "action": "text", "target": "q" }""";
    Check(Quick($"[{one},{one},{one},{one},{one}]").StartsWith("quickButtons:"), "at most 4");
    Check(Quick("""[ { "label": "G", "items": [ { "label": "x", "action": "screenshot" } ] } ]""").StartsWith("quickButtons[0].items"), "no children");
    Check(Quick("""[ { "label": "G", "action": "group" } ]""").StartsWith("quickButtons[0].action"), "no group");
    Check(Quick("""[ { "label": "U", "action": "url", "target": "nope" } ]""").StartsWith("quickButtons[0].target"), "targets validated");
    RingConfigs.Parse(One("""{ "label": "S", "action": "ring-settings", "target": "open" }"""));
});
Test("satelliteGap moves circle 2 only; null keeps today's layout", () =>
{
    var items = new List<RingItem> { new() { Label = "P", Action = "screenshot", Items = [new() { Label = "c", Action = "screenshot" }] }, new() { Label = "Q", Action = "screenshot" } };
    var a = new RingAppearance();
    RingLayout.Result before = RingLayout.Compute(items, a);
    a.SatelliteGap = a.Spacing * 0.7;
    RingLayout.Result same = RingLayout.Compute(items, a);
    Check(Math.Abs(before.Radii[1] - same.Radii[1]) < 1e-9 && Math.Abs(before.DiscSize - same.DiscSize) < 1e-9, "default = 0.7 × spacing");
    a.SatelliteGap = 0;
    RingLayout.Result closer = RingLayout.Compute(items, a);
    Check(closer.Radii[0] == before.Radii[0] && closer.Radii[1] < before.Radii[1], $"circle 2 closer: {closer.Radii[1]} < {before.Radii[1]}");
});
Test("AI tutorial ships next to ring.json and documents the new fields", () => Temp(dir =>
{
    var store = new RingConfigStore(Path.Combine(dir, "ring.json"));
    store.EnsureFiles();
    string tutorial = File.ReadAllText(Path.Combine(dir, RingConfigStore.TutorialFileName));
    Check(new[] { "quickButtons", "satelliteGap", "workspaceButtonSize", "rimIconSize", "fluent" }.All(tutorial.Contains), "tutorial fields");
    string guide = RingConfigStore.Resource(RingConfigStore.GuideFileName);
    Check(new[] { "quickButtons", "satelliteGap", "fluent", "Réglages" }.All(guide.Contains), "guide fields");
    var schema = JsonNode.Parse(RingConfigStore.Resource(RingConfigStore.SchemaFileName))!;
    Check(schema["properties"]!["quickButtons"] is not null, "schema quickButtons");
}));

int failures = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
Console.WriteLine($"Power Ring tests: {tests.Count - failures}/{tests.Count} passed; {failures} failed.");
return failures == 0 ? 0 : 1;
