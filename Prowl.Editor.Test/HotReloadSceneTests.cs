// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Linq;
using System.Reflection;

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
            "using Prowl.Runtime; public class Enemy : Component { public int Hp; public Enemy Target; }");
        Type enemyV1 = v1.GetType("Enemy")!;
        Assert.Null(enemyV1.GetField("Shield")); // v1 has no Shield yet

        var scene = new SceneInstance(SceneMode.Edit);
        var goA = new GameObject("A");
        var goB = new GameObject("B");
        Component a = goA.AddComponent(enemyV1);
        Component b = goB.AddComponent(enemyV1);
        enemyV1.GetField("Hp")!.SetValue(a, 100);
        enemyV1.GetField("Hp")!.SetValue(b, 50);
        enemyV1.GetField("Target")!.SetValue(a, b); // A targets B
        scene.Add(goA);
        scene.Add(goB);

        Assembly v2 = CompileGameAssembly("Enemy.cs",
            "using Prowl.Runtime; public class Enemy : Component { public int Hp; public Enemy Target; public int Shield; }");
        Type enemyV2 = v2.GetType("Enemy")!;
        Assert.NotSame(v1, v2);

        var logs = new System.Collections.Generic.List<string>();
        void Capture(LogEvent log)
        {
            if (log.Severity is LogSeverity.Error or LogSeverity.Warning)
                lock (logs) logs.Add(log.Message);
        }

        Debug.OnLog += Capture;
        try { SceneHotReload.Migrate(scene, v1, v2); }
        finally { Debug.OnLog -= Capture; }

        var cctorFailures = logs.Where(m => m.Contains("TypeInitialization", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(cctorFailures.Count == 0,
            $"Walking Prowl.Editor forced {cctorFailures.Count} static constructor failure(s):\n{string.Join("\n", cctorFailures)}");

        Component newA = goA.GetComponents().Single();
        Component newB = goB.GetComponents().Single();

        Assert.Same(enemyV2, newA.GetType());
        Assert.Same(v2, newA.GetType().Assembly);
        Assert.Equal(100, enemyV2.GetField("Hp")!.GetValue(newA));
        Assert.Equal(50, enemyV2.GetField("Hp")!.GetValue(newB));
        Assert.Equal(0, enemyV2.GetField("Shield")!.GetValue(newA));   // new field defaulted
        Assert.Same(newB, enemyV2.GetField("Target")!.GetValue(newA)); // cross ref keeps identity
        Assert.Same(goA, newA.GameObject);                             // same GameObject
    }

    // Every world is a root of one migration, the dormant one too, and each drops its type indices.
    [Fact]
    public void RealRecompile_MigratesEveryWorld_IncludingADormantOne()
    {
        Assembly v1 = CompileGameAssembly("Lantern.cs",
            "using Prowl.Runtime; public class Lantern : Component { public int Lit; }");
        Type lanternV1 = v1.GetType("Lantern")!;

        var running = new SceneInstance(SceneMode.Edit);
        running.Activate();
        var dormant = new SceneInstance(SceneMode.Edit);
        GameObject first = running.Create("First");
        GameObject second = dormant.Create("Second");
        first.AddComponent(lanternV1);
        second.AddComponent(lanternV1);
        Assert.Single(running.GetAll<Component>().Where(c => c.GetType() == lanternV1));

        Assembly v2 = CompileGameAssembly("Lantern.cs",
            "using Prowl.Runtime; public class Lantern : Component { public int Lit; public int Added; }");
        Type lanternV2 = v2.GetType("Lantern")!;

        SceneHotReload.Migrate([running, dormant], [(v1, v2)]);

        Assert.Same(lanternV2, first.GetComponents().Single().GetType());
        Assert.Same(lanternV2, second.GetComponents().Single().GetType());
        Assert.Single(running.GetAll<Component>().Where(c => c.GetType() == lanternV2));
        running.Dispose();
        dormant.Dispose();
    }

    // A running scene: after migration the registry dispatches to the migrated instance, so the new Update runs.
    [Fact]
    public void RealRecompile_RunningScene_RepointsRegistry_SoNewCodeRunsOnMigratedInstance()
    {
        using PlayModeScope play = EnterPlayMode();

        Assembly v1 = CompileGameAssembly("Ticker.cs",
            "using Prowl.Runtime; public class Ticker : Component { public int Ticks; public override void Update() { Ticks++; } }");
        Type tickerV1 = v1.GetType("Ticker")!;

        var scene = new SceneInstance();
        scene.Activate();
        var go = new GameObject("T");
        Component comp = go.AddComponent(tickerV1);
        scene.Add(go);

        UpdateScene(scene, 3);
        Assert.Equal(3, tickerV1.GetField("Ticks")!.GetValue(comp)); // old code, +1 per frame

        Assembly v2 = CompileGameAssembly("Ticker.cs",
            "using Prowl.Runtime; public class Ticker : Component { public int Ticks; public int Extra; public override void Update() { Ticks += 10; } }");
        Type tickerV2 = v2.GetType("Ticker")!;

        SceneHotReload.Migrate(scene, v1, v2);

        Component newComp = go.GetComponents().Single();
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
            "using Prowl.Runtime; public class Loot : Component { public int Gold; }");
        Type lootType = asm.GetType("Loot")!;

        var scene = new SceneInstance(SceneMode.Edit);
        var go = new GameObject("Chest");
        Component loot = go.AddComponent(lootType);
        lootType.GetField("Gold")!.SetValue(loot, 777);
        scene.Add(go);

        string text = scene.ActiveSection.CaptureTree().WriteToString();
        SceneInstance restored = SceneInstance.Instantiate(SceneContent.Read(EchoObject.ReadFromString(text)), SceneMode.Edit);

        Component restoredLoot = restored.Objects
            .First(g => g.Name == "Chest")
            .GetComponents()
            .First(c => c.GetType().Name == "Loot");

        Assert.Same(lootType, restoredLoot.GetType()); // bound by name to the loaded type
        Assert.Equal(777, lootType.GetField("Gold")!.GetValue(restoredLoot));
    }

    // ---- engine + editor integration (through Prowl.Ember) ----

    // A newly added FixedUpdate override must start firing after a reload, not be dropped.
    [Fact]
    public void Migrate_Scene_NewlyOverriddenCallback_StartsDispatching()
    {
        using PlayModeScope play = EnterPlayMode();

        Assembly v1 = CompileGameAssembly("Cb.cs",
            "using Prowl.Runtime; public class C : Component { public int U; public override void Update() { U++; } }");
        Type cV1 = v1.GetType("C")!;

        var scene = new SceneInstance();
        scene.Activate();
        var go = new GameObject("G");
        go.AddComponent(cV1);
        scene.Add(go);
        UpdateScene(scene, 1);

        Assembly v2 = CompileGameAssembly("Cb.cs",
            "using Prowl.Runtime; public class C : Component { public int U; public int F; public override void Update() { U++; } public override void FixedUpdate() { F++; } }");
        SceneHotReload.Migrate(scene, v1, v2);

        Type cV2 = v2.GetType("C")!;
        Component newC = go.GetComponents().Single();

        Tick(scene, 3);
        Assert.Equal(4, cV2.GetField("U")!.GetValue(newC)); // 1 before + 3 after
        Assert.Equal(3, cV2.GetField("F")!.GetValue(newC)); // newly overridden FixedUpdate now fires
    }

    // A component that overrode no per-frame callback at all is never in the dispatcher's registration array,
    // so re-registration after a reload has to come from the live scene rather than from what was registered.
    [Fact]
    public void Migrate_Scene_ComponentGainingItsFirstCallback_StartsDispatching()
    {
        using PlayModeScope play = EnterPlayMode();

        Assembly v1 = CompileGameAssembly("First.cs",
            "using Prowl.Runtime; public class C : Component { public int Data; }");
        Type cV1 = v1.GetType("C")!;

        var scene = new SceneInstance();
        scene.Activate();
        var go = new GameObject("G");
        Component comp = go.AddComponent(cV1);
        cV1.GetField("Data")!.SetValue(comp, 5);
        scene.Add(go);
        UpdateScene(scene, 1);

        Assembly v2 = CompileGameAssembly("First.cs",
            "using Prowl.Runtime; public class C : Component { public int Data; public int U; public override void Update() { U++; } }");
        SceneHotReload.Migrate(scene, v1, v2);

        Type cV2 = v2.GetType("C")!;
        Component newC = go.GetComponents().Single();

        Tick(scene, 3);

        Assert.Equal(5, cV2.GetField("Data")!.GetValue(newC)); // state still carried across
        Assert.Equal(3, cV2.GetField("U")!.GetValue(newC));    // and it now ticks
    }

    // The mirror case: a component that drops its only per-frame callback has to stop being dispatched.
    [Fact]
    public void Migrate_Scene_ComponentLosingItsOnlyCallback_StopsDispatching()
    {
        using PlayModeScope play = EnterPlayMode();

        Assembly v1 = CompileGameAssembly("Last.cs",
            "using Prowl.Runtime; public class C : Component { public int U; public override void Update() { U++; } }");
        Type cV1 = v1.GetType("C")!;

        var scene = new SceneInstance();
        scene.Activate();
        var go = new GameObject("G");
        go.AddComponent(cV1);
        scene.Add(go);
        UpdateScene(scene, 1);

        Assembly v2 = CompileGameAssembly("Last.cs",
            "using Prowl.Runtime; public class C : Component { public int U; }");
        SceneHotReload.Migrate(scene, v1, v2);

        Type cV2 = v2.GetType("C")!;
        Component newC = go.GetComponents().Single();

        Tick(scene, 3);

        Assert.Equal(1, cV2.GetField("U")!.GetValue(newC)); // the one tick from before the reload, and no more
    }

    // A scene component's detach and attach hooks fire on the old then new instance (state carried between).
    [Fact]
    public void Migrate_Scene_ReloadHooks_FireOnComponents()
    {
        Assembly v1 = CompileGameAssembly("Hook.cs",
            "using System.Collections.Generic; using Prowl.Runtime; using Prowl.Ember; " +
            "public class Net : Component, IReloadAware { public int Setups; " +
            "public void OnReloadDetach(ReloadState s) { s.Set(\"n\", Setups); } " +
            "public void OnReloadAttach(ReloadState s) { Setups = s.GetOrDefault<int>(\"n\") + 1; } }");
        Type netV1 = v1.GetType("Net")!;

        var scene = new SceneInstance(SceneMode.Edit);
        var go = new GameObject("G");
        Component comp = go.AddComponent(netV1);
        netV1.GetField("Setups")!.SetValue(comp, 5);
        scene.Add(go);

        Assembly v2 = CompileGameAssembly("Hook.cs",
            "using System.Collections.Generic; using Prowl.Runtime; using Prowl.Ember; " +
            "public class Net : Component, IReloadAware { public int Setups; public int Extra; " +
            "public void OnReloadDetach(ReloadState s) { s.Set(\"n\", Setups); } " +
            "public void OnReloadAttach(ReloadState s) { Setups = s.GetOrDefault<int>(\"n\") + 1; } }");
        SceneHotReload.Migrate(scene, v1, v2);

        Type netV2 = v2.GetType("Net")!;
        Component newComp = go.GetComponents().Single();
        Assert.Equal(6, netV2.GetField("Setups")!.GetValue(newComp)); // Destroyed(old) stashed 5, Created(new) read it and added 1
    }

    // A component whose script was DELETED must be removed from the GameObject, not left stale.
    [Fact]
    public void Migrate_Scene_RemovedComponentType_IsDroppedFromGameObject()
    {
        Assembly v1 = CompileGameAssembly("Two.cs",
            "using Prowl.Runtime; public class Keeper : Component { public int X; } public class Doomed : Component { public int Y; }");
        Type keeperV1 = v1.GetType("Keeper")!;
        Type doomedV1 = v1.GetType("Doomed")!;

        var scene = new SceneInstance(SceneMode.Edit);
        var go = new GameObject("G");
        go.AddComponent(keeperV1);
        go.AddComponent(doomedV1);
        scene.Add(go);
        Assert.Equal(2, go.GetComponents().Count());

        Assembly v2 = CompileGameAssembly("Two.cs",
            "using Prowl.Runtime; public class Keeper : Component { public int X; public int Extra; }");
        SceneHotReload.Migrate(scene, v1, v2);

        Type keeperV2 = v2.GetType("Keeper")!;
        var comps = go.GetComponents().ToList();
        Assert.Single(comps);                      // the removed-type component is gone
        Assert.Same(keeperV2, comps[0].GetType()); // the surviving one migrated to the new type
    }

    // A user handler subscribed to an engine STATIC event (SceneInstance.MainChanged) must be repointed to the
    // migrated component after a scene hot reload.
    [Fact]
    public void EngineStaticEvent_NamedHandler_RepointedToMigratedComponent()
    {
        FieldInfo loaded = typeof(SceneInstance).GetField("MainChanged", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.NotNull(loaded);

        Assembly v1 = CompileGameAssembly("Sub.cs",
            "using Prowl.Runtime; using Prowl.Runtime.Resources; public class Sub : Component { public int Pings; public void OnPing(SceneInstance a, SceneInstance b){ Pings++; } }");
        Type subV1 = v1.GetType("Sub")!;

        var scene = new SceneInstance(SceneMode.Edit);
        var go = new GameObject("A");
        Component oldSub = go.AddComponent(subV1);
        scene.Add(go);

        var handler = (Action<SceneInstance?, SceneInstance?>)Delegate.CreateDelegate(typeof(Action<SceneInstance?, SceneInstance?>), oldSub, subV1.GetMethod("OnPing")!);
        SceneInstance.MainChanged += handler;
        try
        {
            Assembly v2 = CompileGameAssembly("Sub.cs",
                "using Prowl.Runtime; using Prowl.Runtime.Resources; public class Sub : Component { public int Pings; public int Extra; public void OnPing(SceneInstance a, SceneInstance b){ Pings++; } }");

            SceneHotReload.Migrate(scene, v1, v2);
            Component newSub = go.GetComponents().Single();

            var del = (Delegate?)loaded.GetValue(null);
            Assert.NotNull(del);
            Assert.Same(newSub, del!.Target); // repointed to the migrated component, not the dead old one
        }
        finally { loaded.SetValue(null, null); } // reset the global static event
    }

    // The prefab system caches a whole GameObject tree per prefab, as the baseline every override
    // comparison is made against. Those trees hold user script components, so if the walk did not reach
    // them a recompile would leave the baseline holding old-type components and every comparison against
    // it would see a type mismatch, which reads as "this instance has no overrides to record".
    [Fact]
    public void RealRecompile_MigratesThePrefabComparisonBaseline_SoOverridesKeepBeingRecorded()
    {
        Assembly v1 = CompileGameAssembly("Knob.cs",
            "using Prowl.Runtime; public class Knob : Component { public int Value; }");
        Type knobV1 = v1.GetType("Knob")!;

        var authored = new GameObject("Root");
        authored.AddComponent(knobV1);
        Guid guid = CreatePrefabAsset(authored, "Knob.prefab");

        GameObject instance = GameObject.InstantiateDetached(GetPrefab(guid)!)!;
        var scene = new SceneInstance(SceneMode.Edit);
        scene.Add(instance);
        BindEditWorld(scene);

        // Warm the cache: this is what builds and holds the baseline tree.
        Prefabs.PrefabUtility.ReconcileInstance(instance);

        Assembly v2 = CompileGameAssembly("Knob.cs",
            "using Prowl.Runtime; public class Knob : Component { public int Value; public int Added; }");
        Type knobV2 = v2.GetType("Knob")!;

        SceneHotReload.Migrate(SceneInstance.Main!, v1, v2);

        Component migrated = instance.GetComponents().Single(c => c.GetType().Name == "Knob");
        Assert.Same(knobV2, migrated.GetType());

        // The comparison still works, which it cannot if the baseline is holding a v1 component.
        knobV2.GetField("Value")!.SetValue(migrated, 42);
        Prefabs.PrefabUtility.ReconcileInstance(instance);

        PropertyOverride recorded = Assert.Single(instance.PrefabOverrides);
        Assert.EndsWith("Value", recorded.Path);
    }

    // The list of fields an override can address is cached per type. Carried across a recompile it would
    // still name the old fields: a renamed one has no counterpart and arrives as null, and an added one
    // is missing, so its edits would never be recorded.
    // Each case names its own type: compiled assemblies stay loaded for the run, and a prefab binds its
    // component by type name.
    [Theory]
    [InlineData("Gauge", "public int Value; public int Renamed; public int Added;")]
    [InlineData("Meter", "public int Value; public int Old; public int Added;")]
    public void RealRecompile_ChangedFields_AreComparedOnPrefabInstances(string typeName, string v2Fields)
    {
        Assembly v1 = CompileGameAssembly($"{typeName}.cs",
            $"using Prowl.Runtime; public class {typeName} : Component {{ public int Value; public int Old; }}");
        Type typeV1 = v1.GetType(typeName)!;

        var authored = new GameObject("Root");
        authored.AddComponent(typeV1);
        Guid guid = CreatePrefabAsset(authored, $"{typeName}.prefab");

        GameObject instance = GameObject.InstantiateDetached(GetPrefab(guid)!)!;
        var scene = new SceneInstance(SceneMode.Edit);
        scene.Add(instance);
        BindEditWorld(scene);

        // Warm the field cache against v1.
        Prefabs.PrefabUtility.ReconcileInstance(instance);

        Assembly v2 = CompileGameAssembly($"{typeName}.cs",
            $"using Prowl.Runtime; public class {typeName} : Component {{ {v2Fields} }}");
        Type typeV2 = v2.GetType(typeName)!;

        SceneHotReload.Migrate(SceneInstance.Main!, v1, v2);

        Component migrated = instance.GetComponents().Single(c => c.GetType().Name == typeName);
        Assert.Same(typeV2, migrated.GetType());

        typeV2.GetField("Added")!.SetValue(migrated, 7);
        Prefabs.PrefabUtility.ReconcileInstance(instance);

        PropertyOverride recorded = Assert.Single(instance.PrefabOverrides);
        Assert.EndsWith("Added", recorded.Path);
    }
}
