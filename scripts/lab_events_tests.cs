// Synthetic offline tests for the exact laboratory reader source. No live Siemens calls.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class EventFake
{
    internal string Kind, Name = "synthetic", Type = "Synthetic.Object";
    internal bool Supported, Hidden;
    internal Dictionary<string, object> Values = new Dictionary<string, object>();
    internal Dictionary<string, int> Calls = new Dictionary<string, int>();
    internal string[] Edges = new string[0];
    internal EventFake(string kind) { Kind = kind; }
}
internal sealed class EventFakeAccess : ILabEventAccess
{
    public string Kind(object node) { return ((EventFake)node).Kind; }
    public string TypeName(object node) { return ((EventFake)node).Type; }
    public string[] Edges(object node) { return ((EventFake)node).Edges; }
    public bool SupportsEvents(object node) { return ((EventFake)node).Supported; }
    public bool HiddenChildren(object node) { return ((EventFake)node).Hidden; }
    public object Read(object node, string property)
    {
        var fake = (EventFake)node;
        fake.Calls[property] = fake.Calls.ContainsKey(property) ? fake.Calls[property] + 1 : 1;
        object value = property == "Name" ? fake.Name : fake.Values[property];
        if (value is Exception) throw (Exception)value;
        return value;
    }
}
namespace Siemens.Synthetic { internal sealed class SessionUncertain : Exception { } }
internal sealed class BrokenSequence : IEnumerable, IEnumerator, IDisposable
{
    internal string Fail;
    internal int Count, Index = -1, CurrentCalls;
    internal bool Disposed;
    internal object Value;
    public IEnumerator GetEnumerator() { if (Fail == "GetEnumerator") throw new IOException("source must not leak"); return this; }
    public bool MoveNext() { if (Fail == "MoveNext") throw new IOException("source must not leak"); return ++Index < Count; }
    public object Current { get { CurrentCalls++; if (Fail == "Current") throw new IOException("source must not leak"); return Value; } }
    public void Reset() { throw new NotSupportedException(); }
    public void Dispose() { Disposed = true; if (Fail == "Dispose") throw new IOException("source must not leak"); }
}
internal static class LabEventsTests
{
    private static readonly List<object> Results = new List<object>();
    private static string root, binary;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static void Test(string name, Action action)
    {
        try { action(); Results.Add(new { name = name, status = "PASS" }); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { Results.Add(new { name = name, status = "FAIL", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static EventFake Handler(object code, object global, object async, object eventType)
    {
        var script = new EventFake("SCRIPT");
        script.Values["ScriptCode"] = code; script.Values["GlobalDefinitionAreaScriptCode"] = global; script.Values["Async"] = async;
        var handler = new EventFake("HANDLER"); handler.Values["EventType"] = eventType; handler.Values["Script"] = script;
        return handler;
    }
    private static EventFake Node(string kind, params EventFake[] handlers)
    {
        var node = new EventFake(kind) { Supported = kind == "SCREEN" || kind == "ITEM" };
        node.Values["EventHandlers"] = handlers;
        return node;
    }
    private static EventFake Edge(EventFake node, string edge, params EventFake[] values)
    { node.Edges = node.Edges.Concat(new[] { edge }).ToArray(); node.Values[edge] = values; return node; }
    private static LabEventResult Read(EventFake node, LabEventLimits limits = null)
    { return new LabEventReader(new EventFakeAccess(), limits ?? new LabEventLimits()).Read(node, "/synthetic"); }
    private static bool Reason(LabEventResult result, string reason)
    { return result.observations.Any(x => (string)x["code"] == reason); }
    private static string Destination() { return Path.Combine(root, Guid.NewGuid().ToString("N")); }
    private static bool Reject(Action action)
    { try { action(); return false; } catch { return true; } }
    private enum EventKind { Loaded, Unloaded, Tapped, Up }
    internal static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--echo-args") { Console.WriteLine(Json.Serialize(args)); return 0; }
        root = args[0]; binary = args[1];
        Test("hierarchy_groups_subgroups_and_nested_items", () => {
            var nested = Edge(Node("ITEM", Handler("nested", "g", true, EventKind.Tapped)), "ScreenItems", Node("ITEM"));
            var screen = Edge(Node("SCREEN", Handler("screen", "g", false, EventKind.Loaded)), "ScreenItems", nested);
            var subgroup = Edge(Node("GROUP"), "Screens", screen);
            var group = Edge(Node("GROUP"), "Groups", subgroup);
            var software = Edge(Node("SOFTWARE"), "ScreenGroups", group);
            var r = Read(software);
            Check(r.objects.Count == 6 && r.handlers.Count == 2 && r.status == "COMPLETE_FOR_PUBLIC_SCOPE", "incomplete traversal");
            Check(r.objects.Any(x => ((string)x["locator"]).EndsWith("/ScreenGroups[0]/Groups[0]/Screens[0]/ScreenItems[0]/ScreenItems[0]")), "nested locator lost");
        });
        Test("all_configured_handler_types_and_raw_event_value", () => {
            var r = Read(Node("SCREEN", Handler("a", "", true, EventKind.Loaded), Handler("b", "", false, EventKind.Unloaded), Handler("c", "", true, "RowSelected")));
            Check(r.handlers.Count == 3 && (string)r.handlers[2]["event_type_type"] == "System.String" && r.row_selected_availability == "UNKNOWN", "event identity/qualification changed");
        });
        Test("unknown_enum_preserved_and_partial", () => {
            var r = Read(Node("ITEM", Handler("code", "", true, (EventKind)999)));
            Check((string)r.handlers[0]["event_type"] == "999" && Reason(r, "UNKNOWN_EVENT_VALUE") && r.status == "PARTIAL", "unknown enum hidden");
        });
        Test("empty_handler_is_present_empty", () => {
            var r = Read(Node("ITEM", Handler("", "", false, EventKind.Tapped)));
            Check((string)r.handlers[0]["presence"] == "PRESENT_EMPTY" && (string)r.handlers[0]["ScriptCode_sha256"] == LabEventArtifacts.Digest(new byte[0]), "empty is not absent");
        });
        Test("empty_code_with_global_declarations_retains_reader_semantics", () => {
            var r = Read(Node("ITEM", Handler("", "let shared=1;", true, "Tapped")));
            Check((string)r.handlers[0]["presence"] == "PRESENT_EMPTY" && (string)r.handlers[0]["GlobalDefinitionAreaScriptCode_status"] == "READ", "global declarations changed empty handler semantics");
        });
        Test("no_handlers_is_absent_only_for_complete_collection", () => {
            var r = Read(Node("SCREEN")); Check((string)r.objects[0]["handlers_status"] == "ABSENT" && r.handlers.Count == 0, "absence missing");
        });
        Test("null_handler_collection_is_unknown", () => {
            var n = Node("ITEM"); n.Values["EventHandlers"] = null; var r = Read(n);
            Check((string)r.objects[0]["handlers_status"] == "PARTIAL" && Reason(r, "NULL_VALUE_UNKNOWN"), "null became absent");
        });
        Test("unsupported_object_remains_visible", () => {
            var r = Read(Edge(Node("SCREEN"), "ScreenItems", Node("UNKNOWN")));
            Check(r.objects.Count == 2 && Reason(r, "UNSUPPORTED_OBJECT") && r.status == "PARTIAL", "unsupported silently skipped");
        });
        Test("encapsulated_children_have_unknown_coverage", () => {
            var n = Node("ITEM"); n.Hidden = true; var r = Read(n);
            Check((string)r.objects[0]["children_status"] == "UNKNOWN" && Reason(r, "ENCAPSULATED_CHILDREN_NOT_PUBLIC"), "hidden structure marked complete");
        });
        Test("duplicate_names_and_duplicate_events_have_distinct_files", () => {
            var r = Read(Edge(Node("SCREEN"), "ScreenItems", Node("ITEM", Handler("one", "g1", true, "Tapped"), Handler("two", "g2", true, "Tapped")), Node("ITEM", Handler("three", "g3", true, "Tapped"))));
            string dest = Destination(); LabEventArtifacts.Write(dest, r);
            Check(Directory.GetFiles(Path.Combine(dest, "scripts")).Length == 6 && r.handlers.Select(x => (string)x["ScriptCode_path"]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3, "filename collision");
        });
        Test("js_and_global_text_exact_unicode_mixed_eol_bom", () => {
            string code = "\ufeffconst s='Zażółć 日本 \ud83d\ude80 e\u0301';\r\nline2\nline3\r";
            string global = "let x=1;\n\r\n// global\r";
            var r = Read(Node("ITEM", Handler(code, global, true, "Tapped"))); string dest = Destination(); LabEventArtifacts.Write(dest, r);
            foreach (var pair in new[] { new[] { "ScriptCode", code }, new[] { "GlobalDefinitionAreaScriptCode", global } })
            {
                byte[] actual = File.ReadAllBytes(Path.Combine(dest, (string)r.handlers[0][pair[0] + "_path"]));
                Check(actual.SequenceEqual(new UTF8Encoding(false, true).GetBytes(pair[1])), "raw bytes changed");
            }
            var json = (Dictionary<string, object>)Json.DeserializeObject(File.ReadAllText(Path.Combine(dest, "events.json")));
            var rows = (object[])json["handlers"]; Check((string)((Dictionary<string, object>)rows[0])["ScriptCode"] == code, "JSON changed text");
        });
        Test("invalid_surrogate_never_silently_replaced", () => {
            var r = Read(Node("ITEM", Handler("\ud800", "valid", false, "Tapped")));
            Check(r.handlers[0]["ScriptCode"] == null && (string)r.handlers[0]["GlobalDefinitionAreaScriptCode"] == "valid" && Reason(r, "INVALID_UNICODE"), "lossy encoding accepted");
        });
        Test("code_getter_failure_keeps_independent_global_and_async", () => {
            var r = Read(Node("ITEM", Handler(new IOException("secret JS body"), "global", true, "Tapped")));
            Check(r.handlers[0]["ScriptCode"] == null && (string)r.handlers[0]["GlobalDefinitionAreaScriptCode"] == "global" && (bool)r.handlers[0]["async"] && r.status == "PARTIAL", "independent reads lost");
        });
        Test("global_failure_does_not_erase_code", () => {
            var r = Read(Node("ITEM", Handler("code", new IOException("secret"), false, "Tapped")));
            Check((string)r.handlers[0]["ScriptCode"] == "code" && (string)r.handlers[0]["GlobalDefinitionAreaScriptCode_status"] == "READ_FAILED", "global failure masked");
        });
        Test("async_failure_is_unknown_not_false", () => {
            var r = Read(Node("ITEM", Handler("code", "g", new IOException("secret"), "Tapped")));
            Check(r.handlers[0]["async"] == null && (string)r.handlers[0]["async_status"] == "UNKNOWN" && r.status == "PARTIAL", "async default manufactured");
        });
        Test("event_type_failure_is_partial", () => {
            var r = Read(Node("ITEM", Handler("code", "g", false, new IOException("secret"))));
            Check(r.handlers[0]["event_type"] == null && (string)r.handlers[0]["status"] == "PARTIAL", "event failure masked");
        });
        Test("script_getter_null_is_unknown", () => {
            var h = Handler("code", "g", false, "Tapped"); h.Values["Script"] = null; var r = Read(Node("ITEM", h));
            Check(r.handlers[0]["ScriptCode"] == null && Reason(r, "NULL_VALUE_UNKNOWN"), "null script became empty");
        });
        Test("null_text_getter_is_not_empty", () => {
            var r = Read(Node("ITEM", Handler(null, "g", false, "Tapped")));
            Check((string)r.handlers[0]["ScriptCode_status"] == "READ_FAILED" && (string)r.handlers[0]["presence"] == "PRESENT", "null became empty");
        });
        foreach (string failure in new[] { "GetEnumerator", "MoveNext", "Current", "Dispose" })
        {
            string selected = failure;
            Test("iterator_failure_" + selected, () => {
                var n = Node("ITEM"); var seq = new BrokenSequence { Fail = selected, Count = 1, Value = Handler("code", "g", false, "Tapped") }; n.Values["EventHandlers"] = seq;
                var r = Read(n); Check(r.status == "PARTIAL" && (string)r.objects[0]["handlers_status"] == "PARTIAL", "iterator failure became absent/complete");
                Check(selected == "GetEnumerator" || seq.Disposed, "iterator not disposed");
            });
        }
        Test("null_element_is_partial", () => {
            var n = Node("ITEM"); n.Values["EventHandlers"] = new object[] { null }; var r = Read(n); Check(Reason(r, "NULL_ELEMENT") && r.status == "PARTIAL", "null element hidden");
        });
        Test("collection_limit_probes_without_reading_excess_current", () => {
            var n = Node("ITEM"); var seq = new BrokenSequence { Count = 3, Value = Handler("code", "g", false, "Tapped") }; n.Values["EventHandlers"] = seq;
            var r = Read(n, new LabEventLimits { Handlers = 2 }); Check(r.handlers.Count == 2 && seq.CurrentCalls == 2 && seq.Disposed && Reason(r, "LIMIT_EXCEEDED"), "limit read excess object");
        });
        Test("exact_collection_limit_is_complete", () => {
            var n = Node("ITEM"); n.Values["EventHandlers"] = new BrokenSequence { Count = 2, Value = Handler("code", "g", false, "Tapped") };
            Check(Read(n, new LabEventLimits { Handlers = 2 }).status == "COMPLETE_FOR_PUBLIC_SCOPE", "exact limit treated as overflow");
        });
        Test("screen_collection_limit_records_omissions", () => {
            var n = Edge(Node("SOFTWARE"), "Screens", Node("SCREEN"), Node("SCREEN")); var r = Read(n, new LabEventLimits { Collection = 1 });
            Check(r.objects.Count == 2 && Reason(r, "LIMIT_EXCEEDED"), "screen limit ignored");
        });
        Test("depth_limit_handles_cycles", () => {
            var n = Node("GROUP"); Edge(n, "Groups", n); var r = Read(n, new LabEventLimits { Depth = 2 });
            Check(r.objects.Count == 3 && Reason(r, "LIMIT_EXCEEDED"), "cycle unbounded");
        });
        Test("depth_rejections_consume_global_budget", () => {
            var n = Edge(Node("GROUP"), "Groups", Node("GROUP"), Node("GROUP"), Node("GROUP"));
            var r = Read(n, new LabEventLimits { Depth = 0, Nodes = 2 });
            Check(r.objects.Count == 1 && r.observations.Count == 2, "depth omissions escaped node budget");
        });
        Test("global_node_limit_bounds_handlers_and_items", () => {
            var n = Edge(Node("SCREEN", Handler("x", "g", true, "Loaded")), "ScreenItems", Node("ITEM")); var r = Read(n, new LabEventLimits { Nodes = 2 });
            Check(r.objects.Count + r.handlers.Count == 2 && Reason(r, "LIMIT_EXCEEDED"), "global limit ignored");
        });
        Test("script_limit_does_not_truncate_text", () => {
            var r = Read(Node("ITEM", Handler("12345", "g", false, "Tapped")), new LabEventLimits { TextChars = 4 });
            Check(r.handlers[0]["ScriptCode"] == null && r.handlers[0]["ScriptCode_path"] == null && (string)r.handlers[0]["GlobalDefinitionAreaScriptCode"] == "g" && Reason(r, "LIMIT_EXCEEDED"), "script truncated or omitted silently");
        });
        Test("aggregate_text_limit_is_explicit", () => {
            var r = Read(Node("ITEM", Handler("1234", "12", false, "Tapped")), new LabEventLimits { TotalTextChars = 5 });
            Check((string)r.handlers[0]["ScriptCode"] == "1234" && r.handlers[0]["GlobalDefinitionAreaScriptCode"] == null && Reason(r, "LIMIT_EXCEEDED"), "aggregate text limit ignored");
        });
        Test("metadata_limit_keeps_locator_without_fake_name", () => {
            var n = Node("ITEM"); n.Name = new string('n', 513); var r = Read(n);
            Check(r.objects[0]["name"] == null && (string)r.objects[0]["locator"] == "/synthetic" && Reason(r, "LIMIT_EXCEEDED"), "metadata limit hidden");
        });
        Test("vendor_uncertainty_stops_content_without_retry", () => {
            var h = Handler(new Siemens.Synthetic.SessionUncertain(), "must not read", true, "Tapped"); var script = (EventFake)h.Values["Script"];
            var n = Node("ITEM", h, Handler("later", "g", true, "Up")); var r = Read(n);
            Check(r.handlers.Count == 1 && script.Calls["ScriptCode"] == 1 && !script.Calls.ContainsKey("GlobalDefinitionAreaScriptCode") && !script.Calls.ContainsKey("Async") && r.status == "PARTIAL", "uncertain session continued/retried");
        });
        Test("artifacts_hashes_lengths_identity_and_no_source_in_report", () => {
            var r = Read(Node("ITEM", Handler("secret source sentinel", "secret global sentinel", true, "Tapped"))); r.identity["pid"] = 123;
            string dest = Destination(); LabEventArtifacts.Write(dest, r);
            var manifest = (Dictionary<string, object>)Json.DeserializeObject(File.ReadAllText(Path.Combine(dest, "manifest.json")));
            foreach (Dictionary<string, object> file in (object[])manifest["files"])
            {
                byte[] actual = File.ReadAllBytes(Path.Combine(dest, (string)file["path"]));
                Check(actual.LongLength == Convert.ToInt64(file["length"]) && LabEventArtifacts.Digest(actual) == (string)file["sha256"], "manifest integrity mismatch");
            }
            Check(!File.Exists(Path.Combine(dest, ".incomplete")) && ((Dictionary<string, object>)manifest["identity"])["pid"].ToString() == "123", "identity/completion missing");
            string report = File.ReadAllText(Path.Combine(dest, "events-report.md"));
            Check(!report.Contains("secret source sentinel") && !report.Contains("secret global sentinel"), "source leaked to report");
        });
        Test("exception_messages_and_stack_do_not_leak", () => {
            var r = Read(Node("ITEM", Handler(new IOException("secret source sentinel"), "", false, "Tapped"))); string dest = Destination(); LabEventArtifacts.Write(dest, r);
            Check(!File.ReadAllText(Path.Combine(dest, "events.json")).Contains("secret source sentinel") && !File.ReadAllText(Path.Combine(dest, "events-report.md")).Contains("secret source sentinel"), "exception leaked");
        });
        Test("tool_owned_failure_codes_are_preserved_without_vendor_messages", () => {
            var r = new LabEventResult { limits = new LabEventLimits() };
            r.Issue("/", "device-discovery", "EXPORT_READ_FAILED", new LabEventFailure("DEVICE_ITEM_LIMIT_EXCEEDED"));
            Check(r.status == "PARTIAL" && (string)r.observations[0]["detail_code"] == "DEVICE_ITEM_LIMIT_EXCEEDED", "tool-owned limit reason lost");
        });
        Test("report_escapes_untrusted_metadata", () => {
            var n = Node("SCREEN"); n.Name = "<script>|`\r\n"; string dest = Destination(); LabEventArtifacts.Write(dest, Read(n));
            string report = File.ReadAllText(Path.Combine(dest, "events-report.md")); Check(!report.Contains("<script>") && report.Contains("&#124;") && report.Contains("&#10;"), "metadata injection");
        });
        Test("existing_export_never_overwritten", () => {
            var r = Read(Node("ITEM", Handler("one", "", true, "Tapped"))); string dest = Destination(); LabEventArtifacts.Write(dest, r);
            var before = Directory.GetFiles(dest, "*", SearchOption.AllDirectories).ToDictionary(x => x, x => LabEventArtifacts.Digest(File.ReadAllBytes(x)));
            Check(Reject(() => LabEventArtifacts.Write(dest, Read(Node("SCREEN")))), "existing destination accepted");
            Check(before.All(x => File.Exists(x.Key) && LabEventArtifacts.Digest(File.ReadAllBytes(x.Key)) == x.Value) && !File.Exists(Path.Combine(dest, ".incomplete")), "existing export touched");
        });
        Test("artifact_budget_omits_scripts_with_partial_report", () => {
            var r = Read(Node("ITEM", Handler(new string('x', 20000), "", true, "Tapped")), new LabEventLimits { ArtifactBytes = 10000 }); string dest = Destination(); LabEventArtifacts.Write(dest, r);
            Check(r.status == "PARTIAL" && Reason(r, "LIMIT_EXCEEDED") && r.handlers[0]["ScriptCode"] == null && Directory.GetFiles(Path.Combine(dest, "scripts")).Length == 0, "artifact budget not explicit");
            Check(Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length) <= 10000, "artifact byte budget exceeded");
        });
        Test("impossible_metadata_budget_preserves_unpublished_evidence", () => {
            var r = Read(Node("SCREEN"), new LabEventLimits { ArtifactBytes = 1 }); string dest = Destination();
            int count = Directory.GetDirectories(root, ".event-incomplete-*").Length;
            Check(Reject(() => LabEventArtifacts.Write(dest, r)) && !Directory.Exists(dest) && Directory.GetDirectories(root, ".event-incomplete-*").Length == count + 1, "failed write published or lost evidence");
            Check(Directory.GetFiles(root, ".incomplete", SearchOption.AllDirectories).Any(x => File.ReadAllText(x).Contains("LIMIT_EXCEEDED")), "limit reason not retained");
        });
        Test("production_adapter_IL_allows_only_public_read_members", AuditAdapter);
        Test("lab_output_policy_accepts_user_selected_directories", AuditOutputPolicy);
        File.WriteAllText(Path.Combine(root, "results.json"), Json.Serialize(new { tests = Results, failed = Results.Count(x => Json.Serialize(x).Contains("\"FAIL\"")), production_binary_sha256 = LabEventArtifacts.Digest(File.ReadAllBytes(binary)) }), new UTF8Encoding(false));
        int failed = Results.Count(x => Json.Serialize(x).Contains("\"FAIL\""));
        Console.WriteLine("TOTAL " + Results.Count + "; FAILED " + failed);
        return failed == 0 ? 0 : 1;
    }
    private static Assembly LoadLab()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string path in new[] { @"C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19", @"C:\Program Files\Siemens\Automation\Portal V19\Bin\PublicAPI" })
                if (File.Exists(Path.Combine(path, name))) return Assembly.LoadFrom(Path.Combine(path, name));
            return null;
        };
        return Assembly.LoadFrom(binary);
    }
    private static void AuditAdapter()
    {
        var assembly = LoadLab();
        var vendor = Assembly.LoadFrom(@"C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\Siemens.Engineering.dll");
        var allOps = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => unchecked((ushort)o.Value));
        var readTypes = new HashSet<string>();
        string[] newMethods = { "RunExportEvents", "CheckEventProcess", "CheckEventIdentity", "EventSoftware", "EventDeviceItems" };
        foreach (var type in assembly.GetTypes().Where(t => t.Name == "V19EventAccess" || t.Name == "LabExporter" || t.FullName.Contains("RunExportEvents")))
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (type.Name == "LabExporter" && !newMethods.Contains(method.Name)) continue;
            if (method.GetMethodBody() == null) continue;
            byte[] il = method.GetMethodBody().GetILAsByteArray();
            for (int i = 0; i < il.Length; )
            {
                ushort key = il[i++]; if (key == 0xfe) key = (ushort)(0xfe00 | il[i++]);
                OpCode op = allOps[key]; int size;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: size = 0; break;
                    case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                    case OperandType.InlineVar: size = 2; break;
                    case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                    case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(il, i); break;
                    default: size = 4; break;
                }
                if (op.OperandType == OperandType.InlineMethod)
                {
                    MethodBase target = method.Module.ResolveMethod(BitConverter.ToInt32(il, i)); string name = target.Name;
                    string declaring = target.DeclaringType.FullName;
                    if (declaring.StartsWith("Siemens.", StringComparison.Ordinal))
                    {
                        bool read = name.StartsWith("get_", StringComparison.Ordinal) || name == "GetEnumerator" || name == "GetService" || name == "GetProcesses" || name == "GetCurrentProcess"
                            || (name == "Attach" && declaring == "Siemens.Engineering.TiaPortalProcess") || (name == "Dispose" && declaring == "Siemens.Engineering.TiaPortal");
                        Check(read, "mutating/unapproved vendor call: " + declaring + "." + name);
                        Check(name != "get_Dynamizations" && name != "get_PropertyEventHandlers", "dynamizations mixed with handlers");
                        if (name == "get_EventHandlers") readTypes.Add(declaring);
                    }
                    Check(!(declaring.StartsWith("System.Reflection.") && (name == "Invoke" || name == "SetValue")), "reflection dispatch introduced");
                }
                i += size;
            }
        }
        var expected = vendor.GetExportedTypes().Where(t => t.FullName.StartsWith("Siemens.Engineering.HmiUnified.UI.") && t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Any(p => p.Name == "EventHandlers")).Select(t => t.FullName).ToArray();
        Check(expected.Length == 39 && expected.All(readTypes.Contains), "public V19 EventHandlers types missing from allowlist");
        // These calls inspect metadata only; never instantiate or invoke a Siemens object.
    }
    private static void AuditOutputPolicy()
    {
        Type type = LoadLab().GetType("LabExporter", true);
        MethodInfo method = type.GetMethod("ValidateEventOutput", BindingFlags.NonPublic | BindingFlags.Static);
        string project = @"X:\TESTY_TIA_Bridge\T05_RecipeSim_V19_Testowy";
        foreach (string path in new[] { @"X:\Projects\TIA_Bridge\EventExports", @"X:\Projects\TIA_Bridge-HMI-JS-02\events", @"X:\TESTY_TIA_Bridge\Scripts\events", @"X:\TESTY_TIA_Bridge\src\events", project + @"\events", @"X:\TESTY_TIA_Bridge\bin\events", @"X:\" })
            Check((string)method.Invoke(null, new object[] { path, project }) == Path.GetFullPath(path), "selected output rejected: " + path);
        Check((string)method.Invoke(null, new object[] { Path.Combine(root, "EventExports"), project }) == Path.Combine(root, "EventExports"), "valid external output rejected");
    }
}
