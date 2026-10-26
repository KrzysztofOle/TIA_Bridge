using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using TiaBridge.Host.Api;
using TiaBridge.Host.Storage;
using TiaBridge.Host.Operations;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

public class V19OfflineTests
{
    [Theory]
    [InlineData("all_supported", true, false)]
    [InlineData("all_supported", null, false)]
    [InlineData("all_supported", false, false)]
    [InlineData("plc", true, false)]
    [InlineData("hmi", true, false)]
    public void Missing_project_capability_and_unqualified_inventory_parameters_are_denied(string scope, bool? fresh, bool supported)
    {
        using var files = new StoreFixture(); var store = files.Open();
        var json = System.Text.Json.Nodes.JsonNode.Parse(StoreFixture.Wire("project.inspect"))!;
        json["params"]!["inventory_scope"] = scope;
        if (fresh is null) json["params"]!.AsObject().Remove("fresh"); else json["params"]!["fresh"] = fresh;
        byte[] wire = System.Text.Encoding.UTF8.GetBytes(json.ToJsonString());
        var request = TiaBridge.Core.Transport.V1.ContractJson.ReadRequest(wire); store.RegisterProject(request.Identity.Project!);
        var operation = store.Accept("synthetic-principal", wire).Operation;
        var target = new V19Target(request.Identity.Project!.ToString(), 123, 456, "X:\\synthetic.ap19");
        var config = new HostConfiguration("1.0", files.Root, files.Repository, [target.ProjectKey], V19WorkerExe: "synthetic-not-executed", V19: new([target], "X:\\"));
        Assert.Equal(supported, new TiaBridge.Host.Scheduling.V19InspectionProfile(config).Resolve(operation) is not null);
    }
    [Fact]
    public void Project_discovery_dispatch_requires_exact_project_scope_profile_builds_getters_and_verified_qualification()
    {
        using var files = new StoreFixture(); var store = files.Open();
        byte[] wire = StoreFixture.Wire("project.inspect");
        var request = TiaBridge.Core.Transport.V1.ContractJson.ReadRequest(wire);
        store.RegisterProject(request.Identity.Project!); var operation = store.Accept("synthetic-principal", wire).Operation;
        var target = new V19Target(request.Identity.Project!.ToString(), 123, 456, "X:\\synthetic.ap19");
        var config = new HostConfiguration("1.0", files.Root, files.Repository, [target.ProjectKey], V19WorkerExe: "synthetic-not-executed", V19: new([target], "X:\\"));
        string tiaBuild = "19.0.3.0", workerBuild = "1.0+" + new string('a', 64), qualification = "synthetic-discovery-receipt";
        var key = new TiaBridge.Core.Capabilities.CapabilityKeyV2(TiaBridge.Core.Capabilities.OperationScope.PROJECT, tiaBuild,
            "tia-bridge.worker.v19", workerBuild, null, "project", V19DiscoveryProfile.Id, null, TiaBridge.Core.Operations.OperationMethod.ProjectInspect);
        var entry = new TiaBridge.Core.Capabilities.CapabilityEntryV2(key, true, true, "1.0", V19DiscoveryProfile.Id,
            TiaBridge.Host.Scheduling.V19InspectionProfile.DiscoveryReadScope, [], new TiaBridge.Core.Workspace.Coverage([new(TiaBridge.Core.Workspace.CoverageDimension.STRUCTURE, TiaBridge.Core.Workspace.CoverageState.COMPLETE)]), [qualification]);
        var registry = new TiaBridge.Core.Capabilities.CapabilityRegistry(new TiaBridge.Core.Capabilities.CapabilityConfigurationV2("2.0", "synthetic-v2", [entry]));
        TiaBridge.Host.Scheduling.V19InspectionProfile Profile(string? tia = null, string? worker = null, bool withEvidence = true, TiaBridge.Core.Capabilities.CapabilityRegistry? selected = null) =>
            new(config, selected ?? registry, withEvidence ? new HashSet<string>([qualification], StringComparer.Ordinal) : null,
                () => tia ?? tiaBuild, () => worker ?? workerBuild);

        Assert.NotNull(Profile().Resolve(operation));
        Assert.Null(Profile(tia: "unknown").Resolve(operation));
        Assert.Null(Profile(worker: "1.0+unknown").Resolve(operation));
        Assert.Null(Profile(withEvidence: false).Resolve(operation));
        var incomplete = new TiaBridge.Core.Capabilities.CapabilityRegistry(new TiaBridge.Core.Capabilities.CapabilityConfigurationV2("2.0", "missing-getter", [entry with { ReadScope = entry.ReadScope.Take(entry.ReadScope.Count - 1).ToArray() }]));
        Assert.Null(Profile(selected: incomplete).Resolve(operation));
        var v1 = new TiaBridge.Core.Capabilities.CapabilityRegistry(new TiaBridge.Core.Capabilities.CapabilityConfiguration("1.0", "synthetic-v1", []));
        Assert.Null(Profile(selected: v1).Resolve(operation));
    }
    [Theory]
    [InlineData("PLC", "Siemens.Engineering.SW.PlcSoftware", "hardware")]
    [InlineData("HMI", "Siemens.Engineering.HmiUnified.HmiSoftware", "hardware")]
    [InlineData("UNKNOWN", "Siemens.Engineering.SW.PlcSoftware", "Siemens.Engineering.HmiUnified.HmiSoftware")]
    [InlineData("UNKNOWN", "untrusted.Hmi.PlcSoftware", "hardware")]
    public void Mixed_device_family_is_unknown_and_independent_of_traversal_order(string expected, string first, string second)
    {
        Assert.Equal(expected, TiaBridge.Worker.V19.InventoryFamily.Classify([first, second]));
        Assert.Equal(expected, TiaBridge.Worker.V19.InventoryFamily.Classify([second, first]));
    }
    [Theory]
    [InlineData("PLC", "Siemens.Engineering.SW.PlcSoftware", "hardware")]
    [InlineData("HMI_UNIFIED", "Siemens.Engineering.HmiUnified.HmiSoftware", "hardware")]
    [InlineData("HMI_CLASSIC", "Siemens.Engineering.Hmi.HmiSoftware", "hardware")]
    [InlineData("UNKNOWN", "Siemens.Engineering.Hmi.HmiSoftware", "Siemens.Engineering.HmiUnified.HmiSoftware")]
    [InlineData("UNKNOWN", "untrusted.Hmi.HmiSoftware", "hardware")]
    public void Detailed_hmi_classification_is_exact_and_order_independent(string expected, string first, string second)
    {
        Assert.Equal(expected, TiaBridge.Worker.V19.InventoryFamily.ClassifyDetailed([first, second]));
        Assert.Equal(expected, TiaBridge.Worker.V19.InventoryFamily.ClassifyDetailed([second, first]));
    }
    [Fact]
    public void Discovery_locator_round_trips_unicode_and_parent_context_without_changing_ipc_11_shape()
    {
        var locator = new V19NodeLocator(17, 4, 2, "Próba Δ", "Siemens.Engineering.HW.DeviceItem", "UNKNOWN", "UNKNOWN", "UNAVAILABLE");
        string encoded = V19NodeLocatorCodec.Encode(locator);
        Assert.True(V19NodeLocatorCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(locator, decoded);
        Assert.Equal(["family", "name"], JsonDocument.Parse(JsonSerializer.Serialize(new V19Device("PLC", "PLC"), Protocol.Json)).RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(["locator", "software_type"], JsonDocument.Parse(JsonSerializer.Serialize(new V19Node(encoded, "unknown"), Protocol.Json)).RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
    }
    [Fact]
    public void Real_profile_preserves_the_exact_project_file_path_without_a_directory_suffix()
    {
        using var files = new StoreFixture(); _ = files.Location;
        string worker = Path.Combine(files.Root, "TiaBridge.Worker.V19.exe"); File.WriteAllText(worker, "synthetic-not-executable");
        string project = Path.Combine(files.Root, "synthetic.ap19"); File.WriteAllText(project, "synthetic-not-a-TIA-project");
        var target = new V19Target("11111111-1111-4111-8111-111111111111", 123, 456, project);
        var config = new HostConfiguration("1.0", files.Root, files.Repository, [target.ProjectKey], V19WorkerExe: worker, V19: new([target], files.Root));
        string path = Path.Combine(files.Root, "v19-config.json"); File.WriteAllText(path, JsonSerializer.Serialize(config, HostConfiguration.Json));
        var parsed = HostConfiguration.Read(path); Assert.Equal(project, parsed.V19!.Targets.Single().ProjectPath);
    }
    [Theory]
    [InlineData("wrong-project")]
    [InlineData("duplicate-pid")]
    [InlineData("unregistered")]
    [InlineData("synthetic-mutation")]
    [InlineData("apartment")]
    public void Unconfirmed_native_bindings_are_denied_before_any_worker_start(string change)
    {
        using var files = new StoreFixture(); _ = files.Location;
        string worker = Path.Combine(files.Root, "TiaBridge.Worker.V19.exe"); File.WriteAllText(worker, "synthetic");
        string project = Path.Combine(files.Root, "synthetic.ap19"); File.WriteAllText(project, "synthetic");
        var target = new V19Target("11111111-1111-4111-8111-111111111111", 123, 456, project);
        var other = target with { ProjectKey = Guid.NewGuid().ToString() };
        var config = new HostConfiguration("1.0", files.Root, files.Repository, [target.ProjectKey], V19WorkerExe: worker, V19: new([target], files.Root));
        config = change switch
        {
            "wrong-project" => config with { V19 = config.V19! with { ProjectRoot = Path.Combine(files.Root, "other") } },
            "duplicate-pid" => config with { RegisteredProjects = [target.ProjectKey, other.ProjectKey], V19 = config.V19! with { Targets = [target, other] } },
            "unregistered" => config with { RegisteredProjects = [Guid.NewGuid().ToString()] },
            "synthetic-mutation" => config with { EnableSyntheticMutations = true },
            "apartment" => config with { V19 = config.V19! with { Apartment = "UNKNOWN" } },
            _ => throw new ArgumentException()
        };
        string path = Path.Combine(files.Root, "v19-config.json"); File.WriteAllText(path, JsonSerializer.Serialize(config, HostConfiguration.Json));
        Assert.ThrowsAny<ArgumentException>(() => HostConfiguration.Read(path));
    }
    [Theory]
    [InlineData("1.0", true)]
    [InlineData("1.1", false)]
    public void Protocol_version_binds_worker_heartbeat_to_the_correct_executor_kind(string version, bool synthetic)
    {
        var message = new WireMessage(version, Guid.NewGuid(), 1, MessageKind.Heartbeat, null, 0, Protocol.Body(new HeartbeatBody(synthetic)));
        Protocol.Validate(message);
        Assert.Throws<ProtocolException>(() => Protocol.Validate(message with { Body = Protocol.Body(new HeartbeatBody(!synthetic)) }));
    }
    [Fact]
    public void Real_dispatch_has_no_synthetic_template_or_mutation_replay()
    {
        var pointer = new ArtifactPointer("artifact-" + new string('a', 32), new string('b', 64));
        var body = new InspectDispatch(pointer, 1, new string('c', 64), null);
        var message = new WireMessage("1.1", Guid.NewGuid(), 1, MessageKind.Dispatch, Guid.NewGuid(), 1, Protocol.Body(body));
        Protocol.Validate(message);
        Assert.Throws<ProtocolException>(() => Protocol.Validate(message with { Body = Protocol.Body(body with { Attempt = 2 }) }));
        var fake = new Dispatch(pointer, pointer, FakeScenario.Success, 0, 1, new string('c', 64), null, "TIA_UNAVAILABLE");
        Assert.Throws<ProtocolException>(() => Protocol.Validate(message with { Body = Protocol.Body(fake) }));
        Assert.Throws<ProtocolException>(() => Protocol.Validate(message with { ProtocolVersion = "1.0" }));
    }
    [Fact]
    public async Task Cross_version_connection_cannot_silently_downgrade_the_readonly_protocol()
    {
        using var stream = new MemoryStream(); byte[] secret = new byte[32]; Guid epoch = Guid.NewGuid();
        await using var writer = new FramedConnection(stream, secret, epoch, "1.1");
        await writer.SendAsync(MessageKind.Heartbeat, null, 0, Protocol.Body(new HeartbeatBody(false))); stream.Position = 0;
        await using var reader = new FramedConnection(stream, secret, epoch);
        await Assert.ThrowsAsync<ProtocolException>(() => reader.ReadAsync());
    }
    [Theory]
    [InlineData("null-device")]
    [InlineData("wrong-process")]
    [InlineData("no-dispose-proof")]
    [InlineData("negative-time")]
    [InlineData("modified-drift")]
    public void Invalid_native_observation_is_rejected_before_artifact_or_success_commit(string fault)
    {
        using var files = new StoreFixture(); var store = files.Open(); var previous = files.Accept(store, "project.inspect").Operation.Report;
        var target = new V19Target("11111111-1111-4111-8111-111111111111", 123, 456, "X:\\synthetic.ap19");
        var observation = new V19Observation(target.ProjectKey, Guid.NewGuid(), 123, 456, target.ProjectPath, "synthetic", false, false, "synthetic.19", [], [], 1, 1, 1, 1, 1, "STA", [], true);
        observation = fault switch
        {
            "null-device" => observation with { Devices = [null!] },
            "wrong-process" => observation with { TiaPid = 999 },
            "no-dispose-proof" => observation with { PortalAliveAfterDispose = false },
            "negative-time" => observation with { DisposeMs = -1 },
            "modified-drift" => observation with { IsModifiedBefore = false, IsModifiedAfter = true },
            _ => throw new ArgumentException()
        };
        Assert.Throws<ProtocolException>(() => Project(store, previous, target, observation));
        Assert.NotEqual(TiaBridge.Core.Operations.OperationState.SUCCEEDED, store.Read(previous.OperationId).Report.State);
    }
    [Theory]
    [InlineData("missing-parent")]
    [InlineData("cross-device-parent")]
    [InlineData("duplicate-index")]
    [InlineData("read-empty-masked")]
    [InlineData("cycle")]
    [InlineData("depth-limit")]
    [InlineData("node-limit")]
    [InlineData("device-limit")]
    public void Invalid_discovery_relations_or_unknowns_are_rejected_without_false_success(string fault)
    {
        using var files = new StoreFixture(); var store = files.Open(); var previous = files.Accept(store, "project.inspect").Operation.Report;
        var target = new V19Target("11111111-1111-4111-8111-111111111111", 123, 456, "X:\\synthetic.ap19");
        var observation = DiscoveryObservation(target);
        observation = fault switch
        {
            "missing-parent" => observation with { Nodes = [observation.Nodes[0], Node(1, 0, 500, "Réacteur", null), observation.Nodes[2]] },
            "cross-device-parent" => observation with { Nodes = [observation.Nodes[0], Node(1, 0, 2, "Réacteur", null), observation.Nodes[2]] },
            "duplicate-index" => observation with { Nodes = [observation.Nodes[0], Node(0, 0, 0, "Réacteur", null), observation.Nodes[2]] },
            "read-empty-masked" => observation with { Nodes = [observation.Nodes[0], Node(1, 0, 0, "Réacteur", "unknown", "READ"), observation.Nodes[2]] },
            "cycle" => observation with { Nodes = [Node(0, 0, 1, "PLC repeated", null), Node(1, 0, 0, "Réacteur", null), observation.Nodes[2]] },
            "depth-limit" => observation with { Nodes = Enumerable.Range(0, V19DiscoveryProfile.MaxDepth + 3).Select(i => Node(i, 0, i == 0 ? null : i - 1, "n" + i, null)).ToArray() },
            "node-limit" => observation with { Nodes = Enumerable.Range(0, V19DiscoveryProfile.MaxNodes + 1).Select(i => Node(i, 0, null, "n" + i, null)).ToArray() },
            "device-limit" => observation with { Devices = Enumerable.Range(0, V19DiscoveryProfile.MaxDevices + 1).Select(i => new V19Device("d" + i, "UNKNOWN")).ToArray() },
            _ => throw new ArgumentException()
        };
        Assert.Throws<ProtocolException>(() => Project(store, previous, target, observation));
        Assert.NotEqual(TiaBridge.Core.Operations.OperationState.SUCCEEDED, store.Read(previous.OperationId).Report.State);
    }
    [Fact]
    public void Discovery_artifact_preserves_unicode_names_hierarchy_families_and_partial_identity_coverage()
    {
        using var files = new StoreFixture(); var store = files.Open(); var running = StoreFixture.Start(store, files.Accept(store, "project.inspect").Operation, out _);
        var target = new V19Target("11111111-1111-4111-8111-111111111111", 123, 456, "X:\\synthetic.ap19");
        var observation = DiscoveryObservation(target);
        var projection = Project(store, running.Report, target, observation);
        store.Record(running.Report.OperationId, running.Revision, projection);
        var final = files.Open().ReadResult(running.Report.OperationId)!;
        var result = (TiaBridge.Core.Transport.V1.ProjectInspectResult)final.Result!;
        Assert.Empty(result.Inventory); // No stable vendor IDs: never fabricate ObjectIds.
        using var artifact = store.Artifacts.OpenVerified(result.CoverageRefs.Single());
        using var json = JsonDocument.Parse(artifact.Content);
        var root = json.RootElement;
        Assert.Equal("v19.discovery.v1", root.GetProperty("profile_id").GetString());
        Assert.Equal("1.0+" + new string('a', 64), root.GetProperty("worker_build").GetString());
        Assert.Equal("Réacteur", root.GetProperty("nodes")[1].GetProperty("original_name").GetString());
        Assert.Equal(0, root.GetProperty("nodes")[1].GetProperty("parent_observation_index").GetInt32());
        Assert.Equal("UNAVAILABLE", root.GetProperty("nodes")[1].GetProperty("identity_state").GetString());
        Assert.Equal("UNKNOWN", root.GetProperty("nodes")[1].GetProperty("software_state").GetString());
        Assert.Equal("HMI_UNIFIED", root.GetProperty("devices")[1].GetProperty("family").GetString());
        Assert.Contains(root.GetProperty("coverage").GetProperty("dimensions").EnumerateArray(), x => x.GetProperty("state").GetString() == "PARTIAL" && x.GetProperty("dimension").GetString() == "INVENTORY");
    }
    [Theory]
    [InlineData("worker-build")]
    [InlineData("negative-handles")]
    [InlineData("negative-private-bytes")]
    public void Discovery_rejects_unbound_worker_build_and_invalid_resource_samples(string fault)
    {
        using var files = new StoreFixture(); var store = files.Open(); var previous = files.Accept(store, "project.inspect").Operation.Report;
        var target = new V19Target("11111111-1111-4111-8111-111111111111", 123, 456, "X:\\synthetic.ap19");
        var observation = DiscoveryObservation(target);
        string build = fault == "worker-build" ? "1.0+unknown" : "1.0+" + new string('a', 64);
        TiaBridge.Core.Discovery.DiscoveryResourceSample before = fault == "negative-handles" ? new(-1, 1024, 2048) : new(10, 1024, 2048);
        TiaBridge.Core.Discovery.DiscoveryResourceSample after = fault == "negative-private-bytes" ? new(10, -1, 2048) : new(12, 1536, 2048);
        Assert.Throws<ProtocolException>(() =>
        {
            _ = TiaBridge.Host.Scheduling.V19InspectionProjection.Create(store, previous, target,
                JsonSerializer.Serialize(observation, Protocol.Json), build, Binding(), before, after);
        });
        Assert.NotEqual(OperationState.SUCCEEDED, store.Read(previous.OperationId).Report.State);
    }
    [Fact]
    public void Native_result_and_all_coverage_references_are_durable_verified_artifacts()
    {
        using var files = new StoreFixture(); var store = files.Open();
        var running = StoreFixture.Start(store, files.Accept(store, "project.inspect").Operation, out _);
        var target = new V19Target("11111111-1111-4111-8111-111111111111", 123, 456, "X:\\synthetic.ap19");
        var observation = DiscoveryObservation(target);
        var projection = Project(store, running.Report, target, observation);
        store.Record(running.Report.OperationId, running.Revision, projection);
        var final = files.Open().ReadResult(running.Report.OperationId)!;
        Assert.Equal(TiaBridge.Core.Operations.OperationState.SUCCEEDED, final.State);
        var result = (TiaBridge.Core.Transport.V1.ProjectInspectResult)final.Result!;
        Assert.Equal(2, final.ArtifactRefs.Length);
        foreach (string reference in result.CoverageRefs)
        { using var proof = store.Artifacts.OpenVerified(reference); Assert.True(proof.Content.Length > 0); }
    }
    [Fact]
    public async Task Net48_x64_child_uses_the_same_authenticated_framing_without_any_vendor_library()
    {
        using var files = new StoreFixture();
        string binary = Path.Combine(files.Repository, "tests", "integration", "TiaBridge.Ipc.FrameworkProbe", "bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net48", "TiaBridge.Ipc.FrameworkProbe.exe");
        string name = "tia-bridge2-" + Guid.NewGuid().ToString("N"); Guid epoch = Guid.NewGuid(), nonce = Guid.NewGuid(); byte[] secret = RandomNumberGenerator.GetBytes(32);
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
        using var host = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(binary) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var child = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new Bootstrap(name, Convert.ToBase64String(secret), epoch, nonce, host.Id, host.StartTime.ToUniversalTime().Ticks, files.Root), Protocol.Json)); child.StandardInput.Close();
            await server.WaitForConnectionAsync(timeout.Token); PipePeer.Verify(server, child.Id, true);
            await using var framed = new FramedConnection(server, secret, epoch, "1.1");
            var hello = Protocol.Read<Hello>((await framed.ReadAsync(timeout.Token)).Body);
            Assert.Equal(child.Id, hello.Pid); Assert.Equal(nonce, hello.Nonce); Assert.Equal("framework-probe", hello.WorkerId);
            await framed.SendAsync(MessageKind.Ready, null, 0, Protocol.Body(new ReadyBody(true)), timeout.Token);
            var dispatch = new InspectDispatch(new("artifact-" + new string('a', 32), new string('b', 64)), 1, new string('c', 64), null);
            Guid id = Guid.NewGuid(); await framed.SendAsync(MessageKind.Dispatch, id, 1, Protocol.Body(dispatch), timeout.Token);
            Assert.Equal(MessageKind.Heartbeat, (await framed.ReadAsync(timeout.Token)).Kind);
            var receipt = await framed.ReadAsync(timeout.Token); Assert.Equal(id, receipt.OperationId);
            Assert.Equal("UNSUPPORTED", Protocol.Read<WorkerReply>(receipt.Body).Receipt.ErrorCode);
            await framed.SendAsync(MessageKind.Stop, null, 0, Protocol.Body(new StopBody(false)), timeout.Token);
            await child.WaitForExitAsync(timeout.Token); Assert.Equal(0, child.ExitCode);
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static V19Observation DiscoveryObservation(V19Target target)
    {
        var devices = new[] { new V19Device("PLC repeated", "PLC"), new V19Device("PLC repeated", "HMI") };
        var nodes = new[] { Node(0, 0, null, "PLC repeated", "Siemens.Engineering.SW.PlcSoftware"), Node(1, 0, 0, "Réacteur", null), Node(2, 1, null, "PLC repeated", "Siemens.Engineering.HmiUnified.HmiSoftware") };
        return new V19Observation(target.ProjectKey, Guid.NewGuid(), target.Pid, target.StartTicks, target.ProjectPath,
            "synthetic", false, false, "synthetic.19", devices, nodes, 1, 2, 3, 4, 7, "STA", [], true);
    }

    private static V19Node Node(int index, int device, int? parent, string name, string? type, string? state = null) =>
        new(V19NodeLocatorCodec.Encode(new(index, parent, device, name, "Synthetic.DeviceItem", state ?? (type is null ? "UNKNOWN" : "READ"), "UNKNOWN", "UNAVAILABLE")), type ?? "unknown");
    private static TiaBridge.Core.Capabilities.CapabilityBinding Binding() =>
        new(TiaBridge.Core.Capabilities.OperationScope.PROJECT, "synthetic-v2", new string('a', 64), V19DiscoveryProfile.Id);
    private static OperationReport Project(SqliteOperationStore store, OperationReport previous, V19Target target, V19Observation observation) =>
        TiaBridge.Host.Scheduling.V19InspectionProjection.Create(store, previous, target, JsonSerializer.Serialize(observation, Protocol.Json),
            "1.0+" + new string('a', 64), Binding(), new(10, 1024, 2048), new(12, 1536, 2048));
}
