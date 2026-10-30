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
    private static LabEventResult Buttons(string name = "Tapped", bool present = true)
    {
        var software = new EventFake("SOFTWARE") { Name = "HMI", Edges = new[] { "Screens" } };
        var screen = new EventFake("SCREEN") { Name = "JSON_Test", Supported = true, Edges = new[] { "ScreenItems" } };
        screen.Values["EventHandlers"] = new EventFake[0];
        var button = new EventFake("ITEM") { Name = "Button_3", Supported = true };
        var other = new EventFake("ITEM") { Name = "Other", Supported = true };
        Func<string, EventFake> handler = ev => {
            var script = new EventFake("SCRIPT"); script.Values["ScriptCode"] = "// TEST LOGGER " + ev;
            script.Values["GlobalDefinitionAreaScriptCode"] = "// declarations"; script.Values["Async"] = false;
            var h = new EventFake("HANDLER"); h.Values["EventType"] = ev; h.Values["Script"] = script; return h;
        };
        button.Values["EventHandlers"] = present ? new[] { handler(name) } : new EventFake[0];
        other.Values["EventHandlers"] = new[] { handler("Tapped") };
        screen.Values["ScreenItems"] = new[] { button, other }; software.Values["Screens"] = new[] { screen };
        var r = new LabEventReader(new EventFakeAccess(), new LabEventLimits()).Read(software, "/HMI/Synthetic/HMI");
        r.identity["project_path"] = "X:/Synthetic.ap19"; r.identity["device_path"] = new[] { "Synthetic", "HMI" }; return r;
    }
    private static string ButtonLocator(LabEventResult r)
    { return (string)r.objects.Single(x => (string)x["name"] == "Button_3")["locator"]; }
    private static LabEventSync.State State(string d)
    { return json.Deserialize<LabEventSync.State>(File.ReadAllText(Path.Combine(d,".event-sync","state.json"))); }
    private static void PutState(string d, LabEventSync.State s)
    { File.WriteAllText(Path.Combine(d,".event-sync","state.json"),json.Serialize(s)); }
    private static LabEventSync.Entry[] ButtonFiles(string d)
    { return State(d).files.Where(x => x.origin != null && x.origin.owner.Last().name == "Button_3" && x.origin.event_name == "Tapped").ToArray(); }
    private static void Removed(string d, LabEventSync.Entry[] files)
    { Check(files.Length == 4 && files.All(x => !File.Exists(Path.Combine(d,x.path)))); }
    private static void Retained(string d, LabEventSync.Entry[] files, string reason = null)
    {
        var s = State(d); Check(files.Length == 4 && files.All(x => File.Exists(Path.Combine(d,x.path)) && s.retained.Contains(x.path)
            && s.decisions.Any(v => v.path == x.path && v.action == "RETAIN_UNVERIFIED" && (reason == null || v.reason == reason))));
    }
    private static void OtherPartial(LabEventResult r)
    { r.Issue((string)r.objects.Single(x => (string)x["name"] == "Other")["locator"], "children", "ENCAPSULATED_CHILDREN_NOT_PUBLIC", null); }
    private static void Legacy(string d)
    {
        var s = State(d); s.schema="lab-03o-owner/1"; foreach(var e in s.files) e.origin=null; s.decisions.Clear(); PutState(d,s);
    }
    private static void Snapshot(string d, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var e in State(d).files) {
            string dest=Path.Combine(target,e.path); Directory.CreateDirectory(Path.GetDirectoryName(dest)); File.Copy(Path.Combine(d,e.path),dest);
        }
    }
    private static void Integrity(string d)
    {
        var s=State(d); foreach(var e in s.files) { byte[] b=File.ReadAllBytes(Path.Combine(d,e.path)); Check(b.LongLength==e.length && LabEventArtifacts.Digest(b)==e.hash); }
        var manifest=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(Path.Combine(d,"manifest.json")));
        var fresh=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var value in (object[])manifest["files"]) {
            var e=(Dictionary<string,object>)value;string p=(string)e["path"];byte[] b=File.ReadAllBytes(Path.Combine(d,p));
            Check(b.LongLength==Convert.ToInt64(e["length"])&&LabEventArtifacts.Digest(b)==(string)e["sha256"]);fresh.Add(p);
        }
        fresh.Add("manifest.json"); Check(s.files.All(x=>fresh.Contains(x.path)||s.retained.Contains(x.path)));
        var report=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(Path.Combine(d,"sync-report.json")));
        Check(json.Serialize(report["decisions"])==json.Serialize(s.decisions));
        Check(File.ReadAllText(Path.Combine(d,"events-report.md")).Contains("sync-report.md"));
        var index=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(Path.Combine(d,"events-index.json")));
        var source=(Dictionary<string,object>)index["source"];
        Check((string)source["sha256"]==LabEventArtifacts.Digest(File.ReadAllBytes(Path.Combine(d,"events.json"))));
    }
    private static void ObjectTests()
    {
        Test("local_complete_removes_four_absent_artifacts",()=>{string d=Dir("local");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);LabEventSync.Write(d,Buttons(present:false));Removed(d,old);Integrity(d);});
        Test("local_partial_retains_with_reason",()=>{string d=Dir("local-partial");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var r=Buttons(present:false);r.objects.Single(x=>(string)x["name"]=="Button_3")["handlers_status"]="PARTIAL";r.status="PARTIAL";LabEventSync.Write(d,r);Retained(d,old,"LOCAL_ENUMERATION_INCOMPLETE");Integrity(d);});
        Test("global_partial_local_complete_removes_only_target",()=>{string d=Dir("global-partial");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var other=State(d).files.Where(x=>x.origin!=null&&x.origin.owner.Last().name=="Other").ToArray();var r=Buttons(present:false);OtherPartial(r);Check(LabEventSync.Write(d,r)=="PARTIAL");Removed(d,old);Check(other.All(x=>LabEventArtifacts.Digest(File.ReadAllBytes(Path.Combine(d,x.path)))==x.hash));Integrity(d);});
        Test("button_3_tapped_to_up_global_partial",()=>{string d=Dir("tapped-up");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var r=Buttons("Up");OtherPartial(r);LabEventSync.Write(d,r);Removed(d,old);var s=State(d);Check(s.files.Count(x=>x.origin!=null&&x.origin.owner.Last().name=="Button_3"&&x.origin.event_name=="Up")==4);Check(File.ReadAllText(Path.Combine(d,"screens/JSON_Test/Button_3.Up.js")).Contains("TEST LOGGER"));Integrity(d);});
        Test("unknown_event_name_retains_old_handler",()=>{string d=Dir("unknown-name");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var r=Buttons("UNKNOWN");r.status="PARTIAL";LabEventSync.Write(d,r);Retained(d,old,"EVENT_NAME_UNKNOWN");});
        Test("null_event_name_retains_and_publishes_partial",()=>{string d=Dir("null-name");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var r=Buttons("Up");r.handlers[0]["event_name"]=null;r.handlers[0]["status"]="PARTIAL";r.status="PARTIAL";LabEventSync.Write(d,r);Retained(d,old,"EVENT_NAME_UNKNOWN");Integrity(d);});
        Test("unknown_enum_observation_retains_old_handler",()=>{string d=Dir("enum");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var r=Buttons("999");r.Issue(ButtonLocator(r)+"/EventHandlers[0]","EventType","UNKNOWN_EVENT_VALUE",null);LabEventSync.Write(d,r);Retained(d,old,"LOCAL_READ_UNCERTAIN");});
        Test("wrong_historical_mapping_retained",()=>{string d=Dir("wrong-origin");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var s=State(d);foreach(var e in s.files.Where(x=>old.Any(o=>o.path==x.path)))e.origin.handler_key=new string('0',64);PutState(d,s);LabEventSync.Write(d,Buttons("Up"));Retained(d,old,"HISTORICAL_MAPPING_INVALID");});
        Test("stale_sha_mismatch_preserves_edited_bytes",()=>{string d=Dir("sha");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);foreach(var e in old)File.AppendAllText(Path.Combine(d,e.path)," user edit");LabEventSync.Write(d,Buttons("Up"));Retained(d,old,"SHA256_MISMATCH");Check(old.All(x=>File.ReadAllText(Path.Combine(d,x.path)).EndsWith(" user edit")));Integrity(d);});
        Test("local_error_does_not_allow_deletion",()=>{string d=Dir("local-error");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var r=Buttons("Up");r.Issue(ButtonLocator(r),"EventHandlers","ENUMERATION_FAILED",null);LabEventSync.Write(d,r);Retained(d,old,"LOCAL_READ_UNCERTAIN");});
        Test("engineering_identity_postcheck_failure_retains",()=>{string d=Dir("identity");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var r=Buttons("Up");r.execution_evidence="ENGINEERING_OBSERVATION_ONLY";r.identity["identity_before"]="VERIFIED";r.identity["identity_after"]="UNKNOWN";LabEventSync.Write(d,r);Retained(d,old,"IDENTITY_OR_SESSION_UNVERIFIED");});
        Test("hmi_binding_mismatch_rejects_without_deletion",()=>{string d=Dir("hmi");LabEventSync.Write(d,Buttons());var before=Hashes(d);var r=Buttons("Up");r.identity["device_path"]=new[]{"Other","HMI"};Reject(()=>LabEventSync.Write(d,r));Equal(before,d);});
        Test("legacy_registry_maps_verified_current_metadata",()=>{string d=Dir("legacy");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);Legacy(d);var r=Buttons("Up");OtherPartial(r);LabEventSync.Write(d,r);Removed(d,old);Check(State(d).schema=="lab-03o-owner/2");Integrity(d);});
        Test("legacy_unmapped_retained_preserved_even_complete",()=>{string d=Dir("legacy-retained");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var r=Buttons("Up");r.objects.Single(x=>(string)x["name"]=="Button_3")["handlers_status"]="PARTIAL";r.status="PARTIAL";LabEventSync.Write(d,r);Legacy(d);LabEventSync.Write(d,Buttons("Up"));Retained(d,old,"LEGACY_ORIGIN_UNVERIFIED");});
        Test("legacy_retained_resolved_by_explicit_verified_history",()=>{string d=Dir("history");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);string history=Dir("prior-export");Snapshot(d,history);var r=Buttons("Up");r.objects.Single(x=>(string)x["name"]=="Button_3")["handlers_status"]="PARTIAL";r.status="PARTIAL";LabEventSync.Write(d,r);Legacy(d);r=Buttons("Up");OtherPartial(r);LabEventSync.Write(d,r,history);Removed(d,old);Integrity(d);});
        Test("history_sha_corruption_rejects_before_mutation",()=>{string d=Dir("bad-history");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);string history=Dir("bad-prior-export");Snapshot(d,history);Legacy(d);File.AppendAllText(Path.Combine(history,old[0].path),"corrupt");var before=Hashes(d);Reject(()=>LabEventSync.Write(d,Buttons("Up"),history));Equal(before,d);});
        Test("legacy_unverified_metadata_never_proves_origin",()=>{string d=Dir("bad-meta");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);Legacy(d);File.AppendAllText(Path.Combine(d,"events-index.json")," ");LabEventSync.Write(d,Buttons("Up"));Retained(d,old,"LEGACY_ORIGIN_UNVERIFIED");});
        Test("local_deletion_interruption_recovers_origins_and_bytes",()=>{string d=Dir("local-recovery");LabEventSync.Write(d,Buttons());var before=Hashes(d);var old=ButtonFiles(d);LabEventSync.Checkpoint=p=>{if(p=="removed:"+old[0].path)throw new IOException();};var r=Buttons("Up");OtherPartial(r);Reject(()=>LabEventSync.Write(d,r));LabEventSync.Checkpoint=null;LabEventSync.Recover(d);Equal(before,d);Check(ButtonFiles(d).Length==4);});
        Test("legacy_migration_interruption_recovers_registry",()=>{string d=Dir("legacy-recovery");LabEventSync.Write(d,Buttons());Legacy(d);var before=Hashes(d);LabEventSync.Checkpoint=p=>{if(p=="prepared")throw new IOException();};Reject(()=>LabEventSync.Write(d,Buttons("Up")));LabEventSync.Checkpoint=null;LabEventSync.Recover(d);Equal(before,d);});
        Test("history_snapshot_mode_refused",()=>{bool failed=false;try{LabEventSync.History("snapshot",root);}catch(ArgumentException){failed=true;}Check(failed);});
        Test("ambiguous_owner_without_handlers_retains",()=>{string d=Dir("amb-owner");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var r=Buttons(present:false);var owner=r.objects.Single(x=>(string)x["name"]=="Button_3");var duplicate=new Dictionary<string,object>(owner);duplicate["locator"]=ButtonLocator(r).Replace("ScreenItems[0]","ScreenItems[2]");r.objects.Add(duplicate);r.status="PARTIAL";LabEventSync.Write(d,r);Retained(d,old,"OWNER_IDENTITY_AMBIGUOUS");});
        Test("ancestor_enumeration_error_retains",()=>{string d=Dir("ancestor");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var r=Buttons("Up");r.Issue((string)r.objects.Single(x=>(string)x["kind"]=="SCREEN")["locator"],"ScreenItems","ENUMERATION_FAILED",null);LabEventSync.Write(d,r);Retained(d,old,"LOCAL_READ_UNCERTAIN");});
        Test("handler_incomplete_keeps_other_historical_event",()=>{string d=Dir("incomplete-handler");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var r=Buttons("Up");r.handlers[0]["status"]="PARTIAL";r.status="PARTIAL";LabEventSync.Write(d,r);Retained(d,old,"LOCAL_HANDLER_INCOMPLETE");});
        Test("engineering_verified_identity_allows_local_deletion",()=>{string d=Dir("verified-identity");LabEventSync.Write(d,Buttons());var old=ButtonFiles(d);var r=Buttons("Up");OtherPartial(r);r.execution_evidence="ENGINEERING_OBSERVATION_ONLY";foreach(string k in new[]{"identity_before","identity_after","hmi_after"})r.identity[k]="VERIFIED";foreach(string k in new[]{"is_modified_before","is_modified_after"})r.identity[k]=false;r.identity["dispose"]="RETURNED";LabEventSync.Write(d,r);Removed(d,old);});
        Test("wrong_history_identity_rejects",()=>{string d=Dir("history-id");LabEventSync.Write(d,Buttons());string history=Dir("foreign-history");var foreign=Buttons();foreign.identity["project_path"]="Other.ap19";LabEventSync.Write(history,foreign);var before=Hashes(d);Reject(()=>LabEventSync.Write(d,Buttons("Up"),history));Equal(before,d);});
        Test("legacy_pending_journal_without_new_fields_recovers",()=>{
            string d=Dir("v1-journal");LabEventSync.Write(d,Buttons());Legacy(d);var before=Hashes(d);
            LabEventSync.Checkpoint=p=>{if(p=="prepared")throw new IOException();};Reject(()=>LabEventSync.Write(d,Buttons("Up")));LabEventSync.Checkpoint=null;
            Action<Dictionary<string,object>> strip=s=>{s.Remove("decisions");foreach(var value in (object[])s["files"])((Dictionary<string,object>)value).Remove("origin");};
            string statePath=Path.Combine(d,".event-sync/state.json"),journalPath=Path.Combine(d,".event-sync/pending.json");
            var state=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(statePath));strip(state);File.WriteAllText(statePath,json.Serialize(state));
            var journal=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(journalPath));foreach(string k in new[]{"registered","before","after"})strip((Dictionary<string,object>)journal[k]);File.WriteAllText(journalPath,json.Serialize(journal));
            LabEventSync.Recover(d);Equal(before,d);
        });
    }
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
        Test("edited_managed_files_are_overwritten", () => { string d = Dir("edited"); LabEventSync.Write(d,Fixture());
            File.WriteAllText(Js(d),"user edit"); File.WriteAllText(Path.Combine(d,"events.json"),"user edit");
            LabEventSync.Write(d,Fixture("new")); Check(File.ReadAllText(Js(d))=="new" && File.ReadAllText(Path.Combine(d,"events.json")).Contains("new")); });
        Test("interrupted_overwrite_restores_latest_managed_bytes", () => { string d = Dir("edited-recovery"); LabEventSync.Write(d,Fixture());
            File.WriteAllText(Js(d),"latest user edit"); LabEventSync.Checkpoint=p=>{if(p=="prepared")throw new IOException("synthetic interruption");};
            Reject(()=>LabEventSync.Write(d,Fixture("new"))); LabEventSync.Checkpoint=null; LabEventSync.Recover(d); Check(File.ReadAllText(Js(d))=="latest user edit"); });
        Test("missing_managed_file_is_recreated", () => { string d = Dir("missing-managed"); LabEventSync.Write(d,Fixture()); File.Delete(Js(d));
            LabEventSync.Write(d,Fixture("new")); Check(File.ReadAllText(Js(d))=="new"); });
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
        Test("publication_receipt_distinguishes_read_from_failed_publish",()=>{string d=Dir("receipt");LabEventSync.Write(d,Fixture());LabEventSync.Checkpoint=p=>{if(p=="prepared")throw new IOException("synthetic interruption");};Reject(()=>LabEventSync.Write(d,Fixture("new")));LabEventSync.Checkpoint=null;var receipt=json.Deserialize<LabEventSync.Attempt>(File.ReadAllText(Path.Combine(d,".event-sync","publication.json")));Check(receipt.publication_status=="IN_PROGRESS"&&receipt.read_status=="COMPLETE_FOR_PUBLIC_SCOPE");LabEventSync.Recover(d);});
        Test("concurrent_publisher_lock_refused",()=>{string d=Dir("lock");LabEventSync.Write(d,Fixture());var before=Hashes(d);using(var claim=new FileStream(Path.Combine(d,".event-sync","lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){Reject(()=>LabEventSync.Write(d,Fixture("new")));}Equal(before,d);});
        Test("unknown_control_directory_is_preserved",()=>{string d=Dir("unknown-control");Directory.CreateDirectory(Path.Combine(d,".event-sync"));File.WriteAllText(Path.Combine(d,".event-sync","notes.txt"),"user");Reject(()=>LabEventSync.Write(d,Fixture()));Check(File.ReadAllText(Path.Combine(d,".event-sync","notes.txt"))=="user");});
        Test("overwrite_unicode_path",()=>{string d=Dir("Za\u017c\u00f3\u0142\u0107-\u65e5\u672c");LabEventSync.Write(d,Fixture());LabEventSync.Write(d,Fixture("new"));Check(File.ReadAllText(Js(d))=="new");});
        Test("artifact_budget_partial_preserves_read_and_previous_js",()=>{string d=Dir("budget");LabEventSync.Write(d,Fixture());var r=Fixture(new string('x',100000));r.limits.ArtifactBytes=20000;string status=LabEventSync.Write(d,r);Check(status=="PARTIAL"&&r.status=="COMPLETE_FOR_PUBLIC_SCOPE"&&File.ReadAllText(Js(d))=="old");});
        ObjectTests();
        File.WriteAllText(Path.Combine(root,"results.json"),json.Serialize(results),new UTF8Encoding(false));
        int failed=results.Count(x=>json.Serialize(x).Contains("\"FAIL\"")); Console.WriteLine("TOTAL "+results.Count+"; FAILED "+failed); return failed==0?0:1;
    }
}
