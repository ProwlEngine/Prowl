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

    internal static IReadOnlyList<GameObject> Preserved => _preserved;

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
        Raise(Created, world, handler => handler(world), nameof(Created));
        return world;
    }

    /// <summary>Fires after a scene is loaded via Load(), inside that scene, so its handlers see it as <see cref="Active"/>.</summary>
    public static event Action? OnSceneLoaded;

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

    private static SceneInstance? _pendingScene;

    /// <summary>
    /// Ends the async session at the next scene swap, between the outgoing scene's teardown and the incoming
    /// scene's start. The editor sets it when entering and leaving play mode.
    /// </summary>
    internal static bool EndSessionOnSwap;

    /// <summary>Whether a <see cref="Load(SceneInstance)"/> is queued and has not been applied yet, so <see cref="Main"/>
    /// is still the outgoing scene.</summary>
    public static bool IsLoadPending => !ReferenceEquals(_pendingScene, null);

    /// <summary>
    /// Queues a scene to become the current one, replacing the previously loaded scene. The swap
    /// happens at the end of the frame, alongside the destroy queue, so the outgoing scene stays
    /// usable for everything still running this frame.
    /// </summary>
    public static void Load(SceneInstance scene)
    {
        MainThreadContext.AssertMainThread();
        if (ReferenceEquals(scene, null))
            throw new ArgumentNullException(nameof(scene));

        _pendingLoad?.Cancel();
        _pendingLoad = null;
        DropPendingScene(except: scene);
        _pendingScene = scene;
    }

    // A scene queued and then replaced before it became current belongs to nobody, and would stay live.
    private static void DropPendingScene(SceneInstance? except)
    {
        SceneInstance? dropped = _pendingScene;
        _pendingScene = null;
        if (dropped.IsValid() && !ReferenceEquals(dropped, except) && !ReferenceEquals(dropped, _main))
            dropped!.Dispose();
    }

    /// <summary>Loads a stored scene and everything it uses, blocking, and makes it current at the end of the frame.</summary>
    public static void Load(Scene asset)
    {
        using AssetGroup preload = AssetDatabase.Preload(asset);
        preload.Wait();
        Load(Instantiate(asset));
    }

    /// <summary>
    /// Loads a stored scene and everything it uses in the background while the current scene keeps running, and
    /// makes it current at the end of the frame the loading finishes on. A second load cancels this one.
    /// </summary>
    public static SceneLoad LoadAsync(Scene asset)
    {
        MainThreadContext.AssertMainThread();
        _pendingLoad?.Cancel();
        DropPendingScene(except: null);
        _pendingLoad = new SceneLoad(asset);
        return _pendingLoad;
    }

    private static SceneLoad? _pendingLoad;

    /// <summary>Builds a live scene from a stored one, without making it current.</summary>
    public static SceneInstance Instantiate(Scene asset, SceneMode mode = SceneMode.Game)
    {
        asset.Load();
        SceneInstance scene = Instantiate(asset.ReadContent(), mode);
        scene.Name = asset.Name;
        scene.Source = asset;
        return scene;
    }

    /// <summary>Builds a live world holding detached content, without making it current. The content's objects move into it.</summary>
    public static SceneInstance Instantiate(SceneContent content, SceneMode mode = SceneMode.Game)
    {
        var scene = new SceneInstance(mode)
        {
            Environment = content.Environment,
            Lighting = content.Lighting,
            LightmapBake = content.LightmapBake,
        };
        foreach (GameObject obj in content.Objects)
            if (obj.IsValid()) scene.Add(obj);
        return scene;
    }

    /// <summary>The stored scene this one was instantiated from, if any. Not held, since a live scene no longer needs its stored form.</summary>
    [NonSerialized, NotHeld] public Scene? Source;

    private static readonly List<GameObject> _preserved = [];

    /// <summary>
    /// Keeps a GameObject alive across scene loads. It moves straight from the outgoing scene to the
    /// incoming one when the swap applies, so it is never held by a scene that is about to be
    /// disposed, and it is not disabled and re-enabled on the way.
    /// <para/>
    /// Only roots can be preserved, since half a hierarchy surviving a load is never what was meant.
    /// Passing a child preserves its root instead.
    /// </summary>
    public static void DontDestroyOnLoad(GameObject go)
    {
        MainThreadContext.AssertMainThread();
        if (go.IsNotValid())
        {
            Debug.LogWarning("[Scene] DontDestroyOnLoad on a null or destroyed GameObject does nothing.");
            return;
        }

        GameObject root = go;
        while (root.Parent.IsValid())
            root = root.Parent;

        if (!ReferenceEquals(root, go))
            Debug.LogWarning($"[Scene] '{go.Name}' is not a root object, so its root '{root.Name}' is preserved instead.");

        if (!_preserved.Any(p => ReferenceEquals(p, root)))
            _preserved.Add(root);
    }

    /// <summary>
    /// Stops preserving a GameObject. It stays in whatever scene it is in now, and goes with that
    /// scene on the next load.
    /// </summary>
    public static void CancelDontDestroyOnLoad(GameObject go)
    {
        MainThreadContext.AssertMainThread();
        _preserved.RemoveAll(p => ReferenceEquals(p, go));
    }

    /// <summary>
    /// Destroys everything <see cref="DontDestroyOnLoad"/> is holding and empties the registry.
    /// <para/>
    /// Teardown is queued like any other <see cref="EngineObject.Destroy"/>, so it lands at the end of
    /// the frame, which is before the scene swap and therefore before anything could be carried over.
    /// Pass <paramref name="immediate"/> when no further frame will run, since nothing would be left
    /// to drain the queue.
    /// </summary>
    internal static void DestroyPreserved(bool immediate = false)
    {
        foreach (GameObject go in _preserved)
        {
            if (go.IsNotValid()) continue;

            if (immediate)
                go.Dispose();
            else
                go.Destroy();
        }

        _preserved.Clear();
    }

    /// <summary>Whether this GameObject (or the root it belongs to) survives scene loads.</summary>
    public static bool IsPreserved(GameObject go)
    {
        if (go.IsNotValid()) return false;

        GameObject root = go;
        while (root.Parent.IsValid())
            root = root.Parent;

        return _preserved.Any(p => ReferenceEquals(p, root));
    }

    /// <summary>
    /// Applies a queued <see cref="Load(SceneInstance)"/>. Driven once per frame by the game loop, right after the
    /// destroy queue. Nothing is mid-callback at that point, so the outgoing scene is disposed
    /// outright rather than queued for another frame.
    /// </summary>
    internal static void ProcessPendingLoad()
    {
        if (_pendingLoad is { IsReady: true } ready)
        {
            _pendingLoad = null;
            _pendingScene = ready.Activate();
        }
        if (ReferenceEquals(_pendingScene, null)) return;

        SceneInstance next = _pendingScene;
        _pendingScene = null;

        bool endSession = EndSessionOnSwap;
        EndSessionOnSwap = false;

        if (next._state is SceneState.Destroying or SceneState.Destroyed)
        {
            Debug.LogWarning("[Scene] The scene queued for loading was disposed before the frame ended, so it was skipped.");
            if (endSession) MainThreadContext.Restart();
            return;
        }

        // Loading the scene that is already current would dispose it and then enable the corpse.
        if (ReferenceEquals(next, _main))
        {
            if (endSession) MainThreadContext.Restart();
            return;
        }

        // Preserved objects leave before the outgoing scene is disposed, and join the incoming one
        // after it is enabled, so they are never registered with a scene that is being torn down.
        _preserved.RemoveAll(p => p.IsNotValid());
        foreach (GameObject go in _preserved)
            if (ReferenceEquals(go.Scene, _main))
                _main!.Detach(go);

        // A failure in either step below is reported and the swap carries on, so the incoming scene always
        // becomes current and the session always ends when it was asked to, rather than leaving both half done.

        // Everything the incoming scene uses is loaded before any of it enables, so OnEnable never sees an asset still loading.
        try { AssetDatabase.LoadEverythingReached(); }
        catch (Exception e) { Debug.LogException($"[Scene] Loading what '{next.Name}' uses threw", e); }

        SceneInstance? outgoing = _main;
        if (!ReferenceEquals(outgoing, null) && !outgoing.IsDisposed)
        {
            try { outgoing.Dispose(); }
            catch (Exception e) { Debug.LogException($"[Scene] Tearing down '{outgoing.Name}' threw", e); }
        }

        // Between the two scenes, so the outgoing one's teardown belongs to its own session and the incoming
        // one starts in a fresh one.
        if (endSession) MainThreadContext.Restart();

        // The game's output follows Main across the swap.
        if (!ReferenceEquals(outgoing, null))
        {
            next.Present |= outgoing.Present;
            next.RenderOrder = outgoing.RenderOrder;
        }

        Main = next;
        next.Activate();

        foreach (GameObject go in _preserved)
            _main.Attach(go);

        // Assets only the outgoing scene used are unreached from here, and go once the grace period is over.
        AssetDatabase.Walk();

        using (_main.Enter())
            OnSceneLoaded?.Invoke();
    }

    /// <summary>
    /// Disposes the current scene, so everything in it runs its teardown callbacks. Driven by the
    /// game loop on the way out. Reading <see cref="Main"/> afterwards creates a fresh empty scene.
    /// </summary>
    internal static void Shutdown()
    {
        _pendingScene = null;
        _pendingLoad?.Cancel();
        _pendingLoad = null;

        DestroyPreserved(immediate: true);

        SceneInstance? main = _main;
        _main = null;
        if (!ReferenceEquals(main, null) && !main.IsDisposed)
            main.Dispose();
    }

    #endregion

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

    /// <summary>The cameras in this scene, enabled or not, so finding them each frame doesn't walk every object.</summary>
    [SerializeIgnore, NotHeld]
    internal readonly List<Camera> Cameras = [];

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
        foreach (GameObject go in _allObj)
            if (!ReferenceEquals(go, null) && !go.IsDisposed)
                go.OnHotReload();

        // Re-registering from the live scene rather than from what was registered before is what lets a
        // component whose new type gained its first per-frame callback start dispatching at all.
        _dispatcher.Reset();
        Canvases.Clear();
        Cameras.Clear();

        foreach (GameObject go in _allObj)
        {
            if (ReferenceEquals(go, null) || go.IsDisposed) continue;

            foreach (Component comp in go._components)
            {
                if (ReferenceEquals(comp, null) || comp.IsDisposed) continue;
                if (comp is Camera camera) camera.JoinScene(this);
                if (!comp.EnabledInHierarchy) continue;
                comp._countedCollisionListener = false;
                _dispatcher.Register(comp);
                if (comp is GameCanvas canvas) canvas.RegisterIn(this);
            }
        }
    }

    [SerializeIgnore]
    private bool _isActive = false;

    /// <summary>The scene's sky, ambient light and fog.</summary>
    public SceneEnvironment Environment = new();

    public SceneLighting Lighting = new();

    public LightmapBakeSettings LightmapBake = new();

    [SerializeField, NotHeld]
    [NonSerialized] private LightProbeVolume? _probeVolume;

    /// <summary>Runtime probe sampler built from <see cref="Lighting"/> (lazy). Null when there are no baked probes.</summary>
    public LightProbeVolume? ProbeVolume
    {
        get
        {
            EnsureNotDisposed();
            if (_probeVolume == null && Lighting.HasProbes)
                _probeVolume = new LightProbeVolume(Lighting.ProbePositions, Lighting.ProbeSH,
                                                    Lighting.ProbeTetrahedra, Lighting.ProbeTetNeighbours);
            return _probeVolume;
        }
    }

    /// <summary>Drop the cached probe volume so the next access rebuilds it (call after a rebake).</summary>
    public void InvalidateProbeVolume() { EnsureNotDisposed(); _probeVolume = null; BakedLightingVersion++; }

    /// <summary>Moves whenever the baked lighting changes, so the render world takes the new probes.</summary>
    [SerializeIgnore, NonSerialized]
    internal int BakedLightingVersion;

    /// <summary> The number of registered, non-disposed objects. </summary>
    public int Count { get { EnsureNotDisposed(); return _allObj.Count(o => !o.IsDisposed); } }

    /// <summary> Enumerates all registered objects. </summary>
    public IEnumerable<GameObject> AllObjects { get { EnsureNotDisposed(); return _allObj.Where(o => !o.IsDisposed); } }

    /// <summary> Enumerates all registered objects that are currently active and saveable. </summary>
    public IEnumerable<GameObject> SaveableObjects { get { EnsureNotDisposed(); return _allObj.Where(o => !o.IsDisposed && IsSaveable(o)); } }

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

    /// <summary> Enumerates all registered objects that are currently active. </summary>
    public IEnumerable<GameObject> ActiveObjects { get { EnsureNotDisposed(); return _allObj.Where(o => !o.IsDisposed && o.EnabledInHierarchy); } }

    /// <summary> Enumerates all root GameObjects, i.e. all GameObjects without a parent object. </summary>
    public IEnumerable<GameObject> RootObjects { get { EnsureNotDisposed(); return _allObj.Where(o => !o.IsDisposed && o.Transform.Parent == null); } }

    /// <summary> Enumerates all <see cref="RootObjects"/> that are currently active. </summary>
    public IEnumerable<GameObject> ActiveRootObjects { get { EnsureNotDisposed(); return _allObj.Where(o => !o.IsDisposed && o.Transform.Parent == null && o.EnabledInHierarchy); } }

    /// <summary> Returns whether this Scene is completely empty. </summary>
    public bool IsEmpty { get { EnsureNotDisposed(); return !AllObjects.Any(); } }

    /// <summary>Creates an empty, dormant world. <see cref="Create"/> is the same and raises <see cref="Created"/>.</summary>
    public SceneInstance(SceneMode mode = SceneMode.Game)
    {
        Mode = mode;
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

        // Create a copy to avoid collection modification during enumeration
        List<GameObject> allObjectsCopy = [.. AllObjects];

        // Trigger OnEnable for all enabled components in the scene
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
                        component.InternalOnEnable();
                }
            }
        }
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
        List<GameObject> allObjectsCopy = [.. AllObjects];

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
    public void Add(GameObject obj)
    {
        EnsureNotDisposed();
        MainThreadContext.AssertOwner(this);
        MainThreadContext.AssertOwner(obj);
        if (obj.Scene.IsValid() && obj.Scene != this) obj.Scene.Remove(obj);
        AddObject(obj);
    }

    /// <summary>
    /// Move a root-level GameObject to a specific index within the root object list.
    /// Only works for root objects (no parent). Index is clamped to valid range.
    /// </summary>
    public void SetRootIndex(GameObject obj, int index)
    {
        EnsureNotDisposed();
        MainThreadContext.AssertOwner(this);
        if (obj.Scene != this || obj.Parent.IsValid()) return;
        int current = _allObj.IndexOf(obj);
        if (current < 0) return;
        _allObj.RemoveAt(current);
        // Collect root indices to map root-order index to list index
        var rootIndices = new List<int>();
        for (int i = 0; i < _allObj.Count; i++)
            if (!_allObj[i].IsDisposed && _allObj[i].Transform.Parent == null)
                rootIndices.Add(i);
        index = Math.Max(0, Math.Min(index, rootIndices.Count));
        int insertAt = index < rootIndices.Count ? rootIndices[index] : _allObj.Count;
        _allObj.Insert(insertAt, obj);
    }

    /// <summary>
    /// Get the index of a root-level GameObject among other root objects.
    /// Returns -1 if not a root or not in this scene.
    /// </summary>
    public int GetRootIndex(GameObject obj)
    {
        EnsureNotDisposed();
        if (obj.Scene != this || obj.Parent.IsValid()) return -1;
        int rootIdx = 0;
        foreach (GameObject go in _allObj)
        {
            if (go.IsDisposed || go.Transform.Parent != null) continue;
            if (go == obj) return rootIdx;
            rootIdx++;
        }
        return -1;
    }

    /// <summary>
    /// Unregisters a GameObject and all of its children.
    /// </summary>
    /// <remarks>
    /// Asking to remove something this scene does not hold is reported rather than ignored. It leaves the
    /// object ticking in whichever scene does hold it, which reads as the engine having ignored the call.
    /// </remarks>
    public void Remove(GameObject obj)
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

    /// <summary>
    /// Hands a GameObject tree over to another scene without running any lifecycle callback. Only the
    /// registration moves: the scene's object list and, for each enabled component, the per-frame
    /// dispatch slot. Used by <see cref="DontDestroyOnLoad"/>, where the object is not entering or
    /// leaving the world, just changing which scene holds it.
    /// </summary>
    internal void Detach(GameObject obj)
    {
        foreach (GameObject child in obj.Children.ToArray())
            Detach(child);

        if (!_allObjSet.Remove(obj)) return;

        _allObj.Remove(obj);

        foreach (Component component in obj._components)
        {
            if (component.IsDisposed) continue;
            _dispatcher.Unregister(component);
            component.LeaveScene(this);
        }

        obj.Scene = null;
    }

    /// <inheritdoc cref="Detach"/>
    internal void Attach(GameObject obj)
    {
        if (_allObjSet.Add(obj))
        {
            _allObj.Add(obj);
            obj.Scene = this;

            if (_isActive && obj.EnabledInHierarchy)
                foreach (Component component in obj._components)
                {
                    if (component.IsDisposed || !component.Enabled || !component.EnabledInHierarchy) continue;
                    _dispatcher.Register(component);
                    component.JoinScene(this);
                }
        }

        foreach (GameObject child in obj.Children.ToArray())
            Attach(child);
    }

    private void AddObject(GameObject obj)
    {
        using SceneScope active = Enter(this);
        if (_allObjSet.Add(obj))
        {
            _allObj.Add(obj);
            obj.Scene = this;

            IEnumerable<Component> components = obj.GetComponents<Component>();

            // Call OnAddedToScene for all components
            foreach (Component component in components)
            {
                if (component.IsDisposed) continue;
                try { component.OnAddedToScene(); }
                catch (Exception ex) { Debug.LogException($"[{obj.Name}/{component.GetType().Name}] OnAddedToScene() threw", ex); }
            }

            // Call OnEnable for enabled components, but only if the scene is active
            if (_isActive && obj.EnabledInHierarchy)
            {
                foreach (Component component in components)
                {
                    if (component.IsDisposed) continue;
                    if (component.Enabled && component.EnabledInHierarchy)
                        component.InternalOnEnable();
                }
            }
        }

        // Create a copy to avoid modification during enumeration
        List<GameObject> children = [.. obj.Children];
        foreach (GameObject child in children)
            AddObject(child);
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

    public T?[] FindObjectsOfType<T>() where T : EngineObject
    {
        EnsureNotDisposed();
        List<T> objects = [];
        foreach (GameObject go in AllObjects)
        {
            if (go is T t)
                objects.Add(t);

            foreach (Component comp in go.GetComponents<Component>())
                if (comp is T t2)
                    objects.Add(t2);
        }
        return [.. objects];
    }

    public T? FindObjectByID<T>(int id) where T : EngineObject
    {
        EnsureNotDisposed();
        foreach (GameObject go in AllObjects)
        {
            if (go.InstanceID == id)
                return go as T;
            foreach (Component comp in go.GetComponents<Component>())
                if (comp.InstanceID == id)
                    return comp as T;
        }
        return null;
    }

    public T? FindObjectByIdentifier<T>(Guid identifier) where T : EngineObject
    {
        EnsureNotDisposed();
        foreach (GameObject go in AllObjects)
        {
            if (go.Identifier == identifier)
                return go as T;
            foreach (Component comp in go.GetComponents<Component>())
                if (comp.Identifier == identifier)
                    return comp as T;
        }
        return null;
    }

    /// <summary> Unregisters all GameObjects. </summary>
    public void Clear()
    {
        EnsureNotDisposed();
        // Create a copy to iterate over since RemoveObject modifies the collection
        List<GameObject> rootObjects = [.. RootObjects];
        foreach (GameObject obj in rootObjects)
        {
            Remove(obj);
        }
    }

    /// <summary> Unregisters all dead / disposed GameObjects </summary>
    public void Flush()
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
            obj.Scene = null;
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
        Raise(Destroying, this, handler => handler(this), nameof(Destroying));

        if (ReferenceEquals(_main, this))
            _main = null;

        lock (s_live) s_live.RemoveAll(entry => !entry.TryGetTarget(out SceneInstance? scene) || ReferenceEquals(scene, this));


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
        Cameras.Clear();

        _rendering?.Dispose();
        _rendering = null;

        _state = SceneState.Destroyed;
    }

    /// <summary>
    /// The world's saveable objects and settings as stored content, read back as a detached copy with the same
    /// identifiers. A reference to an object outside this world saves as null.
    /// </summary>
    public SceneContent Capture() => SceneContent.Read(CaptureTree());

    /// <summary>What <see cref="Capture"/> reads back, written as a .scene file holds it.</summary>
    internal EchoObject CaptureTree()
    {
        var content = new SceneContent { Objects = [.. SaveableObjects], Environment = Environment, Lighting = Lighting, LightmapBake = LightmapBake };
        return Serializer.Serialize(typeof(object), content, new SerializationContext { ExternalReferences = SceneReferenceResolver.ForCapture(content.Objects) });
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
