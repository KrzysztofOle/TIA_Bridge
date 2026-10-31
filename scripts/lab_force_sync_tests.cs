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
    private static void Check(bool value) { if (!value) throw new Exception("assertion failed"); }
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
    { foreach (string ext in new[] { ".hmi.js", ".hmi.yml" }) File.Copy(Path.Combine(source, "Synthetic" + ext), Path.Combine(destination, "Synthetic" + ext)); }
    private static string Pair(string js = "old", string yml = "metadata")
    { string p = New(root); Write(Path.Combine(p, "Synthetic.hmi.js"), js); Write(Path.Combine(p, "Synthetic.hmi.yml"), yml); return p; }
    private static Dictionary<string, object> Expected(string pair)
    { return new Dictionary<string, object> { { "baseline_js_sha256", Hash(Path.Combine(pair, "Synthetic.hmi.js")) }, { "baseline_yml_sha256", Hash(Path.Combine(pair, "Synthetic.hmi.yml")) } }; }
    private static string Prepare(string source)
    { string p = New(root); Call("PrepareForceImportInput", source, p); return p; }
    private static string Readback(string prepared)
    {
        string p = New(exports), native = Path.Combine(p, "Synthetic", ".native"); Directory.CreateDirectory(native); CopyPair(prepared, native);
        var files = Directory.GetFiles(native).Select(f => new { name = Path.GetFileName(f), sha256 = Hash(f), length = new FileInfo(f).Length }).ToArray();
        Write(Path.Combine(p, "force-import-readback.json"), json.Serialize(new { status = "PASS_EXACT_MODULE_SET_AND_READBACK", saved = true,
            import_returned = true, is_modified_after = false, compile = new { state = "Success", error_count = 0, warning_count = 0 },
            modules = new[] { new { name = "Synthetic", native_files = files } } }));
        return p;
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
            Test("compile state policy " + state, () => { Action run = () => Call("ValidateCompileOutcome", s, 0, 0); if (s == "Success" || s == "Information") run(); else Reject(run, "HMI_COMPILE_NOT_ACCEPTED"); });
        }
        Test("compile error count blocks even Success", () => Reject(() => Call("ValidateCompileOutcome", "Success", 1, 0), "HMI_COMPILE_NOT_ACCEPTED"));
        Test("compile warning count blocks publication", () => Reject(() => Call("ValidateCompileOutcome", "Success", 0, 1), "HMI_COMPILE_NOT_ACCEPTED"));
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
