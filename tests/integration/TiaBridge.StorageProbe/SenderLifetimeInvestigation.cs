using System.Text.Json;
using TiaBridge.Host.Storage;
using TiaBridge.Ipc;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class SenderLifetimeInvestigation
{
    internal static async Task<int> Run(string root, string repo, string specification)
    {
        StorageLocation.Validate(root, repo);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(specification));
        var spec = doc.RootElement;
        int count = spec.GetProperty("operations").GetInt32(), delay = spec.GetProperty("delay_ms").GetInt32(), idle = spec.GetProperty("idle_seconds").GetInt32();
        if (count is < 2 or > 2000 || delay is < 0 or > 1000 || idle is < 1 or > 120) return 2;
        InfrastructureInvestigation.WriteNew(Path.Combine(root, "lifetime.json"), spec);
        await using var session = await InfrastructureInvestigation.ProbeSession.Start(root, repo, "sender-lifetime", false);
        long fence = 0;
        async Task Send(string action, string phase)
        {
            var reply = await session.Dispatch(Guid.NewGuid(), ++fence, new { action, phase });
            if (reply.ProtocolVersion != "1.1" || Protocol.Read<WorkerReply>(reply.Body).Receipt.Disposition != "Succeeded") throw new ProtocolException();
        }
        await Send("sample", "cold");
        for (int n = 0; n < 5; n++) await Send("send", "warmup");
        await Send("snapshot", "warm");
        for (int n = 0; n < count; n++) { await Send("send", "active"); if (delay > 0) await Task.Delay(delay); }
        await Send("snapshot", "idle-enter");
        await Task.Delay(TimeSpan.FromSeconds(idle));
        await Send("snapshot", "idle");
        if (spec.GetProperty("controlled_gc").GetBoolean()) await Send("gc", "controlled-gc");
        if (spec.GetProperty("recovery").GetBoolean()) await Send("checks", "recovery");
        if (spec.GetProperty("disconnect").GetBoolean()) await session.Disconnect(); else await session.Stop();
        InfrastructureInvestigation.WriteNew(Path.Combine(root, "controller-result.json"), new { status = "PASS", epoch = session.Epoch, pid = session.Child.Id, exit_code = session.Child.ExitCode, dispatches = fence, heartbeats = session.Heartbeats });
        return 0;
    }
}
