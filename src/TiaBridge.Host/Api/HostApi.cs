using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using TiaBridge.Client;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Scheduling;
using TiaBridge.Host.Storage;
using TiaBridge.Host.Supervision;
using static TiaBridge.Host.Api.ApiResponses;
using BadHttpRequestException = Microsoft.AspNetCore.Http.BadHttpRequestException;

namespace TiaBridge.Host.Api;

[SupportedOSPlatform("windows")]
public static class HostApi
{
    public static async Task RunAsync(HostConfiguration config, bool stdioLifetime = false)
    {
        var location = StorageLocation.Open(config.StatePath, config.RepositoryRoot); var store = new SqliteOperationStore(location);
        using var databaseLifetime = store.KeepDatabaseOpen();
        using var metadataReaders = store.OpenMetadataReaders();
        foreach (string project in config.RegisteredProjects) store.RegisterProject(new(UuidIdentity.ParseValue(project)));
        var profile = new ConfiguredFakeProfile(config, store);
        IWorkerSupervisor supervisor = config.FakeWorkerDll is null ? new DisabledSupervisor() : new FakeWorkerSupervisor(config.FakeWorkerDll, location.RootPath);
        await using var scheduler = new OperationScheduler(store, supervisor, profile);
        byte[] secret = RandomNumberGenerator.GetBytes(32); string token = Convert.ToBase64String(secret); byte[] tokenHash = SHA256.HashData(Encoding.ASCII.GetBytes(token));
        string principal = DiscoveryFile.CurrentSid; Guid instance = Guid.NewGuid(); string discoveryPath = Path.Combine(location.RootPath, "api", "host.json");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory, EnvironmentName = Environments.Production });
        builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false; k.Listen(IPAddress.Loopback, 0, o => o.Protocols = HttpProtocols.Http1);
            k.Limits.MaxRequestBodySize = ContractJson.MaxRequestBytes; k.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            k.Limits.MaxConcurrentConnections = 64; k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5); k.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(15);
        });
        await using var app = builder.Build(); var gate = new RequestGate(config.RequestsPerSecond, config.SubmitsPerSecond, config.MaxInFlight);
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store"; context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY"; context.Response.Headers["X-TiaBridge-Contract-Version"] = "1.0";
            try
            {
                if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) || context.Request.Host.Value != "127.0.0.1:" + context.Connection.LocalPort
                    || context.Request.Headers.ContainsKey("Origin") || context.Request.Headers.Keys.Any(k => k.StartsWith("Sec-Fetch-", StringComparison.OrdinalIgnoreCase))
                    || context.Request.Headers.ContainsKey("Forwarded") || context.Request.Headers.ContainsKey("X-Forwarded-Host"))
                { await Error(ErrorCode.ACCESS_DENIED, 403).ExecuteAsync(context); return; }
                var authorization = context.Request.Headers.Authorization;
                if (authorization.Count != 1 || authorization[0] is not { } auth || auth.Length != 51 || !auth.StartsWith("Bearer ", StringComparison.Ordinal)
                    || !CryptographicOperations.FixedTimeEquals(tokenHash, SHA256.HashData(Encoding.ASCII.GetBytes(auth[7..]))))
                { await Error(ErrorCode.UNAUTHENTICATED, 401).ExecuteAsync(context); return; }
                using var admitted = gate.Enter(context.Request.Method == "POST");
                if (admitted is null) { context.Response.Headers.RetryAfter = "1"; await Error(ErrorCode.QUEUE_FULL, 429).ExecuteAsync(context); return; }
                await next(context);
            }
            catch (ContractValidationException e) { await Error(e.Code, Status(e.Code)).ExecuteAsync(context); }
            catch (TiaBridge.Ipc.ProtocolException) { await Error(ErrorCode.INVALID_ARGUMENT, 400).ExecuteAsync(context); }
            catch (StorageException e) { await Error(e.Code, Status(e.Code)).ExecuteAsync(context); }
            catch (BadHttpRequestException e) { await Error(e.StatusCode == 413 ? ErrorCode.LIMIT_EXCEEDED : ErrorCode.INVALID_ARGUMENT, e.StatusCode == 413 ? 413 : 400).ExecuteAsync(context); }
            catch (Exception e) when (e is JsonException or ArgumentException or FormatException or OverflowException)
            { await Error(ErrorCode.INVALID_ARGUMENT, 400).ExecuteAsync(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (OperationCanceledException) { await Error(ErrorCode.TIMEOUT, 400).ExecuteAsync(context); }
            catch (IOException) { if (!context.Response.HasStarted) await Error(ErrorCode.STORE_UNAVAILABLE, 503).ExecuteAsync(context); }
        });
        app.MapPost("/v1/operations", async (HttpContext ctx) =>
        {
            Query(ctx); byte[] bytes = await Body(ctx, ContractJson.MaxRequestBytes); var request = ContractJson.ReadRequest(bytes);
            if (SqliteOperationStore.IsMetadataMethod(request.Identity.Method))
            {
                var metadata = store.AcceptMetadata(principal, bytes, Health(store, scheduler, request.Parameters is DiagnosticsHealthParameters health ? health.ProjectKey : request.Identity.Project));
                return Bytes(ContractJson.WriteReport(metadata.Operation.Report));
            }
            var admission = store.AcceptSupported(principal, bytes, profile.Supports);
            if (admission.AlreadyAccepted) return Bytes(ContractJson.WriteReport(admission.Operation.Report));
            var r = admission.Operation.Report;
            var acceptance = new Acceptance(r.OperationId, OperationState.ACCEPTED, 1000, "/v1/operations/" + r.OperationId, "/v1/operations/" + r.OperationId + "/result", r.AcceptedAt!.Value);
            ctx.Response.Headers.Location = acceptance.StatusUrl; ctx.Response.Headers.RetryAfter = "1";
            return Bytes(ContractJson.WriteAcceptance(acceptance), 202);
        });
        app.MapGet("/v1/operations/{id}", (string id, HttpContext ctx) =>
        {
            Query(ctx); var metadata = store.Metadata(Id(id), principal);
            ctx.Response.Headers.LastModified = metadata.UpdatedAt.ToUniversalTime().ToString("R");
            return Bytes(StatusReport(metadata, scheduler.Observation));
        });
        app.MapGet("/v1/operations/{id}/result", (string id, HttpContext ctx) =>
        {
            Query(ctx); var operation = Id(id); var metadata = store.Metadata(operation, principal);
            using var doc = JsonDocument.Parse(metadata.ReportJson);
            if (!OperationLifecycle.IsTerminal(Enum.Parse<OperationState>(doc.RootElement.GetProperty("state").GetString()!)))
            { ctx.Response.Headers.RetryAfter = "1"; ctx.Response.Headers.Location = "/v1/operations/" + operation; return Bytes(StatusReport(metadata, scheduler.Observation), 202); }
            return Bytes(ContractJson.WriteReport(store.ReadResult(operation)!));
        });
        app.MapGet("/v1/operations/{id}/events", (string id, HttpContext ctx) =>
        {
            Query(ctx, "after_sequence", "limit"); var operation = Id(id);
            long after = Number(ctx, "after_sequence", 0); int limit = checked((int)Number(ctx, "limit", 100));
            return Bytes(ContractJson.WriteReport(QueryReport(OperationMethod.OperationEvents, store.JournalProjection(operation, principal, after, limit), operation)));
        });
        app.MapPost("/v1/operations/{id}/cancel", async (string id, HttpContext ctx) =>
        {
            Query(ctx); var operation = Id(id); byte[] bytes = await Body(ctx, 4096, optional: true);
            if (bytes.Length > 0)
            {
                TiaBridge.Ipc.Protocol.CheckJson(bytes); using var doc = JsonDocument.Parse(bytes);
                if (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.EnumerateObject().Any(p => p.Name != "reason")) throw new ArgumentException("Invalid cancel body.");
                if (doc.RootElement.TryGetProperty("reason", out var reason) && (reason.ValueKind != JsonValueKind.String || reason.GetString()!.EnumerateRunes().Count() > 512)) throw new ArgumentException("Invalid cancel reason.");
            }
            var result = store.CancelAuthorizedProjection(operation, principal);
            return Bytes(ContractJson.WriteReport(QueryReport(OperationMethod.OperationCancel, result, operation)));
        });
        app.MapGet("/v1/diagnostics/health", (HttpContext ctx) =>
        {
            Query(ctx, "project_key", "include_cached_components");
            ProjectKey? project = ctx.Request.Query.TryGetValue("project_key", out var key) ? new(UuidIdentity.ParseValue(key.ToString())) : null;
            if (ctx.Request.Query.TryGetValue("include_cached_components", out var cached) && cached.ToString() is not ("true" or "false")) throw new ArgumentException("Invalid query flag.");
            return Bytes(ContractJson.WriteReport(QueryReport(OperationMethod.DiagnosticsHealth, Health(store, scheduler, project))));
        });
        app.MapFallback(() => Error(ErrorCode.NOT_FOUND, 404));
        try
        {
            await app.StartAsync();
            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var host = Process.GetCurrentProcess();
            DiscoveryFile.Publish(discoveryPath, new("1.0", address.EndsWith('/') ? address : address + "/", token, principal, host.Id, host.StartTime.ToUniversalTime().Ticks, instance));
            scheduler.Start(); Console.WriteLine("{\"status\":\"READY\"}");
            if (stdioLifetime) _ = StopFromStdin(app.Lifetime);
            await app.WaitForShutdownAsync();
        }
        finally
        {
            await app.StopAsync(); DiscoveryFile.RemoveOwn(discoveryPath, instance); CryptographicOperations.ZeroMemory(secret); CryptographicOperations.ZeroMemory(tokenHash);
        }
    }
    private static async Task StopFromStdin(IHostApplicationLifetime lifetime)
    {
        var text = new char[5]; int count = 0;
        while (count < text.Length) { int read = await Console.In.ReadAsync(text.AsMemory(count, 1)); if (read == 0 || text[count] == '\n') break; count++; }
        if (new string(text, 0, count).TrimEnd('\r') == "stop") lifetime.StopApplication();
    }
    private static OperationId Id(string id) => new(UuidIdentity.ParseValue(id));
    private static void Query(HttpContext ctx, params string[] allowed)
    { if (ctx.Request.Query.Any(p => !allowed.Contains(p.Key, StringComparer.Ordinal) || p.Value.Count != 1)) throw new ArgumentException("Invalid query."); }
    private static long Number(HttpContext ctx, string name, long fallback) => ctx.Request.Query.TryGetValue(name, out var value) ? long.Parse(value.ToString(), System.Globalization.CultureInfo.InvariantCulture) : fallback;
    private static async Task<byte[]> Body(HttpContext ctx, int limit, bool optional = false)
    {
        if (ctx.Request.ContentLength > limit) throw new BadHttpRequestException("Body limit.", 413);
        if (ctx.Request.ContentLength != 0 && (ctx.Request.ContentType is null || !System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(ctx.Request.ContentType, out var type) || type.MediaType != "application/json" || type.CharSet is not null && !type.CharSet.Equals("utf-8", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("JSON UTF-8 required.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var output = new MemoryStream(); byte[] buffer = new byte[16384];
        while (true) { int read = await ctx.Request.Body.ReadAsync(buffer, timeout.Token); if (read == 0) break; if (output.Length + read > limit) throw new BadHttpRequestException("Body limit.", 413); output.Write(buffer, 0, read); }
        if (!optional && output.Length == 0) throw new ArgumentException("JSON request required.");
        return output.ToArray();
    }
    private sealed class DisabledSupervisor : IWorkerSupervisor
    { public Task<IWorkerConnection> StartAsync(CancellationToken ct) => throw new InvalidOperationException("No qualified worker configured."); }
}
