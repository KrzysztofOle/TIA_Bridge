using System.Security.Cryptography;
using System.Text.Json;

namespace TiaBridge.Host.Tests;

internal sealed class QualificationProfile
{
    private QualificationProfile(string name, string version, string sha256, JsonElement limits, JsonElement testHarness)
    { Name = name; Version = version; Sha256 = sha256; Limits = limits; TestHarness = testHarness; }

    internal string Name { get; }
    internal string Version { get; }
    internal string Sha256 { get; }
    internal JsonElement Limits { get; }
    internal JsonElement TestHarness { get; }
    internal double Number(string name) => Limits.GetProperty(name).GetDouble();
    internal double HarnessNumber(string name) => TestHarness.GetProperty(name).GetDouble();

    internal static QualificationProfile Load(string repository, string? selectedName = null)
    {
        string path = Path.Combine(repository, "tests", "resilience", "performance-profiles.json");
        byte[] bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        string name = selectedName ?? Environment.GetEnvironmentVariable("TIA_BRIDGE_QUALIFICATION_PROFILE") ?? root.GetProperty("default_profile").GetString()!;
        JsonElement profile = root.GetProperty("profiles").GetProperty(name);
        return new(name, profile.GetProperty("version").GetString()!, Convert.ToHexStringLower(SHA256.HashData(bytes)), profile.GetProperty("limits").Clone(), profile.GetProperty("test_harness").Clone());
    }
}
