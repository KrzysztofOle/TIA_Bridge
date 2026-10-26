using System.Reflection;
using System.Security.Cryptography;

namespace TiaBridge.Worker.V19;

internal static class VendorResolver
{
    internal static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Siemens", "Automation", "Portal V19");
    private static readonly Dictionary<string, string> Files = new(StringComparer.Ordinal)
    {
        ["Siemens.Engineering"] = @"PublicAPI\V19\Siemens.Engineering.dll",
        ["Siemens.Engineering.Hmi"] = @"PublicAPI\V19\Siemens.Engineering.Hmi.dll",
        ["Siemens.Engineering.Contract"] = @"Bin\PublicAPI\Siemens.Engineering.Contract.dll",
        ["Siemens.Engineering.ClientAdapter.Interfaces"] = @"Bin\PublicAPI\Siemens.Engineering.ClientAdapter.Interfaces.dll"
    };
    internal static void Install()
    {
        if (Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory, "Siemens*.dll").Any()) throw new FileLoadException("Vendor binaries beside worker are forbidden.");
        foreach (var pair in Files) _ = Validate(Path.Combine(Root, pair.Value), pair.Key);
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var requested = new AssemblyName(args.Name);
            if (requested.Name is null || !Files.TryGetValue(requested.Name, out var relative)) return null;
            string path = Path.Combine(Root, relative); var actual = Validate(path, requested.Name);
            if (requested.Version != actual.Version || Token(requested) != Token(actual)) throw new FileLoadException("Vendor identity mismatch.");
            return Assembly.LoadFrom(path);
        };
    }
    internal static AssemblyName Validate(string path, string name)
    {
        if (!Files.TryGetValue(name, out var relative) || !string.Equals(Path.GetFullPath(path), Path.Combine(Root, relative), StringComparison.OrdinalIgnoreCase)) throw new FileLoadException("Unapproved vendor location.");
        for (string? p = path; p is not null; p = Path.GetDirectoryName(p))
            if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new FileLoadException("Vendor reparse point.");
        var actual = AssemblyName.GetAssemblyName(path);
        string token = name == "Siemens.Engineering.Contract" ? "37a18b206f7724a6" : name == "Siemens.Engineering.ClientAdapter.Interfaces" ? "82af32ec2e7eb4c1" : "d29ec89bac048f84";
        if (actual.Name != name || actual.Version != new Version(19, 0, 0, 0) || Token(actual) != token) throw new FileLoadException("Unapproved vendor identity.");
        return actual;
    }
    private static string Token(AssemblyName name) => BitConverter.ToString(name.GetPublicKeyToken() ?? []).Replace("-", "").ToLowerInvariant();
    internal static string Hash(string path) { using var file = File.OpenRead(path); using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
}
