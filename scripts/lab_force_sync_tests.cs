// Synthetic filesystem and injected failure tests. Never attaches to Siemens.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

internal static class LabForceSyncTests
{
    private static Type lab;
    private static string root, exports, package;
    private static JavaScriptSerializer json = new JavaScriptSerializer();
    private static List<object> results = new List<object>();
    private static object Call(string name, params object[] args)
    {
        try { return lab.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    private static string New(string parent) { string p = Path.Combine(parent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
    private static void Check(bool value) { Check(value, "assertion failed"); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action, string code)
    {
        try { action(); } catch (Exception ex) { if (ex.Message.Contains(code)) return; throw; }
        throw new Exception("expected refusal: " + code);
    }
    private static void Test(string name, Action body)
    {
        var previous = Console.Error;
        var diagnostics = new StringWriter();
        Console.SetError(diagnostics);
        try { body(); results.Add(new { name, status = "PASS", diagnostics = diagnostics.ToString() }); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { results.Add(new { name, status = "FAIL", error = ex.ToString(), diagnostics = diagnostics.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex); }
        finally { Console.SetError(previous); }
    }
    private static string Hash(string file)
    { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").ToLowerInvariant(); }
    private static void Write(string file, string value) { File.WriteAllText(file, value, new UTF8Encoding(false)); }
    private static string Clone(string source) { string dest = New(root); Call("CopyDirectory", source, dest); return dest; }
    private static void CopyPair(string source, string destination)
    { foreach (string ext in new[] { ".hmi.js", ".hmi.yml" }) File.Copy(Path.Combine(source, "Synthetic" + ext), Path.Combine(destination, "Synthetic" + ext), true); }
    private static string Pair(string js = "old", string yml = "metadata")
    { string p = New(root); Write(Path.Combine(p, "Synthetic.hmi.js"), js); Write(Path.Combine(p, "Synthetic.hmi.yml"), yml); return p; }
    private static Dictionary<string, object> Expected(string pair)
    { return new Dictionary<string, object> { { "baseline_js_sha256", Hash(Path.Combine(pair, "Synthetic.hmi.js")) }, { "baseline_yml_sha256", Hash(Path.Combine(pair, "Synthetic.hmi.yml")) } }; }
    private static string Prepare(string source)
    { string p = New(root); Call("PrepareForceImportInput", source, p); return p; }
    private static string Readback(string prepared, string warning = null, bool noOp = false)
    {
        string p = New(exports), native = Path.Combine(p, "Synthetic", ".native"); Directory.CreateDirectory(native); CopyPair(prepared, native);
        var files = Directory.GetFiles(native).Select(f => new { name = Path.GetFileName(f), sha256 = Hash(f), length = new FileInfo(f).Length }).ToArray();
        object compilation = noOp
            ? (object)new { status = "NOT_RUN", state = "NOT_RUN", error_count = 0, warning_count = 0, messages = new object[0] }
            : warning == null
            ? (object)new { state = "Success", error_count = 0, warning_count = 0, messages = new object[0] }
            : new { state = "Warning", error_count = 0, warning_count = 1, messages = new object[] {
                new { path = (string)null, state = "Warning", description = warning },
                new { path = (string)null, state = "Warning", description = "Compiling finished (errors: 0; warnings: 1)" } } };
        Write(Path.Combine(p, "force-import-readback.json"), json.Serialize(new { status = "PASS_EXACT_MODULE_SET_AND_READBACK",
            mode = noOp ? "NO_OP" : "UPDATED", module_count = 1,
            saved = !noOp, import_returned = !noOp, is_modified_after = false, compile = compilation,
            module_plan = new[] { new { module_name = "Synthetic", action = "UPDATE" } },
            modules = new[] { new { name = "Synthetic", native_files = files } } }));
        return p;
    }
    private static Dictionary<string, Dictionary<string, object>> ExpectedInput(string prepared)
    {
        var root = (Dictionary<string, object>)json.DeserializeObject(File.ReadAllText(Path.Combine(prepared, "input-manifest.json")));
        return ((object[])root["modules"]).Cast<Dictionary<string, object>>()
            .ToDictionary(x => (string)x["module_name"], x => x, StringComparer.Ordinal);
    }
    private static string Preflight(string prepared)
    {
        string p = New(root), module = Path.Combine(p, "Synthetic"); Directory.CreateDirectory(module);
        CopyPair(prepared, module); return p;
    }
    private static int Main(string[] args)
    {
        root = args[0]; exports = args[1]; package = args[2];
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in new[] { @"C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19", @"C:\Program Files\Siemens\Automation\Portal V19\Bin\PublicAPI" })
                if (File.Exists(Path.Combine(dir, name))) return Assembly.LoadFrom(Path.Combine(dir, name));
            return null;
        };
        lab = Assembly.LoadFrom(args[3]).GetType("LabExporter", true);
        Test("unchanged historical baseline accepted in strict mode", () => { string p = Pair(); Check(Call("CheckHistoricalBaseline", "Synthetic", p, Expected(p), false) == null); });
        Test("strict mode rejects previous import JS baseline", () => { string old = Pair(); Reject(() => Call("CheckHistoricalBaseline", "Synthetic", Pair("new"), Expected(old), false), "TIA_BASELINE_DRIFT_BEFORE_IMPORT"); });
        Test("forced replacement records both historical and current hashes", () => {
            string old = Pair(), current = Pair("new"); string report = json.Serialize(Call("CheckHistoricalBaseline", "Synthetic", current, Expected(old), true));
            Check(report.Contains(Hash(Path.Combine(old, "Synthetic.hmi.js"))) && report.Contains(Hash(Path.Combine(current, "Synthetic.hmi.js"))));
        });
        Test("strict YAML drift rejected", () => { string old = Pair(); Reject(() => Call("CheckHistoricalBaseline", "Synthetic", Pair("old", "new metadata"), Expected(old), false), "TIA_BASELINE_DRIFT_BEFORE_IMPORT"); });
        Test("forced YAML drift recorded", () => { string old = Pair(); Check(Call("CheckHistoricalBaseline", "Synthetic", Pair("old", "new metadata"), Expected(old), true) != null); });
        Test("forced mode still rejects a missing native pair", () => { string p = New(root); Reject(() => Call("CheckHistoricalBaseline", "Synthetic", p, Expected(Pair()), true), "NATIVE_PAIR_MISSING"); });
        Test("valid editable JavaScript change is reported and prepared", () => {
            string input = Clone(package), part = Path.Combine(input, "Synthetic", "alpha.js");
            Write(part, File.ReadAllText(part).Replace("return 1", "return 2"));
            string prepared = Prepare(input), manifest = Path.Combine(prepared, "input-manifest.json");
            Check(File.ReadAllText(manifest).Contains("SOURCE_EDITED"));
            Check(File.ReadAllText(Path.Combine(prepared, "Synthetic.hmi.js")).Contains("return 2"));
        });
        Test("native manifest part hashes are checked against native bytes", () => {
            string input = Clone(package), rootManifestPath = Path.Combine(input, "manifest.json");
            var rootManifest = (Dictionary<string, object>)json.DeserializeObject(File.ReadAllText(rootManifestPath));
            var rootRow = ((object[])rootManifest["modules"]).Cast<Dictionary<string, object>>().Single();
            var rootPart = ((object[])rootRow["parts"])[0] as Dictionary<string, object>;
            var modulePath = Path.Combine(input, "Synthetic", ".native", "manifest.json");
            var moduleManifest = (Dictionary<string, object>)json.DeserializeObject(File.ReadAllText(modulePath));
            var modulePart = ((object[])moduleManifest["parts"])[0] as Dictionary<string, object>;
            string invalidHash = new string('0', 64);
            rootPart["sha256"] = invalidHash; modulePart["sha256"] = invalidHash;
            Write(rootManifestPath, json.Serialize(rootManifest)); Write(modulePath, json.Serialize(moduleManifest));
            Reject(() => Prepare(input), "MODULE_MANIFEST_NATIVE_PART_HASH_MISMATCH");
        });
        Test("native artifact bytes inconsistent with its manifest are rejected", () => {
            string input = Clone(package), native = Path.Combine(input, "Synthetic", ".native", "Synthetic.hmi.js");
            byte[] bytes = File.ReadAllBytes(native); bytes[0] ^= 1; File.WriteAllBytes(native, bytes);
            Reject(() => Prepare(input), "MODULE_NATIVE_ARTIFACT_INVALID");
        });
        Test("prepared input comparison identifies exact JS and YAML mismatches", () => {
            string prepared = Prepare(package), native = Preflight(prepared);
            var map = ExpectedInput(prepared);
            var result = (Dictionary<string, object>)Call("ComparePreparedInputWithPreflight", new[] { "Synthetic" }, map, native);
            Check((bool)result["matches"], "initial pair comparison should match: " + json.Serialize(result));
            Write(Path.Combine(native, "Synthetic", "Synthetic.hmi.js"), "different js");
            result = (Dictionary<string, object>)Call("ComparePreparedInputWithPreflight", new[] { "Synthetic" }, map, native);
            Check(!(bool)result["matches"] && json.Serialize(result["differences"]).Contains("JS_CONTENT_MISMATCH"), "JS difference not identified: " + json.Serialize(result));
            CopyPair(prepared, Path.Combine(native, "Synthetic"));
            Write(Path.Combine(native, "Synthetic", "Synthetic.hmi.yml"), "different yaml");
            result = (Dictionary<string, object>)Call("ComparePreparedInputWithPreflight", new[] { "Synthetic" }, map, native);
            Check(!(bool)result["matches"] && json.Serialize(result["differences"]).Contains("YAML_CONTENT_MISMATCH"), "YAML difference not identified: " + json.Serialize(result));
            result = (Dictionary<string, object>)Call("ComparePreparedInputWithPreflight", new[] { "Missing" }, map, native);
            Check(!(bool)result["matches"] && json.Serialize(result["differences"]).Contains("PREPARED_MODULE_MISSING"), "module set difference not identified: " + json.Serialize(result));
        });
        Test("NO_OP decision invokes no Import Compile or Save delegates", () => {
            var calls = new List<string>();
            bool changed = (bool)Call("ExecuteForceSyncDecision", true, (Action)(() => calls.Add("noop")),
                (Action)(() => { calls.Add("import"); calls.Add("compile"); calls.Add("save"); }));
            Check(!changed && String.Join(",", calls) == "noop");
        });
        foreach (string extension in new[] { ".hmi.js", ".hmi.yml", "unchanged" })
        {
            string ext = extension;
            Test("fresh in-operation recheck " + ext, () => {
                string baseline = New(root), native = Path.Combine(baseline, "Synthetic"); Directory.CreateDirectory(native); CopyPair(Pair(), native);
                Action<string, string> export = (name, target) => { CopyPair(native, target); if (ext != "unchanged") Write(Path.Combine(target, name + ext), "drift"); };
                Action run = () => Call("RecheckLiveBaseline", new[] { "Synthetic" }, baseline, New(root), export);
                if (ext == "unchanged") run(); else Reject(run, "TIA_CHANGED_DURING_IMPORT_PREFLIGHT");
            });
        }
        foreach (string state in new[] { "Success", "Information", "Warning", "Error", "UNKNOWN" })
        {
            string s = state;
            Test("compile state policy " + state, () => { Action run = () => Call("ValidateCompileOutcome", s, 0, 0, new string[0]); if (s == "Success" || s == "Information") run(); else Reject(run, "HMI_COMPILE_NOT_ACCEPTED"); });
        }
        string anonymousWarning = "The user \"Anonymous\" is not supported by WinCC Unified Runtime devices and is not available in runtime.";
        Test("known Anonymous runtime warning is accepted", () => Call("ValidateCompileOutcome", "Warning", 0, 1, new[] { anonymousWarning, "Compiling finished (errors: 0; warnings: 1)" }));
        Test("unknown compile warning still blocks publication", () => Reject(() => Call("ValidateCompileOutcome", "Warning", 0, 1, new[] { "Different warning", "Compiling finished (errors: 0; warnings: 1)" }), "HMI_COMPILE_NOT_ACCEPTED"));
        Test("known and unknown warnings block publication", () => Reject(() => Call("ValidateCompileOutcome", "Warning", 0, 1, new[] { anonymousWarning, "Different warning", "Compiling finished (errors: 0; warnings: 1)" }), "HMI_COMPILE_NOT_ACCEPTED"));
        Test("compile error count blocks even Success", () => Reject(() => Call("ValidateCompileOutcome", "Success", 1, 0, new string[0]), "HMI_COMPILE_NOT_ACCEPTED"));
        Test("known warning with compile errors still blocks", () => Reject(() => Call("ValidateCompileOutcome", "Warning", 1, 1, new[] { anonymousWarning }), "HMI_COMPILE_NOT_ACCEPTED"));
        Test("additional warning count blocks even when known warning is present", () => Reject(() => Call("ValidateCompileOutcome", "Warning", 0, 2, new[] { anonymousWarning, "Compiling finished (errors: 0; warnings: 2)" }), "HMI_COMPILE_NOT_ACCEPTED"));
        foreach (string fault in new[] { "none", "import", "compile", "save" })
        {
            string fail = fault;
            Test("import compile save failure barrier " + fail, () => {
                var calls = new List<string>();
                Action<string> step = name => { calls.Add(name); if (name == fail) throw new Exception("injected_" + name); };
                Action run = () => { Call("ExecuteImportCompileSave", (Action)(() => step("import")), (Action)(() => step("compile")), (Action)(() => step("save"))); calls.Add("export"); };
                if (fail == "none") { run(); Check(String.Join(",", calls) == "import,compile,save,export"); }
                else { Reject(run, "injected_" + fail); Check(calls.Last() == fail && !calls.Contains("export")); }
            });
        }
        Test("plain import does not compile", () => { var calls = new List<string>(); Call("ExecuteImportCompileSave", (Action)(() => calls.Add("import")), null, (Action)(() => calls.Add("save"))); Check(String.Join(",", calls) == "import,save"); });
        foreach (string mutation in new[] { "edit", "add", "delete", "empty-directory" })
        {
            string change = mutation;
            Test("workspace drift preserves local data: " + change, () => {
                string input = Clone(package), snapshot = Clone(input), staged = Path.Combine(New(root), "stage"), backup = Path.Combine(New(root), "backup");
                string part = Path.Combine(input, "Synthetic", "alpha.js");
                if (change == "edit") Write(part, "local edit");
                if (change == "add") Write(Path.Combine(input, "extra.txt"), "local file");
                if (change == "delete") File.Delete(part);
                if (change == "empty-directory") Directory.CreateDirectory(Path.Combine(input, "new-directory"));
                Reject(() => Call("PublishSyncPackage", package, input, snapshot, staged, backup), change == "edit" ? "STAGING_SHA256_MISMATCH" : change == "empty-directory" ? "WORKSPACE_DIRECTORY_SET_CHANGED" : "STAGING_FILE_SET_MISMATCH");
                Check(Directory.Exists(input) && !Directory.Exists(backup));
                if (change == "edit") Check(File.ReadAllText(part) == "local edit");
            });
        }
        Test("byte-identical publication is a filesystem NO_OP with unchanged timestamps", () => {
            string input = Clone(package), marker = Path.Combine(input, "Synthetic", "alpha.js");
            DateTime created = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), written = new DateTime(2026, 10, 2, 13, 0, 0, DateTimeKind.Utc);
            File.SetCreationTimeUtc(marker, created); File.SetLastWriteTimeUtc(marker, written);
            string snapshot = Clone(input), stage = Path.Combine(New(root), "stage"), backup = Path.Combine(New(root), "backup");
            var result = (Dictionary<string, object>)Call("PublishSyncPackage", package, input, snapshot, stage, backup);
            Check(!(bool)result["published"] && !Directory.Exists(stage) && !Directory.Exists(backup));
            Check(File.GetCreationTimeUtc(marker) == created && File.GetLastWriteTimeUtc(marker) == written);
        });
        Test("differential folder publication preserves timestamps of byte-identical files", () => {
            string input = Clone(package), unchanged = Path.Combine(input, "Synthetic", ".native", "Synthetic.hmi.yml");
            DateTime created = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc), written = new DateTime(2026, 10, 4, 13, 0, 0, DateTimeKind.Utc);
            File.SetCreationTimeUtc(unchanged, created); File.SetLastWriteTimeUtc(unchanged, written);
            string changed = Path.Combine(input, "Synthetic", "alpha.js"); Write(changed, "export function alpha() { return 0; }\r\n");
            string snapshot = Clone(input), stage = Path.Combine(New(root), "stage"), backup = Path.Combine(New(root), "backup");
            var result = (Dictionary<string, object>)Call("PublishSyncPackage", package, input, snapshot, stage, backup);
            Check((bool)result["published"] && Directory.Exists(backup) && !Directory.Exists(stage));
            Check(Hash(Path.Combine(input, "Synthetic", "alpha.js")) == Hash(Path.Combine(package, "Synthetic", "alpha.js")));
            Check(File.GetCreationTimeUtc(unchanged) == created && File.GetLastWriteTimeUtc(unchanged) == written);
        });
        Test("full force-sync NO_OP reports three independent PASS statuses without mutation", () => {
            string input = Clone(package), marker = Path.Combine(input, "Synthetic", "alpha.js"), snapshot = Clone(input);
            DateTime created = File.GetCreationTimeUtc(marker), written = File.GetLastWriteTimeUtc(marker);
            string prepared = Prepare(input), readback = Readback(prepared, null, true), token = Guid.NewGuid().ToString("N");
            Check((int)Call("FinishForceSync", readback, input, snapshot, "synthetic-lab03p", token) == 0);
            string reportPath = Path.Combine(@"X:\TESTY_TIA_Bridge\evidence", "LAB-03P-force-sync-" + token + ".json");
            var report = (Dictionary<string, object>)json.DeserializeObject(File.ReadAllText(reportPath));
            Check((string)report["mode"] == "NO_OP" && (string)report["sync_status"] == "SYNC PASS");
            Check((string)report["readback_status"] == "READBACK MATCH" && (string)report["roundtrip_status"] == "ROUNDTRIP CLEAN");
            Check((string)report["compile_status"] == "NOT_RUN" && !(bool)report["backup_created"] && !(bool)report["folder_published"]);
            Check(File.GetCreationTimeUtc(marker) == created && File.GetLastWriteTimeUtc(marker) == written);
        });
        Test("TIA NO_OP can refresh stale repository artifacts and reports UPDATED", () => {
            string input = Clone(package), yaml = Path.Combine(input, "Synthetic", ".native", "Synthetic.hmi.yml");
            DateTime created = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc), written = new DateTime(2026, 10, 6, 13, 0, 0, DateTimeKind.Utc);
            File.SetCreationTimeUtc(yaml, created); File.SetLastWriteTimeUtc(yaml, written);
            string source = Path.Combine(input, "Synthetic", "alpha.js");
            Write(source, File.ReadAllText(source).Replace("return 1", "return 2"));
            string snapshot = Clone(input), prepared = Prepare(input), readback = Readback(prepared, null, true), token = Guid.NewGuid().ToString("N");
            Check((int)Call("FinishForceSync", readback, input, snapshot, "synthetic-lab03p", token) == 0);
            string reportPath = Path.Combine(@"X:\TESTY_TIA_Bridge\evidence", "LAB-03P-force-sync-" + token + ".json");
            var report = (Dictionary<string, object>)json.DeserializeObject(File.ReadAllText(reportPath));
            Check((string)report["mode"] == "UPDATED" && (string)report["tia_action"] == "NO_OP");
            Check((string)report["repository_action"] == "UPDATED" && (string)report["compile_status"] == "NOT_RUN");
            Check((bool)report["backup_created"] && Directory.Exists((string)report["previous_workspace_backup"]));
            Check(File.GetCreationTimeUtc(yaml) == created && File.GetLastWriteTimeUtc(yaml) == written);
        });
        Test("publish callback drift restores previous workspace", () => {
            string input = Clone(package), snapshot = Clone(input), staged = Clone(package), backup = Path.Combine(New(root), "backup");
            Reject(() => Call("ReplaceOutputWithRollback", staged, input, backup, (Action)(() => {
                Write(Path.Combine(backup, "Synthetic", "alpha.js"), "concurrent edit"); Call("VerifyWorkspaceSnapshot", backup, snapshot);
            })), "STAGING_SHA256_MISMATCH");
            Check(File.ReadAllText(Path.Combine(input, "Synthetic", "alpha.js")) == "concurrent edit" && Directory.Exists(staged));
        });
        Test("two successive sync publications refresh native baseline byte exactly", () => {
            string input = Clone(package);
            for (int cycle = 1; cycle <= 2; cycle++)
            {
                string part = Path.Combine(input, "Synthetic", "alpha.js");
                Write(part, File.ReadAllText(part).Replace("return " + (cycle == 1 ? "1" : "2"), "return " + (cycle + 1)));
                string snapshot = Clone(input), prepared = Prepare(snapshot), readback = Readback(prepared), before = Hash(part);
                Check((int)Call("FinishForceSync", readback, input, snapshot, "synthetic-binding", Guid.NewGuid().ToString("N")) == 0);
                Call("ValidatePackage", input);
                Check(before == Hash(part));
                string rebuilt = Prepare(input);
                foreach (string ext in new[] { ".hmi.js", ".hmi.yml" }) Check(Hash(Path.Combine(input, "Synthetic", ".native", "Synthetic" + ext)) == Hash(Path.Combine(rebuilt, "Synthetic" + ext)));
                Check(File.ReadAllText(part).Contains("return " + (cycle + 1)));
            }
        });
        Test("known Anonymous warning in verified receipt permits publication", () => {
            string input = Clone(package), snapshot = Clone(input), anonymous = "The user \"Anonymous\" is not supported by WinCC Unified Runtime devices and is not available in runtime.";
            string readback = Readback(Prepare(input), anonymous);
            Check((int)Call("FinishForceSync", readback, input, snapshot, "synthetic-binding", Guid.NewGuid().ToString("N")) == 0);
            Call("ValidatePackage", input);
        });
        Test("unknown warning in verified receipt prevents publication", () => {
            string input = Clone(package), snapshot = Clone(input), readback = Readback(Prepare(input), "Different warning");
            Check((int)Call("FinishForceSync", readback, input, snapshot, "synthetic-binding", Guid.NewGuid().ToString("N")) == 2);
            Call("VerifyWorkspaceSnapshot", input, snapshot);
        });
        Test("readback tamper prevents publication", () => {
            string input = Clone(package), snapshot = Clone(input), readback = Readback(Prepare(input));
            Write(Path.Combine(readback, "Synthetic", ".native", "Synthetic.hmi.js"), "tampered");
            Check((int)Call("FinishForceSync", readback, input, snapshot, "synthetic-binding", Guid.NewGuid().ToString("N")) == 2);
            Call("VerifyWorkspaceSnapshot", input, snapshot);
        });
        Test("invalid compile receipt prevents publication", () => {
            string input = Clone(package), snapshot = Clone(input), readback = Readback(Prepare(input));
            string receipt = Path.Combine(readback, "force-import-readback.json"); Write(receipt, File.ReadAllText(receipt).Replace("\"compile\":{", "\"compile\":null,\"ignored\":{"));
            Check((int)Call("FinishForceSync", readback, input, snapshot, "synthetic-binding", Guid.NewGuid().ToString("N")) == 2);
            Call("VerifyWorkspaceSnapshot", input, snapshot);
        });
        int failures = results.Count(r => json.Serialize(r).Contains("\"status\":\"FAIL\""));
        Write(Path.Combine(root, "results.json"), json.Serialize(new { status = failures == 0 ? "PASS" : "FAIL", passed = results.Count - failures, failed = failures, results, binary_sha256 = Hash(args[3]) }));
        Console.WriteLine("RESULTS=" + Path.Combine(root, "results.json")); return failures == 0 ? 0 : 1;
    }
}
