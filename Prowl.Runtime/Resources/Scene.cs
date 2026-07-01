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

    /// <summary>
    /// The currently active scene managed by the built-in Scene Manager.
    /// For simple games, use Scene.Load() and Scene.Current for automatic scene management.
    /// For advanced use cases (e.g., multiplayer servers with multiple scenes),
    /// create and manage your own Scene instances directly.
    /// </summary>
    public static Scene? Current { get; private set; }

    /// <summary>Fires after a scene is loaded via Load().</summary>
    public static event Action? OnSceneLoaded;

    /// <summary>
    /// Loads a scene as the current active scene, replacing any previously loaded scene.
    /// The previous scene will be disabled and disposed.
    /// </summary>
    public static void Load(Scene scene)
    {
        if (scene == null)
            throw new ArgumentNullException(nameof(scene));

        if (Current != null)
        {
            if (Current.IsActive)
                Current.Disable();
            Current.Dispose();
        }

        Current = scene;
        Current.Enable();
        OnSceneLoaded?.Invoke();
    }

    /// <summary>
    /// Unloads the current scene, disabling and disposing it.
    /// After calling this, Scene.Current will be null.
    /// </summary>
    /// <summary>
    /// Loads a scene as Current without calling Enable().
    /// Kept for backward compatibility now just calls Load() since lifecycle gating
    /// is handled per-component via ShouldExecuteGameplay.
    /// </summary>
    [Obsolete("Use Scene.Load() instead. Lifecycle gating is now per-component via [ExecuteAlways].")]
    public static void LoadWithoutEnable(Scene scene) => Load(scene);

    public static void Unload()
    {
        if (Current != null)
        {
            if (Current.IsActive)
                Current.Disable();
            Current.Dispose();
            Current = null;
        }
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

    public PhysicsWorld Physics => _physics;

    [SerializeIgnore]
    private readonly SceneComponentRegistry _componentRegistry = new();

    /// <summary>Per-scene registry of components that implement per-frame callbacks. Drives Update.</summary>
    internal SceneComponentRegistry ComponentRegistry => _componentRegistry;

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
        public Float4 Color = new(0.2f, 0.2f, 0.2f, 1.0f);

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
            if (_probeVolume == null && BakedLighting.HasProbes)
                _probeVolume = new LightProbeVolume(BakedLighting.ProbePositions, BakedLighting.ProbeSH,
                                                    BakedLighting.ProbeTetrahedra, BakedLighting.ProbeTetNeighbours);
            return _probeVolume;
        }
    }

    /// <summary>Drop the cached probe volume so the next access rebuilds it (call after a rebake).</summary>
    public void InvalidateProbeVolume() => _probeVolume = null;

    /// <summary> The number of registered, non-disposed objects. </summary>
    public int Count => _allObj.Count(o => !o.IsDisposed);

    /// <summary> Enumerates all registered objects. </summary>
    public IEnumerable<GameObject> AllObjects => _allObj.Where(o => !o.IsDisposed);

    /// <summary> Enumerates all registered objects that are currently active and saveable. </summary>
    public IEnumerable<GameObject> SaveableObjects => _allObj.Where(o => !o.IsDisposed && !o.HideFlags.HasFlag(HideFlags.DontSave) && !o.HideFlags.HasFlag(HideFlags.HideAndDontSave));

    /// <summary> Enumerates all registered objects that are currently active. </summary>
    public IEnumerable<GameObject> ActiveObjects => _allObj.Where(o => !o.IsDisposed && o.EnabledInHierarchy);

    /// <summary> Enumerates all root GameObjects, i.e. all GameObjects without a parent object. </summary>
    public IEnumerable<GameObject> RootObjects => _allObj.Where(o => !o.IsDisposed && o.Transform.Parent == null);

    /// <summary> Enumerates all <see cref="RootObjects"/> that are currently active. </summary>
    public IEnumerable<GameObject> ActiveRootObjects => _allObj.Where(o => !o.IsDisposed && o.Transform.Parent == null && o.EnabledInHierarchy);

    /// <summary> Returns whether this Scene is completely empty. </summary>
    public bool IsEmpty => !AllObjects.Any();

    /// <summary> Returns whether this scene is currently active. </summary>
    public bool IsActive => _isActive;

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
        if (_isActive) throw new Exception("Scene is already enabled!");

        _isActive = true;

        // Create a copy to avoid collection modification during enumeration
        List<GameObject> allObjectsCopy = [.. AllObjects];

        // Trigger OnEnable for all enabled components in the scene
        foreach (GameObject go in allObjectsCopy)
        {
            if (go.IsDisposed) continue;

            if (go.EnabledInHierarchy)
            {
                // Create a copy of components to avoid modification during enumeration
                MonoBehaviour[] components = [.. go.GetComponents<MonoBehaviour>()];
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
        if (!_isActive) throw new Exception("Scene is not enabled!");

        // Create a copy to avoid collection modification during enumeration
        List<GameObject> allObjectsCopy = [.. AllObjects];

        // Trigger OnDisable for all enabled components in the scene
        foreach (GameObject go in allObjectsCopy)
        {
            if (go.IsDisposed) continue;

            if (go.EnabledInHierarchy)
            {
                // Create a copy of components to avoid modification during enumeration
                MonoBehaviour[] components = [.. go.GetComponents<MonoBehaviour>()];
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
        if (obj.Scene.IsValid() && obj.Scene != this) obj.Scene.Remove(obj);
        AddObject(obj);
    }

    /// <summary>
    /// Move a root-level GameObject to a specific index within the root object list.
    /// Only works for root objects (no parent). Index is clamped to valid range.
    /// </summary>
    public void SetRootIndex(GameObject obj, int index)
    {
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
    /// Unregisters a GameObject and all of its children
    /// </summary>
    public void Remove(GameObject obj)
    {
        if (obj.Scene != this) return;
        if (obj.Parent.IsValid() && obj.Parent.Scene == this)
        {
            obj.SetParent(null);
        }
        RemoveObject(obj);
    }

    private void AddObject(GameObject obj)
    {
        if (_allObjSet.Add(obj))
        {
            _allObj.Add(obj);
            obj.Scene = this;

            // Create a copy of components to avoid modification during enumeration
            MonoBehaviour[] components = [.. obj.GetComponents<MonoBehaviour>()];

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
            // Create a copy of components to avoid modification during enumeration
            MonoBehaviour[] components = [.. obj.GetComponents<MonoBehaviour>()];

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

    public override void OnDispose()
    {
        base.OnDispose();

        // Clear the current scene reference if this is the current scene
        if (Current == this)
            Current = null;

        // Clear the physics world
        _physics.Clear();

        // Dispose all GameObjects which will also remove them from the scene. Dispose() (not the raw
        // OnDispose() body) sets IsDisposed and is idempotent, so the flat list's double-hits on
        // already-disposed children are no-ops.
        List<GameObject> allObjects = [.. AllObjects];
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
            Add(obj);
    }

    /// <summary>
    /// Runs Start, then Update, then LateUpdate for the scene's registered components (those that
    /// implement those callbacks and are enabled in an active scene), in execution order. Each call
    /// is still gated per-component by ShouldExecuteGameplay.
    /// </summary>
    public void Update()
    {
        _componentRegistry.RunStart();
        _componentRegistry.RunUpdate();
        _componentRegistry.RunLateUpdate();

        Flush();
    }

    /// <summary>
    /// Executes physics update on all active GameObjects and their components.
    /// FixedUpdate is gated internally by each component's ShouldExecuteGameplay.
    /// </summary>
    public void FixedUpdate()
    {
        // Start must run before a component's first FixedUpdate. The loop runs FixedUpdate before
        // Update, so drive Start here too (RunStart is idempotent - it only starts un-started ones).
        _componentRegistry.RunStart();

        Physics.Update();

        _componentRegistry.RunFixedUpdate();

        Flush();
    }

    /// <summary>
    /// Collects render data from all active components for the given camera.
    /// Components add their renderables and lights to the provided lists.
    /// </summary>
    public void CollectRenderables(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        _componentRegistry.RunRenderCollect(camera, renderables, lights);
    }

    /// <summary>
    /// Draws gizmos for all active GameObjects and their components.
    /// </summary>
    public void DrawGizmos()
    {
        _componentRegistry.RunDrawGizmos();

        Flush();
    }

    /// <summary>
    /// Executes GUI update on all active GameObjects and their components.
    /// Calls OnGUI.
    /// </summary>
    public void OnGui(Paper paper)
    {
        _componentRegistry.RunOnGui(paper);

        Flush();
    }

    /// <summary>
    /// Renders all cameras in this scene, sorted by depth.
    /// </summary>
    /// <param name="target">Optional render target to render into</param>
    /// <returns>True if any cameras were rendered, false otherwise</returns>
    public bool Render(RenderTexture? target = null)
    {
        // Renderables are now collected per-camera inside pipeline.Render()

        // ActiveObjects is a flat list, so GetComponentsInChildren (which recurses) would collect a
        // child camera once per active ancestor - Distinct() prevents rendering it multiple times.
        var Cameras = ActiveObjects.SelectMany(x => x.GetComponentsInChildren<Camera>()).Distinct().ToList();

        Cameras.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        if (Cameras.Count == 0)
            return false;

        foreach (Camera? cam in Cameras)
        {
            RenderPipeline pipeline = cam.Pipeline ?? DefaultRenderPipeline.Default;

            // If we have a target and the Camera doesnt, draw into the target
            if (target.IsValid() && cam.Target.IsNotValid())
            {
                cam.Target = target;
                pipeline.Render(cam, new());
                cam.Target = null;
            }
            else
            {
                // Have no target or the camera has its own target
                pipeline.Render(cam, new());
            }
        }

        return true;
    }

}
