using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Hmi;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Core.Workspace;
using TiaBridge.Host.Api;
using TiaBridge.Host.Scheduling;
using TiaBridge.Host.Supervision;
using TiaBridge.Host.Operations;
using TiaBridge.Core.Identity;
using TiaBridge.Ipc;
using TiaBridge.Worker.V19;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class HmiScriptExportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Signed_offline_evidence_cannot_qualify_real_Hmi_export(bool engineering)
    {
        using var files = new StoreFixture(); using var local = new LocalTrustedFixture(files);
        var binding = Grant(local.Config.RegisteredProjects.Single());
        var evidence = new QualificationEvidence("synthetic-hmi-kind", 1,
            engineering ? QualificationEvidenceKind.ENGINEERING_TEST : QualificationEvidenceKind.OFFLINE_TEST,
            "synthetic test only", "synthetic signer", Tia, "tia-bridge.worker.v19", Worker, Worker,
            OperationMethod.ProjectExport, OperationScope.PROJECT, null, "hmi-script-module", V19HmiExportPolicy.Profile,
            V19InspectionProfile.HmiExportReadScope, [new(CoverageDimension.CONTENT, CoverageState.COMPLETE)],
            QualificationResult.PASS, "synthetic-run", binding.ProjectKey, V19InspectionProfile.HmiExportScopeClass(binding),
            [new("sample.txt", HmiScriptSource.Hash(Encoding.UTF8.GetBytes("synthetic qualification sample")))], DateTimeOffset.UtcNow);
        local.WritePackage(evidence);
        if (!engineering)
        {
            Assert.False(local.Tool.Inspect(evidence.EvidenceId, 1).Eligible);
            Assert.Throws<IOException>(() => local.Tool.Approve(evidence.EvidenceId, 1, local.Tool.Inspect(evidence.EvidenceId, 1).PackageSha256, true));
            local.Enroll();
            Assert.Equal("ENGINEERING_EVIDENCE_REQUIRED", new QualificationEvidenceProvider(local.Config).AssessEngineering(local.Requirement).Reason);
            return;
        }
        local.Tool.Approve(evidence.EvidenceId, 1, local.Tool.Inspect(evidence.EvidenceId, 1).PackageSha256, true);
        local.Tool.Enroll(evidence.EvidenceId, 1, local.Signer);
        var requirement = new QualificationRequirement(evidence.EvidenceId, Tia, evidence.AdapterId, Worker, Worker,
            evidence.Operation, evidence.Scope, null, evidence.ObjectKind, evidence.Profile, evidence.ReadScope,
            [CoverageDimension.CONTENT], binding.ProjectKey, evidence.ProjectClass);
        var provider = new QualificationEvidenceProvider(local.Config);
        Assert.True(provider.Assess(requirement).Available);
        Assert.Equal(engineering, provider.AssessEngineering(requirement).Available);
        if (!engineering) Assert.Equal("ENGINEERING_EVIDENCE_REQUIRED", provider.AssessEngineering(requirement).Reason);
    }

    private const string Tia = "19.0.synthetic";
    private static readonly string Worker = "1.0+" + new string('a', 64);
    private static V19HmiExportBinding Grant(string project) => new(Guid.NewGuid().ToString("D"), project, "synthetic.1",
        ["synthetic-device", "synthetic-item"], "T05RecipeCatalog", Tia, Worker,
        DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5), ["one"]);

    [Fact]
    public void Export_requires_new_exact_binding_and_separate_E1_export_qualification_and_revalidation()
    {
        using var files = new StoreFixture(); var store = files.Open();
        byte[] wire = StoreFixture.Wire("project.export"); var request = ContractJson.ReadRequest(wire);
        store.RegisterProject(request.Identity.Project!); var operation = store.Accept("synthetic", wire).Operation;
        var grant = Grant(request.Identity.Project!.ToString());
        var target = new V19Target(grant.ProjectKey, 123, 456, "X:\\synthetic.ap19");
        var config = new HostConfiguration("1.0", files.Root, files.Repository, [target.ProjectKey],
            V19WorkerExe: "synthetic-never-started", V19: new([target], "X:\\", HmiExport: grant));
        const string evidence = "synthetic-hmi-export";
        var key = new CapabilityKeyV2(OperationScope.PROJECT, Tia, "tia-bridge.worker.v19", Worker, null,
            "hmi-script-module", V19HmiExportPolicy.Profile, null, OperationMethod.ProjectExport);
        var registry = new CapabilityRegistry(new CapabilityConfigurationV2("2.0", "synthetic", [new(key, true, true,
            "1.0", V19HmiExportPolicy.Profile, V19InspectionProfile.HmiExportReadScope, [],
            new Coverage([new(CoverageDimension.CONTENT, CoverageState.COMPLETE)]), [evidence])]));
        bool qualified = true; string build = Worker;
        V19InspectionProfile Profile(HostConfiguration? c = null, CapabilityRegistry? r = null) => new(c ?? config, r ?? registry,
            requirement => qualified && requirement.Operation == OperationMethod.ProjectExport && requirement.Profile == V19HmiExportPolicy.Profile
                ? new(new HashSet<string>([evidence]), "synthetic-evidence-snapshot", "QUALIFIED") : QualificationEvidenceDecision.Denied("DENIED"),
            () => Tia, () => build);
        var profile = Profile(); var spec = profile.Resolve(operation);
        Assert.NotNull(spec); Assert.Equal(grant, spec.HmiExport); Assert.False(spec.QualifiedEffectFreeRead);
        Assert.True(profile.Revalidate(operation, spec));
        qualified = false; Assert.Null(profile.Resolve(operation)); Assert.False(profile.Revalidate(operation, spec)); qualified = true;
        build = "1.0+" + new string('b', 64); Assert.Null(profile.Resolve(operation)); build = Worker;
        Assert.Null(Profile(config with { V19 = config.V19! with { HmiExport = null } }).Resolve(operation));
        Assert.Null(Profile(config with { V19 = config.V19! with { HmiExport = grant with { ScopeToken = "another" } } }).Resolve(operation));
        Assert.Null(Profile(config with { V19 = config.V19! with { HmiExport = grant with { ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1) } } }).Resolve(operation));
        Assert.Null(Profile(r: CapabilityRegistry.DenyAllV2).Resolve(operation));
        // ProjectInspect qualification is insufficient: a matching export evidence ID/scope is mandatory.
        Assert.Null(new V19InspectionProfile(config).Resolve(operation));
        Assert.False(V19HmiExportPolicy.Valid(grant with { AuthorizationId = Guid.Empty.ToString("D") }, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Typed_module_export_runs_inside_existing_owner_lifecycle_and_preserves_unreported_sidecar()
    {
        using var files = new StoreFixture();
        using var own = Process.GetCurrentProcess();
        var grant = Grant(Guid.NewGuid().ToString("D"));
        var scenario = new SyntheticVendorScenario();
        var software = new Siemens.Engineering.HmiUnified.HmiSoftware(); scenario.Software = software;
        var audit = new List<string>(); int exportThread = 0;
        byte[] source = Encoding.UTF8.GetBytes("const π = '😀';\r\nexport function one() {\r\n return π;\r\n}\r\n");
        software.Scripts.Add(new("T05RecipeCatalog", directory =>
        {
            exportThread = Environment.CurrentManagedThreadId;
            File.WriteAllBytes(Path.Combine(directory.FullName, "T05RecipeCatalog.hmi.js"), source);
            File.WriteAllBytes(Path.Combine(directory.FullName, "T05RecipeCatalog.hmi.yml"), [0, 1, 2]);
            File.WriteAllBytes(Path.Combine(directory.FullName, "unknown.vendor"), [255, 0]);
            return [new(Path.Combine(directory.FullName, "T05RecipeCatalog.hmi.js"))];
        }));
        SyntheticVendorScenario.Current.Value = scenario;
        var target = new V19Target(grant.ProjectKey, own.Id, own.StartTime.ToUniversalTime().Ticks, scenario.Path);
        var id = Guid.NewGuid(); V19HmiNativeFile[]? native = null;
        try
        {
            var observation = VendorReader.Inspect(target, audit.Add, (_, _) => { }, null,
                project => native = HmiScriptExporter.ExportAttached(project, grant, files.Root, id, audit.Add));
            Assert.False(observation.IsModifiedBefore); Assert.False(observation.IsModifiedAfter);
            Assert.True(observation.PortalAliveAfterDispose); Assert.Equal(1, scenario.Disposes); Assert.Equal(0, scenario.ActiveEnumerators);
            Assert.Equal(observation.OwnerThreadId, exportThread); Assert.Equal(3, native!.Length);
            Assert.Contains(native, f => f.Name == "unknown.vendor");
            Assert.Equal(source, File.ReadAllBytes(Path.Combine(files.Root, V19HmiExportPolicy.DirectoryName(id), "native", "T05RecipeCatalog.hmi.js")));
            Assert.True(audit.IndexOf("TiaPortalProcess.Attach") < audit.IndexOf("HmiScriptModule.Export.Enter"));
            Assert.True(audit.IndexOf("HmiScriptModule.Export.Returned") < audit.IndexOf("TiaPortal.DisposeAttachedSession"));
        }
        finally { SyntheticVendorScenario.Current.Value = null; }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("throw")]
    [InlineData("dirty")]
    public void Missing_ambiguous_failed_or_dirty_exports_never_retry_and_release_only_own_attached_session(string failure)
    {
        using var files = new StoreFixture(); using var own = Process.GetCurrentProcess();
        var scenario = new SyntheticVendorScenario { Dirty = failure == "dirty" };
        var software = new Siemens.Engineering.HmiUnified.HmiSoftware(); scenario.Software = software;
        int calls = 0;
        IEnumerable<FileInfo> Export(DirectoryInfo directory)
        {
            calls++; File.WriteAllBytes(Path.Combine(directory.FullName, "partial.hmi.js"), [1, 2]);
            throw new IOException("synthetic export fault");
        }
        if (failure != "missing") software.Scripts.Add(new("T05RecipeCatalog", Export));
        if (failure == "duplicate") software.Scripts.Add(new("T05RecipeCatalog", Export));
        var grant = Grant(Guid.NewGuid().ToString("D")); var id = Guid.NewGuid();
        var target = new V19Target(grant.ProjectKey, own.Id, own.StartTime.ToUniversalTime().Ticks, scenario.Path);
        SyntheticVendorScenario.Current.Value = scenario;
        try
        {
            if (failure == "dirty")
            {
                var result = VendorReader.Inspect(target, _ => { }, (_, _) => { }, null,
                    p => HmiScriptExporter.ExportAttached(p, grant, files.Root, id, _ => { }));
                Assert.True(result.IsModifiedBefore);
            }
            else Assert.Throws<VendorReadFailure>(() => VendorReader.Inspect(target, _ => { }, (_, _) => { }, null,
                p => HmiScriptExporter.ExportAttached(p, grant, files.Root, id, _ => { })));
            Assert.Equal(failure == "throw" ? 1 : 0, calls); Assert.Equal(1, scenario.Disposes);
            if (failure == "throw") Assert.True(File.Exists(Path.Combine(files.Root, V19HmiExportPolicy.DirectoryName(id), "native", "partial.hmi.js")));
        }
        finally { SyntheticVendorScenario.Current.Value = null; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Host_projects_verified_native_artifacts_and_preserves_them_when_splitting_is_blocked(bool incomplete)
    {
        using var files = new StoreFixture(); var store = files.Open();
        byte[] wire = StoreFixture.Wire("project.export"); var request = ContractJson.ReadRequest(wire);
        store.RegisterProject(request.Identity.Project!); var previous = store.Accept("synthetic", wire).Operation.Report;
        var grant = Grant(request.Identity.Project!.ToString()); var target = new V19Target(grant.ProjectKey, 123, 456, "X:\\synthetic.ap19");
        string directory = Path.Combine(files.Root, V19HmiExportPolicy.DirectoryName(previous.OperationId.Value), "native"); Directory.CreateDirectory(directory);
        NativeScriptArtifact[] native = [new("T05RecipeCatalog.hmi.js", Encoding.UTF8.GetBytes(incomplete ? "export function one() {" : "const x = 1;\nexport function one() { return x; }\n")), new("T05RecipeCatalog.hmi.yml", [255, 0])];
        foreach (var item in native) File.WriteAllBytes(Path.Combine(directory, item.Name), item.Content);
        var project = new V19Observation(target.ProjectKey, Guid.NewGuid(), target.Pid, target.StartTicks, target.ProjectPath,
            "synthetic", false, false, Tia, [], [], 0, 0, 0, 0, 1, "STA", [], true);
        var exported = new V19HmiExportObservation(project, grant.AuthorizationId, grant.ScopeToken, grant.ModuleName, "UNAVAILABLE",
            native.Select(n => new V19HmiNativeFile(n.Name, HmiScriptSource.Hash(n.Content), n.Content.Length)).ToArray());
        var report = HmiScriptExportProjection.Create(store, previous, target, grant, JsonSerializer.Serialize(exported, Protocol.Json));
        Assert.Equal(incomplete ? OperationState.FAILED : OperationState.SUCCEEDED, report.State);
        Assert.True(report.ArtifactRefs.Length >= 4); Assert.NotNull(ContractJson.WriteReport(report));
        Assert.Equal(incomplete ? 0 : 2, Directory.GetFiles(Path.Combine(Path.GetDirectoryName(directory)!, "package"), "*.js", SearchOption.AllDirectories).Count(p => !p.Contains("native")));
        Assert.Equal(native[0].Content, File.ReadAllBytes(Path.Combine(directory, native[0].Name)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Durable_scheduler_completes_export_or_split_failure_and_duplicate_key_never_reexports(bool incomplete)
    {
        using var files = new StoreFixture(); var store = files.Open();
        byte[] wire = StoreFixture.Wire("project.export"); var request = ContractJson.ReadRequest(wire);
        store.RegisterProject(request.Identity.Project!); var admitted = store.Accept("synthetic", wire);
        var grant = Grant(request.Identity.Project!.ToString()); var target = new V19Target(grant.ProjectKey, 123, 456, "X:\\synthetic.ap19");
        var profile = new FakeProfile();
        profile.Specs[admitted.Operation.Report.OperationId] = new("synthetic-portal", V19HmiExportPolicy.Profile, false, null,
            V19Target: target)
        { HmiExport = grant };
        var connection = new SyntheticExportConnection(store, target, grant, incomplete);
        await using (var scheduler = new OperationScheduler(store, connection, profile))
        {
            for (int i = 0; i < 5 && store.ReadResult(admitted.Operation.Report.OperationId) is null; i++) await scheduler.PumpOnceAsync();
            var result = store.ReadResult(admitted.Operation.Report.OperationId)!;
            Assert.NotNull(result);
            Assert.Equal(incomplete ? OperationState.FAILED : OperationState.SUCCEEDED, result.State);
            Assert.Equal(incomplete ? ErrorCode.COVERAGE_GAP : (ErrorCode?)null, result.Error?.Code);
            Assert.True(store.Accept("synthetic", wire).AlreadyAccepted);
            await scheduler.PumpOnceAsync(); Assert.Equal(1, connection.Dispatches);
            foreach (var artifact in result.ArtifactRefs)
            {
                using var verified = store.Artifacts.OpenVerified(artifact.ArtifactId);
                Assert.Equal(artifact.Sha256, verified.Descriptor.Sha256);
            }
        }
        Assert.Equal(incomplete ? OperationState.FAILED : OperationState.SUCCEEDED,
            files.Open().ReadResult(admitted.Operation.Report.OperationId)!.State);
    }

    private sealed class SyntheticExportConnection(SqliteOperationStore store, V19Target target, V19HmiExportBinding grant, bool incomplete)
        : IWorkerSupervisor, IV19WorkerConnection
    {
        private readonly Queue<WireMessage> messages = new();
        public Guid Epoch { get; } = Guid.NewGuid();
        public int Pid => Environment.ProcessId;
        public long StartTicks { get; } = DateTime.UtcNow.Ticks;
        public string WorkerId => "tia-bridge.v19.read-only";
        public string Build => Worker;
        public bool HasExited { get; private set; }
        public bool Connected => !HasExited;
        public DateTimeOffset LastHeartbeat => DateTimeOffset.UtcNow;
        public DateTimeOffset? LastProgress => DateTimeOffset.UtcNow;
        public int Dispatches { get; private set; }
        public Core.Discovery.DiscoveryResourceSample SampleResources() => new(1, 1, 1);
        public Task<IWorkerConnection> StartAsync(CancellationToken ct) => Task.FromResult<IWorkerConnection>(this);
        public bool TryRead(out WireMessage message) => messages.TryDequeue(out message!);
        public Task SendAsync(MessageKind kind, Guid? operationId, long fence, JsonElement body, CancellationToken ct)
        {
            if (kind != MessageKind.Dispatch) return Task.CompletedTask;
            Dispatches++;
            var command = Protocol.Read<InspectDispatch>(body); var id = new OperationId(operationId!.Value);
            Assert.Equal(OperationState.RUNNING, store.Read(id).Report.State); // durable intent before execution
            using var request = JsonDocument.Parse(Protocol.ReadArtifact(store.ArtifactRoot, command.Request, 1024 * 1024));
            Assert.Equal("project.export", request.RootElement.GetProperty("method").GetString());
            string directory = Path.Combine(store.ArtifactRoot, V19HmiExportPolicy.DirectoryName(id.Value), "native"); Directory.CreateDirectory(directory);
            NativeScriptArtifact[] native = [new("T05RecipeCatalog.hmi.js", Encoding.UTF8.GetBytes(incomplete ? "export function one() {" : "export function one() { return 1; }\n")), new("T05RecipeCatalog.hmi.yml", [0, 255])];
            foreach (var item in native) File.WriteAllBytes(Path.Combine(directory, item.Name), item.Content);
            var project = new V19Observation(target.ProjectKey, Guid.NewGuid(), target.Pid, target.StartTicks, target.ProjectPath,
                "synthetic", false, false, Tia, [], [], 0, 0, 0, 0, 1, "STA", [], true);
            var exported = new V19HmiExportObservation(project, grant.AuthorizationId, grant.ScopeToken, grant.ModuleName, "UNAVAILABLE",
                native.Select(n => new V19HmiNativeFile(n.Name, HmiScriptSource.Hash(n.Content), n.Content.Length)).ToArray());
            messages.Enqueue(new("1.1", Epoch, 1, MessageKind.Receipt, operationId, fence, Protocol.Body(new WorkerReply(1,
                command.InputHash, null, new("Succeeded", JsonSerializer.Serialize(exported, Protocol.Json), null, 0, true)))));
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() { HasExited = true; return ValueTask.CompletedTask; }
    }
}
