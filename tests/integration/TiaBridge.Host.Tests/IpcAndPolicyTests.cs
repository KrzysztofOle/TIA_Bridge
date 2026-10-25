using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Operations;
using TiaBridge.Host.Scheduling;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

public class IpcAndPolicyTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("ready")]
    [InlineData("heartbeat")]
    [InlineData("dispatch")]
    [InlineData("started")]
    [InlineData("progress")]
    [InlineData("receipt")]
    [InlineData("cancel")]
    [InlineData("stop")]
    public async Task Shared_ipc_schema_examples_round_trip_through_actual_framing(string name)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "IpcFixtures", name + ".json"));
        Protocol.CheckJson(bytes); var message = JsonSerializer.Deserialize<WireMessage>(bytes, Protocol.Json)!; Protocol.Validate(message);
        using var output = new MemoryStream(); await using var sender = new FramedConnection(output, new byte[32], message.WorkerEpoch);
        await sender.SendAsync(message.Kind, message.OperationId, message.Fence, message.Body);
        await using var reader = new FramedConnection(new MemoryStream(output.ToArray()), new byte[32], message.WorkerEpoch);
        Assert.Equal(message.Kind, (await reader.ReadAsync()).Kind);
    }
    [Fact]
    public async Task Authenticated_frames_round_trip_and_enforce_direction_sequence()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32); Guid epoch = Guid.NewGuid();
        using var bytes = new MemoryStream(); await using var writer = new FramedConnection(bytes, key, epoch);
        await writer.SendAsync(MessageKind.Heartbeat, null, 0, Protocol.Body(new { synthetic = true }));
        byte[] frame = bytes.ToArray(); using var input = new MemoryStream(frame.Concat(frame).ToArray());
        await using var reader = new FramedConnection(input, key, epoch);
        Assert.Equal(MessageKind.Heartbeat, (await reader.ReadAsync()).Kind);
        await Assert.ThrowsAsync<ProtocolException>(() => reader.ReadAsync());
    }
    [Theory]
    [InlineData("hmac")]
    [InlineData("version")]
    [InlineData("epoch")]
    [InlineData("sequence")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("utf8")]
    [InlineData("depth")]
    public async Task Corrupt_replayed_or_incompatible_ipc_is_rejected(string fault)
    {
        byte[] key = RandomNumberGenerator.GetBytes(32); Guid epoch = Guid.NewGuid();
        var message = new WireMessage("1.0", epoch, 1, MessageKind.Heartbeat, null, 0, Protocol.Body(new { synthetic = true }));
        var node = JsonNode.Parse(JsonSerializer.Serialize(message, Protocol.Json))!;
        if (fault == "version") node["protocol_version"] = "2.0";
        if (fault == "epoch") node["worker_epoch"] = Guid.NewGuid().ToString();
        if (fault == "sequence") node["sequence"] = 2;
        if (fault == "unknown") node["unknown_required_feature"] = true;
        if (fault == "missing") node.AsObject().Remove("kind");
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(node);
        if (fault == "duplicate") json = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(json).Replace("\"sequence\":1", "\"sequence\":1,\"sequence\":1"));
        if (fault == "utf8") json = [0xff, 0xfe];
        if (fault == "depth") json = Encoding.UTF8.GetBytes(new string('[', 33) + "0" + new string(']', 33));
        byte[] frame = new byte[json.Length + 36]; BinaryPrimitives.WriteInt32LittleEndian(frame, json.Length + 32); json.CopyTo(frame, 4); HMACSHA256.HashData(key, json).CopyTo(frame, json.Length + 4);
        if (fault == "hmac") frame[^1] ^= 1;
        await using var reader = new FramedConnection(new MemoryStream(frame), key, epoch);
        await Assert.ThrowsAsync<ProtocolException>(() => reader.ReadAsync());
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(1048577)]
    public async Task Frame_limits_are_checked_before_body_allocation(int length)
    {
        byte[] prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, length);
        await using var connection = new FramedConnection(new MemoryStream(prefix), new byte[32], Guid.NewGuid());
        await Assert.ThrowsAsync<ProtocolException>(() => connection.ReadAsync());
    }
    [Fact]
    public async Task Truncated_or_wrong_key_frames_never_become_receipts()
    {
        using var output = new MemoryStream(); var epoch = Guid.NewGuid();
        await using var sender = new FramedConnection(output, new byte[32], epoch);
        await sender.SendAsync(MessageKind.Heartbeat, null, 0, Protocol.Body(new { synthetic = true }));
        byte[] bytes = output.ToArray();
        await using var shortReader = new FramedConnection(new MemoryStream(bytes[..^1]), new byte[32], epoch);
        await Assert.ThrowsAsync<EndOfStreamException>(() => shortReader.ReadAsync());
        await using var wrong = new FramedConnection(new MemoryStream(bytes), Enumerable.Repeat((byte)1, 32).ToArray(), epoch);
        await Assert.ThrowsAsync<ProtocolException>(() => wrong.ReadAsync());
    }
    [Theory]
    [InlineData(ErrorCode.IDENTITY_MISMATCH)]
    [InlineData(ErrorCode.SCHEMA_VERSION)]
    [InlineData(ErrorCode.ACCESS_DENIED)]
    [InlineData(ErrorCode.AUTHORIZATION_EXPIRED)]
    [InlineData(ErrorCode.CONFLICT)]
    [InlineData(ErrorCode.STALE_PLAN)]
    public void Policy_and_identity_errors_are_never_retryable(ErrorCode code)
        => Assert.False(RetryPolicy.CanRetry(true, false, true, 1, code));
    [Fact]
    public void Retry_budget_and_jitter_are_bounded_and_deterministic()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), RetryPolicy.Delay(1, new FixedJitter()));
        Assert.Equal(TimeSpan.FromSeconds(2.5), RetryPolicy.Delay(2, new FixedJitter(1)));
        Assert.Throws<ArgumentException>(() => RetryPolicy.Delay(1, new FixedJitter(double.NaN)));
        Assert.True(RetryPolicy.CanRetry(true, false, true, 2, ErrorCode.TIA_UNAVAILABLE));
        Assert.False(RetryPolicy.CanRetry(true, false, true, 3, ErrorCode.TIA_UNAVAILABLE));
        Assert.False(RetryPolicy.CanRetry(true, false, false, 1, ErrorCode.TIA_UNAVAILABLE));
        Assert.False(RetryPolicy.CanRetry(true, true, true, 1, ErrorCode.TIA_UNAVAILABLE));
    }
    [Fact]
    public void Breaker_has_one_read_only_half_open_probe_and_resets_on_success()
    {
        var clock = new ManualClock(); var breaker = new CircuitBreaker();
        for (int i = 0; i < 3; i++) breaker.Failure(clock.GetUtcNow());
        Assert.False(breaker.TryEnter(clock.GetUtcNow(), true)); clock.Advance(TimeSpan.FromSeconds(60));
        Assert.False(breaker.TryEnter(clock.GetUtcNow(), false)); Assert.True(breaker.TryEnter(clock.GetUtcNow(), true));
        Assert.False(breaker.TryEnter(clock.GetUtcNow(), true)); breaker.Success(); Assert.True(breaker.TryEnter(clock.GetUtcNow(), true));
        Assert.Null(breaker.State.OpenedAt);
    }
    [Fact]
    public void Breaker_window_and_failed_half_open_keep_cooldown()
    {
        var clock = new ManualClock(); var breaker = new CircuitBreaker(); breaker.Failure(clock.GetUtcNow());
        clock.Advance(TimeSpan.FromSeconds(61)); breaker.Failure(clock.GetUtcNow()); Assert.Equal(1, breaker.State.Failures);
        breaker.Failure(clock.GetUtcNow()); breaker.Failure(clock.GetUtcNow()); clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(breaker.TryEnter(clock.GetUtcNow(), true)); breaker.Failure(clock.GetUtcNow());
        Assert.False(breaker.TryEnter(clock.GetUtcNow(), true));
    }
    [Fact]
    public void Watchdog_keeps_heartbeat_deadline_progress_and_lease_separate()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(Watchdog.Evaluate(now, now.AddSeconds(30), now, true, false));
        Assert.Equal(ErrorCode.WORKER_UNAVAILABLE, Watchdog.Evaluate(now, now.AddSeconds(30), now.AddSeconds(-6), true, false));
        Assert.Equal(ErrorCode.TIMEOUT, Watchdog.Evaluate(now, now, now, true, false));
        Assert.Equal(TimeSpan.FromSeconds(15), Watchdog.Lease); Assert.Equal(TimeSpan.FromSeconds(2), Watchdog.HeartbeatInterval);
    }
}
