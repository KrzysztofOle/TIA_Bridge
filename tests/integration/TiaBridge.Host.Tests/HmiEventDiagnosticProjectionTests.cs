using System.Security.Cryptography;
using System.Text;
using TiaBridge.Worker.V19;
using Xunit;

namespace TiaBridge.Host.Tests;

public sealed class HmiEventDiagnosticProjectionTests
{
    private static HmiEventDiagnosticRecord Project(
        bool complete = true,
        bool present = true,
        string? code = "return 1;",
        string? global = "import * as RecipeCatalog from \"T05RecipeCatalog\";",
        string? error = null,
        string? objectName = "ACTION_0",
        string? objectType = "Siemens.Engineering.HmiUnified.UI.Widgets.HmiButton",
        string? eventName = "Tapped",
        string? vendorEventType = "Tapped",
        string[]? locator = null)
        => HmiEventDiagnosticProjection.Project(locator ?? ["HMI_JSON_TEST", "HMI_RT_1", "T05_Recipes_Main", "ACTION_0"],
            objectName, objectType, eventName, vendorEventType, complete, present, code, global, error);

    [Fact]
    public void Preserves_script_global_declarations_event_and_object_identity_and_hashes_exact_utf8_values()
    {
        const string code = "async function Δ() {\r\n  return '✓';\r\n}";
        const string global = "import * as RecipeCatalog from \"T05RecipeCatalog\";\n";
        var result = Project(code: code, global: global, objectName: "Przycisk Δ", objectType: "Vendor.Button", eventName: "Tapped", vendorEventType: "Tapped/19");

        Assert.Equal("Przycisk Δ", result.ObjectName);
        Assert.Equal("Vendor.Button", result.ObjectType);
        Assert.Equal("Tapped", result.EventName);
        Assert.Equal("Tapped/19", result.VendorEventType);
        Assert.Equal(["HMI_JSON_TEST", "HMI_RT_1", "T05_Recipes_Main", "ACTION_0"], result.Locator);
        Assert.Equal(code, result.ScriptCode);
        Assert.Equal(global, result.GlobalDefinitionAreaScriptCode);
        Assert.Equal(Hash(code), result.ScriptCodeSha256);
        Assert.Equal(Hash(global), result.GlobalDefinitionAreaSha256);
        Assert.Equal("READ", result.CodeStatus);
        Assert.Equal("READ", result.GlobalDefinitionAreaStatus);
    }

    [Fact]
    public void Reports_absent_only_when_event_enumeration_is_complete()
    {
        var complete = Project(complete: true, present: false, code: null, global: null);
        var partial = Project(complete: false, present: false, code: null, global: null);

        Assert.Equal("ABSENT", complete.HandlerStatus);
        Assert.Equal("NOT_APPLICABLE", complete.CodeStatus);
        Assert.Equal("UNKNOWN", complete.GlobalDefinitionAreaStatus);
        Assert.Equal("UNKNOWN", partial.HandlerStatus);
        Assert.Equal("UNKNOWN", partial.CodeStatus);
        Assert.Equal("PARTIAL", partial.Coverage);
    }

    [Fact]
    public void Keeps_observed_handler_values_while_marking_incomplete_parent_inventory_partial()
    {
        var result = Project(complete: false, present: true, code: "return 1;", global: "import X from 'X';");

        Assert.Equal("PRESENT", result.HandlerStatus);
        Assert.Equal("READ", result.CodeStatus);
        Assert.Equal("READ", result.GlobalDefinitionAreaStatus);
        Assert.Equal("PARTIAL", result.Coverage);
    }

    [Fact]
    public void Distinguishes_present_empty_code_from_null_and_preserves_empty_hash()
    {
        var empty = Project(code: string.Empty, global: string.Empty);
        var nullValue = Project(code: null, global: null);

        Assert.Equal("PRESENT_EMPTY", empty.HandlerStatus);
        Assert.Equal("EMPTY", empty.CodeStatus);
        Assert.Equal(Hash(string.Empty), empty.ScriptCodeSha256);
        Assert.Equal("EMPTY", empty.GlobalDefinitionAreaStatus);
        Assert.Equal(Hash(string.Empty), empty.GlobalDefinitionAreaSha256);
        Assert.Equal("PRESENT", nullValue.HandlerStatus);
        Assert.Equal("UNKNOWN", nullValue.CodeStatus);
        Assert.Null(nullValue.ScriptCodeSha256);
        Assert.Equal("UNKNOWN", nullValue.GlobalDefinitionAreaStatus);
    }

    [Fact]
    public void Getter_error_is_not_collapsed_into_empty_or_absent_and_is_bounded()
    {
        var result = Project(code: null, global: null, error: "Getter failed\r\n" + new string('x', 400));

        Assert.Equal("PRESENT", result.HandlerStatus);
        Assert.Equal("READ_FAILED", result.CodeStatus);
        Assert.Equal("UNKNOWN", result.GlobalDefinitionAreaStatus);
        Assert.Equal("PARTIAL", result.Coverage);
        Assert.NotNull(result.Error);
        Assert.DoesNotContain('\r', result.Error);
        Assert.DoesNotContain('\n', result.Error);
        Assert.True(result.Error.Length <= 256);
    }

    [Fact]
    public void Enumeration_error_cannot_be_overridden_by_a_claimed_complete_absence()
    {
        var result = Project(complete: true, present: false, code: null, global: null, error: "composition failed");

        Assert.Equal("UNKNOWN", result.HandlerStatus);
        Assert.Equal("READ_FAILED", result.CodeStatus);
        Assert.Equal("PARTIAL", result.Coverage);
    }

    [Fact]
    public void Incomplete_identity_never_reports_complete_coverage_or_absence()
    {
        var result = Project(complete: true, present: false, code: null, global: null, objectType: null, locator: []);

        Assert.Equal("UNKNOWN", result.MetadataStatus);
        Assert.Equal("UNKNOWN", result.HandlerStatus);
        Assert.Equal("PARTIAL", result.Coverage);
    }

    [Fact]
    public void Missing_vendor_event_type_keeps_handler_identity_unknown()
    {
        var result = Project(vendorEventType: null);

        Assert.Equal("UNKNOWN", result.MetadataStatus);
        Assert.Equal("UNKNOWN", result.HandlerStatus);
        Assert.Equal("UNKNOWN", result.CodeStatus);
        Assert.Equal("PARTIAL", result.Coverage);
    }

    [Fact]
    public void Known_allowed_kind_can_prove_absence_without_fabricating_an_observed_vendor_event_type()
    {
        var result = HmiEventDiagnosticProjection.Project(["synthetic", "ACTION_0"], "ACTION_0", "Vendor.Button",
            "Tapped", null, true, false, null, null, eventKindKnown: true);
        Assert.Equal("ABSENT", result.HandlerStatus); Assert.Null(result.VendorEventType);
        Assert.Equal("COMPLETE_FOR_EVENT_ENUMERATION", result.Coverage);
        var partial = HmiEventDiagnosticProjection.Project(["synthetic", "ACTION_0"], "ACTION_0", "Vendor.Button",
            "Tapped", null, false, false, null, null, eventKindKnown: true);
        Assert.Equal("UNKNOWN", partial.HandlerStatus);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Independent_getter_errors_preserve_only_successful_content_hashes(bool scriptFailed)
    {
        var result = HmiEventDiagnosticProjection.Project(["synthetic", "ACTION_0"], "ACTION_0", "Vendor.Button",
            "Tapped", "Tapped", true, true, "synthetic();", "// declarations",
            scriptError: scriptFailed ? "CODE_FAILED" : null, globalDefinitionAreaError: scriptFailed ? null : "GLOBAL_FAILED");
        Assert.Equal(scriptFailed ? "READ_FAILED" : "READ", result.CodeStatus);
        Assert.Equal(scriptFailed ? "READ" : "READ_FAILED", result.GlobalDefinitionAreaStatus);
        Assert.Equal(scriptFailed ? null : Hash("synthetic();"), result.ScriptCodeSha256);
        Assert.Equal(scriptFailed ? Hash("// declarations") : null, result.GlobalDefinitionAreaSha256);
        Assert.Equal("PRESENT", result.HandlerStatus); Assert.Equal("PARTIAL", result.Coverage);
    }

    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty).ToLowerInvariant();
    }
}
