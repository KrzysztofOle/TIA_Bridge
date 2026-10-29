// Compile-time doubles for the exact typed reader source. No Siemens reference,
// remoting, TIA process, attachment, or vendor constructor is used by these tests.
using System.Collections;
using TiaBridge.Host.Tests;

namespace TiaBridge.Host.Tests
{
    internal class HmiTestNode
    {
        internal HashSet<string> Fail { get; } = [];
        internal HashSet<string> Null { get; } = [];
        internal List<string> Calls { get; } = [];
        internal T Read<T>(string getter, T value)
        {
            Calls.Add(getter);
            if (Fail.Contains(getter)) throw new InvalidOperationException("synthetic secret; must not appear in report");
            if (Null.Contains(getter)) return default!;
            return value;
        }
        internal void Mutate() => throw new Xunit.Sdk.XunitException("Mutating vendor call");
    }

    internal class HmiTestComposition<T> : HmiTestNode, IEnumerable<T> where T : class
    {
        internal IEnumerable<T> Values { get; set; } = [];
        internal int FailMoveAfter { get; set; } = -1;
        internal int ActiveEnumerators { get; private set; }
        internal int Moves { get; private set; }
        public IEnumerator<T> GetEnumerator()
        {
            if (!Read("GetEnumerator", true)) return null!;
            ActiveEnumerators++; return new Iterator(this, Values.GetEnumerator());
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Iterator(HmiTestComposition<T> owner, IEnumerator<T> inner) : IEnumerator<T>
        {
            private int yielded;
            public T Current => owner.Read("Current", inner.Current);
            object IEnumerator.Current => Current;
            public bool MoveNext()
            {
                owner.Moves++; owner.Read("MoveNext", true);
                if (owner.FailMoveAfter == yielded) throw new InvalidOperationException("synthetic enumeration failure");
                bool result = inner.MoveNext(); if (result) yielded++; return result;
            }
            public void Dispose()
            {
                owner.ActiveEnumerators--; inner.Dispose(); owner.Read("Dispose", true);
            }
            public void Reset() => throw new NotSupportedException();
        }
    }
}
namespace Siemens.Engineering.HmiUnified
{
    internal sealed class HmiSoftware : HmiTestNode
    {
        internal HmiTestComposition<UI.Screens.HmiScreen> ScreenValues { get; } = new();
        internal HmiTestComposition<UI.ScreenGroup.HmiScreenGroup> GroupValues { get; } = new();
        public IEnumerable<UI.Screens.HmiScreen> Screens => Read("Screens", ScreenValues);
        public IEnumerable<UI.ScreenGroup.HmiScreenGroup> ScreenGroups => Read("ScreenGroups", GroupValues);
    }
}
namespace Siemens.Engineering.HmiUnified.UI.ScreenGroup
{
    internal sealed class HmiScreenGroup : HmiTestNode
    {
        public string Name => Read("Name", "synthetic-group");
        internal HmiTestComposition<Screens.HmiScreen> ScreenValues { get; } = new();
        internal HmiTestComposition<HmiScreenGroup> GroupValues { get; } = new();
        public IEnumerable<Screens.HmiScreen> Screens => Read("Screens", ScreenValues);
        public IEnumerable<HmiScreenGroup> Groups => Read("Groups", GroupValues);
    }
}
namespace Siemens.Engineering.HmiUnified.UI.Base
{
    internal class HmiScreenItemBase(string name) : HmiTestNode
    {
        public string Name { get => Read("Name", name); set => Mutate(); }
        public void Delete() => Mutate();
        public void SetAttribute(string name, object value) => Mutate();
    }
    internal sealed class HmiCustomWidgetContainer(string name) : HmiScreenItemBase(name)
    {
        internal Events.HmiTestEventHandlers<Events.HmiCustomWidgetContainerEventHandler> Handlers { get; } = new();
        public Events.HmiTestEventHandlers<Events.HmiCustomWidgetContainerEventHandler> EventHandlers => Read("EventHandlers", Handlers);
    }
    internal sealed class HmiCustomWebControlContainer(string name) : HmiScreenItemBase(name)
    {
        internal Events.HmiTestEventHandlers<Events.HmiCustomWebControlContainerEventHandler> Handlers { get; } = new();
        public Events.HmiTestEventHandlers<Events.HmiCustomWebControlContainerEventHandler> EventHandlers => Read("EventHandlers", Handlers);
    }
}
namespace Siemens.Engineering.HmiUnified.UI.Screens
{
    internal sealed class HmiScreen : HmiTestNode
    {
        public string Name { get => Read("Name", "T05_Recipes_Main"); set => Mutate(); }
        internal HmiTestComposition<Events.HmiScreenEventHandler> Handlers { get; } = new();
        internal HmiTestComposition<Base.HmiScreenItemBase> Items { get; } = new();
        public IEnumerable<Events.HmiScreenEventHandler> EventHandlers => Read("EventHandlers", Handlers);
        public IEnumerable<Base.HmiScreenItemBase> ScreenItems => Read("ScreenItems", Items);
    }
}
namespace Siemens.Engineering.HmiUnified.UI.Widgets
{
    internal sealed class HmiButton(string name) : Base.HmiScreenItemBase(name)
    {
        internal HmiTestComposition<Events.HmiButtonEventHandler> Handlers { get; } = new();
        public IEnumerable<Events.HmiButtonEventHandler> EventHandlers => Read("EventHandlers", Handlers);
    }
}
namespace Siemens.Engineering.HmiUnified.UI.Enum
{
    internal enum HmiScreenEventType { None, Tapped, ContextTapped, Loaded, Unloaded }
    internal enum HmiButtonEventType { None, Activated, Deactivated, Tapped, KeyDown, KeyUp, Down, Up, ContextTapped }
}
namespace Siemens.Engineering.HmiUnified.UI.Dynamization.Script
{
    internal interface IHmiScript
    {
        string ScriptCode { get; }
        string GlobalDefinitionAreaScriptCode { get; }
        bool Async { get; }
    }
    internal sealed class HmiTestScript : HmiTestNode, IHmiScript
    {
        internal string Code { get; set; } = "synthetic();\r\n";
        internal string Global { get; set; } = "// synthetic declarations\n";
        internal bool IsAsync { get; set; } = true;
        public string ScriptCode { get => Read("ScriptCode", Code); set => Mutate(); }
        public string GlobalDefinitionAreaScriptCode { get => Read("GlobalDefinitionAreaScriptCode", Global); set => Mutate(); }
        public bool Async { get => Read("Async", IsAsync); set => Mutate(); }
    }
}
namespace Siemens.Engineering.HmiUnified.UI.Events
{
    internal sealed class HmiTestEventHandlers<T> : HmiTestComposition<T> where T : class
    {
        internal HmiTestComposition<string> ValidValues { get; } = new() { Values = ["RowSelected"] };
        public IEnumerable<string> GetValidEnumValues() => Read("GetValidEnumValues", ValidValues);
    }
    internal class HmiTestHandler<T>(T eventType) : HmiTestNode
    {
        internal Dynamization.Script.IHmiScript Body { get; set; } = new Dynamization.Script.HmiTestScript();
        public T EventType => Read("EventType", eventType);
        public Dynamization.Script.IHmiScript Script => Read("Script", Body);
    }
    internal sealed class HmiScreenEventHandler(Enum.HmiScreenEventType eventType) : HmiTestHandler<Enum.HmiScreenEventType>(eventType);
    internal sealed class HmiButtonEventHandler(Enum.HmiButtonEventType eventType) : HmiTestHandler<Enum.HmiButtonEventType>(eventType);
    internal sealed class HmiCustomWidgetContainerEventHandler(string eventType) : HmiTestHandler<string>(eventType);
    internal sealed class HmiCustomWebControlContainerEventHandler(string eventType) : HmiTestHandler<string>(eventType);
}
