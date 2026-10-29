// Synthetic console tests against the exact laboratory sources. No vendor calls.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

internal static class LabEventConsoleTests
{
    private static readonly List<object> Results = new List<object>();
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
    private static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
    private static void Test(string name, Action test)
    {
        try { test(); Results.Add(new { name = name, status = "PASS" }); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { Results.Add(new { name = name, status = "FAIL", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static LabEventResult Fixture()
    {
        var r = new LabEventResult { limits = new LabEventLimits() };
        r.identity["project_name"] = "Synthetic_RecipeSim";
        r.identity["project_path"] = "X:/synthetic.ap19";
        r.identity["device_path"] = new[] { "HMI_JSON_TEST", "HMI_RT_1" };
        r.identity["hmi_software_name"] = "HMI_RT_1";
        r.identity["started_utc"] = "2026-10-29T00:00:00Z";
        Object(r, "/HMI/Synthetic", "HMI", "SOFTWARE");
        Object(r, "/HMI/Synthetic/Screens[0]", "T05_Recipes_Main", "SCREEN");
        Object(r, "/HMI/Synthetic/Screens[0]/ScreenItems[0]", "RecipeTable_Main", "ITEM");
        Object(r, "/HMI/Synthetic/Screens[0]/ScreenItems[1]", "ACTION_0", "ITEM");
        var h = new Dictionary<string, object> {
            {"locator", "/HMI/Synthetic/Screens[0]/ScreenItems[1]/EventHandlers[0]"},
            {"object_locator", "/HMI/Synthetic/Screens[0]/ScreenItems[1]"}, {"object_name", "ACTION_0"},
            {"object_type", "Synthetic.ITEM"}, {"handler_type", "Synthetic.Handler"},
            {"event_name", "Tapped"}, {"event_type", "Tapped"}, {"event_type_type", "Synthetic.Event"},
            {"presence", "PRESENT"}, {"status", "READ"}, {"async", true}, {"async_status", "READ"} };
        foreach (var pair in new[] { new[] { "ScriptCode", "// sentinel JS\r\nlet x='Zażółć 日本 🚀';\n" }, new[] { "GlobalDefinitionAreaScriptCode", "import * as m from 'module';\r\n" } })
        {
            h[pair[0]] = pair[1]; h[pair[0] + "_status"] = "READ";
            h[pair[0] + "_sha256"] = LabEventArtifacts.Digest(LabEventArtifacts.Utf8.GetBytes(pair[1]));
            h[pair[0] + "_path"] = "scripts/" + LabEventArtifacts.Digest(LabEventArtifacts.Utf8.GetBytes((string)h["locator"])) + "-" + pair[0] + ".js";
        }
        r.objects[3]["handlers_status"] = "ENUMERATED"; r.handlers.Add(h);
        return r;
    }
    private static void Object(LabEventResult r, string locator, string name, string kind)
    {
        r.objects.Add(new Dictionary<string, object> { {"locator", locator}, {"name", name}, {"kind", kind},
            {"object_type", "Synthetic." + kind}, {"handlers_status", "ABSENT"}, {"children_status", "COMPLETE_FOR_PUBLIC_SCOPE"} });
    }
#if !BASELINE
    private static string Render(LabEventResult r, string output = "X:\\Synthetic\\EventExports\\run")
    { var writer = new StringWriter(); LabEventConsole.Summary(writer, r, output); return writer.ToString(); }
    private static void Count(string text, string field, int expected)
    { Check(text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Contains(field.PadRight(13) + ": " + expected), field + " count"); }
#endif
    internal static int Main(string[] args)
    {
        string root = args[0];
        if (args.Length > 1 && args[1] == "--fixture")
        {
            var r = Fixture(); r.Issue((string)r.objects[2]["locator"], "children", "ENCAPSULATED_CHILDREN_NOT_PUBLIC", null);
            LabEventArtifacts.Write(Path.Combine(root, "fixture"), r);
#if !BASELINE
            File.WriteAllText(Path.Combine(root, "console-sample.txt"), Render(r), new UTF8Encoding(false));
#endif
            return 0;
        }
#if !BASELINE
        Test("complete_export_no_observations_has_zero_errors", () => {
            string s = Render(Fixture());
            Check(s.Contains("Project : Synthetic_RecipeSim") && s.Contains("HMI     : HMI_JSON_TEST/HMI_RT_1") && s.Contains("COMPLETE_FOR_PUBLIC_SCOPE"), "identity/status missing");
            Count(s, "Screens", 1); Count(s, "Objects", 2); Count(s, "Handlers", 1); Count(s, "Observations", 0); Count(s, "Warnings", 0); Count(s, "Errors", 0);
            Check(s.Contains("INFO  Publikacja zakończona") && s.Contains("Output: X:\\Synthetic\\EventExports\\run"), "success/output missing");
        });
        Test("container_limit_is_warning_not_execution_error", () => {
            var r = Fixture(); r.Issue((string)r.objects[2]["locator"], "children", "ENCAPSULATED_CHILDREN_NOT_PUBLIC", null);
            string s = Render(r); Count(s, "Warnings", 1); Count(s, "Errors", 0); Count(s, "Observations", 1);
            Check(s.Contains("Status       : PARTIAL") && s.Contains("WARN  T05_Recipes_Main / RecipeTable_Main") && s.Contains("ENCAPSULATED_CHILDREN_NOT_PUBLIC"), "coverage/name/code lost");
        });
        Test("read_and_iterator_failures_are_errors", () => {
            var r = Fixture();
            foreach (string code in new[] { "READ_FAILED", "ENUMERATION_FAILED", "DISPOSE_FAILED", "EXPORT_READ_FAILED", "POSTCHECK_FAILED", "PROJECT_DIRTY_AFTER_READ", "INVALID_UNICODE", "NULL_ITERATOR", "NULL_ELEMENT" })
                r.Issue((string)r.handlers[0]["locator"], "ScriptCode", code, new IOException("do not leak vendor message"));
            string s = Render(r); Count(s, "Errors", 9); Count(s, "Warnings", 0); Count(s, "Observations", 9);
            Check(s.Contains("ERROR T05_Recipes_Main / ACTION_0 / Event: Tapped") && !s.Contains("do not leak vendor message"), "handler mapping/exception privacy");
        });
        Test("mixed_diagnostics_preserve_order_and_every_original_code", () => {
            var r = Fixture(); var codes = new[] { "ENCAPSULATED_CHILDREN_NOT_PUBLIC", "READ_FAILED", "LIMIT_EXCEEDED", "FUTURE_VENDOR_CODE" };
            foreach (string code in codes) r.Issue((string)r.objects[2]["locator"], "phase", code, null);
            string s = Render(r); Count(s, "Warnings", 3); Count(s, "Errors", 1); Count(s, "Unclassified", 1);
            int last = -1; foreach (string code in codes) { int next = s.IndexOf(code, StringComparison.Ordinal); Check(next > last, "observation hidden/reordered"); last = next; }
        });
        Test("unknown_codes_warn_with_unknown_classification_even_with_exception", () => {
            var r = Fixture(); r.Issue("/unmapped", "phase", "FUTURE_VENDOR_CODE", new IOException("private"));
            string s = Render(r); Count(s, "Errors", 0); Count(s, "Warnings", 1); Count(s, "Unclassified", 1);
            Check(s.Contains("WARN  /unmapped") && s.Contains("classification=UNKNOWN") && s.Contains("exception_type=System.IO.IOException"), "unknown code silently classified");
        });
        Test("partial_or_unknown_status_does_not_manufacture_errors", () => {
            foreach (string status in new[] { "PARTIAL", "UNKNOWN" })
            { var r = Fixture(); r.status = status; string s = Render(r); Count(s, "Errors", 0); Count(s, "Warnings", 0); Check(s.Contains("Status       : " + status), "status overwritten"); }
        });
        Test("missing_object_name_or_ancestor_uses_full_locator", () => {
            foreach (int index in new[] { 1, 2 })
            {
                var r = Fixture(); string locator = (string)r.objects[2]["locator"]; r.objects[index]["name"] = null;
                r.Issue(locator, "children", "ENCAPSULATED_CHILDREN_NOT_PUBLIC", null); Check(Render(r).Contains("WARN  " + locator), "guessed missing name");
            }
            var missing = Fixture(); string loc = (string)missing.objects[2]["locator"]; missing.objects.RemoveAt(1);
            missing.Issue(loc, "children", "ENCAPSULATED_CHILDREN_NOT_PUBLIC", null); Check(Render(missing).Contains("WARN  " + loc), "guessed missing parent");
        });
        Test("duplicate_names_locators_and_events_fall_back_to_locator", () => {
            var r = Fixture(); r.objects[3]["name"] = r.objects[2]["name"];
            string locator = (string)r.objects[2]["locator"]; r.Issue(locator, "children", "ENCAPSULATED_CHILDREN_NOT_PUBLIC", null);
            Check(Render(r).Contains("WARN  " + locator), "ambiguous name hidden");
            r.objects.Add(new Dictionary<string, object>(r.objects[2])); Check(Render(r).Contains("WARN  " + locator), "duplicate locator guessed");
            r = Fixture(); r.handlers.Add(new Dictionary<string, object>(r.handlers[0])); locator = (string)r.handlers[0]["locator"];
            r.Issue(locator, "code", "READ_FAILED", null); Check(Render(r).Contains("ERROR " + locator), "duplicate handler guessed");
            r = Fixture(); r.objects[2]["name"] = "A\nB"; r.objects[3]["name"] = "A\\u000aB";
            locator = (string)r.objects[2]["locator"]; r.Issue(locator, "children", "ENCAPSULATED_CHILDREN_NOT_PUBLIC", null);
            Check(Render(r).Contains("WARN  " + locator), "console escaping made labels ambiguous");
        });
        Test("groups_and_nested_elements_resolve_from_inventory", () => {
            var r = Fixture(); r.objects.Clear(); r.handlers.Clear();
            Object(r, "/hmi", "HMI", "SOFTWARE"); Object(r, "/hmi/ScreenGroups[0]", "Group", "GROUP");
            Object(r, "/hmi/ScreenGroups[0]/Screens[0]", "Main", "SCREEN");
            Object(r, "/hmi/ScreenGroups[0]/Screens[0]/ScreenItems[0]", "Panel", "ITEM");
            string loc = "/hmi/ScreenGroups[0]/Screens[0]/ScreenItems[0]/ScreenItems[0]";
            Object(r, loc, "Button", "ITEM"); r.Issue(loc, "children", "ENCAPSULATED_CHILDREN_NOT_PUBLIC", null);
            Check(Render(r).Contains("WARN  Group / Main / Panel / Button"), "nested hierarchy lost");
        });
        Test("long_paths_unicode_and_control_characters_are_visible", () => {
            var r = Fixture(); r.identity["project_name"] = "Zażółć 日本 🚀";
            r.identity["device_path"] = new[] { "Device 日本", "HMI/100%" };
            string locator = "/" + new string('界', 3000); string output = "X:\\" + new string('路', 2000) + "\\run";
            r.Issue(locator, "phase\r\n\u001b[31m", "FUTURE_CODE", null); string s = Render(r, output);
            Check(s.Contains("Zażółć 日本 🚀") && s.Contains("Device 日本/HMI%2F100%25") && s.Contains(locator) && s.Contains(output), "Unicode/path truncated");
            Check(s.Contains("phase\\u000d\\u000a\\u001b[31m") && !s.Contains("\u001b"), "console control injection");
        });
        Test("render_preserves_entire_result_and_emits_no_javascript", () => {
            var r = Fixture(); r.Issue((string)r.objects[2]["locator"], "children", "ENCAPSULATED_CHILDREN_NOT_PUBLIC", null);
            string before = Json.Serialize(r); string s = Render(r);
            Check(before == Json.Serialize(r) && !s.Contains("sentinel JS") && !s.Contains("import * as"), "console mutated data/leaked source");
        });
        Test("failure_and_attach_messages_keep_codes_without_raw_exceptions", () => {
            var writer = new StringWriter(); LabEventConsole.AttachNotice(writer);
            Check(writer.ToString().StartsWith("INFO  EXPORT EVENTS: read-only Attach") && writer.ToString().Contains("Brak automatycznego retry"), "authorization notice lost");
            writer = new StringWriter(); LabEventConsole.Failure(writer, new LabEventFailure("CONTROLLED_FAILURE"), "X:\\out");
            Check(writer.ToString().Contains("ERROR EXPORT EVENTS FAILED") && writer.ToString().Contains("code=CONTROLLED_FAILURE"), "failure/code hidden");
            writer = new StringWriter(); LabEventConsole.Failure(writer, new IOException("secret source code"), null);
            Check(writer.ToString().Contains("code=UNSPECIFIED") && writer.ToString().Contains("Output: UNKNOWN") && !writer.ToString().Contains("secret source code"), "exception leak/default");
        });
        Test("coverage_warning_codes_are_explicit_and_count_observations", () => {
            var r = Fixture();
            foreach (string code in new[] { "UNSUPPORTED_OBJECT", "UNSUPPORTED_COLLECTION", "LIMIT_EXCEEDED", "UNKNOWN_EVENT_VALUE", "NULL_VALUE_UNKNOWN" })
                r.Issue("/", "phase", code, null);
            string s = Render(r); Count(s, "Warnings", 5); Count(s, "Errors", 0); Count(s, "Unclassified", 0); Count(s, "Observations", 5);
        });
        Test("unmapped_child_never_inherits_parent_name", () => {
            var r = Fixture(); string locator = "/HMI/Synthetic/Screens[0]/ScreenItems[99]";
            r.Issue(locator, "ScreenItems", "NULL_ELEMENT", null);
            Check(Render(r).Contains("ERROR " + locator), "invented a child name");
        });
        File.WriteAllText(Path.Combine(root, "results.json"), Json.Serialize(Results), new UTF8Encoding(false));
        int failed = Results.Count(x => Json.Serialize(x).Contains("\"FAIL\"")); Console.WriteLine("TOTAL " + Results.Count + "; FAILED " + failed);
        return failed == 0 ? 0 : 1;
#else
        return 1;
#endif
    }
}
