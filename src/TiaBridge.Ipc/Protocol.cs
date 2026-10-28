using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
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
public sealed record V19Target(string ProjectKey, int Pid, long StartTicks, string ProjectPath);
public sealed record V19HmiExportBinding(string AuthorizationId, string ProjectKey, string ScopeToken, string[] DevicePath,
    string ModuleName, string TiaBuild, string WorkerBuild, DateTimeOffset NotBeforeUtc, DateTimeOffset ExpiresUtc, string[] ExpectedFunctions);
public sealed record V19Settings(V19Target[] Targets, string ProjectRoot, string Apartment = "STA", V19HmiExportBinding? HmiExport = null);
public sealed record V19HmiNativeFile(string Name, string Sha256, long Bytes);
public sealed record V19HmiExportObservation(V19Observation Project, string AuthorizationId, string ScopeToken, string ModuleName,
    string IdentityState, V19HmiNativeFile[] Files);
public static class V19HmiExportPolicy
{
    public const string Profile = "v19.hmi.script-module.export.v1";
    public static bool Valid(V19HmiExportBinding b, DateTimeOffset now) => Guid.TryParseExact(b.AuthorizationId, "D", out var id) && id != Guid.Empty
        && Guid.TryParseExact(b.ProjectKey, "D", out var key) && key != Guid.Empty
        && !string.IsNullOrWhiteSpace(b.ScopeToken) && b.ScopeToken.Length <= 128
        && b.DevicePath is { Length: >= 2 and <= 10 } && b.DevicePath.All(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 512)
        && !string.IsNullOrWhiteSpace(b.ModuleName) && b.ModuleName.Length <= 160
        && !string.IsNullOrWhiteSpace(b.TiaBuild) && b.TiaBuild.Length <= 128
        && b.WorkerBuild is not null && Regex.IsMatch(b.WorkerBuild, "\\A1\\.0\\+[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)
        && b.NotBeforeUtc <= now && now < b.ExpiresUtc && b.ExpiresUtc - b.NotBeforeUtc <= TimeSpan.FromMinutes(30)
        && b.ExpectedFunctions is { Length: >= 1 and <= 128 } && b.ExpectedFunctions.All(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 160)
        && b.ExpectedFunctions.Distinct(StringComparer.OrdinalIgnoreCase).Count() == b.ExpectedFunctions.Length;
    public static string DirectoryName(Guid operationId) => "hmi-export-" + operationId.ToString("N");
}
public sealed record V19Bootstrap(Bootstrap Transport, V19Settings Settings);
public sealed record InspectDispatch(ArtifactPointer Request, int Attempt, string InputHash, string? PlanHash);
// Keep the IPC 1.1 observation field set stable. DiscoveryV1 metadata is encoded
// inside the existing opaque Locator string; old 1.1 readers still see a string.
public sealed record V19Device(string Name, string Family);
public sealed record V19Node(string Locator, string SoftwareType);
public sealed record V19NodeLocator(int ObservationIndex, int? ParentObservationIndex, int DeviceObservationIndex,
    string Name, string ObjectKind, string SoftwareState, string HmiKind, string IdentityState,
    string? PublicId = null, string? PublicIdValidityScope = null);
public static class V19NodeLocatorCodec
{
    private const string Prefix = "v19discovery1:";
    public static string Encode(V19NodeLocator value)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, Protocol.Json);
        string encoded = Prefix + Convert.ToBase64String(json).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (encoded.Length > 4096) throw new InvalidOperationException("LIMIT_EXCEEDED");
        return encoded;
    }
    public static bool TryDecode(string value, out V19NodeLocator locator)
    {
        locator = null!;
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal) || value.Length > 4096) return false;
        try
        {
            string base64 = value.Substring(Prefix.Length).Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            byte[] json = Convert.FromBase64String(base64); Protocol.CheckJson(json);
            locator = JsonSerializer.Deserialize<V19NodeLocator>(json, Protocol.Json)!;
            return locator is not null;
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException or ProtocolException) { return false; }
    }
}
public sealed record V19Observation(string ProjectKey, Guid SessionEpoch, int TiaPid, long TiaStartTicks,
    string ProjectPath, string ProjectName, bool IsModifiedBefore, bool IsModifiedAfter, string ApiBuild,
    V19Device[] Devices, V19Node[] Nodes, double EnumerationMs, double AttachMs, double ReadMs, double DisposeMs,
    int OwnerThreadId, string Apartment, int[] CallbackThreads, bool PortalAliveAfterDispose);
public static class V19DiscoveryProfile
{
    public const string Id = "v19.discovery.v1";
    public const int MaxDevices = 64;
    public const int MaxNodes = 256;
    public const int MaxDepth = 8;
}
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
        if (message.ProtocolVersion is not ("1.0" or "1.1") || message.WorkerEpoch == Guid.Empty || message.Sequence <= 0 || message.OperationId == Guid.Empty || message.Fence < 0 || !Enum.IsDefined(typeof(MessageKind), message.Kind)) throw new ProtocolException();
        bool control = message.Kind is MessageKind.Hello or MessageKind.Ready or MessageKind.Heartbeat or MessageKind.Stop;
        if (control ? message.OperationId is not null || message.Fence != 0 : message.OperationId is null || message.Fence <= 0) throw new ProtocolException();
        switch (message.Kind)
        {
            case MessageKind.Hello:
                var hello = Read<Hello>(message.Body);
                if (hello.Pid <= 0 || hello.StartTicks <= 0 || hello.Nonce == Guid.Empty || string.IsNullOrWhiteSpace(hello.WorkerId) || hello.WorkerId.Length > 128 || string.IsNullOrWhiteSpace(hello.Build) || hello.Build.Length > 128) throw new ProtocolException();
                break;
            case MessageKind.Ready: if (!Read<ReadyBody>(message.Body).Accepted) throw new ProtocolException(); break;
            case MessageKind.Heartbeat: if (Read<HeartbeatBody>(message.Body).Synthetic != (message.ProtocolVersion == "1.0")) throw new ProtocolException(); break;
            case MessageKind.Cancel: if (!Read<CancelBody>(message.Body).Requested) throw new ProtocolException(); break;
            case MessageKind.Stop: if (Read<StopBody>(message.Body).SyntheticShutdown != (message.ProtocolVersion == "1.0")) throw new ProtocolException(); break;
            case MessageKind.Started: if (Read<StartedBody>(message.Body).Attempt is < 1 or > 3) throw new ProtocolException(); break;
            case MessageKind.Progress:
                var phase = Read<ProgressBody>(message.Body).Phase;
                if (phase is null || !Regex.IsMatch(phase, "\\A[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}\\z")) throw new ProtocolException(); break;
            case MessageKind.Dispatch:
                if (message.ProtocolVersion == "1.1")
                {
                    var inspect = Read<InspectDispatch>(message.Body);
                    if (inspect.Attempt != 1 || inspect.PlanHash is not null || !IsHash(inspect.InputHash) || inspect.Request is null
                        || !Regex.IsMatch(inspect.Request.ArtifactId ?? "", "\\Aartifact-[0-9a-f]{32}\\z") || !IsHash(inspect.Request.Sha256)) throw new ProtocolException();
                    break;
                }
                var dispatch = Read<Dispatch>(message.Body);
                if (dispatch.Attempt is < 1 or > 3 || dispatch.DelayMs is < 0 or > 600000 || !Enum.IsDefined(typeof(FakeScenario), dispatch.Scenario) || !IsHash(dispatch.InputHash) || dispatch.PlanHash is not null && !IsHash(dispatch.PlanHash)
                    || dispatch.FailureCode is null || !Regex.IsMatch(dispatch.FailureCode, "\\A[A-Z][A-Z0-9_]{0,127}\\z")) throw new ProtocolException();
                foreach (var pointer in new[] { dispatch.Request, dispatch.SuccessReport })
                    if (pointer is null || !Regex.IsMatch(pointer.ArtifactId ?? "", "\\Aartifact-[0-9a-f]{32}\\z") || !IsHash(pointer.Sha256)) throw new ProtocolException();
                break;
            case MessageKind.Receipt:
                var reply = Read<WorkerReply>(message.Body);
                if (reply.Attempt is < 1 or > 3 || !IsHash(reply.InputHash) || reply.PlanHash is not null && !IsHash(reply.PlanHash) || reply.Receipt is null) throw new ProtocolException();
                var receipt = reply.Receipt;
                if (message.ProtocolVersion == "1.1" && (reply.Attempt != 1 || reply.PlanHash is not null || receipt.EffectCount != 0)) throw new ProtocolException();
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
        if (!Compatibility.IsWindows || !Compatibility.FullyQualified(root) || root.StartsWith("\\\\", StringComparison.Ordinal)) throw new ProtocolException();
        string path = Path.Combine(full, "artifacts", pointer.ArtifactId + ".blob");
        for (string? parent = path; parent is not null; parent = Path.GetDirectoryName(parent))
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new ProtocolException();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum) throw new ProtocolException();
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (Compatibility.Hex(Compatibility.Sha256(bytes)) != pointer.Sha256) throw new ProtocolException();
        return bytes;
    }
}

/// <summary>One reader; concurrent sends are serialized. A fresh secret/epoch binds each connection.</summary>
public sealed class FramedConnection(Stream stream, byte[] secret, Guid epoch, string protocolVersion = "1.0") : IAsyncDisposable
{
    private readonly SemaphoreSlim sendGate = new(1);
    private readonly byte[] key = secret.Length == 32 ? secret.ToArray() : throw new ArgumentException("32-byte key required.");
    private long sent, received;
    public async Task SendAsync(MessageKind kind, Guid? operationId, long fence, JsonElement body, CancellationToken ct = default)
    {
        await sendGate.WaitAsync(ct);
        try
        {
            var message = new WireMessage(protocolVersion, epoch, checked(++sent), kind, operationId, fence, body);
            Protocol.Validate(message);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, Protocol.Json);
            if (json.Length + 32 > Protocol.MaxFrameBytes) throw new ProtocolException();
            var prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, json.Length + 32);
            await stream.WriteAsync(prefix, ct); await stream.WriteAsync(json, ct);
            await stream.WriteAsync(Compatibility.Mac(key, json), ct); await stream.FlushAsync(ct);
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
        if (!Compatibility.FixedEquals(Compatibility.Mac(key, bytes.Take(count).ToArray()), bytes.Skip(count).ToArray())) throw new ProtocolException();
        Protocol.CheckJson(bytes.AsMemory(0, count)); WireMessage message;
        try { message = JsonSerializer.Deserialize<WireMessage>(bytes.AsSpan(0, count), Protocol.Json) ?? throw new ProtocolException(); }
        catch (JsonException) { throw new ProtocolException(); }
        if (message.ProtocolVersion != protocolVersion || message.WorkerEpoch != epoch || message.Sequence != checked(received + 1)
            || !Enum.IsDefined(typeof(MessageKind), message.Kind) || message.Fence < 0 || message.OperationId == Guid.Empty) throw new ProtocolException();
        Protocol.Validate(message);
        received = message.Sequence; return message;
    }
    public async ValueTask DisposeAsync() { await stream.DisposeAsync(); Array.Clear(key, 0, key.Length); }
}
