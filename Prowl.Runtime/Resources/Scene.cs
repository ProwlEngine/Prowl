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

    /// <summary>
    /// Loads a scene as the current active scene, replacing any previously loaded scene.
    /// The previous scene will be disabled and disposed.
    /// </summary>
    /// <param name="scene">The scene to load as the current scene.</param>
    public static void Load(Scene scene)
    {
        if (scene == null)
            throw new ArgumentNullException(nameof(scene));

        // Disable and dispose the current scene if one exists
        if (Current != null)
        {
            if (Current.IsActive)
                Current.Disable();
            Current.Dispose();
        }

        Current = scene;
        Current.Enable();
    }

    /// <summary>
    /// Unloads the current scene, disabling and disposing it.
    /// After calling this, Scene.Current will be null.
    /// </summary>
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

    [SerializeIgnore]
    private HashSet<GameObject> _allObj = new(ReferenceEqualityComparer.Instance);

    private PhysicsWorld _physics = new();

    public PhysicsWorld Physics => _physics;

    // Rendering tracking - cleared each frame before Update, populated during Update
    [SerializeIgnore]
    private readonly List<IRenderable> _renderables = [];
    [SerializeIgnore]
    private readonly List<IRenderableLight> _lights = [];

    [SerializeIgnore]
    private bool _isActive = false;

    public int RenderableCount => _renderables.Count;
    public IReadOnlyList<IRenderable> Renderables => _renderables;
    public IReadOnlyList<IRenderableLight> Lights => _lights;

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

    /// <summary> The number of registered objects. </summary>
    public int Count => _allObj.Count;

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
                        component.OnDisable();
                }
            }
        }

        _isActive = false;
    }

    /// <summary>
    /// Adds a renderable to the scene's render list. Called by components during Update.
    /// </summary>
    public void PushRenderable(IRenderable renderable)
    {
        _renderables.Add(renderable);
    }

    /// <summary>
    /// Adds a light to the scene's light list. Called by light components during Update.
    /// </summary>
    public void PushLight(IRenderableLight light)
    {
        _lights.Add(light);
    }

    /// <summary>
    /// Clears the renderable and light tracking lists. Called at the start of each Update.
    /// </summary>
    private void ClearRenderTracking()
    {
        _renderables.Clear();
        _lights.Clear();
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
        if (_allObj.Add(obj))
        {
            obj.Scene = this;

            // Create a copy of components to avoid modification during enumeration
            MonoBehaviour[] components = [.. obj.GetComponents<MonoBehaviour>()];

            // Call OnAddedToScene for all components
            foreach (MonoBehaviour component in components)
            {
                if (component.IsDisposed) continue;
                component.OnAddedToScene();
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

        if (_allObj.Remove(obj))
        {
            // Create a copy of components to avoid modification during enumeration
            MonoBehaviour[] components = [.. obj.GetComponents<MonoBehaviour>()];

            // Call OnDisable for currently enabled components (only if scene is active)
            if (IsActive && obj.EnabledInHierarchy)
            {
                foreach (MonoBehaviour component in components)
                {
                    if (component.IsDisposed) continue;
                    if (component.Enabled && component.EnabledInHierarchy)
                        component.OnDisable();
                }
            }

            // Call OnRemovedFromScene for all components
            foreach (MonoBehaviour component in components)
            {
                if (component.IsDisposed) continue;
                component.OnRemovedFromScene();
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

        _allObj.RemoveWhere(obj => obj.IsDisposed);

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

        // Dispose all GameObjects which will also remove them from the scene
        List<GameObject> allObjects = [.. AllObjects];
        foreach (GameObject g in allObjects)
            g.OnDispose();

        // Clear any remaining references
        _allObj.Clear();
    }

    public void OnBeforeSerialize()
    {
        serializeObj = [.. AllObjects];
    }

    public void OnAfterDeserialize()
    {
        if (serializeObj != null)
            foreach (GameObject obj in serializeObj)
                Add(obj);
    }

    /// <summary>
    /// Updates all active GameObjects and their components in this scene.
    /// Calls PreUpdate, Update, and LateUpdate.
    /// </summary>
    public void Update()
    {
        // Clear render tracking at the start of each update
        ClearRenderTracking();

        List<GameObject> activeGOs = [.. ActiveObjects];
        foreach (GameObject go in activeGOs)
            go.PreUpdate();

        ForeachComponent(activeGOs, (x) => x.Update());

        ForeachComponent(activeGOs, (x) => x.LateUpdate());

        Flush();
    }

    /// <summary>
    /// Executes physics update on all active GameObjects and their components.
    /// Calls Physics.Update and FixedUpdate.
    /// </summary>
    public void FixedUpdate()
    {
        Physics.Update();

        List<GameObject> activeGOs = [.. ActiveObjects];
        ForeachComponent(activeGOs, (x) => x.FixedUpdate());

        Flush();
    }

    /// <summary>
    /// Draws gizmos for all active GameObjects and their components.
    /// </summary>
    public void DrawGizmos()
    {
        List<GameObject> activeGOs = [.. ActiveObjects];
        ForeachComponent(activeGOs, (x) =>
        {
            x.DrawGizmos();
        });

        Flush();
    }

    /// <summary>
    /// Executes GUI update on all active GameObjects and their components.
    /// Calls OnGUI.
    /// </summary>
    public void OnGui(Paper paper)
    {
        List<GameObject> activeGOs = [.. ActiveObjects];
        ForeachComponent(activeGOs, (x) =>
        {
            x.OnGui(paper);
        });

        Flush();
    }

    /// <summary>
    /// Renders all cameras in this scene, sorted by depth.
    /// </summary>
    /// <param name="target">Optional render target to render into</param>
    /// <returns>True if any cameras were rendered, false otherwise</returns>
    public bool Render(RenderTexture? target = null)
    {
        var Cameras = ActiveObjects.SelectMany(x => x.GetComponentsInChildren<Camera>()).ToList();

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

    /// <summary>
    /// Helper method to iterate over all MonoBehaviour components in a collection of GameObjects
    /// and execute an action on each enabled component.
    /// </summary>
    public void ForeachComponent(IEnumerable<GameObject> objs, Action<MonoBehaviour> action)
    {
        foreach (GameObject go in objs)
        {
            MonoBehaviour[] components = [.. go.GetComponents<MonoBehaviour>()];
            foreach (MonoBehaviour? comp in components)
                if (comp.EnabledInHierarchy)
                    action.Invoke(comp);
        }
    }
}
