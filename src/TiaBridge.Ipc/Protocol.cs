using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TiaBridge.Ipc;

public enum MessageKind { Hello, Ready, Dispatch, Started, Heartbeat, Progress, Receipt, Cancel, Stop }
public enum FakeScenario { Success, DelayedSuccess, FailBefore, FailAfter, Hang, NoHeartbeat, HeartbeatWithoutProgress, Timeout, Disconnect, Exit, LateSuccess, UnknownMutation, Cancellable, DuplicateDispatch, SuccessAfterRetry, WrongFence, WrongPlanHash }
public sealed record ArtifactPointer(string ArtifactId, string Sha256);
public sealed record Bootstrap(string PipeName, string Secret, Guid WorkerEpoch, Guid Nonce, int HostPid, long HostStartTicks, string ArtifactRoot);
public sealed record Hello(int Pid, long StartTicks, Guid Nonce, string WorkerId, string Build);
public sealed record Dispatch(ArtifactPointer Request, ArtifactPointer SuccessReport, FakeScenario Scenario, int DelayMs, int Attempt, string InputHash, string? PlanHash, string FailureCode);
public sealed record Receipt(string Disposition, string? Report, string? ErrorCode, int EffectCount, bool Quiescent);
public sealed record WorkerReply(int Attempt, string InputHash, string? PlanHash, Receipt Receipt);
public sealed record HeartbeatBody(bool Synthetic);
public sealed record ReadyBody(bool Accepted);
public sealed record CancelBody(bool Requested);
public sealed record StopBody(bool SyntheticShutdown);
public sealed record StartedBody(int Attempt);
public sealed record ProgressBody(string Phase);
public sealed record WireMessage(string ProtocolVersion, Guid WorkerEpoch, long Sequence, MessageKind Kind, Guid? OperationId, long Fence, JsonElement Body);
public sealed class ProtocolException() : IOException("Invalid authenticated IPC message.");

/// <summary>Wire-only DTOs. No Core, vendor references, runtime activation or arbitrary type loading.</summary>
public static class Protocol
{
    public const int MaxFrameBytes = 1024 * 1024;
    public const int MaxReceiptBytes = 64 * 1024;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        RespectRequiredConstructorParameters = true
    };
    public static T Read<T>(JsonElement body)
    { try { return body.Deserialize<T>(Json) ?? throw new ProtocolException(); } catch (JsonException) { throw new ProtocolException(); } }
    public static JsonElement Body<T>(T body) => JsonSerializer.SerializeToElement(body, Json);
    public static bool IsHash(string? hash) => hash is not null && Regex.IsMatch(hash, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant);
    public static void Validate(WireMessage message)
    {
        if (message.ProtocolVersion != "1.0" || message.WorkerEpoch == Guid.Empty || message.Sequence <= 0 || message.OperationId == Guid.Empty || message.Fence < 0 || !Enum.IsDefined(message.Kind)) throw new ProtocolException();
        bool control = message.Kind is MessageKind.Hello or MessageKind.Ready or MessageKind.Heartbeat or MessageKind.Stop;
        if (control ? message.OperationId is not null || message.Fence != 0 : message.OperationId is null || message.Fence <= 0) throw new ProtocolException();
        switch (message.Kind)
        {
            case MessageKind.Hello:
                var hello = Read<Hello>(message.Body);
                if (hello.Pid <= 0 || hello.StartTicks <= 0 || hello.Nonce == Guid.Empty || string.IsNullOrWhiteSpace(hello.WorkerId) || hello.WorkerId.Length > 128 || string.IsNullOrWhiteSpace(hello.Build) || hello.Build.Length > 128) throw new ProtocolException();
                break;
            case MessageKind.Ready: if (!Read<ReadyBody>(message.Body).Accepted) throw new ProtocolException(); break;
            case MessageKind.Heartbeat: if (!Read<HeartbeatBody>(message.Body).Synthetic) throw new ProtocolException(); break;
            case MessageKind.Cancel: if (!Read<CancelBody>(message.Body).Requested) throw new ProtocolException(); break;
            case MessageKind.Stop: if (!Read<StopBody>(message.Body).SyntheticShutdown) throw new ProtocolException(); break;
            case MessageKind.Started: if (Read<StartedBody>(message.Body).Attempt is < 1 or > 3) throw new ProtocolException(); break;
            case MessageKind.Progress:
                var phase = Read<ProgressBody>(message.Body).Phase;
                if (phase is null || !Regex.IsMatch(phase, "\\A[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}\\z")) throw new ProtocolException(); break;
            case MessageKind.Dispatch:
                var dispatch = Read<Dispatch>(message.Body);
                if (dispatch.Attempt is < 1 or > 3 || dispatch.DelayMs is < 0 or > 600000 || !Enum.IsDefined(dispatch.Scenario) || !IsHash(dispatch.InputHash) || dispatch.PlanHash is not null && !IsHash(dispatch.PlanHash)
                    || dispatch.FailureCode is null || !Regex.IsMatch(dispatch.FailureCode, "\\A[A-Z][A-Z0-9_]{0,127}\\z")) throw new ProtocolException();
                foreach (var pointer in new[] { dispatch.Request, dispatch.SuccessReport })
                    if (pointer is null || !Regex.IsMatch(pointer.ArtifactId ?? "", "\\Aartifact-[0-9a-f]{32}\\z") || !IsHash(pointer.Sha256)) throw new ProtocolException();
                break;
            case MessageKind.Receipt:
                var reply = Read<WorkerReply>(message.Body);
                if (reply.Attempt is < 1 or > 3 || !IsHash(reply.InputHash) || reply.PlanHash is not null && !IsHash(reply.PlanHash) || reply.Receipt is null) throw new ProtocolException();
                var receipt = reply.Receipt;
                if (receipt.Disposition is not ("Succeeded" or "Rejected" or "Failed" or "Cancelled") || receipt.EffectCount is < 0 or > 1
                    || receipt.Report is not null && System.Text.Encoding.UTF8.GetByteCount(receipt.Report) > MaxReceiptBytes
                    || receipt.ErrorCode is not null && !Regex.IsMatch(receipt.ErrorCode, "\\A[A-Z][A-Z0-9_]{0,127}\\z")) throw new ProtocolException();
                break;
            default: throw new ProtocolException();
        }
    }
    public static void CheckJson(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            void Walk(JsonElement node)
            {
                if (node.ValueKind == JsonValueKind.Object)
                { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var p in node.EnumerateObject()) { if (!names.Add(p.Name)) throw new ProtocolException(); Walk(p.Value); } }
                else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) Walk(item);
                else if (node.ValueKind == JsonValueKind.String) _ = new System.Text.UTF8Encoding(false, true).GetByteCount(node.GetString()!);
            }
            Walk(doc.RootElement);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { throw new ProtocolException(); }
    }
    public static byte[] ReadArtifact(string root, ArtifactPointer pointer, int maximum)
    {
        if (!Regex.IsMatch(pointer.ArtifactId ?? "", "\\Aartifact-[0-9a-f]{32}\\z") || !IsHash(pointer.Sha256)) throw new ProtocolException();
        string full = Path.GetFullPath(root);
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(root) || root.StartsWith("\\\\", StringComparison.Ordinal)) throw new ProtocolException();
        string path = Path.Combine(full, "artifacts", pointer.ArtifactId + ".blob");
        for (string? parent = path; parent is not null; parent = Path.GetDirectoryName(parent))
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new ProtocolException();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum) throw new ProtocolException();
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != pointer.Sha256) throw new ProtocolException();
        return bytes;
    }
}

/// <summary>One reader; concurrent sends are serialized. A fresh secret/epoch binds each connection.</summary>
public sealed class FramedConnection(Stream stream, byte[] secret, Guid epoch) : IAsyncDisposable
{
    private readonly SemaphoreSlim sendGate = new(1);
    private readonly byte[] key = secret.Length == 32 ? secret.ToArray() : throw new ArgumentException("32-byte key required.");
    private long sent, received;
    public async Task SendAsync(MessageKind kind, Guid? operationId, long fence, JsonElement body, CancellationToken ct = default)
    {
        await sendGate.WaitAsync(ct);
        try
        {
            var message = new WireMessage("1.0", epoch, checked(++sent), kind, operationId, fence, body);
            Protocol.Validate(message);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, Protocol.Json);
            if (json.Length + 32 > Protocol.MaxFrameBytes) throw new ProtocolException();
            var prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, json.Length + 32);
            await stream.WriteAsync(prefix, ct); await stream.WriteAsync(json, ct);
            await stream.WriteAsync(HMACSHA256.HashData(key, json), ct); await stream.FlushAsync(ct);
        }
        finally { sendGate.Release(); }
    }
    public async Task<WireMessage> ReadAsync(CancellationToken ct = default)
    {
        var prefix = new byte[4]; await stream.ReadExactlyAsync(prefix, ct);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 32 or > Protocol.MaxFrameBytes) throw new ProtocolException();
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, ct);
        int count = length - 32;
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, bytes.AsSpan(0, count)), bytes.AsSpan(count))) throw new ProtocolException();
        Protocol.CheckJson(bytes.AsMemory(0, count)); WireMessage message;
        try { message = JsonSerializer.Deserialize<WireMessage>(bytes.AsSpan(0, count), Protocol.Json) ?? throw new ProtocolException(); }
        catch (JsonException) { throw new ProtocolException(); }
        if (message.ProtocolVersion != "1.0" || message.WorkerEpoch != epoch || message.Sequence != checked(received + 1)
            || !Enum.IsDefined(message.Kind) || message.Fence < 0 || message.OperationId == Guid.Empty) throw new ProtocolException();
        Protocol.Validate(message);
        received = message.Sequence; return message;
    }
    public async ValueTask DisposeAsync() { await stream.DisposeAsync(); CryptographicOperations.ZeroMemory(key); }
}
