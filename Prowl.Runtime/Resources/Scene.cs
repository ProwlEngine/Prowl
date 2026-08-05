// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.PaperUI;
using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;

[CreateAssetMenu("Scene", Extension = ".scene", Order = 0)]
public class Scene : EngineObject, ISerializationCallbackReceiver
{
    #region Scene Manager

    private static Scene? _current;

    /// <summary>
    /// The currently active scene. There is always one: reading this before anything has been loaded
    /// creates an empty scene, so the engine is never in a no-scene state and callers never have to
    /// handle null. Use <see cref="Load"/> to replace it.
    /// </summary>
    public static Scene Current
    {
        get
        {
            if (_current is null || _current.IsDisposed)
            {
                _current = new Scene { Name = "Untitled" };
                _current.Enable();
            }
            return _current;
        }
    }

    /// <summary>Fires after a scene is loaded via Load().</summary>
    public static event Action? OnSceneLoaded;

    private static Scene? _pendingScene;

    /// <summary>
    /// Queues a scene to become the current one, replacing the previously loaded scene. The swap
    /// happens at the end of the frame, alongside the destroy queue, so the outgoing scene stays
    /// usable for everything still running this frame.
    /// </summary>
    public static void Load(Scene scene)
    {
        if (scene == null)
            throw new ArgumentNullException(nameof(scene));

        _pendingScene = scene;
    }

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
        => _preserved.RemoveAll(p => ReferenceEquals(p, go));

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
    /// Applies a queued <see cref="Load"/>. Driven once per frame by the game loop, right after the
    /// destroy queue. Nothing is mid-callback at that point, so the outgoing scene is disposed
    /// outright rather than queued for another frame.
    /// </summary>
    public static void ProcessPendingLoad()
    {
        if (_pendingScene is null) return;

        Scene next = _pendingScene;
        _pendingScene = null;

        if (next.IsDisposed)
        {
            Debug.LogWarning("[Scene] The scene queued for loading was disposed before the frame ended, so it was skipped.");
            return;
        }

        // Loading the scene that is already current would dispose it and then enable the corpse.
        if (ReferenceEquals(next, _current)) return;

        // Preserved objects leave before the outgoing scene is disposed, and join the incoming one
        // after it is enabled, so they are never registered with a scene that is being torn down.
        _preserved.RemoveAll(p => p.IsNotValid());
        foreach (GameObject go in _preserved)
            if (ReferenceEquals(go.Scene, _current))
                _current!.Detach(go);

        if (_current is not null && !_current.IsDisposed)
        {
            if (_current.IsActive)
                _current.Disable();
            _current.Dispose();
        }

        _current = next;
        _current.Enable();

        foreach (GameObject go in _preserved)
            _current.Attach(go);

        OnSceneLoaded?.Invoke();
    }

    /// <summary>
    /// Disposes the current scene, so everything in it runs its teardown callbacks. Driven by the
    /// game loop on the way out. Reading <see cref="Current"/> afterwards creates a fresh empty scene.
    /// </summary>
    internal static void Shutdown()
    {
        _pendingScene = null;

        DestroyPreserved(immediate: true);

        if (_current is null || _current.IsDisposed)
        {
            _current = null;
            return;
        }

        if (_current.IsActive)
            _current.Disable();
        _current.Dispose();
        _current = null;
    }

    #endregion

    [SerializeField]
    private GameObject[] serializeObj = null;

    /// <summary>
    /// Parallel to serializeObj stores the original identifier for each GO.
    /// </summary>
    [SerializeField]
    private Guid[] _goIdentifiers = null;

    /// <summary>
    /// Flat array of component identifiers. _compIdOffsets[i] is the index into this
    /// array for GO i's first component. Component count = offset[i+1] - offset[i].
    /// </summary>
    [SerializeField]
    private Guid[] _compIdentifiers = null;

    [SerializeField]
    private int[] _compIdOffsets = null;

    [SerializeIgnore]
    private List<GameObject> _allObj = new();
    [SerializeIgnore]
    private HashSet<GameObject> _allObjSet = new(ReferenceEqualityComparer.Instance);

    private PhysicsWorld _physics = new();

    public PhysicsWorld Physics { get { EnsureNotDisposed(); return _physics; } }

    [SerializeIgnore]
    private readonly SceneDispatcher _dispatcher = new();

    /// <summary>The scene's dispatch point for per-frame component callbacks and physics events.</summary>
    internal SceneDispatcher Dispatcher => _dispatcher;

    /// <summary>
    /// Called once after a hot reload has migrated the scene graph in place: each GameObject drops removed
    /// components and rebuilds its lookup, then the dispatcher re-derives membership and ordering from the new
    /// types. Ordered that way deliberately, since the dispatcher must not re-register a component that is
    /// about to be dropped.
    /// </summary>
    internal void OnHotReload()
    {
        foreach (GameObject go in _allObj)
            if (go is not null && !go.IsDisposed)
                go.OnHotReload();

        // Re-registering from the live scene rather than from what was registered before is what lets a
        // component whose new type gained its first per-frame callback start dispatching at all.
        _dispatcher.Reset();

        foreach (GameObject go in _allObj)
        {
            if (go is null || go.IsDisposed) continue;

            foreach (MonoBehaviour comp in go._components)
                if (comp is not null && !comp.IsDisposed && comp.EnabledInHierarchy)
                    _dispatcher.Register(comp);
        }
    }

    [SerializeIgnore]
    private bool _isActive = false;

    public struct FogParams
    {
        public enum FogMode
        {
            Off,
            Linear,
            Exponential,
            ExponentialSquared
        }
        public FogMode Mode = FogMode.ExponentialSquared;
        public Color Color = new(0.5f, 0.5f, 0.5f, 1.0f);
        public float Start = 20;
        public float End = 100;
        public float Density = 0.01f;

        public bool IsFogLinear => Mode == FogMode.Linear;

        public FogParams()
        {
        }
    }

    public FogParams Fog = new();

    public struct AmbientLightParams
    {
        public enum AmbientMode
        {
            Uniform,
            Hemisphere
        }

        public AmbientMode Mode = AmbientMode.Uniform;

        public float Strength = 1f;

        // Uniform ambient
        public Float4 Color = new(0.43f, 0.55f, 0.65f, 1.0f);

        // Hemisphere ambient
        public Float4 SkyColor = new(0.3f, 0.3f, 0.4f, 1.0f);
        public Float4 GroundColor = new(0.2f, 0.2f, 0.2f, 1.0f);

        public bool UseHemisphere => Mode == AmbientMode.Hemisphere;

        public AmbientLightParams()
        {
        }
    }

    public AmbientLightParams Ambient = new();

    public enum SkyboxMode
    {
        Procedural,
        SolidColor,
        Gradient,
        Material
    }

    public struct SkyboxParams
    {
        public SkyboxMode Mode = SkyboxMode.Procedural;
        public Color SolidColor = new(0.2f, 0.3f, 0.5f, 1f);
        public Color GradientTop = new(0.4f, 0.6f, 0.9f, 1f);
        public Color GradientBottom = new(0.8f, 0.8f, 0.7f, 1f);
        public float GradientExponent = 1f;
        public AssetRef<Resources.Material> CustomMaterial;

        public SkyboxParams() { }
    }

    public SkyboxParams Skybox = new();

    /// <summary>Baked lightmaps + light-probe data for this scene, produced by the editor lightmap bake.</summary>
    public sealed class BakedLightingData
    {
        /// <summary>Baked lightmap atlas pages (RGBM-encoded). A renderer's <c>LightmapIndex</c> selects one.</summary>
        public List<AssetRef<Texture2D>> Lightmaps = new();
        /// <summary>World-space light-probe positions.</summary>
        public Float3[] ProbePositions = [];
        /// <summary>Baked SH per probe, indexed with <see cref="ProbePositions"/>.</summary>
        public SphericalHarmonicsL2[] ProbeSH = [];
        /// <summary>Tetrahedralization of the probes: 4 probe indices per tetrahedron.</summary>
        public int[] ProbeTetrahedra = [];
        /// <summary>Per-tetra neighbour links: 4 per tetra (across the face opposite vertex i), -1 = hull.</summary>
        public int[] ProbeTetNeighbours = [];

        public bool HasLightmaps => Lightmaps.Count > 0;
        public bool HasProbes => ProbeSH.Length > 0;
    }

    public BakedLightingData BakedLighting = new();

    /// <summary>
    /// Per-scene lightmapper configuration, edited in the editor's Environment panel and consumed by
    /// the bake. Persisted with the scene (it's a public field) so a bake's settings survive editor
    /// reloads.
    /// </summary>
    public sealed class LightmapBakeSettings
    {
        // Atlas / resolution
        public int AtlasSize = 1024;
        public float TexelsPerUnit = 20f;
        public int DilatePixels = 2;          // edge dilation to stop bilinear bleed at seams

        // Quality
        public int Bounces = 2;
        public int Samples = 64;              // progressive indirect iterations before finalize
        public int ProbeSamples = 256;
        public bool DoBackfaceCull = false;   // cull back faces on all bake rays (matches Prowl's backface-culled rendering)
        public float RussianRoulette = 0f;    // 0 = off

        // Edge-avoiding denoiser (runs once at finalize); geometry-guided only.
        public bool Denoise = false;
        public int DenoiseRadius = 5;         // a-trous pass count; each step ~doubles the smoothing reach (~2^N texels)

        // Feed the scene's ambient colour in as ray-miss (sky) radiance.
        public bool BakeSkyLighting = false;

        // Debug: bake every surface as a white Lambertian (isolates light/GI from albedo).
        public bool IgnoreAlbedo = false;
    }

    public LightmapBakeSettings LightmapBake = new();

    [NonSerialized] private LightProbeVolume? _probeVolume;

    /// <summary>Runtime probe sampler built from <see cref="BakedLighting"/> (lazy). Null when there are no baked probes.</summary>
    public LightProbeVolume? ProbeVolume
    {
        get
        {
            EnsureNotDisposed();
            if (_probeVolume == null && BakedLighting.HasProbes)
                _probeVolume = new LightProbeVolume(BakedLighting.ProbePositions, BakedLighting.ProbeSH,
                                                    BakedLighting.ProbeTetrahedra, BakedLighting.ProbeTetNeighbours);
            return _probeVolume;
        }
    }

    /// <summary>Drop the cached probe volume so the next access rebuilds it (call after a rebake).</summary>
    public void InvalidateProbeVolume() { EnsureNotDisposed(); _probeVolume = null; }

    /// <summary> The number of registered, non-disposed objects. </summary>
    public int Count { get { EnsureNotDisposed(); return _allObj.Count(o => !o.IsDisposed); } }

    /// <summary> Enumerates all registered objects. </summary>
    public IEnumerable<GameObject> AllObjects { get { EnsureNotDisposed(); return _allObj.Where(o => !o.IsDisposed); } }

    /// <summary> Enumerates all registered objects that are currently active and saveable. </summary>
    public IEnumerable<GameObject> SaveableObjects { get { EnsureNotDisposed(); return _allObj.Where(o => !o.IsDisposed && !o.HideFlags.HasFlag(HideFlags.DontSave) && !o.HideFlags.HasFlag(HideFlags.HideAndDontSave)); } }

    /// <summary> Enumerates all registered objects that are currently active. </summary>
    public IEnumerable<GameObject> ActiveObjects { get { EnsureNotDisposed(); return _allObj.Where(o => !o.IsDisposed && o.EnabledInHierarchy); } }

    /// <summary> Enumerates all root GameObjects, i.e. all GameObjects without a parent object. </summary>
    public IEnumerable<GameObject> RootObjects { get { EnsureNotDisposed(); return _allObj.Where(o => !o.IsDisposed && o.Transform.Parent == null); } }

    /// <summary> Enumerates all <see cref="RootObjects"/> that are currently active. </summary>
    public IEnumerable<GameObject> ActiveRootObjects { get { EnsureNotDisposed(); return _allObj.Where(o => !o.IsDisposed && o.Transform.Parent == null && o.EnabledInHierarchy); } }

    /// <summary> Returns whether this Scene is completely empty. </summary>
    public bool IsEmpty { get { EnsureNotDisposed(); return !AllObjects.Any(); } }

    /// <summary> Returns whether this scene is currently active. </summary>
    public bool IsActive { get { EnsureNotDisposed(); return _isActive; } }

    /// <summary>
    /// Creates a new, empty scene which does not contain any <see cref="GameObject">GameObjects</see>.
    /// </summary>
    public Scene()
    {
    }

    /// <summary>
    /// Enables this scene, triggering OnEnable callbacks for its components.
    /// For most use cases, prefer using Scene.Load() instead of calling Enable() directly.
    /// </summary>
    public void Enable()
    {
        EnsureNotDisposed();
        if (_isActive) return; // already enabled, nothing to deliver

        _isActive = true;

        // Create a copy to avoid collection modification during enumeration
        List<GameObject> allObjectsCopy = [.. AllObjects];

        // Trigger OnEnable for all enabled components in the scene
        foreach (GameObject go in allObjectsCopy)
        {
            if (go.IsDisposed) continue;

            if (go.EnabledInHierarchy)
            {
                var components = go.GetComponents<MonoBehaviour>();
                foreach (MonoBehaviour component in components)
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
    public void Disable()
    {
        EnsureNotDisposed();
        if (!_isActive) return; // already disabled, nothing to deliver

        // Create a copy to avoid collection modification during enumeration
        List<GameObject> allObjectsCopy = [.. AllObjects];

        // Trigger OnDisable for all enabled components in the scene
        foreach (GameObject go in allObjectsCopy)
        {
            if (go.IsDisposed) continue;

            if (go.EnabledInHierarchy)
            {
                var components = go.GetComponents<MonoBehaviour>();
                foreach (MonoBehaviour component in components)
                {
                    if (component.IsDisposed) continue;
                    if (component.Enabled && component.EnabledInHierarchy)
                        component.InternalOnDisable();
                }
            }
        }

        _isActive = false;
    }


    /// <summary>
    /// Registers a GameObject and all of its children.
    /// </summary>
    public void Add(GameObject obj)
    {
        EnsureNotDisposed();
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
        foreach (var go in _allObj)
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

        foreach (MonoBehaviour component in obj._components)
            if (!component.IsDisposed)
                _dispatcher.Unregister(component);

        obj.Scene = null;
    }

    /// <inheritdoc cref="Detach"/>
    internal void Attach(GameObject obj)
    {
        if (_allObjSet.Add(obj))
        {
            _allObj.Add(obj);
            obj.Scene = this;

            if (IsActive && obj.EnabledInHierarchy)
                foreach (MonoBehaviour component in obj._components)
                    if (!component.IsDisposed && component.Enabled && component.EnabledInHierarchy)
                        _dispatcher.Register(component);
        }

        foreach (GameObject child in obj.Children.ToArray())
            Attach(child);
    }

    private void AddObject(GameObject obj)
    {
        if (_allObjSet.Add(obj))
        {
            _allObj.Add(obj);
            obj.Scene = this;

            var components = obj.GetComponents<MonoBehaviour>();

            // Call OnAddedToScene for all components
            foreach (MonoBehaviour component in components)
            {
                if (component.IsDisposed) continue;
                try { component.OnAddedToScene(); }
                catch (Exception ex) { Debug.LogError($"[{obj.Name}/{component.GetType().Name}] OnAddedToScene() threw: {ex.Message}\n{ex.StackTrace}"); }
            }

            // Call OnEnable for enabled components, but only if the scene is active
            if (IsActive && obj.EnabledInHierarchy)
            {
                foreach (MonoBehaviour component in components)
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
        // Create a copy to avoid modification during enumeration
        List<GameObject> children = [.. obj.Children];
        foreach (GameObject child in children)
            RemoveObject(child);

        if (_allObjSet.Remove(obj))
        {
            _allObj.Remove(obj);
            var components = obj.GetComponents<MonoBehaviour>();

            // Call OnDisable for currently enabled components (only if scene is active)
            if (IsActive && obj.EnabledInHierarchy)
            {
                foreach (MonoBehaviour component in components)
                {
                    if (component.IsDisposed) continue;
                    if (component.Enabled && component.EnabledInHierarchy)
                        component.InternalOnDisable();
                }
            }

            // Call OnRemovedFromScene for all components
            foreach (MonoBehaviour component in components)
            {
                if (component.IsDisposed) continue;
                try { component.OnRemovedFromScene(); }
                catch (Exception ex) { Debug.LogError($"[{component.Name}/{component.GetType().Name}] OnRemovedFromScene() threw: {ex.Message}\n{ex.StackTrace}"); }
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

            foreach (MonoBehaviour comp in go.GetComponents<MonoBehaviour>())
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
            foreach (MonoBehaviour comp in go.GetComponents<MonoBehaviour>())
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
            foreach (MonoBehaviour comp in go.GetComponents<MonoBehaviour>())
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
        if (IsDisposed) return;
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

    protected override void OnDispose()
    {
        base.OnDispose();

        // Drop the current-scene reference without going through the property, which would build a
        // replacement scene in the middle of this one's teardown.
        if (ReferenceEquals(_current, this))
            _current = null;

        // Scene-scoped locks auto-expire with the scene rather than leaking forever.
        AssetDatabase.ReleaseSceneLocks(this);

        // Clear the physics world
        _physics.Clear();

        // Dispose all GameObjects which will also remove them from the scene. Dispose() (not the raw
        // OnDispose() body) sets IsDisposed and is idempotent, so the flat list's double-hits on
        // already-disposed children are no-ops.
        List<GameObject> allObjects = [.. _allObj.Where(o => !o.IsDisposed)];
        foreach (GameObject g in allObjects)
            g.Dispose();

        // Clear any remaining references
        _allObj.Clear();
        _allObjSet.Clear();

        // Remove all identifiers and reference to any possible gameobject that could hold a
        // user-defined script as it might leave the ALC alive
        serializeObj = null;
        _goIdentifiers = null;
        _compIdentifiers = null;
        _compIdOffsets = null;
    }

    public void OnBeforeSerialize()
    {
        serializeObj = [.. AllObjects];

        // Capture identifiers so they can be restored after deserialization
        _goIdentifiers = new Guid[serializeObj.Length];
        var compIds = new List<Guid>();
        _compIdOffsets = new int[serializeObj.Length + 1];

        for (int i = 0; i < serializeObj.Length; i++)
        {
            _goIdentifiers[i] = serializeObj[i].Identifier;
            _compIdOffsets[i] = compIds.Count;
            foreach (var comp in serializeObj[i].GetComponents<MonoBehaviour>())
                compIds.Add(comp.Identifier);
        }
        _compIdOffsets[serializeObj.Length] = compIds.Count;
        _compIdentifiers = compIds.ToArray();
    }

    public void OnAfterDeserialize()
    {
        if (serializeObj == null) return;

        // Restore identifiers GOs and components got fresh IDs during deserialization
        if (_goIdentifiers != null && _goIdentifiers.Length == serializeObj.Length)
        {
            for (int i = 0; i < serializeObj.Length; i++)
            {
                // A GameObject that failed to deserialize leaves a null slot; skip it rather than lose the rest.
                if (serializeObj[i] == null) continue;
                serializeObj[i].SetIdentifier(_goIdentifiers[i]);

                if (_compIdentifiers != null && _compIdOffsets != null)
                {
                    int start = _compIdOffsets[i];
                    int end = _compIdOffsets[i + 1];
                    var comps = serializeObj[i].GetComponents<MonoBehaviour>().ToList();
                    for (int c = 0; c < comps.Count && start + c < end; c++)
                        comps[c].Identifier = _compIdentifiers[start + c];
                }
            }
        }

        // Clear temp data
        _goIdentifiers = null;
        _compIdentifiers = null;
        _compIdOffsets = null;

        foreach (GameObject obj in serializeObj)
            if (obj != null) Add(obj);
    }

    /// <summary>
    /// Runs Start, then Update, then LateUpdate for the scene's registered components (those that
    /// implement those callbacks and are enabled in an active scene), in execution order. Each call
    /// is still gated per-component by ShouldExecuteGameplay.
    /// </summary>
    public void Update()
    {
        if (IsDisposed) return;
        _dispatcher.RunStart();
        _dispatcher.RunUpdate();
        _dispatcher.RunLateUpdate();

        Flush();
    }

    /// <summary>
    /// Executes physics update on all active GameObjects and their components.
    /// FixedUpdate is gated internally by each component's ShouldExecuteGameplay.
    /// </summary>
    public void FixedUpdate()
    {
        if (IsDisposed) return;
        // Start must run before a component's first FixedUpdate. The loop runs FixedUpdate before
        // Update, so drive Start here too (RunStart is idempotent - it only starts un-started ones).
        _dispatcher.RunStart();

        // A solver blow up (NaN or Inf transforms, degenerate collider) must not crash the frame.
        try { Physics.Update(); }
        catch (Exception ex) { Debug.LogError($"[Physics] Step threw and was skipped this frame: {ex.Message}\n{ex.StackTrace}"); }

        _dispatcher.RunFixedUpdate();

        Flush();
    }

    /// <summary>
    /// Collects render data from all active components for the given camera.
    /// Components add their renderables and lights to the provided lists.
    /// </summary>
    public void CollectRenderables(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        if (IsDisposed) return;
        _dispatcher.RunRenderCollect(camera, renderables, lights);
    }

    /// <summary>
    /// Draws gizmos for all active GameObjects and their components.
    /// </summary>
    public void DrawGizmos()
    {
        if (IsDisposed) return;
        _dispatcher.RunDrawGizmos();

        Flush();
    }

    /// <summary>
    /// Executes GUI update on all active GameObjects and their components.
    /// Calls OnGUI.
    /// </summary>
    public void OnGui(Paper paper)
    {
        if (IsDisposed) return;
        _dispatcher.RunOnGui(paper);

        Flush();
    }

    /// <summary>
    /// Collects every Camera on an enabled-in-hierarchy GameObject, sorted by Camera.Depth.
    /// </summary>
    internal List<Camera> GatherActiveCameras()
    {
        var cameras = new List<Camera>();

        for (int i = 0; i < _allObj.Count; i++)
        {
            GameObject go = _allObj[i];
            if (go.IsDisposed || !go.EnabledInHierarchy) continue;

            foreach (MonoBehaviour component in go._components)
            {
                if (component is Camera camera)
                {
                    cameras.Add(camera);
                }
            }
        }

        cameras.Sort(static (a, b) => a.Depth.CompareTo(b.Depth));
        return cameras;
    }

    /// <summary>
    /// Renders all cameras in this scene, sorted by depth.
    /// </summary>
    /// <param name="target">Optional render target to render into</param>
    /// <returns>True if any cameras were rendered, false otherwise</returns>
    public bool Render(RenderTexture? target = null)
    {
        if (IsDisposed) return false;
        // Renderables are now collected per-camera inside pipeline.Render()

        List<Camera> Cameras = GatherActiveCameras();

        if (Cameras.Count == 0)
            return false;

        foreach (Camera? cam in Cameras)
        {
            // One broken camera (bad effect, disposed RT, failed GPU alloc) must not take down the
            // other cameras or the whole frame. Contain it and keep rendering the rest.
            try
            {
                var camPipeline = cam.Pipeline;
                RenderPipeline pipeline = camPipeline.IsValid() ? camPipeline : DefaultRenderPipeline.Default;

                // A camera with its own Target asset draws there; everything else draws into `target`
                // (null for the backbuffer). Nothing on the camera is touched, so there is nothing to
                // restore and nothing a scene save could catch mid-render.
                pipeline.Render(cam, new RenderingData { FallbackTarget = target });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Render] Camera '{(cam.GameObject.IsValid() ? cam.GameObject.Name : null)}' render threw and was skipped: {ex.Message}\n{ex.StackTrace}");
            }
        }

        return true;
    }

}
