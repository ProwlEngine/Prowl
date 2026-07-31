// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Linq;
using System.Reflection;

using Prowl.Ember;
using Prowl.Echo;
using Prowl.Editor.Projects.Scripting;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>Full-system hot reload tests: real scripts, a real scene, a real recompile, migrated onto the new types.</summary>
[Trait("Category", "Build")]
public class HotReloadSceneTests : EditorTestHarness
{
    public HotReloadSceneTests()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();
    }

    // Migrate a two-object scene with a cross reference onto a recompiled component with an added field.
    [Fact]
    public void RealRecompile_MigratesLiveScene_ToNewTypes_PreservingStateAndIdentity()
    {
        Assembly v1 = CompileGameAssembly("Enemy.cs",
            "using Prowl.Runtime; public class Enemy : MonoBehaviour { public int Hp; public Enemy Target; }");
        Type enemyV1 = v1.GetType("Enemy")!;
        Assert.Null(enemyV1.GetField("Shield")); // v1 has no Shield yet

        var scene = new Scene();
        var goA = new GameObject("A");
        var goB = new GameObject("B");
        MonoBehaviour a = goA.AddComponent(enemyV1);
        MonoBehaviour b = goB.AddComponent(enemyV1);
        enemyV1.GetField("Hp")!.SetValue(a, 100);
        enemyV1.GetField("Hp")!.SetValue(b, 50);
        enemyV1.GetField("Target")!.SetValue(a, b); // A targets B
        scene.Add(goA);
        scene.Add(goB);

        Assembly v2 = CompileGameAssembly("Enemy.cs",
            "using Prowl.Runtime; public class Enemy : MonoBehaviour { public int Hp; public Enemy Target; public int Shield; }");
        Type enemyV2 = v2.GetType("Enemy")!;
        Assert.NotSame(v1, v2);

        SceneHotReload.Migrate(scene, v1, v2);

        MonoBehaviour newA = goA.GetComponents().Single();
        MonoBehaviour newB = goB.GetComponents().Single();

        Assert.Same(enemyV2, newA.GetType());
        Assert.Same(v2, newA.GetType().Assembly);
        Assert.Equal(100, enemyV2.GetField("Hp")!.GetValue(newA));
        Assert.Equal(50, enemyV2.GetField("Hp")!.GetValue(newB));
        Assert.Equal(0, enemyV2.GetField("Shield")!.GetValue(newA));   // new field defaulted
        Assert.Same(newB, enemyV2.GetField("Target")!.GetValue(newA)); // cross ref keeps identity
        Assert.Same(goA, newA.GameObject);                             // same GameObject
    }

    // A running scene: after migration the registry dispatches to the migrated instance, so the new Update runs.
    [Fact]
    public void RealRecompile_RunningScene_RepointsRegistry_SoNewCodeRunsOnMigratedInstance()
    {
        using var play = EnterPlayMode();

        Assembly v1 = CompileGameAssembly("Ticker.cs",
            "using Prowl.Runtime; public class Ticker : MonoBehaviour { public int Ticks; public override void Update() { Ticks++; } }");
        Type tickerV1 = v1.GetType("Ticker")!;

        var scene = new Scene();
        scene.Enable();
        var go = new GameObject("T");
        MonoBehaviour comp = go.AddComponent(tickerV1);
        scene.Add(go);

        UpdateScene(scene, 3);
        Assert.Equal(3, tickerV1.GetField("Ticks")!.GetValue(comp)); // old code, +1 per frame

        Assembly v2 = CompileGameAssembly("Ticker.cs",
            "using Prowl.Runtime; public class Ticker : MonoBehaviour { public int Ticks; public int Extra; public override void Update() { Ticks += 10; } }");
        Type tickerV2 = v2.GetType("Ticker")!;

        SceneHotReload.Migrate(scene, v1, v2);

        MonoBehaviour newComp = go.GetComponents().Single();
        Assert.Same(tickerV2, newComp.GetType());
        Assert.Equal(3, tickerV2.GetField("Ticks")!.GetValue(newComp)); // state carried over
        Assert.Equal(0, tickerV2.GetField("Extra")!.GetValue(newComp));

        UpdateScene(scene, 2);
        Assert.Equal(23, tickerV2.GetField("Ticks")!.GetValue(newComp)); // new code (+10) ran on the migrated instance
    }

    // Echo binds a deserialized component to a type by name against the loaded assemblies, round-tripping fields.
    // This is what the serialize/unload/reload path relies on; the migration path above avoids that dependency.
    [Fact]
    public void EchoRoundTrip_BindsComponentToLoadedTypeByName_AndCarriesFields()
    {
        Assembly asm = CompileGameAssembly("Loot.cs",
            "using Prowl.Runtime; public class Loot : MonoBehaviour { public int Gold; }");
        Type lootType = asm.GetType("Loot")!;

        var scene = new Scene();
        var go = new GameObject("Chest");
        MonoBehaviour loot = go.AddComponent(lootType);
        lootType.GetField("Gold")!.SetValue(loot, 777);
        scene.Add(go);

        string text = Serializer.Serialize(scene).WriteToString();
        var restored = Serializer.Deserialize<Scene>(EchoObject.ReadFromString(text))!;

        MonoBehaviour restoredLoot = restored.AllObjects
            .First(g => g.Name == "Chest")
            .GetComponents()
            .First(c => c.GetType().Name == "Loot");

        Assert.Same(lootType, restoredLoot.GetType()); // bound by name to the loaded type
        Assert.Equal(777, lootType.GetField("Gold")!.GetValue(restoredLoot));
    }

    // ---- engine + editor integration (through Prowl.Ember) ----

    // Watching the whole Prowl.Editor assembly and walking its statics headless must not crash (a cctor throwing
    // is caught + logged; a hard native crash would kill the process).
    [Fact]
    public void WatchingEditorAssembly_WalksStaticsHeadless_WithoutCrashing()
    {
        Assembly v1 = CompileGameAssembly("W.cs", "public class E { public int Id; }");
        Assembly v2 = CompileGameAssembly("W.cs", "public class E { public int Id; public int X; }");

        var logs = new System.Collections.Generic.List<string>();
        void Capture(string m, DebugStackTrace? _, LogSeverity s)
        {
            if (s is LogSeverity.Error or LogSeverity.Warning)
                lock (logs) logs.Add(m);
        }

        Debug.OnLog += Capture;
        try
        {
            var engine = ReloadEngine.Create(options =>
            {
                options.AssemblyBytes = AssemblyBytesResolver;

                foreach (var prefix in new[] { "Silk.NET", "Jitter2", "Magick.NET" })
                    options.Scope.ExcludePrefix(prefix);

                options.Scope.Include(typeof(GameObject).Assembly);                          // Prowl.Runtime
                options.Scope.Include(typeof(Prowl.Editor.Core.EditorApplication).Assembly); // Prowl.Editor
                options.Scope.Include(v1);
            });

            engine.Apply(ReloadRequest.Create().Replace(v1, v2).Build());
        }
        finally { Debug.OnLog -= Capture; }

        var cctorFailures = logs.Where(m => m.Contains("TypeInitialization", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(cctorFailures.Count == 0,
            $"Walking Prowl.Editor forced {cctorFailures.Count} static constructor failure(s):\n{string.Join("\n", cctorFailures)}");
    }

    // A ReloadCache reached by the walk clears itself (reload hook.Persisted), so entries keyed on old types are
    // dropped rather than pinning the old assembly.
    [Fact]
    public void ReloadCache_ReachedByWalk_ClearsItself()
    {
        Assembly v1 = CompileGameAssembly("R.cs", "public class E { public int Id; }");
        Assembly v2 = CompileGameAssembly("R.cs", "public class E { public int Id; public int Extra; }");

        var cache = new ReloadCache<Type, int>();
        cache.Set(typeof(string), 42);
        Assert.True(cache.TryGetValue(typeof(string), out _));

        var engine = ReloadEngine.Create(options =>
        {
            options.AssemblyBytes = AssemblyBytesResolver;
            options.Scope.Include(v1);
        });

        // Reached as a root, so it is visited and its preserved hook fires.
        engine.Apply(ReloadRequest.Create().Replace(v1, v2).Root(cache).Build());

        Assert.False(cache.TryGetValue(typeof(string), out _)); // cleared by the walk
    }

    // A cross-GameObject component reference must be repointed to the migrated target.
    [Fact]
    public void Migrate_Scene_CrossGameObjectReference_IsRepointed()
    {
        Assembly v1 = CompileGameAssembly("Link.cs",
            "using Prowl.Runtime; public class Node : MonoBehaviour { public Node Other; public int Val; }");
        Type nodeV1 = v1.GetType("Node")!;

        var scene = new Scene();
        var go1 = new GameObject("1");
        var go2 = new GameObject("2");
        MonoBehaviour a = go1.AddComponent(nodeV1);
        MonoBehaviour b = go2.AddComponent(nodeV1);
        nodeV1.GetField("Val")!.SetValue(b, 42);
        nodeV1.GetField("Other")!.SetValue(a, b); // A (on go1) references B (on go2)
        scene.Add(go1);
        scene.Add(go2);

        Assembly v2 = CompileGameAssembly("Link.cs",
            "using Prowl.Runtime; public class Node : MonoBehaviour { public Node Other; public int Val; public int Extra; }");
        SceneHotReload.Migrate(scene, v1, v2);

        Type nodeV2 = v2.GetType("Node")!;
        MonoBehaviour newA = go1.GetComponents().Single();
        MonoBehaviour newB = go2.GetComponents().Single();
        object? other = nodeV2.GetField("Other")!.GetValue(newA);

        Assert.Same(newB, other); // repointed across GameObjects, not left as the old B
        Assert.Equal(42, nodeV2.GetField("Val")!.GetValue(newB));
    }

    // A newly added FixedUpdate override must start firing after a reload, not be dropped.
    [Fact]
    public void Migrate_Scene_NewlyOverriddenCallback_StartsDispatching()
    {
        using var play = EnterPlayMode();

        Assembly v1 = CompileGameAssembly("Cb.cs",
            "using Prowl.Runtime; public class C : MonoBehaviour { public int U; public override void Update() { U++; } }");
        Type cV1 = v1.GetType("C")!;

        var scene = new Scene();
        scene.Enable();
        var go = new GameObject("G");
        go.AddComponent(cV1);
        scene.Add(go);
        UpdateScene(scene, 1);

        Assembly v2 = CompileGameAssembly("Cb.cs",
            "using Prowl.Runtime; public class C : MonoBehaviour { public int U; public int F; public override void Update() { U++; } public override void FixedUpdate() { F++; } }");
        SceneHotReload.Migrate(scene, v1, v2);

        Type cV2 = v2.GetType("C")!;
        MonoBehaviour newC = go.GetComponents().Single();

        Tick(scene, 3);
        Assert.Equal(4, cV2.GetField("U")!.GetValue(newC)); // 1 before + 3 after
        Assert.Equal(3, cV2.GetField("F")!.GetValue(newC)); // newly overridden FixedUpdate now fires
    }

    // A component that overrode no per-frame callback at all is never in the dispatcher's registration array,
    // so re-registration after a reload has to come from the live scene rather than from what was registered.
    [Fact]
    public void Migrate_Scene_ComponentGainingItsFirstCallback_StartsDispatching()
    {
        using var play = EnterPlayMode();

        Assembly v1 = CompileGameAssembly("First.cs",
            "using Prowl.Runtime; public class C : MonoBehaviour { public int Data; }");
        Type cV1 = v1.GetType("C")!;

        var scene = new Scene();
        scene.Enable();
        var go = new GameObject("G");
        MonoBehaviour comp = go.AddComponent(cV1);
        cV1.GetField("Data")!.SetValue(comp, 5);
        scene.Add(go);
        UpdateScene(scene, 1);

        Assembly v2 = CompileGameAssembly("First.cs",
            "using Prowl.Runtime; public class C : MonoBehaviour { public int Data; public int U; public override void Update() { U++; } }");
        SceneHotReload.Migrate(scene, v1, v2);

        Type cV2 = v2.GetType("C")!;
        MonoBehaviour newC = go.GetComponents().Single();

        Tick(scene, 3);

        Assert.Equal(5, cV2.GetField("Data")!.GetValue(newC)); // state still carried across
        Assert.Equal(3, cV2.GetField("U")!.GetValue(newC));    // and it now ticks
    }

    // The mirror case: a component that drops its only per-frame callback has to stop being dispatched.
    [Fact]
    public void Migrate_Scene_ComponentLosingItsOnlyCallback_StopsDispatching()
    {
        using var play = EnterPlayMode();

        Assembly v1 = CompileGameAssembly("Last.cs",
            "using Prowl.Runtime; public class C : MonoBehaviour { public int U; public override void Update() { U++; } }");
        Type cV1 = v1.GetType("C")!;

        var scene = new Scene();
        scene.Enable();
        var go = new GameObject("G");
        go.AddComponent(cV1);
        scene.Add(go);
        UpdateScene(scene, 1);

        Assembly v2 = CompileGameAssembly("Last.cs",
            "using Prowl.Runtime; public class C : MonoBehaviour { public int U; }");
        SceneHotReload.Migrate(scene, v1, v2);

        Type cV2 = v2.GetType("C")!;
        MonoBehaviour newC = go.GetComponents().Single();

        Tick(scene, 3);

        Assert.Equal(1, cV2.GetField("U")!.GetValue(newC)); // the one tick from before the reload, and no more
    }

    // A scene component's detach and attach hooks fire on the old then new instance (state carried between).
    [Fact]
    public void Migrate_Scene_ReloadHooks_FireOnComponents()
    {
        Assembly v1 = CompileGameAssembly("Hook.cs",
            "using System.Collections.Generic; using Prowl.Runtime; using Prowl.Ember; " +
            "public class Net : MonoBehaviour, IReloadAware { public int Setups; " +
            "public void OnReloadDetach(ReloadState s) { s.Set(\"n\", Setups); } " +
            "public void OnReloadAttach(ReloadState s) { Setups = s.GetOrDefault<int>(\"n\") + 1; } }");
        Type netV1 = v1.GetType("Net")!;

        var scene = new Scene();
        var go = new GameObject("G");
        MonoBehaviour comp = go.AddComponent(netV1);
        netV1.GetField("Setups")!.SetValue(comp, 5);
        scene.Add(go);

        Assembly v2 = CompileGameAssembly("Hook.cs",
            "using System.Collections.Generic; using Prowl.Runtime; using Prowl.Ember; " +
            "public class Net : MonoBehaviour, IReloadAware { public int Setups; public int Extra; " +
            "public void OnReloadDetach(ReloadState s) { s.Set(\"n\", Setups); } " +
            "public void OnReloadAttach(ReloadState s) { Setups = s.GetOrDefault<int>(\"n\") + 1; } }");
        SceneHotReload.Migrate(scene, v1, v2);

        Type netV2 = v2.GetType("Net")!;
        MonoBehaviour newComp = go.GetComponents().Single();
        Assert.Equal(6, netV2.GetField("Setups")!.GetValue(newComp)); // Destroyed(old) stashed 5, Created(new) read it and added 1
    }

    // A component whose script was DELETED must be removed from the GameObject, not left stale.
    [Fact]
    public void Migrate_Scene_RemovedComponentType_IsDroppedFromGameObject()
    {
        Assembly v1 = CompileGameAssembly("Two.cs",
            "using Prowl.Runtime; public class Keeper : MonoBehaviour { public int X; } public class Doomed : MonoBehaviour { public int Y; }");
        Type keeperV1 = v1.GetType("Keeper")!;
        Type doomedV1 = v1.GetType("Doomed")!;

        var scene = new Scene();
        var go = new GameObject("G");
        go.AddComponent(keeperV1);
        go.AddComponent(doomedV1);
        scene.Add(go);
        Assert.Equal(2, go.GetComponents().Count());

        Assembly v2 = CompileGameAssembly("Two.cs",
            "using Prowl.Runtime; public class Keeper : MonoBehaviour { public int X; public int Extra; }");
        SceneHotReload.Migrate(scene, v1, v2);

        Type keeperV2 = v2.GetType("Keeper")!;
        var comps = go.GetComponents().ToList();
        Assert.Single(comps);                      // the removed-type component is gone
        Assert.Same(keeperV2, comps[0].GetType()); // the surviving one migrated to the new type
    }

    // A user handler subscribed to an engine STATIC event (Scene.OnSceneLoaded) must be repointed to the
    // migrated component after a scene hot reload.
    [Fact]
    public void EngineStaticEvent_NamedHandler_RepointedToMigratedComponent()
    {
        FieldInfo loaded = typeof(Scene).GetField("OnSceneLoaded", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.NotNull(loaded);

        Assembly v1 = CompileGameAssembly("Sub.cs",
            "using Prowl.Runtime; public class Sub : MonoBehaviour { public int Pings; public void OnPing(){ Pings++; } }");
        Type subV1 = v1.GetType("Sub")!;

        var scene = new Scene();
        var go = new GameObject("A");
        MonoBehaviour oldSub = go.AddComponent(subV1);
        scene.Add(go);

        var handler = (Action)Delegate.CreateDelegate(typeof(Action), oldSub, subV1.GetMethod("OnPing")!);
        Scene.OnSceneLoaded += handler;
        try
        {
            Assembly v2 = CompileGameAssembly("Sub.cs",
                "using Prowl.Runtime; public class Sub : MonoBehaviour { public int Pings; public int Extra; public void OnPing(){ Pings++; } }");

            SceneHotReload.Migrate(scene, v1, v2);
            MonoBehaviour newSub = go.GetComponents().Single();

            var del = (Delegate?)loaded.GetValue(null);
            Assert.NotNull(del);
            Assert.Same(newSub, del!.Target); // repointed to the migrated component, not the dead old one
        }
        finally { loaded.SetValue(null, null); } // reset the global static event
    }
}
