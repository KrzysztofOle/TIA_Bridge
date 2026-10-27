using System.Text.Json;
using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Operations;
using TiaBridge.Host.Api;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Qualification;
using TiaBridge.Host.Storage;
using TiaBridge.Ipc;

namespace TiaBridge.Host.Scheduling;

/// <summary>Explicit qualification-only composition. Never falls back to E1 or claims capability qualification.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class BootstrapInspectionProfile : IExecutionProfile
{
    private readonly HostConfiguration config;
    private readonly SqliteOperationStore store;
    private readonly BootstrapLocal local;
    private readonly TimeProvider clock;
    private readonly Func<BootstrapAuthorization, bool> verifyBinding;
    private readonly string configHash;
    private OwnerToken? owner;
    internal BootstrapInspectionProfile(HostConfiguration config, SqliteOperationStore store, TimeProvider? clock = null,
        Func<BootstrapAuthorization, bool>? verifyBinding = null)
    {
        this.config = config; this.store = store; local = new(config, store); this.clock = clock ?? TimeProvider.System;
        this.verifyBinding = verifyBinding ?? (auth => BootstrapLocal.VerifyLiveBinding(config, auth));
        configHash = config.StartupConfigurationPath is { } p ? BootstrapAuthorization.Sha(File.ReadAllBytes(p)) : "";
    }
    public void BeginSession(OwnerToken owner)
    {
        this.owner = owner;
        if (config.QualificationBootstrap is { } p) { local.Check(); store.BindBootstrap(p.AuthorizationId, owner); }
    }
    public void EndSession(bool released)
    {
        if (config.QualificationBootstrap is not { } p || store.ReadBootstrap(p.AuthorizationId)?.HostEpoch != owner?.Epoch.ToString()) return;
        store.StopBootstrap(p.AuthorizationId, "HOST_SESSION_ENDED");
        store.BootstrapRelease(p.AuthorizationId, released);
    }
    public void WorkerLost()
    {
        if (config.QualificationBootstrap is { } p) store.StopBootstrap(p.AuthorizationId, "WORKER_LOST_NO_REPLACEMENT");
    }
    public ExecutionSpec? Resolve(StoredOperation operation)
    {
        var p = config.QualificationBootstrap;
        if (p is null || owner is null) return null;
        try
        {
            local.Check();
            if (config.StartupConfigurationPath is not { } path || BootstrapAuthorization.Sha(File.ReadAllBytes(path)) != configHash) throw new IOException("CONFIG_DRIFT");
            var state = store.ReadBootstrap(p.AuthorizationId) ?? throw new IOException("NO_LOCAL_AUTHORIZATION");
            var auth = state.Authorization; auth.Validate(config, clock.GetUtcNow());
            var permit = new BootstrapPermit(p.AuthorizationId, state.Sha256);
            using var doc = JsonDocument.Parse(operation.RequestJson);
            var request = doc.RootElement;
            var parameters = request.GetProperty("params");
            if (operation.Report.Method != OperationMethod.ProjectInspect || request.GetProperty("project_key").GetString() != auth.ProjectKey
                || parameters.GetProperty("inventory_scope").GetString() != "all_supported" || parameters.GetProperty("fresh").ValueKind != JsonValueKind.True
                || parameters.GetProperty("timeout_s").GetInt32() != 120 || parameters.GetProperty("queue_timeout_s").GetInt32() != 300
                || !store.BootstrapCanDispatch(permit, owner, operation.Report.OperationId) || !verifyBinding(auth)) throw new IOException("BINDING_SCOPE_OR_BUDGET_DENIED");
            return new ExecutionSpec("v19:" + auth.TiaPid + ":" + auth.TiaStartTicks, "qualification-only.v19.discovery.v1", false, null,
                V19Target: config.V19!.Targets.Single(), CapabilityBinding: new(OperationScope.PROJECT, "qualification-only", state.Sha256, auth.Profile), QualificationDecisionHash: state.Sha256)
            { Bootstrap = permit };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException or System.ComponentModel.Win32Exception)
        {
            string reason = e is IOException && e.Message.All(c => char.IsAsciiLetterUpper(c) || c == '_') ? e.Message : e.GetType().Name;
            store.StopBootstrap(p.AuthorizationId, "VALIDATION_DENIED:" + reason);
            return null;
        }
    }
}
