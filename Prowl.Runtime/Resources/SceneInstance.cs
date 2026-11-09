// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.PaperUI;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Tasks;
using Prowl.Runtime.UI;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;

/// <summary>What kind of world a <see cref="SceneInstance"/> is.</summary>
public enum SceneMode
{
    /// <summary>Gameplay runs while the world is running.</summary>
    Game,
    /// <summary>An editor world: only components with [ExecuteAlways] run gameplay.</summary>
    Edit,
    /// <summary>Never runs gameplay and is never ticked, rendered on demand.</summary>
    Preview,
}

/// <summary>Where a <see cref="SceneInstance"/> is in its life.</summary>
public enum SceneState
{
    /// <summary>Objects exist and are registered, no OnEnable has been delivered and nothing ticks.</summary>
    Dormant,
    /// <summary>Components are enabled.</summary>
    Running,
    /// <summary>Being torn down. Still counts as playing, so every component gets its OnDisable.</summary>
    Destroying,
    Destroyed,
}

/// <summary>
/// A live world of GameObjects. Its stored form is a <see cref="Scene"/>, and any number of scenes can be
/// instantiated from one, like the editor's edit copy and play copy.
/// </summary>
[CreateAssetMenu("Scene", Extension = ".scene", Order = 0)]
public class SceneInstance : EngineObject, ISerializable
{
    #region Scene Manager

    private static SceneInstance? _main;

    // Weak, so a scene dropped without being disposed goes away with whatever it used rather than being walked forever.
    private static readonly List<WeakReference<SceneInstance>> s_live = [];

    /// <summary>Every world that is not destroyed, in creation order. Held weakly: something else keeps a world alive.</summary>
    public static IReadOnlyList<SceneInstance> All
    {
        get
        {
            SceneInstance[] live = Live;
            Array.Reverse(live);
            return live;
        }
    }

    /// <summary>Every scene not yet disposed: the current one, one waiting to load, previews and prefab editing.</summary>
    internal static SceneInstance[] Live
    {
        get
        {
            lock (s_live)
            {
                var live = new List<SceneInstance>(s_live.Count);
                for (int i = s_live.Count - 1; i >= 0; i--)
                {
                    if (s_live[i].TryGetTarget(out SceneInstance? scene)) live.Add(scene);
                    else s_live.RemoveAt(i);
                }
                return [.. live];
            }
        }
    }


    /// <summary>
    /// The game world. The host binds it: the player at startup, the editor to the world it edits or plays. Reading it
    /// while nothing is bound throws, it is never created on the fly.
    /// </summary>
    public static SceneInstance Main
    {
        get
        {
            SceneInstance? main = _main;
            if (ReferenceEquals(main, null) || main._state == SceneState.Destroyed)
                throw new InvalidOperationException("No world is bound as SceneInstance.Main. The host binds it: the player at startup, the editor to the world it edits, a test through its test base.");
            return main;
        }
        set
        {
            MainThreadContext.AssertMainThread(nameof(Main));
            SceneInstance? previous = _main;
            if (ReferenceEquals(previous, value)) return;
            _main = value;
            Raise(MainChanged, value, handler => handler(previous, value), nameof(MainChanged));
        }
    }

    /// <summary>Whether a world is bound as <see cref="Main"/>.</summary>
    public static bool HasMain => !ReferenceEquals(_main, null) && _main._state != SceneState.Destroyed;

    /// <summary>Fires when a world is created with <see cref="Create"/>.</summary>
    public static event Action<SceneInstance>? Created;

    /// <summary>Fires when a world starts its teardown, inside that world.</summary>
    public static event Action<SceneInstance>? Destroying;

    /// <summary>Fires when <see cref="Main"/> is bound to another world, with the old and the new one.</summary>
    public static event Action<SceneInstance?, SceneInstance?>? MainChanged;

    // Every handler runs inside the raising world and is guarded on its own.
    private static void Raise<T>(T? handlers, SceneInstance? world, Action<T> invoke, string name) where T : Delegate
    {
        if (handlers == null) return;
        using SceneScope scope = Enter(world);
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try { invoke((T)handler); }
            catch (Exception e) { Debug.LogException($"[Scene] A {name} handler threw", e); }
        }
    }

    /// <summary>Creates an empty, dormant world. Call <see cref="Activate"/> to start it.</summary>
    public static SceneInstance Create(string name, SceneMode mode = SceneMode.Game)
    {
        MainThreadContext.AssertMainThread(nameof(Create));
        var world = new SceneInstance(mode) { Name = name };
        world.ActiveSection.Name = name;
        Raise(Created, world, handler => handler(world), nameof(Created));
        return world;
    }

    private static readonly AsyncLocal<SceneInstance?> s_active = new();

    /// <summary>
    /// The ambient world: the one whose code is running right now. While the engine runs a callback on a component it
    /// is that component's scene, inside <see cref="Enter"/> it is that scene, and across an await it is whatever was
    /// active where the await started. Otherwise it is the current scene. Main thread only.
    /// </summary>
    public static SceneInstance Active
    {
        get
        {
            MainThreadContext.AssertMainThread();
            SceneInstance? scene = s_active.Value;
            if (ReferenceEquals(scene, null)) return Main;
            if (scene._state == SceneState.Destroyed)
                throw new ObjectDisposedException(scene.Name, $"The scene '{scene.Name}' this code was running in has been destroyed.");
            return scene;
        }
    }

    /// <summary>
    /// <see cref="Active"/> without its checks, null when no world is bound or the one running has been destroyed. For
    /// readers that must never throw, such as <see cref="Time"/> during deserialization or off the main thread.
    /// </summary>
    internal static SceneInstance? ActiveOrNull
    {
        get
        {
            SceneInstance? scene = s_active.Value;
            if (ReferenceEquals(scene, null)) scene = _main;
            return scene is { _state: not SceneState.Destroyed } ? scene : null;
        }
    }

    /// <summary>The clock of <see cref="ActiveOrNull"/>, null when there is no world.</summary>
    internal static SceneClock? ActiveClock => ActiveOrNull is { } world ? world.Clock : null;

    /// <summary>Makes this scene <see cref="Active"/> until the scope is disposed. Scopes nest.</summary>
    public SceneScope Enter() => Enter(this);

    // Writes the ambient world only when it changes, so a callback in the world already active pays one read.
    internal static SceneScope Enter(SceneInstance? scene)
    {
        if (ReferenceEquals(scene, null)) return default;
        SceneInstance? previous = s_active.Value;
        if (ReferenceEquals(previous, scene)) return default;
        s_active.Value = scene;
        return new SceneScope(previous);
    }

    /// <summary>Puts the previous <see cref="Active"/> scene back when disposed.</summary>
    public readonly struct SceneScope : IDisposable
    {
        private readonly SceneInstance? _previous;
        private readonly bool _changed;

        internal SceneScope(SceneInstance? previous)
        {
            _previous = previous;
            _changed = true;
        }

        public void Dispose()
        {
            if (_changed) s_active.Value = _previous;
        }
    }

    private SceneState _state;

    /// <summary>What kind of world this is, fixed at creation.</summary>
    public SceneMode Mode { get; }

    /// <summary>Whether this world's components are enabled. A world being torn down stays Active for its own teardown callbacks.</summary>
    public SceneState State => _state;

    /// <summary>Whether gameplay runs here: a game world that is running or being torn down.</summary>
    public bool IsPlaying => Mode == SceneMode.Game && _state is SceneState.Running or SceneState.Destroying;

    // Whether components here have had OnEnable delivered, which stays true while a destroy disables them.
    internal bool ComponentsEnabled => _isActive;

    /// <summary>This world's time: scale, modifiers, scaled time and its fixed step accumulator.</summary>
    public SceneClock Clock { get; } = new();

    /// <summary>
    /// Stops Start, Update, FixedUpdate, physics, animation and navigation. Lifecycle callbacks still run, so enabling
    /// a HUD on a paused world works, and its clock stands still.
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>Whether the engine draws this world to the game's output while it runs, in <see cref="RenderOrder"/>.</summary>
    public bool Present { get; set; }

    /// <summary>Presented worlds draw lowest first, each one's cameras over the previous world.</summary>
    public int RenderOrder { get; set; }

    /// <summary>Set by a host that ticks and renders this world itself, so the engine skips it.</summary>
    internal bool HostTicked { get; set; }

    // One frame to run while paused, from the editor's step button.
    private bool _stepRequested;

    /// <summary>Runs exactly one frame of a paused world at its next tick.</summary>
    internal void RequestStep() => _stepRequested = true;

    /// <summary>Whether a step is still waiting to run.</summary>
    internal bool IsStepPending => _stepRequested;

    /// <summary>Whether the engine ticks this world: a running game world no host has claimed.</summary>
    internal bool TicksWithEngine => Mode == SceneMode.Game && _state == SceneState.Running && !HostTicked;

    // True while the engine or a host runs a world's frame, so Step refuses to reenter it.
    [ThreadStatic] private static bool t_ticking;

    /// <summary>
    /// One full frame of this world: its clock advances, then the fixed steps the frame yields, then Update. A paused
    /// world only drops what was destroyed and its time stands still. Fixed steps run only where gameplay plays.
    /// </summary>
    internal void Tick(float unscaledDelta)
    {
        if (IsDisposed || _state != SceneState.Running) return;
        bool wasTicking = t_ticking;
        t_ticking = true;
        try
        {
            if (Paused && !_stepRequested)
            {
                Clock.Hold();
                using (Enter(this)) Flush();
                return;
            }
            _stepRequested = false;

            bool wasExecuting = Application.IsGameplayExecuting;
            Application.IsGameplayExecuting = IsPlaying;
            try
            {
                Clock.Advance(unscaledDelta);
                int steps = Clock.TakeFixedSteps();
                if (IsPlaying)
                    for (int i = 0; i < steps; i++)
                        FixedUpdate();
                Update();
            }
            finally { Application.IsGameplayExecuting = wasExecuting; }
        }
        finally { t_ticking = wasTicking; }
    }

    /// <summary>
    /// Runs one full frame of a world nothing else ticks, such as a preview or a test world. Refused while a world's
    /// frame is already running.
    /// </summary>
    public void Step(float deltaTime)
    {
        if (t_ticking) throw new InvalidOperationException($"'{Name}' cannot step while a world's frame is running.");
        MainThreadContext.AssertOwner(this);
        Tick(deltaTime);
    }

    /// <summary>Builds a dormant world holding detached content, without binding it anywhere. The content's objects move into it.</summary>
    public static SceneInstance Instantiate(SceneContent content, SceneMode mode = SceneMode.Game)
    {
        var scene = new SceneInstance(mode);
        scene.ActiveSection.Fill(content);
        return scene;
    }

    // Loads, additive loads and unloads, applied at the safe point in the order they were queued.
    private static readonly List<object> s_queue = [];

    /// <summary>True from the start of an engine frame until its safe point. Outside one, a waited load activates at once.</summary>
    internal static bool FrameRunning;

    /// <summary>The <see cref="Scene.Load"/> queued for this world, null when there is none.</summary>
    public SceneLoad? PendingLoad
    {
        get
        {
            foreach (object entry in s_queue)
                if (entry is SceneLoad { Kind: SceneLoadKind.Load } load && ReferenceEquals(load.Target, this) && !load.IsFinished)
                    return load;
            return null;
        }
    }

    /// <summary>Fires when a section finished activating, inside its world.</summary>
    public static event Action<SceneLoadedArgs>? Loaded;

    /// <summary>Fires before a section's objects tear down, inside its world.</summary>
    public static event Action<SceneSection>? Unloading;

    /// <summary>Every load from instantiating until done, whose detached objects the asset walk keeps what they use for.</summary>
    internal static IEnumerable<object> PendingContent
    {
        get
        {
            foreach (object entry in s_queue)
                if (entry is SceneLoad { PendingObjects: { } objects })
                    yield return objects;
        }
    }

    internal static void Queue(SceneLoad load)
    {
        MainThreadContext.AssertMainThread();
        // A Load replaces everything the world will hold, so it cancels the Load and the additive loads queued before it.
        if (load.Kind == SceneLoadKind.Load)
            foreach (object entry in s_queue.ToArray())
                if (entry is SceneLoad { Kind: SceneLoadKind.Load or SceneLoadKind.Additive, IsApplying: false } earlier && ReferenceEquals(earlier.Target, load.Target))
                {
                    earlier.Cancel();
                    s_queue.Remove(earlier);
                }
        s_queue.Add(load);
    }

    internal static void Queue(SceneSection unload) => s_queue.Add(unload);

    internal static bool IsUnloadQueued(SceneSection section) => s_queue.Contains(section);

    internal static int QueuedUnloads(SceneInstance world) => s_queue.Count(entry => entry is SceneSection section && ReferenceEquals(section.Instance, world));

    /// <summary>
    /// Applies queued loads and unloads that are ready, in the order they were queued. A world with an entry not yet
    /// ready holds its later entries back, so an additive load queued after a Load never lands before it.
    /// </summary>
    internal static void ApplyQueue()
    {
        if (s_queue.Count == 0) return;
        var blocked = new HashSet<SceneInstance>(ReferenceEqualityComparer.Instance);
        foreach (object entry in s_queue.ToArray())
        {
            if (!s_queue.Contains(entry)) continue;
            if (entry is SceneSection unload)
            {
                if (blocked.Contains(unload.Instance)) continue;
                s_queue.Remove(unload);
                if (unload.IsLoaded && unload.Instance._state is SceneState.Dormant or SceneState.Running)
                    unload.Instance.ApplyUnload(unload);
                continue;
            }

            var load = (SceneLoad)entry;
            if (load.IsFinished) { s_queue.Remove(load); continue; }
            SceneInstance? world = load.Target;
            bool targeted = !ReferenceEquals(world, null);
            if (load.IsApplying)
            {
                if (targeted) blocked.Add(world!);
                continue;
            }
            if (targeted && blocked.Contains(world!)) continue;
            if (targeted && world!._state is SceneState.Destroying or SceneState.Destroyed)
            {
                s_queue.Remove(load);
                load.Cancel();
                continue;
            }
            if (!load.Advance())
            {
                if (targeted) blocked.Add(world!);
                continue;
            }

            // Still queued while it activates, so its detached content is a root of the residency walk.
            load.IsApplying = true;
            try { load.Complete(Activate(load)); }
            catch (Exception e) { load.Fail(e); }
            finally { s_queue.Remove(load); }
        }
    }

    private static SceneSection Activate(SceneLoad load)
    {
        // Everything the level uses is resident before any of it enables, so OnEnable never sees a loading asset. The
        // detached content is still a walk root here.
        try { AssetDatabase.LoadEverythingReached(); }
        catch (Exception e) { Debug.LogException($"[Scene] Loading what '{load.Scene.Name}' uses threw", e); }

        SceneContent content = load.TakeContent();

        SceneSection section;
        if (load.Kind == SceneLoadKind.Open)
        {
            SceneInstance world = Create(load.Scene.Name, load.OpenMode);
            section = world.ActiveSection;
            section.Source = load.Scene;
            section.Fill(content);
            RaiseLoaded(section, additive: false);
        }
        else if (load.Kind == SceneLoadKind.Load)
            section = load.Target!.ReplaceContent(content, load.Scene.Name, load.Scene);
        else
            section = load.Target!.AddContent(content, load.Scene.Name, load.Scene);

        if (load.Options.UnloadUnusedAssets) AssetDatabase.UnloadUnused();
        else AssetDatabase.Walk();
        return section;
    }

    /// <summary>
    /// Tears down every section but <see cref="Persistent"/> and makes one from <paramref name="content"/> the active
    /// section, at once. What a <see cref="Scene.Load"/> does at the safe point, and how the editor opens a scene.
    /// </summary>
    internal SceneSection ReplaceContent(SceneContent content, string name, Scene? source)
    {
        EnsureNotDisposed();
        MainThreadContext.AssertOwner(this);
        var section = new SceneSection(this, name, persistent: false) { Source = source };

        // Torn down while still active, so a death effect spawned in OnDisable goes with them.
        TearDownSections([.. _sections.Where(s => !s.IsPersistent)]);
        _sections.Add(section);
        _activeSection = section;
        section.Fill(content);
        RaiseLoaded(section, additive: false);
        return section;
    }

    /// <summary>Adds a section made from <paramref name="content"/> after the others, at once, leaving the active section.</summary>
    internal SceneSection AddContent(SceneContent content, string name, Scene? source)
    {
        EnsureNotDisposed();
        MainThreadContext.AssertOwner(this);
        var section = new SceneSection(this, name, persistent: false) { Source = source };
        _sections.Add(section);
        section.Fill(content, additive: true);
        RaiseLoaded(section, additive: true);
        return section;
    }

    private static void RaiseLoaded(SceneSection section, bool additive)
        => Raise(Loaded, section.Instance, handler => handler(new SceneLoadedArgs(section, additive)), nameof(Loaded));

    /// <summary>Unloads <paramref name="section"/> at once, the way a queued unload applies at the safe point.</summary>
    internal void UnloadNow(SceneSection section)
    {
        EnsureNotDisposed();
        MainThreadContext.AssertOwner(this);
        if (section.IsPersistent || !ReferenceEquals(section.Instance, this) || !section.IsLoaded) return;
        if (_sections.Count(s => !s.IsPersistent) <= 1) throw new InvalidOperationException($"'{section.Name}' is the last section of '{Name}'.");
        ApplyUnload(section);
    }

    /// <summary>Moves a loaded section to <paramref name="index"/> among the sections, after <see cref="Persistent"/>.</summary>
    internal void MoveSection(SceneSection section, int index)
    {
        if (section.IsPersistent || !_sections.Remove(section)) return;
        _sections.Insert(Math.Clamp(index, 1, _sections.Count), section);
    }

    private void ApplyUnload(SceneSection section)
    {
        bool wasActive = ReferenceEquals(_activeSection, section);
        int index = _sections.IndexOf(section);
        TearDownSections([section]);
        if (wasActive)
        {
            // The next loaded section in order, or the previous one when it was the last.
            SceneSection? next = null;
            for (int i = index; i < _sections.Count && next == null; i++)
                if (!_sections[i].IsPersistent) next = _sections[i];
            for (int i = Math.Min(index, _sections.Count) - 1; i >= 0 && next == null; i--)
                if (!_sections[i].IsPersistent) next = _sections[i];
            if (next != null) ActiveSection = next;
        }
        AssetDatabase.Walk();
    }

    /// <summary>
    /// Tears the sections' objects down inside this world in three passes, every OnDisable, then every
    /// OnRemovedFromScene, then every dispose, each object guarded on its own. Anything created into a section while
    /// it tears down, or destroyed meanwhile, goes in the same passes. Then the sections leave the world.
    /// </summary>
    private void TearDownSections(List<SceneSection> sections)
    {
        using SceneScope active = Enter(this);
        foreach (SceneSection section in sections)
            Raise(Unloading, this, handler => handler(section), nameof(Unloading));

        for (int round = 0; round < 8; round++)
        {
            List<GameObject> objects = [];
            foreach (SceneSection section in sections)
                foreach (GameObject root in section.LiveRoots.ToArray())
                    Collect(root);
            if (objects.Count == 0) break;

            if (_isActive)
            {
                foreach (GameObject g in objects)
                    foreach (Component c in g._components.ToArray())
                        if (!c.IsDisposed && c.HasBeenEnabled && c.EnabledInHierarchy)
                            Guarded(c, "OnDisable", c.InternalOnDisable);

                // An object OnDisable moved out, like one made persistent, stays and enables again.
                var kept = objects.FindAll(g => !g.IsDisposed && g.Section is { } s && !sections.Contains(s)).ToHashSet();
                objects.RemoveAll(kept.Contains);
                foreach (GameObject g in kept)
                    foreach (Component c in g._components.ToArray())
                        if (!c.IsDisposed && c.EnabledInHierarchy)
                            Guarded(c, "OnEnable", c.InternalOnEnable);
            }
            foreach (GameObject g in objects)
                foreach (Component c in g._components.ToArray())
                    if (!c.IsDisposed) Guarded(c, "OnRemovedFromScene", c.OnRemovedFromScene);
            foreach (GameObject g in objects)
                if (!g.IsDisposed) Guarded(g, "Dispose", g.Dispose);
            _hasDisposedObjects = true;
            Flush();

            void Collect(GameObject go)
            {
                objects.Add(go);
                foreach (GameObject child in go.Children)
                    if (!child.IsDisposed) Collect(child);
            }
        }

        foreach (SceneSection section in sections)
            RemoveSection(section);
        RebuildProbes(keepPrevious: false);
    }

    /// <summary>The scene the active section was loaded from, if any.</summary>
    public Scene? Source
    {
        get => ActiveSection.Source;
        set => ActiveSection.Source = value;
    }


    /// <summary>
    /// Applies queued loads and unloads. Driven once per frame by the game loop, right after the destroy queue, where
    /// nothing is mid callback.
    /// </summary>
    internal static void ProcessPendingLoad()
    {
        ApplyQueue();
        try { SafePoint?.Invoke(); }
        catch (Exception e) { Debug.LogException("[Scene] A safe point handler threw", e); }
    }

    /// <summary>Runs at the frame's safe point after queued loads apply, for a host with its own queued scene work, like the editor.</summary>
    internal static event Action? SafePoint;

    /// <summary>
    /// Disposes the current scene, so everything in it runs its teardown callbacks. Driven by the
    /// game loop on the way out. Reading <see cref="Main"/> afterwards creates a fresh empty scene.
    /// </summary>
    internal static void Shutdown()
    {
        foreach (object entry in s_queue.ToArray())
            if (entry is SceneLoad load) load.Cancel();
        s_queue.Clear();

        SceneInstance? main = _main;
        _main = null;
        if (!ReferenceEquals(main, null) && !main.IsDisposed)
            main.Dispose();
    }

    #endregion

    /// <summary>Every object in a world, enumerated without allocating.</summary>
    public readonly struct SceneObjects : IEnumerable<GameObject>
    {
        private readonly List<GameObject> _objects;

        internal SceneObjects(List<GameObject> objects) => _objects = objects;

        public Enumerator GetEnumerator() => new(_objects);
        IEnumerator<GameObject> IEnumerable<GameObject>.GetEnumerator() => GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<GameObject>
        {
            private readonly List<GameObject> _objects;
            private int _index;

            internal Enumerator(List<GameObject> objects)
            {
                _objects = objects;
                _index = -1;
            }

            public GameObject Current => _objects[_index];
            object System.Collections.IEnumerator.Current => Current;

            public bool MoveNext()
            {
                while (++_index < _objects.Count)
                    if (!_objects[_index].IsDisposed) return true;
                return false;
            }

            public void Reset() => _index = -1;
            public void Dispose() { }
        }
    }

    [SerializeIgnore]
    private List<GameObject> _allObj = [];
    [SerializeIgnore]
    private HashSet<GameObject> _allObjSet = new(ReferenceEqualityComparer.Instance);

    // Set when one of this scene's GameObjects is disposed, so Flush only scans when there is something to drop.
    [SerializeIgnore]
    private bool _hasDisposedObjects;

    internal void MarkObjectDisposed() => _hasDisposedObjects = true;

    // The worlds below only index the scene's own components, which the walk reaches through _allObj.
    [NotHeld]
    private PhysicsWorld _physics = new();

    public PhysicsWorld Physics { get { EnsureNotDisposed(); return _physics; } }

    [SerializeIgnore, NotHeld]
    private readonly NavMeshWorld _navigation = new();
    [NotHeld]
    private readonly SceneAnimation _animation = new();

    /// <summary>This scene's navigation state (registered navmeshes, queries, crowd).</summary>
    public NavMeshWorld Navigation { get { EnsureNotDisposed(); return _navigation; } }

    /// <summary>Every animator in the scene, advanced once per frame in its own phase.</summary>
    public SceneAnimation Animation { get { EnsureNotDisposed(); return _animation; } }

    // Rebuilds lazily, so it can still list removed components.
    [SerializeIgnore, NotHeld]
    private readonly SceneDispatcher _dispatcher = new();

    /// <summary>The scene's dispatch point for per-frame component callbacks and physics events.</summary>
    internal SceneDispatcher Dispatcher => _dispatcher;

    /// <summary>The enabled canvases in this scene, each added by its own OnEnable and removed by its OnDisable.</summary>
    [SerializeIgnore, NotHeld]
    internal readonly List<GameCanvas> Canvases = [];

    /// <summary>The enabled cameras in this world, so finding them each frame doesn't walk every object.</summary>
    [SerializeIgnore, NotHeld]
    internal readonly List<Camera> Cameras = [];

    [SerializeIgnore, NotHeld]
    internal readonly List<WindZone> WindZoneList = [];

    /// <summary>The enabled wind zones in this world.</summary>
    public IReadOnlyList<WindZone> WindZones => WindZoneList;

    [SerializeIgnore, NotHeld]
    internal readonly List<Volume> VolumeList = [];

    /// <summary>The enabled volumes in this world, which its cameras blend.</summary>
    public IReadOnlyList<Volume> Volumes => VolumeList;

    [SerializeIgnore, NotHeld]
    internal readonly List<AudioListener> AudioListeners = [];

    /// <summary>The first enabled audio listener in this world, or null. Only Main's listeners are heard.</summary>
    public AudioListener? AudioListener => AudioListeners.Count > 0 ? AudioListeners[0] : null;

    /// <summary>This world's UI input: its event system, its host viewport and pointer arbitration.</summary>
    public SceneInput Input { get; } = new();

    [SerializeIgnore, NotHeld]
    internal readonly LayoutUtility.Cache LayoutCache = new();

    private void ClearRegistries()
    {
        Canvases.Clear();
        Cameras.Clear();
        WindZoneList.Clear();
        VolumeList.Clear();
        AudioListeners.Clear();
        Input.Clear();
        LayoutCache.Sizes.Clear();
    }

    [SerializeIgnore, NotHeld]
    private SceneRendering? _rendering;

    /// <summary>The scene's link to Graphite: its render world and what keeps state in it.</summary>
    public SceneRendering Rendering { get { EnsureNotDisposed(); return _rendering ??= new SceneRendering(this); } }

    /// <summary>
    /// Called once after a hot reload has migrated the scene graph in place: each GameObject drops removed
    /// components and rebuilds its lookup, then the dispatcher re-derives membership and ordering from the new
    /// types. Ordered that way deliberately, since the dispatcher must not re-register a component that is
    /// about to be dropped.
    /// </summary>
    internal void OnHotReload()
    {
        ClearTypeIndices();
        _identifiers.Clear();
        foreach (GameObject go in _allObj)
            if (!ReferenceEquals(go, null) && !go.IsDisposed)
                go.OnHotReload();

        // Re-registering from the live scene rather than from what was registered before is what lets a
        // component whose new type gained its first per-frame callback start dispatching at all.
        _dispatcher.Reset();
        ClearRegistries();
        if (!_isActive) return;

        foreach (GameObject go in _allObj)
        {
            if (ReferenceEquals(go, null) || go.IsDisposed) continue;

            foreach (Component comp in go._components)
            {
                if (ReferenceEquals(comp, null) || comp.IsDisposed || !comp.EnabledInHierarchy) continue;
                comp._countedCollisionListener = false;
                _dispatcher.Register(comp);
                comp.JoinWorld(this);
            }
        }
    }

    [SerializeIgnore]
    private bool _isActive = false;

    private readonly List<SceneSection> _sections = [];
    private SceneSection _activeSection = null!;

    [NotHeld] private CancellationTokenSource? _token;
    [NotHeld] private MainThreadContext? _tokenSession;

    /// <summary>
    /// Cancelled when this world is destroyed and when the session restarts. A world that outlives a restart, like the
    /// dormant edit world, gives a fresh token from then on.
    /// </summary>
    public CancellationToken Token
    {
        get
        {
            if (_state is SceneState.Destroying or SceneState.Destroyed) return new CancellationToken(true);
            MainThreadContext? session = MainThreadContext.Current;
            if (_token == null || !ReferenceEquals(_tokenSession, session))
            {
                _token?.Dispose();
                _tokenSession = session;
                _token = session == null ? new CancellationTokenSource() : CancellationTokenSource.CreateLinkedTokenSource(session.SessionToken);
            }
            return _token.Token;
        }
    }

    private void CancelToken()
    {
        try { _token?.Cancel(); }
        catch (AggregateException e) { Debug.LogException($"[Scene] A callback on the token of '{Name}' threw", e); }
        _token?.Dispose();
        _token = null;
    }

    /// <summary>The world's sections: <see cref="Persistent"/> first, then in load order.</summary>
    public IReadOnlyList<SceneSection> Sections => _sections;

    /// <summary>The section that survives every load. Never unloaded, never saved, never active.</summary>
    public SceneSection Persistent => _sections[0];

    /// <summary>
    /// Where new objects go by default, and whose environment, lighting and bake settings the world shows. Never the
    /// <see cref="Persistent"/> section.
    /// </summary>
    public SceneSection ActiveSection
    {
        get => _activeSection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!ReferenceEquals(value.Instance, this)) throw new InvalidOperationException($"'{value.Name}' is a section of another world.");
            if (value.IsPersistent) throw new InvalidOperationException("The persistent section can never be the active one.");
            if (!_sections.Contains(value)) throw new InvalidOperationException($"'{value.Name}' has been unloaded.");
            if (ReferenceEquals(_activeSection, value)) return;
            _activeSection = value;
            BakedLightingVersion++;
        }
    }

    /// <summary>Adds an empty section after the others. It unloads like any other.</summary>
    public SceneSection CreateSection(string name)
    {
        EnsureNotDisposed();
        var section = new SceneSection(this, name, persistent: false);
        _sections.Add(section);
        return section;
    }

    internal void RemoveSection(SceneSection section)
    {
        _sections.Remove(section);
        section.MarkUnloaded();
    }

    /// <summary>The active section's sky, ambient light and fog.</summary>
    public SceneEnvironment Environment => _activeSection.Environment;

    /// <summary>The active section's baked lighting.</summary>
    public SceneLighting Lighting => _activeSection.Lighting;

    /// <summary>The active section's lightmapper settings.</summary>
    public LightmapBakeSettings LightmapBake => _activeSection.LightmapBake;

    /// <summary>The light probes a world draws with, merged over its sections.</summary>
    internal sealed class ProbeSet
    {
        public required Float3[] Positions;
        public required SphericalHarmonicsL2[] SH;
        public required int[] Tetrahedra;
        public required int[] Neighbours;
    }

    [NotHeld] private ProbeSet? _probes;
    [NotHeld] private LightProbeVolume? _probeVolume;
    [NotHeld] private Task<ProbeSet>? _probeRebuild;
    [NotHeld] private CancellationTokenSource? _probeCancel;

    /// <summary>
    /// The probes over every loaded section: one section's stored set as it is, or one tetrahedralization over all of
    /// them, rebuilt on a worker, when two or more sections have probes. A finished rebuild is taken here.
    /// </summary>
    internal ProbeSet? Probes
    {
        get
        {
            if (_probeRebuild is { IsCompleted: true } done)
            {
                _probeRebuild = null;
                _probeCancel?.Dispose();
                _probeCancel = null;
                if (done.IsCompletedSuccessfully) SetProbes(done.Result);
                else if (done.Exception is { } failure) Debug.LogException($"[Probes] Merging the probes of '{Name}' threw", failure);
            }
            return _probes;
        }
    }

    /// <summary>The rebuild merging several sections' probes while one runs, so a caller can wait for it.</summary>
    internal Task? ProbeRebuild => _probeRebuild;

    /// <summary>Runtime probe sampler over <see cref="Probes"/> (lazy). Null when no section has baked probes.</summary>
    public LightProbeVolume? ProbeVolume
    {
        get
        {
            EnsureNotDisposed();
            ProbeSet? probes = Probes;
            if (_probeVolume == null && probes != null)
                _probeVolume = new LightProbeVolume(probes.Positions, probes.SH, probes.Tetrahedra, probes.Neighbours);
            return _probeVolume;
        }
    }

    /// <summary>Takes the sections' probes again, after a bake changed them.</summary>
    public void InvalidateProbeVolume() => RebuildProbes(keepPrevious: false);

    // Only an additive load keeps the previous set while a rebuild runs. Everything else drops it at once, so the probes
    // of an unloaded level never light the next one.
    internal void RebuildProbes(bool keepPrevious)
    {
        EnsureNotDisposed();
        CancelProbeRebuild();

        List<SceneLighting> baked = [];
        foreach (SceneSection section in _sections)
            if (section.Lighting.HasProbes) baked.Add(section.Lighting);

        if (baked.Count <= 1)
        {
            SetProbes(baked.Count == 0 ? null : new ProbeSet
            {
                Positions = baked[0].ProbePositions,
                SH = baked[0].ProbeSH,
                Tetrahedra = baked[0].ProbeTetrahedra,
                Neighbours = baked[0].ProbeTetNeighbours,
            });
            return;
        }

        if (!keepPrevious) SetProbes(null);

        // Section order, since the tetrahedralization depends on point order.
        Float3[] positions = [.. baked.SelectMany(lighting => lighting.ProbePositions)];
        SphericalHarmonicsL2[] sh = [.. baked.SelectMany(lighting => lighting.ProbeSH)];
        _probeCancel = new CancellationTokenSource();
        CancellationToken cancel = _probeCancel.Token;
        _probeRebuild = Task.Run(() =>
        {
            ProbeTetrahedralizer.Result tet = ProbeTetrahedralizer.Build(positions, cancel);
            return new ProbeSet { Positions = positions, SH = sh, Tetrahedra = tet.Tetrahedra, Neighbours = tet.Neighbours };
        }, cancel);
    }

    private void CancelProbeRebuild()
    {
        _probeCancel?.Cancel();
        _probeCancel?.Dispose();
        _probeCancel = null;
        _probeRebuild = null;
    }

    private void SetProbes(ProbeSet? probes)
    {
        _probes = probes;
        _probeVolume = null;
        BakedLightingVersion++;
    }

    /// <summary>Moves whenever the baked lighting changes, so the render world takes the new probes.</summary>
    [SerializeIgnore, NonSerialized]
    internal int BakedLightingVersion;

    /// <summary>How many objects are in this world, children included.</summary>
    public int ObjectCount { get { EnsureNotDisposed(); return _allObj.Count(o => !o.IsDisposed); } }

    /// <summary>Every object in this world, children included, in the order they joined. Enumerating does not allocate.</summary>
    public SceneObjects Objects { get { EnsureNotDisposed(); return new SceneObjects(_allObj); } }

    /// <summary>Every object a save writes: not marked DontSave, and not under one that is.</summary>
    internal IEnumerable<GameObject> SaveableObjects { get { EnsureNotDisposed(); return _allObj.Where(o => !o.IsDisposed && IsSaveable(o)); } }

    /// <summary>
    /// False for an object marked <see cref="HideFlags.DontSave"/> or <see cref="HideFlags.HideAndDontSave"/>,
    /// and for anything under one: a child written out without its parent would load back as a stray root.
    /// </summary>
    private static bool IsSaveable(GameObject obj)
    {
        for (GameObject? go = obj; go.IsValid(); go = go.Parent)
            if ((go.HideFlags & (HideFlags.DontSave | HideFlags.HideAndDontSave)) != 0)
                return false;
        return true;
    }

    /// <summary>The objects with no parent: section order, then sibling order within a section.</summary>
    public IReadOnlyList<GameObject> Roots
    {
        get
        {
            EnsureNotDisposed();
            List<GameObject> roots = [];
            foreach (SceneSection section in _sections)
                roots.AddRange(section.LiveRoots);
            return roots;
        }
    }

    // One index per component type or interface ever queried, kept as components join. Leaving is lazy: a query drops
    // what was destroyed or left, so no removal path has to know about the indices.
    private readonly Dictionary<Type, TypeIndex> _typeIndices = [];

    private sealed class TypeIndex
    {
        public readonly List<Component> Items = [];
        public readonly HashSet<Component> Members = new(ReferenceEqualityComparer.Instance);

        public void Add(Component component)
        {
            if (Members.Add(component)) Items.Add(component);
        }
    }

    // Identifiers of the objects and components in this world. Checked on every hit, since identifiers can change.
    private readonly Dictionary<Guid, EngineObject> _identifiers = [];

    /// <summary>Called by the engine when a component joins this world, before any gameplay gate.</summary>
    internal void ComponentJoined(Component component)
    {
        foreach ((Type type, TypeIndex index) in _typeIndices)
            if (type.IsInstanceOfType(component))
                index.Add(component);
        _identifiers[component.Identifier] = component;
    }

    /// <summary>Drops every type index, after a script reload changed what the types are.</summary>
    internal void ClearTypeIndices() => _typeIndices.Clear();

    private TypeIndex IndexFor(Type type)
    {
        if (_typeIndices.TryGetValue(type, out TypeIndex? index))
        {
            index.Items.RemoveAll(component =>
            {
                bool gone = component.IsDisposed || !ReferenceEquals(component.GameObject.Scene, this);
                if (gone) index.Members.Remove(component);
                return gone;
            });
            return index;
        }

        index = new TypeIndex();
        foreach (GameObject go in _allObj)
        {
            if (go.IsDisposed) continue;
            foreach (Component component in go._components)
                if (!component.IsDisposed && type.IsInstanceOfType(component))
                    index.Add(component);
        }
        _typeIndices[type] = index;
        return index;
    }

    /// <summary>The first enabled component of type <typeparamref name="T"/> in this world, or null.</summary>
    public T? Get<T>() where T : class
    {
        EnsureNotDisposed();
        foreach (Component component in IndexFor(typeof(T)).Items)
            if (component.EnabledInHierarchy)
                return component as T;
        return null;
    }

    /// <summary>Every component of type <typeparamref name="T"/> in this world, a class or an interface, as a snapshot.</summary>
    public T[] GetAll<T>(bool includeDisabled = false) where T : class
    {
        List<T> results = [];
        GetAll(results, includeDisabled);
        return [.. results];
    }

    /// <summary>Copies every component of type <typeparamref name="T"/> in this world into <paramref name="results"/>, cleared first.</summary>
    public void GetAll<T>(List<T> results, bool includeDisabled = false) where T : class
    {
        EnsureNotDisposed();
        results.Clear();
        foreach (Component component in IndexFor(typeof(T)).Items)
            if (includeDisabled || component.EnabledInHierarchy)
                results.Add((component as T)!);
    }

    /// <summary>The first object with this name, depth first through the roots.</summary>
    public GameObject? FindObject(string name)
    {
        EnsureNotDisposed();
        foreach (GameObject go in _allObj)
            if (!go.IsDisposed && go.Name == name)
                return go;
        return null;
    }

    /// <summary>The object at a path of names from a root, like "Level/Props/Door".</summary>
    public GameObject? FindObjectByPath(string path)
    {
        EnsureNotDisposed();
        string[] names = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0) return null;
        foreach (GameObject root in Roots)
        {
            if (root.Name != names[0]) continue;
            GameObject? found = root;
            for (int i = 1; i < names.Length && found.IsValid(); i++)
                found = found!.Children.FirstOrDefault(child => child.Name == names[i]);
            if (found.IsValid()) return found;
        }
        return null;
    }

    /// <summary>The first object with this tag.</summary>
    public GameObject? FindObjectWithTag(string tag)
    {
        EnsureNotDisposed();
        foreach (GameObject go in _allObj)
            if (!go.IsDisposed && go.Tag == tag)
                return go;
        return null;
    }

    /// <summary>Every object with this tag, copied into <paramref name="results"/>, cleared first.</summary>
    public void FindObjectsWithTag(string tag, List<GameObject> results)
    {
        EnsureNotDisposed();
        results.Clear();
        foreach (GameObject go in _allObj)
            if (!go.IsDisposed && go.Tag == tag)
                results.Add(go);
    }

    /// <summary>The object or component with this identifier, searching the sections in order, or null.</summary>
    public EngineObject? FindObject(Guid identifier)
    {
        EnsureNotDisposed();
        if (_identifiers.TryGetValue(identifier, out EngineObject? known) && IsHere(known, identifier, null)) return known;
        foreach (SceneSection section in _sections)
            if (FindObject(section, identifier) is { } found)
                return found;
        _identifiers.Remove(identifier);
        return null;
    }

    /// <summary>The object or component of <paramref name="section"/> with this identifier, or null. Exact even when another section holds the same identifier.</summary>
    public EngineObject? FindObject(SceneSection section, Guid identifier)
    {
        EnsureNotDisposed();
        if (_identifiers.TryGetValue(identifier, out EngineObject? known) && IsHere(known, identifier, section)) return known;
        foreach (GameObject root in section.LiveRoots)
            if (FindIn(root, identifier) is { } found)
                return _identifiers[identifier] = found;
        return null;

        static EngineObject? FindIn(GameObject go, Guid identifier)
        {
            if (go.Identifier == identifier) return go;
            foreach (Component component in go._components)
                if (!component.IsDisposed && component.Identifier == identifier)
                    return component;
            foreach (GameObject child in go.Children)
                if (!child.IsDisposed && FindIn(child, identifier) is { } found)
                    return found;
            return null;
        }
    }

    /// <summary>The object or component with this identifier when it is a <typeparamref name="T"/>, or null.</summary>
    public T? FindObject<T>(Guid identifier) where T : EngineObject => FindObject(identifier) as T;

    /// <summary>The object or component with this identifier, when it is a <typeparamref name="T"/>.</summary>
    public bool TryFindObject<T>(Guid identifier, out T result) where T : EngineObject
    {
        if (FindObject(identifier) is T found)
        {
            result = found;
            return true;
        }
        result = null!;
        return false;
    }

    private bool IsHere(EngineObject candidate, Guid identifier, SceneSection? section)
    {
        GameObject? go = candidate switch
        {
            GameObject g when g.Identifier == identifier => g,
            Component c when c.Identifier == identifier && !c.IsDisposed => c.GameObject,
            _ => null,
        };
        return go.IsValid() && ReferenceEquals(go!.Scene, this) && (section == null || ReferenceEquals(go.Section, section));
    }

    /// <summary>Creates an empty, dormant world. <see cref="Create"/> is the same and raises <see cref="Created"/>.</summary>
    public SceneInstance(SceneMode mode = SceneMode.Game)
    {
        Mode = mode;
        _sections.Add(new SceneSection(this, "Persistent", persistent: true));
        _activeSection = new SceneSection(this, "Untitled", persistent: false);
        _sections.Add(_activeSection);
        lock (s_live) s_live.Add(new WeakReference<SceneInstance>(this));
    }

    /// <summary>
    /// Enables this scene, triggering OnEnable callbacks for its components.
    /// For most use cases, prefer using Scene.Load() instead of calling Enable() directly.
    /// </summary>
    public void Activate()
    {
        EnsureNotDisposed();
        MainThreadContext.AssertMainThread();
        using MainThreadContext.SessionScope session = MainThreadContext.EnterSession();
        using SceneScope active = Enter(this);
        if (_state is SceneState.Destroying or SceneState.Destroyed)
            throw new InvalidOperationException($"The world '{Name}' is being destroyed and cannot be activated.");
        if (_isActive) return; // already enabled, nothing to deliver

        // Running first, so ShouldExecuteGameplay already sees a playing world in OnEnable.
        _state = SceneState.Running;
        _isActive = true;

        List<Component> enabling = [];
        foreach (GameObject go in Objects)
        {
            if (!go.EnabledInHierarchy) continue;
            foreach (Component component in go._components)
                if (!component.IsDisposed && component.EnabledInHierarchy)
                    enabling.Add(component);
        }
        Enable(enabling);
    }

    // Every component joins the registries before any OnEnable runs, so a script enabled first still finds the camera.
    private void Enable(List<Component> enabling)
    {
        foreach (Component component in enabling)
            component.JoinWorld(this);
        foreach (Component component in enabling)
            if (!component.IsDisposed && component.EnabledInHierarchy)
                component.InternalOnEnable();
    }

    /// <summary>
    /// Disables this scene, triggering OnDisable callbacks for its components.
    /// For most use cases, prefer using Scene.Unload() or Scene.Load() instead of calling Disable() directly.
    /// </summary>
    public void Deactivate()
    {
        EnsureNotDisposed();
        MainThreadContext.AssertOwner(this);
        using MainThreadContext.SessionScope session = MainThreadContext.EnterSession();
        using SceneScope active = Enter(this);
        if (!_isActive) return; // already disabled, nothing to deliver

        // Create a copy to avoid collection modification during enumeration
        List<GameObject> allObjectsCopy = [.. Objects];

        // Trigger OnDisable for all enabled components in the scene
        foreach (GameObject go in allObjectsCopy)
        {
            if (go.IsDisposed) continue;

            if (go.EnabledInHierarchy)
            {
                IEnumerable<Component> components = go.GetComponents<Component>();
                foreach (Component component in components)
                {
                    if (component.IsDisposed) continue;
                    if (component.Enabled && component.EnabledInHierarchy)
                        component.InternalOnDisable();
                }
            }
        }

        // Dormant last, so the gameplay gate still passes for OnDisable.
        _isActive = false;
        _state = SceneState.Dormant;
    }


    /// <summary>
    /// Registers a GameObject and all of its children.
    /// </summary>
    /// <summary>
    /// Adds a detached root, or one from another world, with its children. A root goes into <paramref name="section"/>,
    /// else its owner's section, else <see cref="ActiveSection"/>.
    /// </summary>
    internal void Add(GameObject obj, SceneSection? section = null)
    {
        EnsureNotDisposed();
        MainThreadContext.AssertOwner(this);
        MainThreadContext.AssertOwner(obj);
        if (_state == SceneState.Destroying)
            throw new InvalidOperationException($"'{Name}' is being destroyed and takes no new objects.");
        if (obj.Scene.IsValid() && obj.Scene != this) obj.Scene!.Remove(obj);
        if (obj.Parent.IsNotValid())
            JoinSection(obj, section ?? OwnerSection(obj) ?? ActiveSection);
        AddObject(obj);
    }

    // An owned helper root, like a ragdoll's puppet, follows the section of the component that owns it.
    private SceneSection? OwnerSection(GameObject root)
        => root._owner is { IsDisposed: false } owner && owner.GameObject.IsValid() && ReferenceEquals(owner.GameObject.Scene, this) ? owner.GameObject.Section : null;

    internal void JoinSection(GameObject root, SceneSection section, int index = -1)
    {
        if (!ReferenceEquals(section.Instance, this)) throw new InvalidOperationException($"'{section.Name}' is a section of another world.");
        root._section?.RootsList.Remove(root);
        root._section = section;
        if (index < 0 || index >= section.RootsList.Count) section.RootsList.Add(root);
        else section.RootsList.Insert(index, root);
    }

    internal static void LeaveSection(GameObject root)
    {
        root._section?.RootsList.Remove(root);
        root._section = null;
    }

    /// <summary>
    /// Makes a new live object. Under <paramref name="parent"/> it joins the parent's section, with
    /// <paramref name="owner"/> it joins the owner's section and follows it, otherwise <see cref="ActiveSection"/>.
    /// </summary>
    public GameObject Create(string name = "New GameObject", GameObject? parent = null, Component? owner = null)
    {
        EnsureNotDisposed();
        if (_state == SceneState.Destroying)
            throw new InvalidOperationException($"'{Name}' is being destroyed and takes no new objects.");
        var go = new GameObject(name);
        if (parent.IsValid())
        {
            if (!ReferenceEquals(parent!.Scene, this)) throw new InvalidOperationException($"'{parent.Name}' is not in '{Name}'.");
            go.SetParent(parent, worldPositionStays: false);
            return go;
        }
        go._owner = owner;
        Add(go);
        return go;
    }

    /// <summary>Makes a new live root object in <paramref name="section"/>.</summary>
    public GameObject Create(SceneSection section, string name = "New GameObject")
    {
        EnsureNotDisposed();
        if (section.IsPersistent && Mode != SceneMode.Game)
            throw new InvalidOperationException("Only a game world has persistent objects.");
        var go = new GameObject(name);
        Add(go, section);
        return go;
    }

    /// <summary>
    /// Unregisters a GameObject and all of its children.
    /// </summary>
    /// <remarks>
    /// Asking to remove something this scene does not hold is reported rather than ignored. It leaves the
    /// object ticking in whichever scene does hold it, which reads as the engine having ignored the call.
    /// </remarks>
    internal void Remove(GameObject obj)
    {
        EnsureNotDisposed();
        MainThreadContext.AssertOwner(this);

        if (object.ReferenceEquals(obj, null))
        {
            Debug.LogWarning("[Scene] Remove(null) does nothing.");
            return;
        }

        if (obj.IsNotValid())
        {
            Debug.LogWarning("[Scene] Cannot remove a Disposed or Null GameObject.");
            return;
        }

        if (obj.Scene != this)
        {
            Debug.LogWarning(obj.Scene.IsValid()
                ? $"[Scene] '{obj.Name}' belongs to another scene, so this one cannot remove it."
                : $"[Scene] '{obj.Name}' is not in any scene, so there is nothing to remove.");
            return;
        }

        if (obj.Parent.IsValid() && obj.Parent.Scene == this)
        {
            obj.SetParent(null);
        }
        RemoveObject(obj);
    }

    // The whole tree joins, with OnAddedToScene, before any of it enables.
    private void AddObject(GameObject obj)
    {
        using SceneScope active = Enter(this);
        List<Component> enabling = [];
        JoinTree(obj, enabling);
        if (_isActive) Enable(enabling);
    }

    private void JoinTree(GameObject obj, List<Component> enabling)
    {
        if (_allObjSet.Add(obj))
        {
            _allObj.Add(obj);
            obj.Scene = this;
            _identifiers[obj.Identifier] = obj;

            foreach (Component component in obj._components.ToArray())
            {
                if (component.IsDisposed) continue;
                ComponentJoined(component);
                try { component.OnAddedToScene(); }
                catch (Exception ex) { Debug.LogException($"[{obj.Name}/{component.GetType().Name}] OnAddedToScene() threw", ex); }
            }

            if (obj.EnabledInHierarchy)
                foreach (Component component in obj._components)
                    if (!component.IsDisposed && component.EnabledInHierarchy)
                        enabling.Add(component);
        }

        foreach (GameObject child in obj.Children.ToArray())
            JoinTree(child, enabling);
    }

    private void RemoveObject(GameObject obj)
    {
        using SceneScope active = Enter(this);
        // Create a copy to avoid modification during enumeration
        List<GameObject> children = [.. obj.Children];
        foreach (GameObject child in children)
            RemoveObject(child);

        if (_allObjSet.Remove(obj))
        {
            _allObj.Remove(obj);
            if (ReferenceEquals(obj._section?.Instance, this)) LeaveSection(obj);
            IEnumerable<Component> components = obj.GetComponents<Component>();

            // Call OnDisable for currently enabled components (only if scene is active)
            if (_isActive && obj.EnabledInHierarchy)
            {
                foreach (Component component in components)
                {
                    if (component.IsDisposed) continue;
                    if (component.Enabled && component.EnabledInHierarchy)
                        component.InternalOnDisable();
                }
            }

            // Call OnRemovedFromScene for all components
            foreach (Component component in components)
            {
                if (component.IsDisposed) continue;
                try { component.OnRemovedFromScene(); }
                catch (Exception ex) { Debug.LogException($"[{component.Name}/{component.GetType().Name}] OnRemovedFromScene() threw", ex); }
            }

            obj.Scene = null;
        }
    }

    /// <summary> Unregisters all GameObjects. </summary>
    internal void Clear()
    {
        EnsureNotDisposed();
        // Create a copy to iterate over since RemoveObject modifies the collection
        List<GameObject> rootObjects = [.. Roots];
        foreach (GameObject obj in rootObjects)
        {
            Remove(obj);
        }
    }

    /// <summary> Unregisters all dead / disposed GameObjects </summary>
    internal void Flush()
    {
        if (IsDisposed || !_hasDisposedObjects) return;
        _hasDisposedObjects = false;
        List<GameObject> removed = [];
        foreach (GameObject obj in _allObj)
        {
            if (obj.IsDisposed)
                removed.Add(obj);
        }

        _allObj.RemoveAll(obj => obj.IsDisposed);
        _allObjSet.RemoveWhere(obj => obj.IsDisposed);

        foreach (GameObject obj in removed)
        {
            LeaveSection(obj);
            obj.Scene = null;
        }
    }

    private protected override void AssertCanDispose() => MainThreadContext.AssertOwner(this, nameof(Dispose));

    private static void Guarded(EngineObject target, string step, Action action)
    {
        try { action(); }
        catch (Exception ex) { Debug.LogException($"[{target.Name}/{target.GetType().Name}] {step} threw while its scene was torn down", ex); }
    }

    protected override void OnDispose()
    {
        using SceneScope active = Enter(this);
        base.OnDispose();

        // Destroying still counts as playing, so every component gets OnDisable as it would in a running game.
        _state = SceneState.Destroying;
        CancelToken();
        Raise(Destroying, this, handler => handler(this), nameof(Destroying));

        if (ReferenceEquals(_main, this))
            _main = null;

        lock (s_live) s_live.RemoveAll(entry => !entry.TryGetTarget(out SceneInstance? scene) || ReferenceEquals(scene, this));


        CancelProbeRebuild();

        // Clear the physics world
        _physics.Clear();

        // Clear the navigation world (waits out in-flight queries)
        _navigation.Clear();
        _animation.Clear();

        // Three passes, every object guarded on its own, so one that throws leaves the rest torn down: everything is
        // disabled, then told it left the scene, then disposed. Dispose() is idempotent, so the flat list's
        // double hits on children already disposed with their parent are no ops.
        List<GameObject> allObjects = [.. _allObj.Where(o => !o.IsDisposed)];
        if (_isActive)
        {
            foreach (GameObject g in allObjects)
                foreach (Component c in g._components.ToArray())
                    if (!c.IsDisposed && c.HasBeenEnabled && c.EnabledInHierarchy)
                        Guarded(c, "OnDisable", c.InternalOnDisable);
            _isActive = false;
        }
        foreach (GameObject g in allObjects)
            foreach (Component c in g._components.ToArray())
                if (!c.IsDisposed) Guarded(c, "OnRemovedFromScene", c.OnRemovedFromScene);
        foreach (GameObject g in allObjects)
            if (!g.IsDisposed) Guarded(g, "Dispose", g.Dispose);

        // Clear any remaining references
        _allObj.Clear();
        _allObjSet.Clear();
        ClearRegistries();

        _rendering?.Dispose();
        _rendering = null;

        _state = SceneState.Destroyed;
    }


    void ISerializable.Serialize(ref EchoObject compound, SerializationContext ctx)
        => throw new NotSupportedException("A live world is never serialized. Capture() gives its content.");

    void ISerializable.Deserialize(EchoObject value, SerializationContext ctx)
        => throw new NotSupportedException("A live world is never deserialized. Read a SceneContent and Instantiate it.");

    /// <summary>
    /// Runs Start, then Update, then LateUpdate for the scene's registered components (those that
    /// implement those callbacks and are enabled in an active scene), in execution order. Each call
    /// is still gated per-component by ShouldExecuteGameplay.
    /// </summary>
    internal void Update()
    {
        if (IsDisposed) return;
        using FrameStatScope stats = FrameStats.Update.Scope();
        using MainThreadContext.SessionScope session = MainThreadContext.EnterSession();
        using SceneScope active = Enter(this);

        _dispatcher.RunStart();

        // Navigation (crowd steering) advances on the variable update, before component Updates
        // so gameplay code sees fresh agent state. A crowd that blows up must not crash the frame,
        // and does so every frame, so it is reported once.
        using (FrameStats.Navigation.Scope())
        {
            try { _navigation.Update(Time.DeltaTime, steer: IsPlaying); }
            catch (Exception ex) { Debug.LogErrorOnce("Navigation.UpdateThrew", $"[Navigation] Update threw and was skipped this frame: {ex.Message}\n{ex.StackTrace}"); }
        }

        // Rigidbodies that moved get their Transform written before gameplay code reads it.
        Physics.WriteTransforms();

        _dispatcher.RunUpdate();

        // Animation sits between the two so gameplay code reaching in to override a bone in
        // LateUpdate finds the pose already written. An animator that blows up must not take the
        // frame with it, and the manager reports each one rather than rethrowing.
        // Animation is gameplay, so an edit world shows the pose it was left in.
        if (IsPlaying)
            using (FrameStats.Animation.Scope())
                _animation.Update(Time.DeltaTime);

        _dispatcher.RunLateUpdate();

        Flush();
    }

    /// <summary>
    /// Executes physics update on all active GameObjects and their components.
    /// FixedUpdate is gated internally by each component's ShouldExecuteGameplay.
    /// </summary>
    internal void FixedUpdate()
    {
        if (IsDisposed) return;
        using FrameStatScope stats = FrameStats.Update.Scope();
        using MainThreadContext.SessionScope session = MainThreadContext.EnterSession();
        using SceneScope active = Enter(this);
        // Start must run before a component's first FixedUpdate. The loop runs FixedUpdate before
        // Update, so drive Start here too (RunStart is idempotent - it only starts un-started ones).
        _dispatcher.RunStart();

        _dispatcher.RunFixedUpdate();

        Physics.TrackCollisions(_dispatcher.CollisionListeners > 0);
        // A solver blow up (NaN or Inf transforms, degenerate collider) must not crash the frame.
        using (FrameStats.Physics.Scope())
        {
            try { Physics.Update(); }
            // A solver that blows up does so every frame, so report it once rather than per frame.
            catch (Exception ex) { Debug.LogErrorOnce("Physics.StepThrew", $"[Physics] Step threw and was skipped this frame: {ex.Message}\n{ex.StackTrace}"); }
        }

        Flush();
    }

    /// <summary>
    /// Collects render data from all active components for the given camera.
    /// Components add their renderables and lights to the provided lists.
    /// </summary>
    internal void DrawGizmos()
    {
        if (IsDisposed) return;
        using MainThreadContext.SessionScope session = MainThreadContext.EnterSession();
        using SceneScope active = Enter(this);
        _dispatcher.RunDrawGizmos();

        Flush();
    }

    /// <summary>
    /// Executes GUI update on all active GameObjects and their components.
    /// Calls OnGUI.
    /// </summary>
    internal void OnGui(Paper paper)
    {
        if (IsDisposed) return;
        using MainThreadContext.SessionScope session = MainThreadContext.EnterSession();
        using SceneScope active = Enter(this);
        _dispatcher.RunOnGui(paper);

        Flush();
    }

    /// <summary>
    /// Collects every Camera on an enabled-in-hierarchy GameObject, sorted by Camera.Depth.
    /// </summary>
    /// <remarks>
    /// Cameras on <see cref="HideFlags.HideAndDontSave"/> objects are skipped. Those are editor helpers
    /// (scene view, previews) that render themselves into their own targets; letting them into the
    /// game's camera list draws the scene again, and uses shadow atlas space, for nothing.
    /// </remarks>
    internal List<Camera> GatherActiveCameras()
    {
        var cameras = new List<Camera>();

        foreach (Camera camera in Cameras)
        {
            if (!camera.EnabledInHierarchy) continue;
            if ((camera.GameObject.HideFlags & HideFlags.HideAndDontSave) != 0) continue; // not HasFlag: it boxes in unoptimized builds
            cameras.Add(camera);
        }

        cameras.Sort(static (a, b) => a.Depth.CompareTo(b.Depth));
        return cameras;
    }

    /// <summary>
    /// The enabled camera drawing to the screen with the highest depth, or null: no render texture target, a background
    /// other than None, not an editor helper. Found on every call, so a camera added this frame is main at once.
    /// </summary>
    public Camera? MainCamera => FindMainCamera();

    /// <summary>The lines and shapes <see cref="Debug"/> draw calls made in this scene this frame.</summary>
    internal GizmoBuilder Gizmos { get; } = new();

    private Camera? FindMainCamera()
    {
        Camera? main = null;
        foreach (Camera camera in Cameras)
        {
            if (!camera.EnabledInHierarchy) continue;
            if ((camera.GameObject.HideFlags & HideFlags.HideAndDontSave) != 0) continue;

            if (camera.Target.IsNotValid() && camera.Background != CameraBackground.None && (main.IsNotValid() || camera.Depth > main.Depth))
                main = camera;
        }
        return main;
    }

    /// <summary>
    /// Adds a view to this frame for every active camera of the scene, lowest depth first. Cameras without a target of
    /// their own draw into <paramref name="target"/>, or the window when it is null.
    /// </summary>
    /// <param name="target">Where cameras without a target of their own draw, null for the window.</param>
    /// <param name="displayGizmos">Draw the gizmos and Debug.Draw shapes queued this frame on top</param>
    /// <returns>True if any cameras were rendered, false otherwise</returns>
    public bool Render(RenderTexture? target = null, bool displayGizmos = false)
    {
        if (IsDisposed) return false;

        List<Camera> cameras = GatherActiveCameras();
        if (cameras.Count == 0)
            return false;

        Rendering.Sync();
        foreach (Camera cam in cameras)
        {
            try
            {
                cam.Render(new CameraRenderOptions { FallbackTarget = target, DisplayGizmos = displayGizmos });
            }
            catch (Exception ex)
            {
                Debug.LogException($"[Render] Camera '{(cam.GameObject.IsValid() ? cam.GameObject.Name : null)}' render threw and was skipped", ex);
            }
        }

        return true;
    }

}

/// <summary>
/// The content of one <see cref="Scene"/> loaded into a <see cref="SceneInstance"/>: its roots, with their children, and
/// the settings it was loaded with. Unloading it removes exactly the objects it brought.
/// </summary>
public sealed class SceneSection
{
    private readonly CancellationTokenSource _unload = new();

    internal SceneSection(SceneInstance instance, string name, bool persistent)
    {
        Instance = instance;
        Name = name;
        IsPersistent = persistent;
    }

    /// <summary>The world this section is in.</summary>
    public SceneInstance Instance { get; }

    /// <summary>The scene this section was loaded from, null for one made at runtime.</summary>
    public Scene? Source { get; internal set; }

    /// <summary>Editor state, never saved: the section's objects are not drawn or picked in scene views. Play and game views are unaffected.</summary>
    public bool HiddenInSceneView { get; set; }

    public string Name { get; set; }

    /// <summary>Whether this is the world's persistent section, which survives every load.</summary>
    public bool IsPersistent { get; }

    /// <summary>Whether this is the world's <see cref="SceneInstance.ActiveSection"/>.</summary>
    public bool IsActive => ReferenceEquals(Instance.ActiveSection, this);

    /// <summary>Whether this section is still in its world.</summary>
    public bool IsLoaded { get; private set; } = true;

    internal readonly List<GameObject> RootsList = [];

    /// <summary>This section's root objects in sibling order.</summary>
    public IReadOnlyList<GameObject> Roots => [.. LiveRoots];

    internal IEnumerable<GameObject> LiveRoots
    {
        get
        {
            foreach (GameObject root in RootsList)
                if (!root.IsDisposed)
                    yield return root;
        }
    }

    private SceneEnvironment? _environment;

    /// <summary>The sky, ambient light and fog this section was loaded with. The world shows the active section's.</summary>
    public SceneEnvironment Environment
    {
        get => _environment ??= new SceneEnvironment();
        internal set => _environment = value;
    }

    /// <summary>This section's baked lightmaps and probes.</summary>
    public SceneLighting Lighting { get; internal set; } = new();

    /// <summary>The lightmapper settings this section was saved with. Editor data, cleared in player builds.</summary>
    public LightmapBakeSettings LightmapBake { get; internal set; } = new();

    /// <summary>Cancelled when this section unloads, for awaits that must not outlive the level.</summary>
    public CancellationToken UnloadToken => _unload.Token;

    /// <summary>
    /// Unloads this section at the next safe point, in the same queue as loads. The persistent section and the last
    /// other section of a world cannot be unloaded: Load is how a world swaps its only level.
    /// </summary>
    public void Unload()
    {
        MainThreadContext.AssertMainThread();
        if (IsPersistent) throw new InvalidOperationException("The persistent section never unloads.");
        if (!IsLoaded || SceneInstance.IsUnloadQueued(this)) return;
        int remaining = Instance.Sections.Count(s => !s.IsPersistent) - SceneInstance.QueuedUnloads(Instance);
        if (remaining <= 1) throw new InvalidOperationException($"'{Name}' is the last section of '{Instance.Name}'. Load another scene into it instead.");
        SceneInstance.Queue(this);
    }

    internal void MarkUnloaded()
    {
        IsLoaded = false;
        try { _unload.Cancel(); }
        catch (Exception e) { Debug.LogException($"[Scene] A cancellation callback of '{Name}' threw", e); }
    }

    /// <summary>Takes in detached content: its settings, and its objects as this section's.</summary>
    internal void Fill(SceneContent content, bool additive = false)
    {
        Environment = content.Environment;
        Lighting = content.Lighting;
        LightmapBake = content.LightmapBake;
        foreach (GameObject obj in content.Objects)
            if (obj.IsValid() && obj.Parent.IsNotValid())
                Instance.Add(obj, this);
        Instance.RebuildProbes(keepPrevious: additive);
    }

    /// <summary>
    /// This section's saveable objects and settings as stored content, read back as a detached copy with the same
    /// identifiers. A reference to anything outside this section saves as null. The persistent section is never saved.
    /// </summary>
    public SceneContent Capture() => SceneContent.Read(CaptureTree());

    /// <summary>What <see cref="Capture"/> reads back, written as a .scene file holds it.</summary>
    internal EchoObject CaptureTree()
    {
        if (IsPersistent) throw new InvalidOperationException("The persistent section is never saved.");
        List<GameObject> objects = [];
        foreach (GameObject root in LiveRoots)
            Collect(root);
        var content = new SceneContent { Objects = [.. objects], Environment = Environment, Lighting = Lighting, LightmapBake = LightmapBake };
        return Serializer.Serialize(typeof(object), content, new SerializationContext { ExternalReferences = SceneReferenceResolver.ForCapture(content.Objects) });

        void Collect(GameObject go)
        {
            if ((go.HideFlags & (HideFlags.DontSave | HideFlags.HideAndDontSave)) != 0) return;
            objects.Add(go);
            foreach (GameObject child in go.Children)
                if (!child.IsDisposed)
                    Collect(child);
        }
    }

    public override string ToString() => Name;
}
