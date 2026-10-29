// Windows .NET Framework synthetic fixtures; compiled against the saved LAB-03K and current source.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal static class LabEventViewTests
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
    private static readonly List<object> Results = new List<object>();
    private static string root;
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static bool Reject(Action action) { try { action(); return false; } catch { return true; } }
    private static void Test(string name, Action action)
    {
        try { action(); Results.Add(new { name = name, status = "PASS" }); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { Results.Add(new { name = name, status = "FAIL", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static EventFake H(object code, object global, string ev = "Tapped")
    {
        var script = new EventFake("SCRIPT");
        script.Values["ScriptCode"] = code; script.Values["GlobalDefinitionAreaScriptCode"] = global; script.Values["Async"] = true;
        var handler = new EventFake("HANDLER"); handler.Values["EventType"] = ev; handler.Values["Script"] = script;
        return handler;
    }
    private static EventFake N(string kind, string name, params EventFake[] handlers)
    {
        var node = new EventFake(kind) { Name = name, Supported = kind == "SCREEN" || kind == "ITEM" };
        node.Values["EventHandlers"] = handlers; return node;
    }
    private static EventFake E(EventFake node, string edge, params EventFake[] children)
    { node.Edges = node.Edges.Concat(new[] { edge }).ToArray(); node.Values[edge] = children; return node; }
    private static LabEventResult Read(EventFake screen)
    {
        var r = new LabEventReader(new EventFakeAccess(), new LabEventLimits()).Read(screen, "/HMI/synthetic");
        r.identity["project_name"] = "Synthetic"; r.identity["project_path"] = "X:/synthetic.ap19";
        r.identity["device_path"] = "Synthetic/HMI"; r.identity["hmi_software_name"] = "HMI";
        r.identity["started_utc"] = "2026-10-29T00:00:00Z"; return r;
    }
    private static LabEventResult Fixture()
    {
        return Read(E(N("SOFTWARE", "HMI"), "ScreenGroups", E(N("GROUP", "Area"), "Groups",
            E(N("GROUP", "Cell"), "Screens", E(N("SCREEN", "T05_Recipes_Main", H("screen\r\n", "// global\n", "Loaded"), H("", "", "Empty")), "ScreenItems",
                N("ITEM", "ACTION_0", H("/* sentinel source */\r\nconst s='Zażółć 日本 🚀 e\u0301';\n", "import * as x from 'module';\r\n")),
                N("ITEM", "Broken", H(new IOException("private vendor message"), "let g=1;")), N("ITEM", "NoHandlers"))))));
    }
    private static string Write(LabEventResult result)
    { string dest = Path.Combine(root, Guid.NewGuid().ToString("N")); LabEventArtifacts.Write(dest, result); return dest; }
    private static Dictionary<string, object> Load(string path)
    { return (Dictionary<string, object>)Json.DeserializeObject(File.ReadAllText(path)); }
    private static IEnumerable<Dictionary<string, object>> Rows(Dictionary<string, object> row, string key)
    { return ((object[])row[key]).Cast<Dictionary<string, object>>(); }
    private static IEnumerable<Dictionary<string, object>> Handlers(string dest)
    { return Rows(Load(Path.Combine(dest, "events-index.json")), "screens").SelectMany(x => Rows(x, "handlers")); }
    private static IEnumerable<string> Paths(string dest)
    { return Handlers(dest).SelectMany(x => new[] { (Dictionary<string, object>)x["code"], (Dictionary<string, object>)x["global"] })
        .Where(x => x["path"] != null).Select(x => (string)x["path"]); }
    internal static int Main(string[] args)
    {
        root = args[0];
        if (args.Length > 1 && args[1] == "--fixture") { LabEventArtifacts.Write(Path.Combine(root, "fixture"), Fixture()); return 0; }
#if !BASELINE
        Test("screen_events_code_global_empty_failed_and_absence", () => {
            string dest = Write(Fixture()); var index = Load(Path.Combine(dest, "events-index.json"));
            var screen = Rows(index, "screens").Single(); var hs = Handlers(dest).ToArray();
            Check(hs.Length == 4 && Paths(dest).Count() == 7, "invented/missing handler or text file");
            Check(Directory.GetFiles(Path.Combine(dest, "screens"), "Loaded.js", SearchOption.AllDirectories).Length == 1, "screen filename");
            Check(hs.Any(x => (string)((Dictionary<string, object>)x["code"])["status"] == "EMPTY"), "empty status lost");
            var failedHandler = hs.Single(x => (string)((Dictionary<string, object>)x["code"])["status"] == "READ_FAILED");
            Check(((Dictionary<string, object>)failedHandler["code"])["path"] == null && ((Dictionary<string, object>)failedHandler["global"])["path"] != null, "independent read failure");
            Check(Rows(screen, "objects").Any(x => (string)x["name"] == "NoHandlers" && (string)x["handlers_status"] == "ABSENT"), "absence omitted");
            Check(!Paths(dest).Any(x => x.Contains("Unloaded") || x.Contains("NoHandlers")), "invented absent script");
        });
        Test("hierarchy_groups_subgroups_nested_item_names", () => {
            var screen = E(N("SCREEN", "Main"), "ScreenItems", E(N("ITEM", "Panel"), "ScreenItems", N("ITEM", "Button", H("code", "g"))));
            var r = Read(E(N("SOFTWARE", "HMI"), "ScreenGroups", E(N("GROUP", "Group"), "Groups", E(N("GROUP", "Sub"), "Screens", screen))));
            string dest = Write(r);
            Check(Paths(dest).Any(x => x.Contains("Group__Sub__Main/Panel.Button.Tapped.js")), "hierarchy absent from readable path");
            var s = Rows(Load(Path.Combine(dest, "events-index.json")), "screens").Single();
            Check(Rows(s, "groups").Count() == 2, "group locators lost");
        });
        Test("exact_bytes_unicode_crlf_lf_bom_imports_and_hashes", () => {
            string code = "\ufeff// c\r\nconst s='🚀 e\u0301';\n\r\nend\r"; string global = "import * as a from 'b';\r\n  let shared;\n";
            string dest = Write(Read(N("SCREEN", "S", H(code, global)))); var h = Handlers(dest).Single();
            foreach (var pair in new[] { new { Key = "code", Text = code }, new { Key = "global", Text = global } })
            {
                var text = (Dictionary<string, object>)h[pair.Key]; byte[] actual = File.ReadAllBytes(Path.Combine(dest, (string)text["path"]));
                Check(actual.SequenceEqual(new UTF8Encoding(false, true).GetBytes(pair.Text)) && LabEventArtifacts.Digest(actual) == (string)text["sha256"], "text normalized");
            }
        });
        Test("case_sanitization_unicode_and_flattened_hierarchy_collisions", () => {
            var names = new[] { "Button", "button", "A/B", "A_B", "é", "e\u0301", "Panel.Button" };
            var items = names.Select(x => N("ITEM", x, H("c", "g"))).Concat(new[] { E(N("ITEM", "Panel"), "ScreenItems", N("ITEM", "Button", H("c", "g"))) }).ToArray();
            var r = Read(E(N("SCREEN", "Main"), "ScreenItems", items)); string dest = Write(r); var paths = Paths(dest).ToArray();
            Check(paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 16 && paths.All(x => x.Contains("~")), "collision not disambiguated");
            string second = Write(r); Check(paths.SequenceEqual(Paths(second)), "names unstable for same locators");
        });
        Test("duplicate_screen_names_and_cross_gda_suffix_collisions", () => {
            var r = Read(E(N("SOFTWARE", "HMI"), "Screens", N("SCREEN", "Main", H("c", "g", "Loaded"), H("c2", "g2", "Loaded.global")), N("SCREEN", "main", H("x", "y", "Loaded"))));
            string dest = Write(r); var screens = Rows(Load(Path.Combine(dest, "events-index.json")), "screens").ToArray();
            Check(screens.Select(x => (string)x["directory"]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2 && Paths(dest).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 6, "screen/global collision");
        });
        Test("windows_reserved_invalid_traversal_and_markdown_names", () => {
            var items = new[] { "CON", "nul", "LPT1", "COM¹", "../escape:ads?*|<>\\", "A (x) [b] #日本%", "trail. " }.Select(x => N("ITEM", x, H("c", "g"))).ToArray();
            string dest = Write(Read(E(N("SCREEN", "..\\CON"), "ScreenItems", items)));
            foreach (string path in Paths(dest))
            {
                Check(Path.GetFullPath(Path.Combine(dest, path)).StartsWith(dest + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "path escaped");
                string filename = Path.GetFileName(path); Check(filename.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !Regex.IsMatch(filename, "^(CON|NUL|LPT1|COM¹)\\.", RegexOptions.IgnoreCase), "invalid/reserved file");
            }
            Check(Directory.GetFiles(dest, "*", SearchOption.AllDirectories).All(x => new FileInfo(x).Length >= 0), "missing file");
        });
        Test("relative_encoded_links_resolve_without_source_in_readmes", () => {
            string dest = Write(Read(E(N("SCREEN", "日本 (S)"), "ScreenItems", N("ITEM", "A [b] #日本%", H("secret js sentinel", "secret gda sentinel")))));
            foreach (string file in Directory.GetFiles(dest, "README.md", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file); Check(!text.Contains("secret js sentinel") && !text.Contains("secret gda sentinel"), "source in README");
                foreach (Match match in Regex.Matches(text, @"\]\(([^)]+)\)"))
                {
                    string link = match.Groups[1].Value; Check(!link.Contains(" ") && !link.Contains("#"), "link not encoded");
                    Check(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file), Uri.UnescapeDataString(link)))), "broken relative link");
                }
            }
            string index = File.ReadAllText(Path.Combine(dest, "events-index.json"));
            Check(index.Contains("\n  \"schema_version\"") && !index.Contains("secret js sentinel") && !index.Contains("secret gda sentinel"), "index formatting/source duplication");
        });
        Test("partial_unknown_encapsulated_coverage_is_visible", () => {
            var item = N("ITEM", "Container", H(null, null)); item.Hidden = true;
            string dest = Write(Read(E(N("SCREEN", "Main"), "ScreenItems", item))); var index = Load(Path.Combine(dest, "events-index.json"));
            Check((string)index["status"] == "PARTIAL" && !Paths(dest).Any(), "unknown manufactured a script");
            Check(System.Net.WebUtility.HtmlDecode(File.ReadAllText(Path.Combine(dest, "README.md"))).Contains("ENCAPSULATED_CHILDREN_NOT_PUBLIC"), "limitation hidden");
            var o = Rows(Rows(index, "screens").Single(), "objects").Single(x => (string)x["name"] == "Container");
            Check((string)o["children_status"] == "UNKNOWN", "coverage promoted");
        });
        Test("source_json_and_records_unchanged_by_view", () => {
            var r = Fixture(); string before = Json.Serialize(r); string dest = Write(r);
            Check(before == Json.Serialize(r) && before == File.ReadAllText(Path.Combine(dest, "events.json")), "LAB-03K result mutated");
            var index = Load(Path.Combine(dest, "events-index.json")); var source = (Dictionary<string, object>)index["source"];
            Check((string)source["sha256"] == LabEventArtifacts.Digest(File.ReadAllBytes(Path.Combine(dest, "events.json"))), "source binding hash");
            var original = Rows(Load(Path.Combine(dest, "events.json")), "handlers").ToArray();
            foreach (var h in Handlers(dest))
                Check((string)original[Int32.Parse(((string)h["source_ref"]).Split('/').Last())]["locator"] == (string)h["locator"], "record link ambiguous");
        });
        Test("all_new_files_in_verified_manifest", () => {
            string dest = Write(Fixture()); var manifest = Load(Path.Combine(dest, "manifest.json")); var files = Rows(manifest, "files").ToArray();
            foreach (var file in files)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(dest, (string)file["path"]));
                Check((string)file["sha256"] == LabEventArtifacts.Digest(bytes) && Convert.ToInt64(file["length"]) == bytes.LongLength, "manifest mismatch");
            }
            Check(Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Length == files.Length + 1 && !File.Exists(Path.Combine(dest, ".incomplete")), "unmanifested file/incomplete publication");
        });
        Test("long_names_truncated_with_full_locator_hash_and_path_budget", () => {
            string longName = new string('界', 490) + "🚀";
            string dest = Write(Read(E(N("SCREEN", longName), "ScreenItems", N("ITEM", longName, H("c", "g")), N("ITEM", longName + "x", H("c", "g")))));
            Check(Paths(dest).All(x => x.Contains("~")) && Directory.GetFiles(dest, "*", SearchOption.AllDirectories).All(x => x.Length <= 240), "path/name budget exceeded");
        });
        Test("view_generation_failure_preserves_marker_and_history", () => {
            string history = Write(Fixture()); string hash = LabEventArtifacts.Digest(File.ReadAllBytes(Path.Combine(history, "events.json")));
            var r = Read(N("SCREEN", "S", H("c", "g"))); r.handlers[0]["ScriptCode_sha256"] = new string('0', 64);
            string dest = Path.Combine(root, Guid.NewGuid().ToString("N")); var before = new HashSet<string>(Directory.GetDirectories(root, ".event-incomplete-*"));
            Check(Reject(() => LabEventArtifacts.Write(dest, r)) && !Directory.Exists(dest), "failed view published");
            string staging = Directory.GetDirectories(root, ".event-incomplete-*").Single(x => !before.Contains(x));
            Check(File.ReadAllText(Path.Combine(staging, ".incomplete")).Contains("VIEW_GENERATION_FAILED"), "failure marker removed");
            Check(hash == LabEventArtifacts.Digest(File.ReadAllBytes(Path.Combine(history, "events.json"))), "history changed");
        });
        Test("too_long_output_path_fails_with_incomplete_marker", () => {
            string parent = Path.Combine(root, new string('d', 75)); Directory.CreateDirectory(parent);
            string dest = Path.Combine(parent, "export");
            Check(Reject(() => LabEventArtifacts.Write(dest, Read(N("SCREEN", "S", H("c", "g"))))) && !Directory.Exists(dest), "excessive path accepted");
            Check(Directory.GetFiles(parent, ".incomplete", SearchOption.AllDirectories).Length == 1, "path failure evidence missing");
        });
        Test("unknown_code_status_never_creates_empty_file", () => {
            var h = H("c", "g"); h.Values["Script"] = null; string dest = Write(Read(N("SCREEN", "S", h)));
            var record = (Dictionary<string, object>)Handlers(dest).Single()["code"];
            Check((string)record["status"] == "UNKNOWN" && record["path"] == null && !Paths(dest).Any(), "unknown became empty");
        });
        Test("no_additional_access_reads_during_render", () => {
            var handler = H("c", "g"); var screen = N("SCREEN", "S", handler); var r = Read(screen);
            var script = (EventFake)handler.Values["Script"];
            string before = Json.Serialize(new[] { screen.Calls, handler.Calls, script.Calls }); Write(r);
            Check(before == Json.Serialize(new[] { screen.Calls, handler.Calls, script.Calls }), "renderer re-read source");
        });
        Test("no_screens_or_handlers_and_empty_screen_remain_truthful", () => {
            string empty = Write(Read(N("SOFTWARE", "HMI"))); Check(!Rows(Load(Path.Combine(empty, "events-index.json")), "screens").Any(), "invented screen");
            string screen = Write(Read(N("SCREEN", "NoEvents"))); Check(!Handlers(screen).Any() && Directory.GetFiles(Path.Combine(screen, "screens"), "*.js", SearchOption.AllDirectories).Length == 0, "invented handler");
        });
        File.WriteAllText(Path.Combine(root, "results.json"), Json.Serialize(Results), new UTF8Encoding(false));
        int failed = Results.Count(x => Json.Serialize(x).Contains("\"FAIL\"")); Console.WriteLine("TOTAL " + Results.Count + "; FAILED " + failed);
        return failed == 0 ? 0 : 1;
#else
        return 1;
#endif
    }
}
