using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.HmiUnified.UI.Enum;
using Siemens.Engineering.HmiUnified.UI.Events;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using TiaBridge.Worker.V19;
using Xunit;

namespace TiaBridge.Host.Tests;

public sealed class HmiEventDiagnosticReaderTests
{
    private sealed class Tree
    {
        internal HmiSoftware Software { get; } = new();
        internal HmiScreen Screen { get; } = new();
        internal HmiButton Action0 { get; } = new("ACTION_0");
        internal HmiButton Action1 { get; } = new("ACTION_1");
        internal HmiButton Nav { get; } = new("NAV_1");
        internal HmiCustomWidgetContainer Table { get; } = new("RecipeTable_Main");
        internal HmiTestScript Code { get; } = new();
        internal Tree()
        {
            Software.ScreenValues.Values = [Screen];
            Screen.Items.Values = [Action0, Action1, Table, Nav];
            Screen.Handlers.Values = [new(HmiScreenEventType.Loaded), new(HmiScreenEventType.Unloaded)];
            Action0.Handlers.Values = [new(HmiButtonEventType.Tapped) { Body = Code }];
            Action1.Handlers.Values = [new(HmiButtonEventType.Tapped)];
            Nav.Handlers.Values = [new(HmiButtonEventType.Up)];
            Table.Handlers.Values = [new("RowSelected")];
        }
        internal HmiEventDiagnosticResult Read() => HmiEventDiagnosticReader.Read(Software, ["synthetic-device", "synthetic-software"], Environment.CurrentManagedThreadId);
    }
    private static HmiEventDiagnosticEntry Action(HmiEventDiagnosticResult result) => Assert.Single(result.Entries, e => e.Projection.ObjectName == "ACTION_0");

    [Fact]
    public void Enumerates_exact_six_pairs_in_screen_group_and_preserves_values_locators_and_async()
    {
        var tree = new Tree();
        var group = new HmiScreenGroup(); group.ScreenValues.Values = [tree.Screen];
        tree.Software.ScreenValues.Values = []; tree.Software.GroupValues.Values = [group];
        tree.Code.Code = "// Zażółć Δ 🚀\r\nreturn '\u0000';\n\r\n";
        tree.Code.Global = "import * as Δ from 'synthetic';\n// 終\r\n";
        var result = tree.Read(); var action = Action(result);
        Assert.Equal("COMPLETE_FOR_SCOPE", result.Coverage);
        Assert.Equal(["Loaded", "Unloaded", "Tapped", "Tapped", "RowSelected", "Up"], result.Entries.Select(e => e.Projection.EventName));
        Assert.Equal(tree.Code.Code, action.Projection.ScriptCode);
        Assert.Equal(tree.Code.Global, action.Projection.GlobalDefinitionAreaScriptCode);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tree.Code.Code))), action.Projection.ScriptCodeSha256);
        Assert.Equal(["synthetic-device", "synthetic-software", "ScreenGroups", "0", "synthetic-group", "Screens", "0", "T05_Recipes_Main", "ScreenItems", "0", "ACTION_0"], action.Projection.Locator);
        Assert.Equal(typeof(HmiButton).FullName, action.Projection.ObjectType);
        Assert.Equal("Tapped", action.Projection.VendorEventType);
        Assert.True(action.Async); Assert.Equal("READ", action.AsyncStatus);
        Assert.All(result.Inventory, i => Assert.Null(i.Error));
        Assert.Equal(0, group.ScreenValues.ActiveEnumerators);
        Assert.Equal(0, tree.Action0.Handlers.ActiveEnumerators);
    }

    [Fact]
    public void Empty_handler_is_distinct_from_absent_and_missing_object_is_never_absent_handler()
    {
        var tree = new Tree(); tree.Code.Code = ""; tree.Code.Global = "";
        Assert.Equal("PRESENT_EMPTY", Action(tree.Read()).Projection.HandlerStatus);
        tree.Action0.Handlers.Values = [];
        var absent = Action(tree.Read()).Projection;
        Assert.Equal("ABSENT", absent.HandlerStatus);
        Assert.Null(absent.VendorEventType); // No getter observation is invented for a missing handler.
        tree.Screen.Items.Values = [tree.Nav];
        var missing = Action(tree.Read());
        Assert.Equal("OBJECT_ABSENT", missing.InventoryStatus); Assert.Equal("UNKNOWN", missing.Projection.HandlerStatus);
    }

    [Theory]
    [InlineData("ScriptCode")]
    [InlineData("GlobalDefinitionAreaScriptCode")]
    [InlineData("Async")]
    public void Getter_failures_are_independent_and_never_expose_vendor_message(string getter)
    {
        var tree = new Tree(); tree.Code.Fail.Add(getter);
        var result = tree.Read(); var action = Action(result);
        Assert.Equal("PARTIAL", result.Coverage);
        Assert.Equal(getter == "ScriptCode" ? "READ_FAILED" : "READ", action.Projection.CodeStatus);
        Assert.Equal(getter == "GlobalDefinitionAreaScriptCode" ? "READ_FAILED" : "READ", action.Projection.GlobalDefinitionAreaStatus);
        Assert.Equal(getter == "Async" ? "READ_FAILED" : "READ", action.AsyncStatus);
        Assert.NotEqual("ABSENT", action.Projection.HandlerStatus);
        Assert.All(result.Inventory, i => Assert.DoesNotContain("synthetic secret", i.Error ?? ""));
    }

    [Theory]
    [InlineData("EventType")]
    [InlineData("Script")]
    public void Handler_getter_failure_does_not_prove_absence(string getter)
    {
        var tree = new Tree(); tree.Action0.Handlers.Values.Single().Fail.Add(getter);
        var result = tree.Read();
        Assert.Equal("PARTIAL", result.Coverage); Assert.NotEqual("ABSENT", Action(result).Projection.HandlerStatus);
        Assert.Contains(result.Inventory, i => i.Status == "READ_FAILED");
    }

    [Theory]
    [InlineData("code")]
    [InlineData("global")]
    [InlineData("script")]
    [InlineData("event")]
    public void Null_values_never_become_empty_or_absent(string field)
    {
        var tree = new Tree();
        if (field == "code") tree.Code.Code = null!;
        if (field == "global") tree.Code.Global = null!;
        if (field == "script") tree.Action0.Handlers.Values.Single().Body = null!;
        if (field == "event") tree.Table.Handlers.Values = [new(null!)];
        var result = tree.Read();
        var entry = field == "event" ? result.Entries.Single(e => e.Projection.ObjectName == "RecipeTable_Main") : Action(result);
        Assert.Equal("PARTIAL", result.Coverage);
        Assert.NotEqual("ABSENT", entry.Projection.HandlerStatus); Assert.NotEqual("PRESENT_EMPTY", entry.Projection.HandlerStatus);
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("enumerator")]
    [InlineData("element")]
    public void Null_collection_enumerator_or_element_never_proves_absence(string dimension)
    {
        var tree = new Tree();
        if (dimension == "collection") tree.Action0.Null.Add("EventHandlers");
        if (dimension == "enumerator") tree.Action0.Handlers.Null.Add("GetEnumerator");
        if (dimension == "element") tree.Action0.Handlers.Values = [null!];
        var result = tree.Read();
        Assert.Equal("PARTIAL", result.Coverage); Assert.Equal("UNKNOWN", Action(result).Projection.HandlerStatus);
        Assert.Contains(result.Inventory, i => i.Error?.StartsWith("NULL_", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Missing_screen_keeps_all_six_requested_pairs_unknown()
    {
        var tree = new Tree(); tree.Software.ScreenValues.Values = [];
        var result = tree.Read(); Assert.Equal(6, result.Entries.Length);
        Assert.All(result.Entries, entry =>
        {
            Assert.Equal("OBJECT_ABSENT", entry.InventoryStatus); Assert.Equal("UNKNOWN", entry.Projection.HandlerStatus);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Invalid_object_name_is_visible_and_blocks_absence(string? name)
    {
        var tree = new Tree(); tree.Action0.Handlers.Values = [];
        tree.Screen.Items.Values = [tree.Action0, new HmiScreenItemBase(name!)];
        var result = tree.Read(); Assert.Equal("PARTIAL", result.Coverage);
        Assert.Equal("UNKNOWN", Action(result).Projection.HandlerStatus);
        Assert.Contains(result.Inventory, i => i.Kind == "ITEM_NAME" && i.Error is "NULL_VALUE" or "INVALID_METADATA");
    }

    [Fact]
    public void Per_composition_limit_allows_exactly_the_limit_and_never_reads_unrequested_scripts()
    {
        var tree = new Tree(); var outside = new HmiButtonEventHandler(HmiButtonEventType.Up);
        outside.Fail.Add("Script");
        tree.Action0.Handlers.Values = Enumerable.Repeat(outside, HmiEventDiagnosticReader.MaxHandlers - 1)
            .Append(tree.Action0.Handlers.Values.Single());
        var result = tree.Read(); Assert.Equal("COMPLETE_FOR_SCOPE", result.Coverage);
        Assert.DoesNotContain("Script", outside.Calls);
        Assert.Equal(HmiEventDiagnosticReader.MaxHandlers + 1, tree.Action0.Handlers.Moves);
    }

    [Fact]
    public void Total_text_budget_is_bounded_across_the_six_handlers()
    {
        var tree = new Tree(); var shared = new HmiTestScript
        {
            Code = new string('x', HmiEventDiagnosticReader.MaxTextChars),
            Global = new string('y', HmiEventDiagnosticReader.MaxTextChars)
        };
        foreach (var handler in tree.Screen.Handlers.Values) handler.Body = shared;
        var result = tree.Read(); Assert.Equal("PARTIAL", result.Coverage);
        Assert.Contains(result.Inventory, i => i.Error == "TEXT_LIMIT");
        Assert.True(result.Entries.Sum(e => (e.Projection.ScriptCode?.Length ?? 0)
            + (e.Projection.GlobalDefinitionAreaScriptCode?.Length ?? 0)) <= HmiEventDiagnosticReader.MaxTotalTextChars);
    }

    [Theory]
    [InlineData("GetEnumerator")]
    [InlineData("MoveNext")]
    [InlineData("Current")]
    [InlineData("Dispose")]
    public void Every_enumerator_failure_blocks_absence_including_disposal(string phase)
    {
        var tree = new Tree(); tree.Action0.Handlers.Fail.Add(phase);
        var result = tree.Read();
        Assert.Equal("PARTIAL", result.Coverage); Assert.NotEqual("ABSENT", Action(result).Projection.HandlerStatus);
        Assert.Equal(0, tree.Action0.Handlers.ActiveEnumerators);
        Assert.Contains(result.Inventory, i => i.Kind == "EVENT_HANDLERS" && i.Status == "PARTIAL");
    }

    [Fact]
    public void Values_observed_before_late_enumeration_failure_survive_with_partial_coverage()
    {
        var tree = new Tree(); tree.Action0.Handlers.FailMoveAfter = 1;
        var action = Action(tree.Read());
        Assert.Equal(tree.Code.Code, action.Projection.ScriptCode);
        Assert.Equal("PRESENT", action.Projection.HandlerStatus); Assert.Equal("PARTIAL", action.Projection.Coverage);
    }

    [Theory]
    [InlineData("screens")]
    [InlineData("groups")]
    [InlineData("items")]
    [InlineData("item-name")]
    [InlineData("screen-name")]
    [InlineData("group-name")]
    public void Incomplete_ancestor_inventory_blocks_absence(string dimension)
    {
        var tree = new Tree(); tree.Action0.Handlers.Values = [];
        if (dimension == "screens") tree.Software.ScreenValues.FailMoveAfter = 1;
        if (dimension == "groups") tree.Software.GroupValues.Fail.Add("MoveNext");
        if (dimension == "items") tree.Screen.Items.FailMoveAfter = 1;
        if (dimension == "item-name") tree.Action1.Fail.Add("Name");
        if (dimension == "screen-name") tree.Screen.Fail.Add("Name");
        if (dimension == "group-name")
        {
            var group = new HmiScreenGroup(); group.Fail.Add("Name"); tree.Software.GroupValues.Values = [group];
        }
        var result = tree.Read(); Assert.Equal(6, result.Entries.Length);
        Assert.Equal("PARTIAL", result.Coverage); Assert.NotEqual("ABSENT", Action(result).Projection.HandlerStatus);
    }

    [Theory]
    [InlineData("Screens")]
    [InlineData("ScreenGroups")]
    [InlineData("ScreenItems")]
    [InlineData("EventHandlers")]
    public void Composition_getter_failure_is_visible(string getter)
    {
        var tree = new Tree();
        if (getter is "Screens" or "ScreenGroups") tree.Software.Fail.Add(getter);
        else if (getter == "ScreenItems") tree.Screen.Fail.Add(getter);
        else tree.Action0.Fail.Add(getter);
        var result = tree.Read();
        Assert.Equal("PARTIAL", result.Coverage);
        Assert.NotEqual("ABSENT", Action(result).Projection.HandlerStatus);
        Assert.Contains(result.Inventory, i => i.Error?.Contains("ENUMERATION_FAILED", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("enum")]
    [InlineData("custom-event")]
    public void Unsupported_types_and_events_are_explicit(string kind)
    {
        var tree = new Tree();
        if (kind == "object") tree.Screen.Items.Values = [new HmiScreenItemBase("ACTION_0")];
        if (kind == "enum") tree.Action0.Handlers.Values = [new((HmiButtonEventType)999)];
        if (kind == "custom-event") tree.Table.Handlers.ValidValues.Values = ["DifferentEvent"];
        var result = tree.Read();
        Assert.Equal("PARTIAL", result.Coverage);
        Assert.Contains(result.Entries, e => e.SupportStatus == "UNSUPPORTED"
            || result.Inventory.Any(i => i.Status == "UNSUPPORTED"));
        Assert.NotEqual("ABSENT", Action(result).Projection.HandlerStatus);
        if (kind == "enum") Assert.Contains(result.Inventory, i => i.ObservedValue == "999");
    }

    [Fact]
    public void Custom_web_control_uses_typed_events_and_dynamic_valid_values_without_name_inference()
    {
        var tree = new Tree(); var web = new HmiCustomWebControlContainer("RecipeTable_Main");
        web.Handlers.Values = [new("RowSelected")]; tree.Screen.Items.Values = [tree.Action0, tree.Action1, web, tree.Nav];
        Assert.Equal("COMPLETE_FOR_SCOPE", tree.Read().Coverage);
        web.Handlers.ValidValues.FailMoveAfter = 0; web.Handlers.Values = [];
        var entry = tree.Read().Entries.Single(e => e.Projection.ObjectName == "RecipeTable_Main");
        Assert.Equal("UNKNOWN", entry.Projection.HandlerStatus); Assert.Equal("PARTIAL", entry.InventoryStatus);
    }

    [Theory]
    [InlineData("screen")]
    [InlineData("item")]
    [InlineData("handler")]
    public void Duplicate_identities_are_ambiguous_not_first_match_wins(string dimension)
    {
        var tree = new Tree();
        if (dimension == "screen") tree.Software.ScreenValues.Values = [tree.Screen, new HmiScreen()];
        if (dimension == "item") tree.Screen.Items.Values = [tree.Action0, new HmiButton("ACTION_0")];
        if (dimension == "handler") tree.Action0.Handlers.Values = [new(HmiButtonEventType.Tapped), new(HmiButtonEventType.Tapped)];
        var entry = Action(tree.Read());
        Assert.StartsWith("AMBIGUOUS", entry.InventoryStatus); Assert.Equal("UNKNOWN", entry.Projection.HandlerStatus);
    }

    [Theory]
    [InlineData("items")]
    [InlineData("handlers")]
    [InlineData("valid-events")]
    [InlineData("global-nodes")]
    [InlineData("depth")]
    public void Infinite_or_deep_compositions_stop_at_fixed_limits(string dimension)
    {
        var tree = new Tree();
        if (dimension == "items") tree.Screen.Items.Values = Enumerable.Repeat<HmiScreenItemBase>(tree.Nav, int.MaxValue);
        if (dimension == "handlers") tree.Action0.Handlers.Values = Enumerable.Repeat(new HmiButtonEventHandler(HmiButtonEventType.Up), int.MaxValue);
        if (dimension == "valid-events") tree.Table.Handlers.ValidValues.Values = Enumerable.Repeat("OtherEvent", int.MaxValue);
        if (dimension == "global-nodes") tree.Software.GroupValues.Values = Enumerable.Range(0, 20).Select(_ =>
        {
            var group = new HmiScreenGroup(); group.ScreenValues.Values = Enumerable.Range(0, 200).Select(_ => new HmiScreen()); return group;
        });
        if (dimension == "depth")
        {
            var group = new HmiScreenGroup(); group.GroupValues.Values = [group]; tree.Software.GroupValues.Values = [group];
        }
        var result = tree.Read(); Assert.Equal("PARTIAL", result.Coverage);
        Assert.Contains(result.Inventory, i => i.Error is "ELEMENT_LIMIT" or "DEPTH_LIMIT");
        Assert.Equal(6, result.Entries.Length);
        Assert.Equal(0, tree.Screen.Items.ActiveEnumerators);
        Assert.True(tree.Screen.Items.Moves <= HmiEventDiagnosticReader.MaxElements + 1);
    }

    [Fact]
    public void Text_limit_preserves_failure_instead_of_truncating_into_plausible_code()
    {
        var tree = new Tree(); tree.Code.Code = new string('x', HmiEventDiagnosticReader.MaxTextChars + 1);
        var result = tree.Read(); var entry = Action(result);
        Assert.Null(entry.Projection.ScriptCode); Assert.Null(entry.Projection.ScriptCodeSha256);
        Assert.Equal("READ_FAILED", entry.Projection.CodeStatus); Assert.Equal("READ", entry.Projection.GlobalDefinitionAreaStatus);
        Assert.Equal("PARTIAL", result.Coverage); Assert.Equal("TEXT_LIMIT", entry.Projection.ScriptError);
    }

    [Fact]
    public void Wrong_owner_and_invalid_locator_are_rejected_before_any_vendor_getter()
    {
        var tree = new Tree();
        Assert.Throws<InvalidOperationException>(() => HmiEventDiagnosticReader.Read(tree.Software, ["verified"], -1));
        Assert.Throws<ArgumentException>(() => HmiEventDiagnosticReader.Read(tree.Software, [], Environment.CurrentManagedThreadId));
        Assert.Empty(tree.Software.Calls);
    }

    [Fact]
    public void Reader_is_internal_has_no_host_reference_and_only_uses_read_only_vendor_members()
    {
        var tree = new Tree(); Assert.Equal("COMPLETE_FOR_SCOPE", tree.Read().Coverage);
        Assert.All(new HmiTestNode[] { tree.Software, tree.Screen, tree.Action0, tree.Action1, tree.Nav, tree.Table, tree.Code }, node =>
            Assert.All(node.Calls, name => Assert.DoesNotContain(name, new[] { "Save", "Compile", "SetAttribute", "Delete", "Import", "Download" })));
        Assert.False(typeof(HmiEventDiagnosticReader).IsPublic);
        Assert.DoesNotContain(typeof(TiaBridge.Host.Scheduling.V19InspectionProfile).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Siemens.", StringComparison.Ordinal) || a.Name == "TiaBridge.Worker.V19");
    }
}
