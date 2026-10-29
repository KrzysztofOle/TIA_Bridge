// Synthetic filesystem tests of the exact lab output validator/publisher. No Siemens calls.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

internal static class LabEventOutputTests
{
    private static readonly List<object> Results = new List<object>();
    private static string root;
    private static void Check(bool ok) { if (!ok) throw new Exception("assertion failed"); }
    private static void Test(string name, Action action)
    {
        try { action(); Results.Add(new { name, status = "PASS" }); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { Results.Add(new { name, status = "FAIL", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex); }
    }
    private static void Reject(Action action, string code)
    {
        try { action(); } catch (IOException ex) { Check(ex.Message == code); return; }
        throw new Exception("expected " + code);
    }
    private static string Export(string parent, string id)
    {
        string target = LabEventOutput.RunDirectory(parent, id);
        LabEventArtifacts.Write(target, new LabEventResult { limits = new LabEventLimits() });
        Check(File.Exists(Path.Combine(target, "events.json")) && !File.Exists(Path.Combine(target, ".incomplete")));
        return target;
    }
    private static Dictionary<string, string> Hashes(string directory)
    {
        return Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(x => x.Substring(directory.Length),
            x => LabEventArtifacts.Digest(File.ReadAllBytes(x)));
    }
    internal static int Main(string[] args)
    {
        root = args[0];
        Test("inside_real_git_repository", () => Export(args[1], "run-1"));
        Test("outside_repository_mapped_drive", () => Export(Path.Combine(root, "outside"), "run-1"));
        Test("relative_path_resolves_from_working_directory", () => {
            string old = Environment.CurrentDirectory;
            try { Environment.CurrentDirectory = root; Check(Export(@".\relative", "run-1") == Path.Combine(root, "relative", "run-1")); }
            finally { Environment.CurrentDirectory = old; }
        });
        Test("unc_actual_publication", () => Export(args[2], "run-1"));
        Test("same_parent_separate_runs", () => {
            string parent = Path.Combine(root, "repeat"); string one = Export(parent, "run-1");
            var before = Hashes(one); Export(parent, "run-2");
            Check(Directory.GetDirectories(parent).Length == 2 && before.All(x => Hashes(one)[x.Key] == x.Value));
        });
        Test("run_id_directory_collision_no_overwrite", () => {
            string parent = Path.Combine(root, "collision"); string one = Export(parent, "same"); var before = Hashes(one);
            Reject(() => Export(parent, "same"), "EXPORT_ALREADY_EXISTS");
            Check(before.All(x => Hashes(one)[x.Key] == x.Value) && Directory.GetDirectories(parent).Length == 1);
        });
        Test("run_id_file_collision_no_overwrite", () => {
            string parent = Path.Combine(root, "file-collision"); Directory.CreateDirectory(parent);
            string path = Path.Combine(parent, "same"); File.WriteAllText(path, "sentinel");
            Reject(() => Export(parent, "same"), "EXPORT_ALREADY_EXISTS"); Check(File.ReadAllText(path) == "sentinel");
        });
        Test("write_denied_real_ntfs_acl", () => {
            string parent = Path.Combine(Path.GetTempPath(), "LAB-03N-denied-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent); var original = Directory.GetAccessControl(parent);
            var denied = Directory.GetAccessControl(parent);
            denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.Write,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
            try {
                Directory.SetAccessControl(parent, denied);
                bool refused = false;
                try { Export(parent, "run-1"); } catch (UnauthorizedAccessException) { refused = true; }
                Check(refused && Directory.GetFileSystemEntries(parent).Length == 0);
            } finally { Directory.SetAccessControl(parent, original); }
        });
        Test("nonexistent_parents_created", () => Export(Path.Combine(root, "new", "nested", "parent"), "run-1"));
        Test("unicode_path", () => Export(Path.Combine(root, "Za\u017c\u00f3\u0142\u0107-\u65e5\u672c"), "run-1"));
        Test("existing_user_files_preserved", () => {
            string parent = Path.Combine(root, "preserve"); Directory.CreateDirectory(Path.Combine(parent, "user"));
            File.WriteAllBytes(Path.Combine(parent, "user", "data.bin"), new byte[] { 0, 255, 13, 10 });
            File.WriteAllText(Path.Combine(parent, "events.json"), "user data"); var before = Hashes(parent);
            Export(parent, "run-1"); Check(before.All(x => Hashes(parent)[x.Key] == x.Value));
        });
        Test("git_file_and_git_directory_are_not_policy", () => {
            foreach (bool directory in new[] { true, false }) {
                string parent = Path.Combine(root, directory ? "git-dir" : "git-file"); Directory.CreateDirectory(parent);
                if (directory) Directory.CreateDirectory(Path.Combine(parent, ".git")); else File.WriteAllText(Path.Combine(parent, ".git"), "gitdir: synthetic");
                Export(Path.Combine(parent, "events"), "run-1");
            }
        });
        Test("project_and_scripts_names_accepted", () => Export(Path.Combine(root, "project", "Scripts", "src", "bin"), "run-1"));
        Test("volume_and_unc_roots_validate_without_writing", () => {
            Check(LabEventOutput.Root(Path.GetPathRoot(root)) == Path.GetPathRoot(root));
            Check(LabEventOutput.Root(@"\\localhost\X$\").TrimEnd('\\') == @"\\localhost\X$");
        });
        Test("reparse_parent_rejected_without_touching_target", () => {
            Reject(() => Export(args[3], "run-1"), "EVENT_OUTPUT_REPARSE_REJECTED");
            Check(Directory.GetFileSystemEntries(args[4]).Length == 0);
        });
        Test("run_id_cannot_escape_output", () => {
            foreach (string id in new[] { "..", @"..\escape", @"C:\escape", "a/b", "a:stream" })
                Reject(() => LabEventOutput.RunDirectory(root, id), "EVENT_RUN_ID_REJECTED");
        });
        Test("artifact_cannot_escape_staging", () => {
            foreach (string path in new[] { "../escape.js", @"C:\escape.js", "a:stream", "a/../../escape" })
                Reject(() => LabEventOutput.Child(root, path), "EVENT_ARTIFACT_PATH_REJECTED");
        });
        Test("parent_file_is_explicit_failure", () => {
            string path = Path.Combine(root, "parent-file"); File.WriteAllText(path, "sentinel");
            Reject(() => Export(path, "run-1"), "EVENT_OUTPUT_NOT_DIRECTORY"); Check(File.ReadAllText(path) == "sentinel");
        });
        File.WriteAllText(Path.Combine(root, "results.json"), new JavaScriptSerializer().Serialize(Results), new UTF8Encoding(false));
        int failures = Results.Count(x => new JavaScriptSerializer().Serialize(x).Contains("\"FAIL\""));
        Console.WriteLine("TOTAL " + Results.Count + "; FAILED " + failures); return failures == 0 ? 0 : 1;
    }
}
