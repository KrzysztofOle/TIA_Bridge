// Exact laboratory publisher against synthetic captured values. No Siemens calls.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

internal static class LabEventSyncTests
{
    private static string root;
    private static List<object> results = new List<object>();
    private static JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 67108864 };
    private static void Check(bool ok) { if (!ok) throw new Exception("assertion failed"); }
    private static void Test(string name, Action body)
    {
        try { body(); results.Add(new { name, status = "PASS" }); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { results.Add(new { name, status = "FAIL", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex); }
        finally { LabEventSync.Checkpoint = null; }
    }
    private static void Reject(Action body)
    { try { body(); } catch (Exception ex) { if (ex is LabEventFailure || ex is IOException) return; throw; } throw new Exception("expected refusal"); }
    private static LabEventResult Fixture(string code = "old", bool handler = true, bool screen = true, int offset = 0)
    {
        var software = new EventFake("SOFTWARE") { Name = "HMI", Edges = new[] { "Screens" } };
        var s = new EventFake("SCREEN") { Name = "Main", Supported = true };
        var script = new EventFake("SCRIPT"); script.Values["ScriptCode"] = code; script.Values["GlobalDefinitionAreaScriptCode"] = "// g"; script.Values["Async"] = true;
        var h = new EventFake("HANDLER"); h.Values["EventType"] = "Loaded"; h.Values["Script"] = script;
        s.Values["EventHandlers"] = handler ? new[] { h } : new EventFake[0];
        software.Values["Screens"] = screen ? new[] { s } : new EventFake[0];
        var r = new LabEventReader(new EventFakeAccess(), new LabEventLimits()).Read(software, "/HMI/Synthetic/HMI");
        r.identity["project_path"] = "X:/Synthetic.ap19"; r.identity["device_path"] = new[] { "Synthetic", "HMI" };
        if (offset != 0) {
            foreach (var x in r.objects.Concat(r.handlers))
                foreach (string k in new[] { "locator", "object_locator" }) if (x.ContainsKey(k)) x[k] = ((string)x[k]).Replace("Screens[0]", "Screens[" + offset + "]");
        }
        return r;
    }
    private static string Dir(string name) { return Path.Combine(root, name); }
    private static string Js(string d) { return Path.Combine(d, "screens", "Main", "Loaded.js"); }
    private static Dictionary<string, string> Hashes(string d)
    { return Directory.GetFiles(d, "*", SearchOption.AllDirectories).Where(x => !x.Contains(".event-sync"))
        .ToDictionary(x => x.Substring(d.Length), x => LabEventArtifacts.Digest(File.ReadAllBytes(x))); }
    private static void Equal(Dictionary<string, string> before, string d)
    { var after = Hashes(d); Check(before.Count == after.Count && before.All(x => after[x.Key] == x.Value)); }
    internal static int Main(string[] args)
    {
        if (args.Length > 1 && args[1] == "--crash") {
            LabEventSync.Checkpoint = phase => { if (phase.StartsWith("written:")) Environment.Exit(77); };
            LabEventSync.Write(args[0], Fixture("new")); return 99;
        }
        root = args[0];
        Test("mode_default_explicit_and_invalid", () => { Check(LabEventSync.Mode(null) == "overwrite" && LabEventSync.Mode("overwrite") == "overwrite" && LabEventSync.Mode("snapshot") == "snapshot");
            bool invalid = false; try { LabEventSync.Mode("other"); } catch (ArgumentException) { invalid = true; } Check(invalid); });
        Test("first_export_direct_no_version_directory", () => { string d = Dir("first"); LabEventSync.Write(d, Fixture()); Check(File.Exists(Path.Combine(d, "events.json")) && File.ReadAllText(Js(d)) == "old");
            Check(!Directory.GetDirectories(Path.Combine(d, ".event-sync"), "tx-*").Any()); });
        Test("overwrite_javascript_json_markdown_and_manifest", () => { string d = Dir("update"); LabEventSync.Write(d, Fixture()); LabEventSync.Write(d, Fixture("new")); Check(File.ReadAllText(Js(d)) == "new");
            Check(File.ReadAllText(Path.Combine(d, "events.json")).Contains("new")); });
        Test("add_and_delete_handler", () => { string d = Dir("handler"); LabEventSync.Write(d, Fixture(handler:false)); Check(!File.Exists(Js(d))); LabEventSync.Write(d, Fixture()); Check(File.Exists(Js(d)));
            LabEventSync.Write(d, Fixture(handler:false)); Check(!File.Exists(Js(d)) && Directory.GetFiles(Path.Combine(d,"scripts")).Length == 0); });
        Test("delete_screen_managed_files_only", () => { string d = Dir("screen"); LabEventSync.Write(d, Fixture()); string user = Path.Combine(d,"screens","Main","notes.txt"); File.WriteAllText(user,"user");
            LabEventSync.Write(d, Fixture(screen:false)); Check(!File.Exists(Js(d)) && File.ReadAllText(user) == "user"); });
        Test("preserve_user_root_and_nested_files", () => { string d = Dir("user"); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d,"notes.txt"),"user"); LabEventSync.Write(d,Fixture());
            File.WriteAllText(Path.Combine(d,"scripts","mine.js"),"user script"); LabEventSync.Write(d,Fixture("new")); Check(File.ReadAllText(Path.Combine(d,"scripts","mine.js")) == "user script" && File.ReadAllText(Path.Combine(d,"notes.txt")) == "user"); });
        Test("unmanaged_known_filename_is_conflict", () => { string d = Dir("unmanaged"); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d,"events.json"),"user"); Reject(() => LabEventSync.Write(d,Fixture())); Check(File.ReadAllText(Path.Combine(d,"events.json")) == "user"); });
        Test("edited_managed_file_is_conflict", () => { string d = Dir("edited"); LabEventSync.Write(d,Fixture()); File.WriteAllText(Js(d),"user edit"); var before = Hashes(d); Reject(() => LabEventSync.Write(d,Fixture("new"))); Equal(before,d); });
        foreach (string status in new[] { "PARTIAL", "UNKNOWN" }) {
            string captured = status;
            Test(status + "_retains_missing_files_and_read_status", () => { string d = Dir(captured); LabEventSync.Write(d,Fixture()); var r = Fixture(screen:false); r.status = captured;
                LabEventSync.Write(d,r); Check(File.ReadAllText(Js(d)) == "old" && File.ReadAllText(Path.Combine(d,"events.json")).Contains(captured));
                var state = json.Deserialize<LabEventSync.State>(File.ReadAllText(Path.Combine(d,".event-sync","state.json"))); Check(state.retained.Count > 0); });
        }
        Test("failed_script_read_preserves_previous_js", () => { string d = Dir("readfail"); LabEventSync.Write(d,Fixture()); var r = Fixture(); var h = r.handlers[0]; h["ScriptCode"] = null; h["ScriptCode_path"] = null; h["ScriptCode_sha256"] = null; h["ScriptCode_status"] = "READ_FAILED"; h["status"] = "PARTIAL"; r.status="PARTIAL";
            LabEventSync.Write(d,r); Check(File.ReadAllText(Js(d)) == "old"); });
        Test("deterministic_script_paths_after_index_shift", () => { string d = Dir("stable"); LabEventSync.Write(d,Fixture()); var files = Directory.GetFiles(Path.Combine(d,"scripts")).Select(Path.GetFileName).OrderBy(x=>x).ToArray();
            LabEventSync.Write(d,Fixture(offset:5)); Check(files.SequenceEqual(Directory.GetFiles(Path.Combine(d,"scripts")).Select(Path.GetFileName).OrderBy(x=>x))); });
        Test("publication_does_not_mutate_read_result", () => { var r = Fixture(); string before = json.Serialize(r); LabEventSync.Write(Dir("copy"),r); Check(json.Serialize(r)==before); });
        foreach (string phase in new[] { "prepared", "removed:events-index.json", "written:events.json", "state-written" }) {
            string captured = phase;
            Test("recover_"+phase, () => { string d = Dir("fault-" + results.Count); LabEventSync.Write(d,Fixture()); var before=Hashes(d);
                LabEventSync.Checkpoint = p => { if(p==captured) throw new IOException("synthetic interruption"); }; Reject(() => LabEventSync.Write(d,Fixture("new"))); LabEventSync.Checkpoint=null;
                Check(File.Exists(Path.Combine(d,".incomplete"))); LabEventSync.Recover(d); Equal(before,d); LabEventSync.Write(d,Fixture("new")); Check(File.ReadAllText(Js(d))=="new"); });
        }
        Test("actual_process_exit_and_next_export_recovery", () => { string d=Dir("process"); LabEventSync.Write(d,Fixture());
            var p=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, "\""+d+"\" --crash") { UseShellExecute=false, CreateNoWindow=true }); p.WaitForExit(); Check(p.ExitCode==77); p.Dispose();
            Check(File.Exists(Path.Combine(d,".incomplete"))); LabEventSync.Write(d,Fixture("final")); Check(File.ReadAllText(Js(d))=="final"); });
        Test("recovery_user_edit_blocks_all_restoration", () => { string d=Dir("recovery-conflict"); LabEventSync.Write(d,Fixture()); LabEventSync.Checkpoint=p=>{if(p=="prepared")throw new IOException();}; Reject(()=>LabEventSync.Write(d,Fixture("new"))); LabEventSync.Checkpoint=null;
            File.WriteAllText(Js(d),"user edit"); Reject(()=>LabEventSync.Recover(d)); Check(File.ReadAllText(Js(d))=="user edit" && File.Exists(Path.Combine(d,".incomplete"))); });
        Test("project_binding_conflict", () => { string d=Dir("binding"); LabEventSync.Write(d,Fixture()); var before=Hashes(d); var r=Fixture(); r.identity["project_path"]="other.ap19"; Reject(()=>LabEventSync.Write(d,r)); Equal(before,d); });
        Test("registry_traversal_rejected", () => { string d=Dir("registry"); LabEventSync.Write(d,Fixture()); string path=Path.Combine(d,".event-sync","state.json"); var s=json.Deserialize<LabEventSync.State>(File.ReadAllText(path)); s.files[0].path="../user.txt"; File.WriteAllText(path,json.Serialize(s)); Reject(()=>LabEventSync.Write(d,Fixture())); });
        Test("snapshot_remains_nonoverwriting", () => { string d=Dir("snapshots"); var r=Fixture(); string one=LabEventOutput.RunDirectory(d,"run-1"); LabEventArtifacts.Write(one,r); var before=Hashes(one); LabEventArtifacts.Write(LabEventOutput.RunDirectory(d,"run-2"),Fixture("new")); Reject(()=>LabEventArtifacts.Write(one,Fixture())); Equal(before,one); });
        Test("relative_output", () => { string old=Environment.CurrentDirectory; try { Environment.CurrentDirectory=root; LabEventSync.Write(@".\relative",Fixture()); Check(File.Exists(Js(Dir("relative")))); } finally { Environment.CurrentDirectory=old; } });
        Test("real_git_repository_output", () => { LabEventSync.Write(args[1],Fixture()); LabEventSync.Write(args[1],Fixture("new")); Check(File.ReadAllText(Js(args[1]))=="new"); });
        Test("real_unc_output", () => { LabEventSync.Write(args[2],Fixture()); LabEventSync.Write(args[2],Fixture("new")); Check(File.ReadAllText(Js(args[2]))=="new"); });
        Test("junction_output_refused", () => Reject(()=>LabEventSync.Write(args[3],Fixture())));
        Test("nested_reparse_file_refused", () => { string d=Dir("nested-link"); LabEventSync.Write(d,Fixture()); string scripts=Path.Combine(d,"scripts");
            string moved = Dir("nested-original"); Directory.Move(scripts,moved);
            string command = "New-Item -ItemType Junction -Path '"+scripts.Replace("'","''")+"' -Target '"+moved.Replace("'","''")+"' | Out-Null";
            var p=Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(command))) {UseShellExecute=false,CreateNoWindow=true}); p.WaitForExit(); Check(p.ExitCode==0); p.Dispose();
            var before=Hashes(moved); Reject(()=>LabEventSync.Write(d,Fixture("new"))); Equal(before,moved); });
        Test("ambiguous_logical_handler_refused",()=>{var r=Fixture(); var duplicate=new Dictionary<string,object>(r.handlers[0]); duplicate["locator"]=(string)duplicate["locator"]+"duplicate"; r.handlers.Add(duplicate); Reject(()=>LabEventSync.Write(Dir("ambiguous"),r));});
        Test("stable_readable_collision_names_after_index_shift",()=>{
            var one=Fixture(); one.objects[1]["name"]="A/B";
            var two=Fixture(offset:1); two.objects[1]["name"]="A?B";
            one.objects.Add(two.objects[1]); one.handlers.Add(two.handlers[0]);
            string d=Dir("stable-collision"); LabEventSync.Write(d,one);
            var before=Directory.GetFiles(Path.Combine(d,"screens"),"*.js",SearchOption.AllDirectories).Select(x=>x.Substring(d.Length)).OrderBy(x=>x).ToArray();
            foreach(var x in one.objects.Concat(one.handlers)) foreach(string k in new[]{"locator","object_locator"}) if(x.ContainsKey(k)) x[k]=((string)x[k]).Replace("Screens[0]","Screens[8]").Replace("Screens[1]","Screens[9]");
            LabEventSync.Write(d,one); var after=Directory.GetFiles(Path.Combine(d,"screens"),"*.js",SearchOption.AllDirectories).Select(x=>x.Substring(d.Length)).OrderBy(x=>x).ToArray(); Check(before.SequenceEqual(after));
        });
        Test("stale_deletion_interruption_restores_original",()=>{
            string d=Dir("stale-recovery"); LabEventSync.Write(d,Fixture()); var before=Hashes(d);
            LabEventSync.Checkpoint=p=>{if(p.StartsWith("removed:screens/"))throw new IOException();}; Reject(()=>LabEventSync.Write(d,Fixture(screen:false))); LabEventSync.Checkpoint=null;
            LabEventSync.Recover(d); Equal(before,d);
        });
        Test("partial_retained_file_deleted_only_after_complete_read",()=>{
            string d=Dir("partial-complete"); LabEventSync.Write(d,Fixture()); var r=Fixture(screen:false);r.status="PARTIAL";LabEventSync.Write(d,r);Check(File.Exists(Js(d)));
            LabEventSync.Write(d,Fixture(screen:false));Check(!File.Exists(Js(d)));
        });
        Test("unmanaged_incomplete_marker_preserved",()=>{string d=Dir("marker");Directory.CreateDirectory(d);File.WriteAllText(Path.Combine(d,".incomplete"),"user");Reject(()=>LabEventSync.Write(d,Fixture()));Check(File.ReadAllText(Path.Combine(d,".incomplete"))=="user");});
        Test("recover_committed_journal_marker_window",()=>{string d=Dir("committed");LabEventSync.Write(d,Fixture());LabEventSync.Checkpoint=p=>{if(p=="journal-committed")throw new IOException();};Reject(()=>LabEventSync.Write(d,Fixture("new")));LabEventSync.Checkpoint=null;Check(File.Exists(Path.Combine(d,".incomplete")));LabEventSync.Recover(d);Check(File.ReadAllText(Js(d))=="new"&&!File.Exists(Path.Combine(d,".incomplete")));});
        Test("publication_receipt_distinguishes_read_from_failed_publish",()=>{string d=Dir("receipt");LabEventSync.Write(d,Fixture());File.WriteAllText(Js(d),"user edit");Reject(()=>LabEventSync.Write(d,Fixture("new")));var receipt=json.Deserialize<LabEventSync.Attempt>(File.ReadAllText(Path.Combine(d,".event-sync","publication.json")));Check(receipt.publication_status=="IN_PROGRESS"&&receipt.read_status=="COMPLETE_FOR_PUBLIC_SCOPE");});
        Test("concurrent_publisher_lock_refused",()=>{string d=Dir("lock");LabEventSync.Write(d,Fixture());var before=Hashes(d);using(var claim=new FileStream(Path.Combine(d,".event-sync","lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){Reject(()=>LabEventSync.Write(d,Fixture("new")));}Equal(before,d);});
        Test("unknown_control_directory_is_preserved",()=>{string d=Dir("unknown-control");Directory.CreateDirectory(Path.Combine(d,".event-sync"));File.WriteAllText(Path.Combine(d,".event-sync","notes.txt"),"user");Reject(()=>LabEventSync.Write(d,Fixture()));Check(File.ReadAllText(Path.Combine(d,".event-sync","notes.txt"))=="user");});
        Test("overwrite_unicode_path",()=>{string d=Dir("Za\u017c\u00f3\u0142\u0107-\u65e5\u672c");LabEventSync.Write(d,Fixture());LabEventSync.Write(d,Fixture("new"));Check(File.ReadAllText(Js(d))=="new");});
        Test("artifact_budget_partial_preserves_read_and_previous_js",()=>{string d=Dir("budget");LabEventSync.Write(d,Fixture());var r=Fixture(new string('x',100000));r.limits.ArtifactBytes=20000;string status=LabEventSync.Write(d,r);Check(status=="PARTIAL"&&r.status=="COMPLETE_FOR_PUBLIC_SCOPE"&&File.ReadAllText(Js(d))=="old");});
        File.WriteAllText(Path.Combine(root,"results.json"),json.Serialize(results),new UTF8Encoding(false));
        int failed=results.Count(x=>json.Serialize(x).Contains("\"FAIL\"")); Console.WriteLine("TOTAL "+results.Count+"; FAILED "+failed); return failed==0?0:1;
    }
}
