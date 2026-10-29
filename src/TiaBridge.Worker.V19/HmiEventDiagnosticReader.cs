using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.HmiUnified.UI.Enum;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Widgets;

namespace TiaBridge.Worker.V19;

// Deliberately unreachable from Program/VendorReader dispatch. A future approved
// session integration must supply the already verified Unified software/locator
// on its existing owner, with identity and IsModified checks before AND after.
// This component neither attaches nor grants authorization nor owns any proxy.
internal static class HmiEventDiagnosticReader
{
    internal const int MaxElements = 256, MaxHandlers = 64, MaxDepth = 8, MaxNodes = 2048;
    internal const int MaxTextChars = 262144, MaxTotalTextChars = 1048576;
    private const string ScreenName = "T05_Recipes_Main";
    private static readonly (string Object, string Event)[] Scope =
    [
        (ScreenName, "Loaded"), (ScreenName, "Unloaded"), ("ACTION_0", "Tapped"),
        ("ACTION_1", "Tapped"), ("RecipeTable_Main", "RowSelected"), ("NAV_1", "Up")
    ];

    internal static HmiEventDiagnosticResult Read(HmiSoftware software, string[] verifiedDeviceLocator, int ownerThreadId)
    {
        if (Thread.CurrentThread.ManagedThreadId != ownerThreadId) throw new InvalidOperationException("OWNER_MISMATCH");
        if (software is null || verifiedDeviceLocator is null || verifiedDeviceLocator.Length is < 1 or > 16
            || verifiedDeviceLocator.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 512))
            throw new ArgumentException("Verified software and device locator required.");
        return new Traversal().Read(software, verifiedDeviceLocator.ToArray());
    }

    private sealed class Traversal
    {
        private int nodes, textChars;
        private readonly List<HmiEventDiagnosticInventory> inventory = [];
        private readonly List<(HmiScreen Screen, string[] Locator)> screens = [];

        internal HmiEventDiagnosticResult Read(HmiSoftware software, string[] root)
        {
            bool screensComplete = Screens(() => software.Screens, root);
            screensComplete &= Groups(() => software.ScreenGroups, root, 0);
            if (screens.Count != 1)
            {
                string reason = screens.Count > 1 ? "AMBIGUOUS_SCREEN" : screensComplete ? "OBJECT_ABSENT" : "PARTIAL";
                return new(Scope.Select(s => Unknown(s.Object == ScreenName ? Append(root, "Screens", ScreenName)
                    : Append(root, "Screens", ScreenName, "ScreenItems", s.Object), s.Object, null, s.Event, reason)).ToArray(), inventory.ToArray());
            }
            var selected = screens[0];
            var items = Snapshot(() => selected.Screen.ScreenItems, selected.Locator, "SCREEN_ITEMS", MaxElements);
            var named = new List<(HmiScreenItemBase Item, string Name, string[] Locator)>();
            bool itemsComplete = items.Complete;
            for (int i = 0; i < items.Values.Count; i++)
            {
                var item = items.Values[i];
                string[] locator = Append(selected.Locator, "ScreenItems", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                string? name = Metadata(() => item.Name, locator, "ITEM_NAME", out var error);
                if (error is not null) { itemsComplete = false; continue; }
                named.Add((item, name!, Append(locator, name!)));
            }
            var results = new List<HmiEventDiagnosticEntry>();
            results.AddRange(Events(() => selected.Screen.EventHandlers, h => h.EventType.ToString(), h => h.Script,
                Enum.GetNames(typeof(HmiScreenEventType)), null, selected.Locator, ScreenName,
                selected.Screen.GetType().FullName, ["Loaded", "Unloaded"], screensComplete));
            foreach (var target in Scope.Skip(2))
            {
                var matches = named.Where(n => n.Name == target.Object).ToArray();
                if (matches.Length != 1)
                {
                    string reason = matches.Length > 1 ? "AMBIGUOUS_OBJECT" : itemsComplete && screensComplete ? "OBJECT_ABSENT" : "PARTIAL";
                    results.Add(Unknown(Append(selected.Locator, target.Object), target.Object, null, target.Event, reason));
                    continue;
                }
                var match = matches[0];
                bool complete = screensComplete && itemsComplete;
                string? type = match.Item.GetType().FullName;
                // Explicit public V19 types only; no dynamic/reflection composition sweep.
                switch (match.Item)
                {
                    case HmiButton button:
                        results.AddRange(Events(() => button.EventHandlers, h => h.EventType.ToString(), h => h.Script,
                            Enum.GetNames(typeof(HmiButtonEventType)), null, match.Locator, match.Name, type, [target.Event], complete));
                        break;
                    case HmiCustomWidgetContainer widget:
                        results.AddRange(Events(() => widget.EventHandlers, h => h.EventType, h => h.Script, null,
                            () => widget.EventHandlers.GetValidEnumValues(), match.Locator, match.Name, type, [target.Event], complete));
                        break;
                    case HmiCustomWebControlContainer web:
                        results.AddRange(Events(() => web.EventHandlers, h => h.EventType, h => h.Script, null,
                            () => web.EventHandlers.GetValidEnumValues(), match.Locator, match.Name, type, [target.Event], complete));
                        break;
                    default:
                        results.Add(Unknown(match.Locator, match.Name, type, target.Event, "UNSUPPORTED_OBJECT"));
                        break;
                }
            }
            return new(results.ToArray(), inventory.ToArray());
        }

        private bool Screens(Func<IEnumerable<HmiScreen>> source, string[] parent)
        {
            var batch = Snapshot(source, parent, "SCREENS", MaxElements);
            bool complete = batch.Complete;
            for (int i = 0; i < batch.Values.Count; i++)
            {
                var screen = batch.Values[i];
                var locator = Append(parent, "Screens", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                string? name = Metadata(() => screen.Name, locator, "SCREEN_NAME", out var error);
                if (error is not null) { complete = false; continue; }
                if (name == ScreenName) screens.Add((screen, Append(locator, name)));
            }
            return complete;
        }

        private bool Groups(Func<IEnumerable<HmiScreenGroup>> source, string[] parent, int depth)
        {
            var batch = Snapshot(source, parent, "SCREEN_GROUPS", MaxElements);
            bool complete = batch.Complete;
            for (int i = 0; i < batch.Values.Count; i++)
            {
                var group = batch.Values[i];
                var locator = Append(parent, "ScreenGroups", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (depth >= MaxDepth)
                {
                    inventory.Add(new(locator, "GROUP_DEPTH", "PARTIAL", 0, "DEPTH_LIMIT")); complete = false; continue;
                }
                string? name = Metadata(() => group.Name, locator, "GROUP_NAME", out var error);
                if (error is not null) { complete = false; continue; }
                locator = Append(locator, name!);
                complete &= Screens(() => group.Screens, locator);
                complete &= Groups(() => group.Groups, locator, depth + 1);
            }
            return complete;
        }

        private IEnumerable<HmiEventDiagnosticEntry> Events<T>(Func<IEnumerable<T>> source, Func<T, string> eventType,
            Func<T, IHmiScript> script, string[]? enumValues, Func<IEnumerable<string>>? validValues,
            string[] locator, string name, string? type, string[] requested, bool ancestorsComplete) where T : class
        {
            var allowed = enumValues is null ? Snapshot(validValues!, locator, "VALID_EVENTS", MaxHandlers)
                : new Batch<string>(enumValues.ToList(), true);
            bool validComplete = allowed.Complete && allowed.Values.All(ValidName);
            if (!allowed.Values.All(ValidName)) inventory.Add(new(locator, "VALID_EVENTS", "PARTIAL", 0, "INVALID_METADATA"));
            bool complete = validComplete;
            var handlers = Snapshot(source, locator, "EVENT_HANDLERS", MaxHandlers);
            complete &= handlers.Complete;
            var observed = new List<(T Handler, string Event)>();
            foreach (var handler in handlers.Values)
            {
                string? value = Metadata(() => eventType(handler), locator, "EVENT_TYPE", out var error);
                if (error is not null) { complete = false; continue; }
                if (!allowed.Values.Contains(value!, StringComparer.Ordinal))
                {
                    inventory.Add(new(locator, "EVENT_TYPE", "UNSUPPORTED", 1, "UNSUPPORTED_EVENT", value)); complete = false; continue;
                }
                observed.Add((handler, value!));
            }
            foreach (string requestedEvent in requested)
            {
                if (!allowed.Values.Contains(requestedEvent, StringComparer.Ordinal))
                {
                    yield return Unknown(locator, name, type, requestedEvent, validComplete ? "UNSUPPORTED_EVENT" : "PARTIAL");
                    continue;
                }
                var matches = observed.Where(h => h.Event == requestedEvent).ToArray();
                bool enumerationComplete = complete && ancestorsComplete;
                if (matches.Length > 1)
                {
                    yield return Unknown(locator, name, type, requestedEvent, "AMBIGUOUS_HANDLER"); continue;
                }
                if (matches.Length == 0)
                {
                    yield return new(HmiEventDiagnosticProjection.Project(locator, name, type, requestedEvent, null,
                        enumerationComplete, false, null, null, eventKindKnown: true), enumerationComplete ? "COMPLETE" : "PARTIAL", "SUPPORTED", null, "NOT_APPLICABLE", null);
                    continue;
                }
                var body = Value(() => script(matches[0].Handler), locator, "SCRIPT", out var scriptObjectError);
                string? code = null, global = null, codeError = null, globalError = null, asyncError = null;
                bool? async = null;
                if (body is not null)
                {
                    code = Text(() => body.ScriptCode, locator, "SCRIPT_CODE", out codeError);
                    global = Text(() => body.GlobalDefinitionAreaScriptCode, locator, "GLOBAL_DEFINITION", out globalError);
                    async = Value<bool?>(() => body.Async, locator, "ASYNC", out asyncError);
                }
                var projection = HmiEventDiagnosticProjection.Project(locator, name, type, requestedEvent, matches[0].Event,
                    enumerationComplete, true, code, global, scriptObjectError, codeError, globalError);
                yield return new(projection, enumerationComplete ? "COMPLETE" : "PARTIAL", "SUPPORTED", async,
                    body is null ? "UNKNOWN" : asyncError is null ? "READ" : "READ_FAILED", asyncError);
            }
        }

        private string? Metadata(Func<string> getter, string[] locator, string kind, out string? error)
        {
            var value = Value(getter, locator, kind, out error);
            if (error is null && !ValidName(value))
            {
                error = "INVALID_METADATA"; inventory.Add(new(locator, kind, "READ_FAILED", 0, error)); return null;
            }
            return value;
        }

        private string? Text(Func<string> getter, string[] locator, string kind, out string? error)
        {
            var value = Value(getter, locator, kind, out error);
            if (value is null) return null;
            if (value.Length > MaxTextChars || value.Length > MaxTotalTextChars - textChars)
            {
                error = "TEXT_LIMIT"; inventory.Add(new(locator, kind, "PARTIAL", 0, error)); return null;
            }
            textChars += value.Length; return value;
        }

        private T? Value<T>(Func<T> getter, string[] locator, string kind, out string? error)
        {
            try
            {
                T value = getter(); error = value is null ? "NULL_VALUE" : null;
                if (error is not null) inventory.Add(new(locator, kind, "PARTIAL", 0, error));
                return value;
            }
            catch (Exception e)
            {
                // Deliberately omit vendor message/stack, which can contain script or secrets.
                error = "GETTER_FAILED:" + e.GetType().Name;
                inventory.Add(new(locator, kind, "READ_FAILED", 0, error)); return default;
            }
        }

        private Batch<T> Snapshot<T>(Func<IEnumerable<T>> source, string[] locator, string kind, int max) where T : class
        {
            var values = new List<T>(); string? error = null; IEnumerator<T>? iterator = null;
            try
            {
                var collection = source();
                if (collection is null) error = "NULL_COLLECTION";
                else
                {
                    iterator = collection.GetEnumerator();
                    if (iterator is null) error = "NULL_ENUMERATOR";
                    else while (iterator.MoveNext())
                    {
                        if (values.Count >= max || nodes >= MaxNodes) { error = "ELEMENT_LIMIT"; break; }
                        nodes++;
                        var value = iterator.Current;
                        if (value is null) { error = "NULL_ELEMENT"; break; }
                        values.Add(value);
                    }
                }
            }
            catch (Exception e) { error = "ENUMERATION_FAILED:" + e.GetType().Name; }
            finally
            {
                try { iterator?.Dispose(); }
                catch (Exception e) { error = (error is null ? "" : error + ";") + "DISPOSE_FAILED:" + e.GetType().Name; }
            }
            inventory.Add(new(locator, kind, error is null ? "COMPLETE" : "PARTIAL", values.Count, error));
            return new(values, error is null);
        }

        private bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value) && value!.Length <= 512;
        private sealed class Batch<T>(List<T> values, bool complete)
        {
            internal List<T> Values { get; } = values;
            internal bool Complete { get; } = complete;
        }
    }

    private static string[] Append(string[] parent, params string[] parts) => parent.Concat(parts).ToArray();
    private static HmiEventDiagnosticEntry Unknown(string[] locator, string name, string? type, string eventName, string reason)
        => new(HmiEventDiagnosticProjection.Project(locator, name, type, eventName, null, false, false, null, null, reason),
            reason, reason.StartsWith("UNSUPPORTED", StringComparison.Ordinal) ? "UNSUPPORTED" : "UNKNOWN", null, "UNKNOWN", null);
}

internal sealed class HmiEventDiagnosticEntry(HmiEventDiagnosticRecord projection, string inventoryStatus,
    string supportStatus, bool? async, string asyncStatus, string? asyncError)
{
    internal HmiEventDiagnosticRecord Projection { get; } = projection;
    internal string InventoryStatus { get; } = inventoryStatus;
    internal string SupportStatus { get; } = supportStatus;
    internal bool? Async { get; } = async;
    internal string AsyncStatus { get; } = asyncStatus;
    internal string? AsyncError { get; } = asyncError;
}

internal sealed class HmiEventDiagnosticInventory(string[] locator, string kind, string status, int count, string? error, string? observedValue = null)
{
    internal string[] Locator { get; } = locator;
    internal string Kind { get; } = kind;
    internal string Status { get; } = status;
    internal int Count { get; } = count;
    internal string? Error { get; } = error;
    internal string? ObservedValue { get; } = observedValue;
}

internal sealed class HmiEventDiagnosticResult(HmiEventDiagnosticEntry[] entries, HmiEventDiagnosticInventory[] inventory)
{
    internal HmiEventDiagnosticEntry[] Entries { get; } = entries;
    internal HmiEventDiagnosticInventory[] Inventory { get; } = inventory;
    internal string Coverage => Entries.All(e => e.Projection.Coverage == "COMPLETE_FOR_EVENT_ENUMERATION"
        && e.InventoryStatus == "COMPLETE" && e.SupportStatus == "SUPPORTED"
        && e.AsyncStatus is "READ" or "NOT_APPLICABLE") && Inventory.All(i => i.Status == "COMPLETE") ? "COMPLETE_FOR_SCOPE" : "PARTIAL";
}
