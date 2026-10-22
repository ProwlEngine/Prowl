using Prowl.Runtime.Resources;
using Xunit;

namespace Prowl.Runtime.Test;

public class SceneEventTests : RuntimeTestBase
{
    public interface ITestEvent : ISceneEvent<ITestEvent> { void Fire(); }
    public class Listener : Component, ITestEvent
    {
        public Action? Action;
        public int Calls;
        public void Fire() { Calls++; Action?.Invoke(); }
    }
    [ExecutionOrder(-10)] public class EarlyListener : Listener { }

    [Fact]
    public void ScopesAndInactiveOptionsAreExplicit()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject(), child = CreateGameObject(), sibling = CreateGameObject();
        child.SetParent(root);
        scene.Add(root); scene.Add(sibling);
        Listener a = root.AddComponent<Listener>(), b = child.AddComponent<Listener>(), c = sibling.AddComponent<Listener>();
        b.Enabled = false;
        root.RunEvent<ITestEvent>(e => e.Fire(), EventScope.Self);
        Assert.Equal((1, 0, 0), (a.Calls, b.Calls, c.Calls));
        root.RunEvent<ITestEvent>(e => e.Fire());
        Assert.Equal((2, 0, 0), (a.Calls, b.Calls, c.Calls));
        root.RunEvent<ITestEvent>(e => e.Fire(), includeInactive: true);
        Assert.Equal((3, 1, 0), (a.Calls, b.Calls, c.Calls));
        scene.RunEvent<ITestEvent>(e => e.Fire(), includeInactive: true);
        Assert.Equal((4, 2, 1), (a.Calls, b.Calls, c.Calls));
    }

    [Fact]
    public void OrderingIsStableAndExceptionsDoNotStopOtherListeners()
    {
        GameObject root = CreateGameObject();
        List<string> order = new();
        root.AddComponent<Listener>().Action = () => { order.Add("first"); throw new Exception("test"); };
        root.AddComponent<EarlyListener>().Action = () => order.Add("early");
        root.AddComponent<Listener>().Action = () => order.Add("last");
        root.RunEvent<ITestEvent>(e => e.Fire());
        Assert.Equal(new[] { "early", "first", "last" }, order);
    }

    [Fact]
    public void AddedListenersWaitAndRemovedListenersAreSkipped()
    {
        GameObject root = CreateGameObject();
        Listener first = root.AddComponent<Listener>(), removed = root.AddComponent<Listener>();
        Listener? added = null;
        first.Action = () => { root.RemoveComponent(removed); added ??= root.AddComponent<Listener>(); };
        root.RunEvent<ITestEvent>(e => e.Fire());
        Assert.Equal(0, removed.Calls);
        Assert.Equal(0, added!.Calls);
        first.Action = null;
        root.RunEvent<ITestEvent>(e => e.Fire());
        Assert.Equal(1, added.Calls);
    }

    [Fact]
    public void NestedDispatchUsesItsOwnMembershipSnapshot()
    {
        GameObject root = CreateGameObject();
        Listener first = root.AddComponent<Listener>();
        bool nested = false;
        Listener? added = null;
        first.Action = () =>
        {
            if (nested) return;
            nested = true;
            added = root.AddComponent<Listener>();
            root.RunEvent<ITestEvent>(e => e.Fire());
        };
        root.RunEvent<ITestEvent>(e => e.Fire());
        Assert.Equal(2, first.Calls);
        Assert.Equal(1, added!.Calls);
    }

    [Fact]
    public void MovingOrDestroyingTargetsDuringDispatchIsSafe()
    {
        GameObject root = CreateGameObject(), child = CreateGameObject(), other = CreateGameObject();
        child.SetParent(root);
        Listener first = root.AddComponent<Listener>(), moved = child.AddComponent<Listener>();
        first.Action = () => child.SetParent(other);
        root.RunEvent<ITestEvent>(e => e.Fire());
        Assert.Equal(0, moved.Calls);
        first.Action = () => root.Destroy();
        Listener last = root.AddComponent<Listener>();
        root.RunEvent<ITestEvent>(e => e.Fire());
        Assert.Equal(0, last.Calls);
    }

    [Fact]
    public void SceneMembershipMovesAndHotReloadInvalidateCachedTargets()
    {
        Scene first = CreateScene(), second = CreateScene();
        GameObject go = CreateGameObject();
        Listener listener = go.AddComponent<Listener>();
        first.Add(go);
        first.RunEvent<ITestEvent>(e => e.Fire());
        second.RunEvent<ITestEvent>(e => e.Fire()); // Warm empty cache.
        second.Add(go);
        first.RunEvent<ITestEvent>(e => e.Fire());
        second.RunEvent<ITestEvent>(e => e.Fire());
        second.OnHotReload();
        second.RunEvent<ITestEvent>(e => e.Fire());
        Assert.Equal(3, listener.Calls);
    }

    [Fact]
    public void ExplicitSceneAndConvenienceMethodsHaveTheSameTargets()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject();
        Listener listener = go.AddComponent<Listener>();
        scene.Add(go);
        Scene.Load(scene); Scene.ProcessPendingLoad();
        ITestEvent.Post(e => e.Fire());
        ITestEvent.PostToGameObject(go, e => e.Fire(), EventScope.Self);
        Assert.Equal(2, listener.Calls);
    }

    [Fact]
    public void WarmDispatchDoesNotAllocate()
    {
        GameObject root = CreateGameObject();
        root.AddComponent<Listener>();
        Action<ITestEvent> action = static e => e.Fire();
        root.RunEvent(action);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) root.RunEvent(action);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void DisabledListenerEligibilityIsCheckedAgainDuringDispatch()
    {
        GameObject go = CreateGameObject();
        Listener first = go.AddComponent<Listener>(), second = go.AddComponent<Listener>();
        first.Action = () => second.Enabled = false;
        go.RunEvent<ITestEvent>(e => e.Fire());
        Assert.Equal(0, second.Calls);
        first.Action = () => second.Enabled = true;
        go.RunEvent<ITestEvent>(e => e.Fire());
        Assert.Equal(1, second.Calls);
    }
}
