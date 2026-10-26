using System.Text.Json;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public class PerformanceEvidenceTests
{
    [Fact]
    public async Task Failure_preserves_only_observed_partial_samples_and_no_qualification_pass()
    {
        await using var fixture = new ApiFixture();
        string scenario = "partial-proof-" + Guid.NewGuid().ToString("N");
        var measured = new List<double>();
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var proof = new PerformanceEvidence(fixture, scenario, nameof(Failure_preserves_only_observed_partial_samples_and_no_qualification_pass));
            proof.Register("nf03-inplace-repository", () => measured, 100, 100, null, fixture.Files.Repository);
            measured.Add(123.456);
            throw new InvalidOperationException("Synthetic interrupted measurement.");
        }));
        string path = Path.Combine(fixture.Files.Repository, ".tia-bridge", "resilience", ResilienceEvidence.CurrentRunId, "performance", scenario + ".json");
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal("FAIL", json.RootElement.GetProperty("test_status").GetString());
        var metric = json.RootElement.GetProperty("metrics")[0];
        Assert.Equal("INCOMPLETE", metric.GetProperty("status").GetString());
        Assert.Equal(1, metric.GetProperty("sample_count").GetInt32());
        Assert.Equal(123.456, metric.GetProperty("samples")[0].GetProperty("value").GetDouble());
        Assert.Equal(123.456, metric.GetProperty("distribution").GetProperty("maximum_ms").GetDouble());
    }

    [Fact]
    public async Task Existing_artifact_cannot_be_overwritten_even_by_another_writer()
    {
        await using var fixture = new ApiFixture();
        string path = Path.Combine(fixture.Files.Root, "owned-proof.json");
        PerformanceEvidence.WriteNew(path, [1, 2, 3]);
        Assert.Throws<IOException>(() => PerformanceEvidence.WriteNew(path, [4, 5]));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
    }
}
