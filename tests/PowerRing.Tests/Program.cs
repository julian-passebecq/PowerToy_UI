using System.Text.Json.Nodes;
using PowerRing.Core;

// Package-free regression tests for Power Ring's config, validation, navigation and key combos.
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

Test("Default ring.json is valid: 3 profiles, sub-circles up to level 3", () =>
{
    RingConfig config = RingDefaults.Create();
    Check(config.Profiles.Select(x => x.Id).SequenceEqual(new[] { "dev", "perso", "work" }));
    Check(config.Profiles.All(p => p.Items.Count is >= 1 and <= 8));
    RingItem web = config.Profiles[0].Items.Single(x => x.Label == "Web");
    Check(web.IsGroup && web.Items!.Single(x => x.Label == "Local").IsGroup, "a third level exists in the defaults");
});
Test("Comments and trailing commas are accepted (hand and AI edits)", () =>
{
    RingConfig config = RingConfigs.Parse("""
        { // comment
          "version": 1, "hotkey": "Ctrl+Alt+Space",
          "profiles": [ { "id": "a", "name": "A", "items": [ { "label": "X", "action": "screenshot" }, ], }, ],
        }
        """);
    Check(config.Profiles[0].Items[0].Label == "X" && config.Appearance.RingSize == 340, "appearance defaults when omitted");
});
Test("Round trip keeps every field", () =>
{
    RingConfig config = RingDefaults.Create();
    config.Appearance.Accent = "#FF112233"; config.Profiles[0].Items[0].Color = "#22C55E";
    RingConfig again = RingConfigs.Parse(RingConfigs.Serialize(config));
    Check(again.Appearance.Accent == "#FF112233" && again.Profiles[0].Items[0].Color == "#22C55E" && again.Profiles.Count == 3);
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
    message = Rejected(() => RingConfigs.Parse(WithChange(r => Item(r, 1, 0)["target"] = "javascript:alert(1)")));
    Check(message.StartsWith("profiles[1].items[0].target"), message);
    message = Rejected(() => RingConfigs.Parse(WithChange(r => r["appearance"]!["ringSize"] = 5000)));
    Check(message.StartsWith("appearance.ringSize") && message.Contains("between"), message);
    message = Rejected(() => RingConfigs.Parse(WithChange(r => Item(r, 0, 0)["icon"] = "rocket-ship")));
    Check(message.Contains("icon") && message.Contains("code"), "unknown icon lists the names: " + message);
});
Test("Limits: 1-5 profiles, 1-8 items, 3 levels, unique ids, sane colours and hotkey", () =>
{
    Rejected(() => RingConfigs.Parse(WithChange(r => r["profiles"] = new JsonArray())));
    Rejected(() => RingConfigs.Parse(WithChange(r => { var items = r["profiles"]![1]!["items"]!.AsArray(); items.Add(JsonNode.Parse("""{ "label": "9", "action": "screenshot" }""")); })));
    Rejected(() => RingConfigs.Parse(WithChange(r => r["profiles"]![1]!["id"] = "dev")));
    Rejected(() => RingConfigs.Parse(WithChange(r => r["profiles"]![0]!["accent"] = "blue")));
    Rejected(() => RingConfigs.Parse(WithChange(r => r["hotkey"] = "R")));
    Rejected(() => RingConfigs.Parse(WithChange(r => r["startProfile"] = "nope")));
    Rejected(() => RingConfigs.Parse(WithChange(r => r["version"] = 2)));
    string deep = Rejected(() => RingConfigs.Parse(WithChange(r =>
    {
        var local = r["profiles"]![0]!["items"]![5]!["items"]![2]!.AsObject();
        local["items"]![0] = JsonNode.Parse("""{ "label": "Too deep", "items": [ { "label": "x", "action": "screenshot" } ] }""");
    })));
    Check(deep.Contains("at most 3 levels"), deep);
    Rejected(() => RingConfigs.Parse(WithChange(r => Item(r, 0, 0)["items"] = new JsonArray())));
    Rejected(() => RingConfigs.Parse(WithChange(r => Item(r, 1, 0)["target"] = "https://user:pw@mail.example/")));
});
Test("Every action validates its target", () =>
{
    string Json(string item) => $$"""{ "version": 1, "profiles": [ { "id": "a", "name": "A", "items": [ {{item}} ] } ] }""";
    RingConfigs.Parse(Json("""{ "label": "a", "action": "run", "target": "code", "args": "." }"""));
    RingConfigs.Parse(Json("""{ "label": "a", "action": "folder", "target": "downloads" }"""));
    RingConfigs.Parse(Json("""{ "label": "a", "action": "keys", "target": "Win+Tab" }"""));
    RingConfigs.Parse(Json("""{ "label": "a", "action": "text", "target": "hello" }"""));
    RingConfigs.Parse(Json("""{ "label": "a", "action": "url", "target": "mailto:me@example.com" }"""));
    RingConfigs.Parse(Json("""{ "label": "a", "action": "powerops" }"""));
    RingConfigs.Parse(Json("""{ "label": "a", "icon": "C:\\Tools\\app.exe", "action": "run", "target": "C:\\Tools\\app.exe" }"""));
    Rejected(() => RingConfigs.Parse(Json("""{ "label": "a", "action": "run" }""")));
    Rejected(() => RingConfigs.Parse(Json("""{ "label": "a", "action": "keys", "target": "Win+Nope" }""")));
    Rejected(() => RingConfigs.Parse(Json("""{ "label": "a", "action": "text" }""")));
    Rejected(() => RingConfigs.Parse(Json("""{ "label": "a", "action": "url", "target": "www.example.com" }""")));
    Rejected(() => RingConfigs.Parse(Json("""{ "label": "", "action": "screenshot" }""")));
    Rejected(() => RingConfigs.Parse(Json("""{ "label": "a", "action": "screenshot", "items": [ { "label": "b", "action": "screenshot" } ] }""")));
});
Test("Key combos parse to virtual keys in press order", () =>
{
    KeyCombo tab = KeyCombo.Parse("Win+Tab");
    Check(tab.VirtualKey == 0x09 && tab.ModifierKeys().SequenceEqual(new[] { 0x5B }) && tab.ToString() == "Win+Tab");
    KeyCombo hot = KeyCombo.ParseHotkey("shift+alt+ctrl+r");
    Check(hot.ToString() == "Ctrl+Alt+Shift+R" && hot.VirtualKey == 'R' && hot.ModifierKeys().SequenceEqual(new[] { 0x11, 0x12, 0x10 }));
    Check(KeyCombo.Parse("Ctrl+Shift+Esc").VirtualKey == 0x1B && KeyCombo.Parse("F12").VirtualKey == 0x7B && KeyCombo.Parse("Win+Left").VirtualKey == 0x25);
    Rejected(() => KeyCombo.Parse("Ctrl+Ctrl+A"));
    Rejected(() => KeyCombo.Parse("A+B"));
    Rejected(() => KeyCombo.ParseHotkey("Shift+R"));
});
Test("Navigator: profiles, sub-circles, back and breadcrumb", () =>
{
    RingConfig config = RingDefaults.Create();
    var nav = new RingNavigator(config);
    Check(nav.Profile.Id == "dev" && nav.AtRoot && nav.Breadcrumb == "Dev");
    RingItem web = nav.Items.Single(x => x.Label == "Web");
    nav.Open(web);
    nav.Open(nav.Items.Single(x => x.Label == "Local"));
    Check(nav.Depth == 3 && nav.Breadcrumb == "Dev › Web › Local" && nav.Items.Any(x => x.Label == "Mongoku"));
    Check(nav.Back() && nav.Back() && !nav.Back() && nav.AtRoot);
    bool refused = false;
    try { nav.Open(nav.Items.Single(x => x.Label == "VS Code")); } catch (InvalidOperationException) { refused = true; }
    Check(refused, "only groups open");
    nav.Open(web); nav.CycleProfile(1);
    Check(nav.Profile.Id == "perso" && nav.AtRoot, "switching profile returns to its first circle");
    nav.CycleProfile(-2); Check(nav.Profile.Id == "work", "wraps");
    config.StartProfile = "perso"; Check(new RingNavigator(config).Profile.Id == "perso");
});
Test("Navigator reload keeps the profile when it still exists", () =>
{
    var nav = new RingNavigator(RingDefaults.Create());
    nav.SetProfile(2);
    RingConfig next = RingDefaults.Create(); next.Profiles.RemoveAt(0);
    nav.Reload(next); Check(nav.Profile.Id == "work");
    RingConfig only = RingDefaults.Create(); only.Profiles.RemoveRange(1, 2);
    nav.Reload(only); Check(nav.Profile.Id == "dev");
});
Test("Slot geometry: 1 at the top, clockwise, inside the disc", () =>
{
    var (x0, y0) = RingNavigator.SlotOffset(0, 8, 100);
    var (x2, y2) = RingNavigator.SlotOffset(2, 8, 100);
    Check(Math.Abs(x0) < 1e-6 && y0 == -100 && x2 == 100 && Math.Abs(y2) < 1e-6);
    var a = new RingAppearance();
    double r = RingNavigator.DefaultSlotRadius(a);
    Check(r - a.SlotSize / 2 > a.CenterSize / 2 && r + a.SlotSize / 2 < a.RingSize / 2, $"radius {r}");
});
Test("Icons: names, codes, files and defaults by action", () =>
{
    Check(RingIcons.Glyph("code") == "\uE943" && RingIcons.Glyph("E715") == "\uE715" && RingIcons.Glyph("x.png") is null);
    Check(RingIcons.IsFile(@"C:\apps\Code.exe") && !RingIcons.IsFile("code"));
    Check(RingIcons.Default(new RingItem { Label = "a", Action = "run", Target = "code" }) == "code");
    Check(RingIcons.Default(new RingItem { Label = "a", Action = "url", Target = "https://mail.google.com/" }) == "mail");
    Check(RingIcons.Default(new RingItem { Label = "a", Items = [] }) == "folder");
});
Test("Schema lists every action, icon name and appearance field", () =>
{
    string schema = RingConfigStore.Resource(RingConfigStore.SchemaFileName);
    var root = JsonNode.Parse(schema)!;
    var actions = root["definitions"]!["item"]!["properties"]!["action"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>());
    Check(actions.SequenceEqual(RingActions.All), "schema actions = code actions");
    var icons = root["definitions"]!["icon"]!["anyOf"]![0]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet();
    Check(icons.SetEquals(RingIcons.Names), "schema icon names = code icon names");
    var fields = root["definitions"]!["appearance"]!["properties"]!.AsObject().Select(x => x.Key).ToHashSet();
    var code = typeof(RingAppearance).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).ToHashSet();
    Check(fields.SetEquals(code), "schema appearance = code appearance: " + string.Join(",", code.Except(fields).Concat(fields.Except(code))));
    string guide = RingConfigStore.Resource(RingConfigStore.GuideFileName);
    Check(RingActions.All.All(a => guide.Contains($"`{a}`")), "guide documents every action");
});
Test("Store: first start writes ring.json, schema and guide; never overwrites the user's file", () =>
{
    string dir = Path.Combine(Path.GetTempPath(), "PowerRingTests-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new RingConfigStore(Path.Combine(dir, "ring.json"));
        store.EnsureFiles();
        Check(File.Exists(store.FilePath) && File.Exists(Path.Combine(dir, "ring.schema.json")) && File.Exists(Path.Combine(dir, "RING_CONFIG.md")));
        Check(store.Load().Profiles.Count == 3);
        File.WriteAllText(store.FilePath, "{ broken");
        store.EnsureFiles();
        Check(File.ReadAllText(store.FilePath) == "{ broken", "user file kept");
        Rejected(() => store.Load());
    }
    finally { Directory.Delete(dir, true); }
});

int failures = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
Console.WriteLine($"Power Ring tests: {tests.Count - failures}/{tests.Count} passed; {failures} failed.");
return failures == 0 ? 0 : 1;
