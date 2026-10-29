using System.Net;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace TiaBridge.Host.Tests;

[SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public sealed class HmiEventDiagnosticDispatchTests
{
    [Fact]
    public async Task Normal_host_rejects_diagnostic_methods_and_inspect_parameter_injection_before_admission()
    {
        await using var fixture = new ApiFixture(); await fixture.Start(fakeEnabled: false);
        using var client = fixture.Client();
        foreach (string method in new[] { "hmi.event.diagnostic.read", "HmiEventDiagnosticReader.Read", "project.inspect" })
        {
            var request = JsonNode.Parse(ApiFixture.Request())!; request["method"] = method;
            if (method == "project.inspect") request["params"]!["hmi_event_diagnostic"] = true;
            var response = await client.SendAsync(HttpMethod.Post, "/v1/operations", JsonSerializer.SerializeToUtf8Bytes(request));
            Assert.Contains(response.StatusCode, new[] { 400, 422 });
            Assert.Equal(0, fixture.Files.Count("operations"));
            Assert.Equal(0, fixture.Files.Count("outbox"));
        }
        using var raw = fixture.Raw();
        Assert.Equal(HttpStatusCode.NotFound, (await raw.PostAsync("/v1/hmi/event-diagnostic", null)).StatusCode);
    }

    [Fact]
    public void Compiled_reader_vendor_calls_are_only_allowlisted_reads_and_production_has_no_caller()
    {
        // Normal offline tests inspect the linked source, requiring no vendor installation.
        InspectBinary(typeof(HmiEventDiagnosticDispatchTests).Assembly.Location, production: false);
        // Opt-in *metadata-only* inspection of the separately built net48 binary.
        // This test setting is never consumed by production Worker/Host.
        string? binary = Environment.GetEnvironmentVariable("TIA_BRIDGE_HMI_READER_BINARY");
        if (binary is not null) InspectBinary(binary, production: true);
    }

    private static void InspectBinary(string binary, bool production)
    {
        // Never load or execute the real Worker or Siemens DLLs.
        using var stream = File.OpenRead(binary); using var pe = new PEReader(stream); var metadata = pe.GetMetadataReader();
        var opcodes = typeof(OpCodes).GetFields().Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
        string RootType(TypeDefinitionHandle handle)
        {
            var type = metadata.GetTypeDefinition(handle);
            return type.GetDeclaringType().IsNil ? metadata.GetString(type.Name) : RootType(type.GetDeclaringType());
        }
        (string Type, string Name) Method(EntityHandle handle)
        {
            if (handle.Kind == HandleKind.MethodSpecification) return Method(metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
            if (handle.Kind == HandleKind.MethodDefinition)
            {
                var definition = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                var declaringType = metadata.GetTypeDefinition(definition.GetDeclaringType());
                string ns = metadata.GetString(declaringType.Namespace);
                string typeName = RootType(definition.GetDeclaringType());
                return (ns.StartsWith("Siemens.", StringComparison.Ordinal) ? ns + "." + typeName : typeName,
                    metadata.GetString(definition.Name));
            }
            if (handle.Kind != HandleKind.MemberReference) return ("", "");
            var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
            if (member.Parent.Kind != HandleKind.TypeReference) return ("", metadata.GetString(member.Name));
            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            return (metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name), metadata.GetString(member.Name));
        }
        int vendorReads = 0, readerMethods = 0;
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle); if (method.RelativeVirtualAddress == 0) continue;
            string caller = RootType(method.GetDeclaringType()); bool reader = caller == "HmiEventDiagnosticReader";
            if (reader) readerMethods++;
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            for (int offset = 0; offset < il.Length;)
            {
                ushort value = il[offset++]; if (value == 0xfe) value = (ushort)(0xfe00 | il[offset++]);
                var opcode = opcodes[value];
                if (opcode.OperandType == OperandType.InlineMethod)
                {
                    var called = Method(MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset)));
                    if (called.Type == "HmiEventDiagnosticReader" && (production || caller == "VendorReader"))
                        Assert.True(reader, "Reader must have no production caller outside its own implementation.");
                    if (reader && called.Type.StartsWith("Siemens.", StringComparison.Ordinal))
                    {
                        Assert.True(called.Name.StartsWith("get_", StringComparison.Ordinal)
                            || called.Name is "GetEnumerator" or "GetValidEnumValues", "Unexpected vendor call: " + called);
                        vendorReads++;
                    }
                }
                offset += opcode.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                    _ => 4
                };
            }
        }
        Assert.True(readerMethods > 0); Assert.True(vendorReads > 0);
        var readerType = metadata.TypeDefinitions.Single(h => metadata.GetString(metadata.GetTypeDefinition(h).Name) == "HmiEventDiagnosticReader");
        Assert.Equal(System.Reflection.TypeAttributes.NotPublic, metadata.GetTypeDefinition(readerType).Attributes & System.Reflection.TypeAttributes.VisibilityMask);
    }
}
