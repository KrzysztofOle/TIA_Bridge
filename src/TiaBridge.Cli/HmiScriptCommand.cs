using System.Text.Json;
using TiaBridge.Core.Hmi;

// Local artifact processing only. Live export always uses submit project.export and the Host scheduler.
internal static class HmiScriptCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length == 6 && args[1] == "split")
        {
            string input = Path.GetFullPath(args[2]); HmiScriptPackage.CheckChain(input);
            if (Directory.GetDirectories(input).Length != 0) throw new ArgumentException("Flat native export directory required.");
            var files = Directory.GetFiles(input);
            if (files.Length is < 1 or > 32) throw new ArgumentException("Native artifact count exceeds limits.");
            var native = new List<NativeScriptArtifact>();
            long total = 0;
            foreach (string file in files.Order(StringComparer.Ordinal))
            {
                HmiScriptPackage.CheckChain(file);
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                total += stream.Length;
                if (total > 16 * 1024 * 1024) throw new ArgumentException("Native artifact size exceeds limits.");
                byte[] content = new byte[checked((int)stream.Length)]; stream.ReadExactly(content);
                native.Add(new(Path.GetFileName(file), content));
            }
            if (args[4] != "T05RecipeCatalog") throw new ArgumentException("Only the explicitly specified catalog inventory is configured by this offline CLI.");
            var manifest = HmiScriptPackage.Create(args[3], args[4], args[5], native, HmiScriptSource.CatalogFunctions);
            Console.WriteLine(JsonSerializer.Serialize(manifest, HmiScriptPackage.Json));
            return manifest.SplitStatus == "PASS" ? 0 : 3;
        }
        if (args.Length == 4 && args[1] == "compare")
        {
            Console.WriteLine(JsonSerializer.Serialize(HmiScriptPackage.Compare(HmiScriptPackage.ReadVerified(args[2]), HmiScriptPackage.ReadVerified(args[3])), HmiScriptPackage.Json));
            return 0;
        }
        throw new ArgumentException("hmi-js split INPUT NEW_DEST T05RecipeCatalog SCOPE_BINDING | hmi-js compare OLD_MANIFEST NEW_MANIFEST");
    }
}
