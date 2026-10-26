using System.Text.Json;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public class ResilienceArtifactRegressionTests
{
    [Theory]
    [InlineData("modified")]
    [InlineData("truncated")]
    [InlineData("missing")]
    [InlineData("replacement")]
    public async Task Previously_verified_result_is_rechecked_during_parallel_reads_and_after_restart(string defect)
    {
        await using var f = new ApiFixture();
        await f.Start(mutation: true, resilienceMode: "artifact");
        using var client = f.Client();
        var id = await f.Submit(client, ApiFixture.Request("sync.apply"));
        await ResilienceEvidence.Marker(f, "receipt-received.reached");
        ResilienceEvidence.Release(f, "receipt-received");
        await f.Until(client, id, r => r.State == OperationState.SUCCEEDED);
        string resultUrl = "/v1/operations/" + id + "/result";
        var verified = await client.SendAsync(HttpMethod.Get, resultUrl);
        Assert.Equal(200, verified.StatusCode);
        var original = ContractJson.ReadReport(verified.Json);
        var reference = Assert.Single(original.ArtifactRefs);
        var descriptor = JsonSerializer.Deserialize<TiaBridge.Host.Storage.ArtifactDescriptor>(
            await File.ReadAllBytesAsync(Path.Combine(f.Files.Root, "result-artifact.json")))!;
        Assert.Equal(reference.ArtifactId, descriptor.ArtifactId);
        Assert.Equal(reference.Sha256, descriptor.Sha256);
        var events = Journal(f, id);

        // A different operation has its own proof. Corruption of the first must not
        // turn successful unrelated result reads into errors or reuse its validation.
        var other = await f.Submit(client, ApiFixture.Request("sync.apply"));
        await f.Until(client, other, r => r.State == OperationState.SUCCEEDED);
        var otherResult = await client.SendAsync(HttpMethod.Get, "/v1/operations/" + other + "/result");
        Assert.Equal(200, otherResult.StatusCode);
        Assert.NotEqual(reference.ArtifactId, Assert.Single(ContractJson.ReadReport(otherResult.Json).ArtifactRefs).ArtifactId);

        string path = Path.Combine(f.Files.Root, descriptor.RelativePath);
        byte[] before = await File.ReadAllBytesAsync(path);
        File.SetAttributes(path, FileAttributes.Normal);
        if (defect == "missing") File.Delete(path);
        else if (defect == "replacement")
        {
            // Same length, different bytes and a new file at the registered path.
            string replacement = Path.Combine(f.Files.Root, "replacement.tmp");
            await File.WriteAllBytesAsync(replacement, before.Select(b => (byte)(b ^ 1)).ToArray());
            File.Move(replacement, path, overwrite: true);
        }
        else await File.WriteAllBytesAsync(path, defect == "truncated" ? before[..1] : before.Select(b => (byte)(b ^ 1)).ToArray());

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var reader = f.Client();
            if (_ == 0)
            {
                var independent = await f.Submit(reader, ApiFixture.Request("sync.apply"));
                await f.Until(reader, independent, r => r.State == OperationState.SUCCEEDED);
                Assert.Equal(200, (await reader.SendAsync(HttpMethod.Get, "/v1/operations/" + independent + "/result")).StatusCode);
            }
            var status = await reader.SendAsync(HttpMethod.Get, "/v1/operations/" + id);
            Assert.Equal(200, status.StatusCode);
            Assert.Equal(OperationState.SUCCEEDED, ContractJson.ReadReport(status.Json).State);
            var invalid = await reader.SendAsync(HttpMethod.Get, resultUrl);
            Assert.Equal(422, invalid.StatusCode);
            Assert.Equal(ErrorCode.VERIFY_FAILED, ContractJson.ReadError(invalid.Json).Code);
            Assert.Equal(200, (await reader.SendAsync(HttpMethod.Get, "/v1/operations/" + other + "/result")).StatusCode);
        }));
        Assert.Equal(events, Journal(f, id));
        Assert.Equal(3, f.Files.Count("operation_results"));
        await f.Stop(); await f.Start(mutation: true);
        using var restarted = f.Client();
        Assert.Equal(422, (await restarted.SendAsync(HttpMethod.Get, resultUrl)).StatusCode);
        Assert.Equal(OperationState.SUCCEEDED, ContractJson.ReadReport((await restarted.SendAsync(HttpMethod.Get, "/v1/operations/" + id)).Json).State);
        Assert.Equal(200, (await restarted.SendAsync(HttpMethod.Get, "/v1/operations/" + other + "/result")).StatusCode);
        Assert.Equal(events, Journal(f, id));
        Assert.Equal(1, f.Files.Open().AttemptCount(id));
        Assert.Equal(1, ResilienceEvidence.DurableEffects(f, id));
        if (defect == "missing") Assert.False(File.Exists(path));
        else Assert.NotEqual(before, await File.ReadAllBytesAsync(path));
    }

    private static (long Sequence, DateTimeOffset At, string Report)[] Journal(ApiFixture f, TiaBridge.Core.Identity.OperationId id)
        => f.Files.Open().Events(id).Select(e => (e.Sequence, e.OccurredAt, Convert.ToBase64String(ContractJson.WriteReport(e.Report)))).ToArray();
}
