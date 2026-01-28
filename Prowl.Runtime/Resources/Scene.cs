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
    internal static List<Scene> s_activeScenes = [];

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

    public bool IsActive => s_activeScenes.Contains(this);

    /// <summary>
    /// Creates a new, empty scene which does not contain any <see cref="GameObject">GameObjects</see>.
    /// </summary>
    public Scene()
    {
    }

    public void Activate()
    {
        if(IsActive) throw new Exception("Scene is already active!");

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
                        component.OnEnable();
                }
            }
        }
    }

    public void Deactivate()
    {
        if (!_isActive) throw new Exception("Scene is not active!");

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

        s_activeScenes.Remove(this);
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
                    if (component.Enabled)
                    if (component.IsDisposed) continue;
                    if (component.Enabled && component.EnabledInHierarchy)
                        component.OnEnable();
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
    internal void Update()
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
    internal void FixedUpdate()
    {
        Physics.Update();

        List<GameObject> activeGOs = [.. ActiveObjects];
        ForeachComponent(activeGOs, (x) => x.FixedUpdate());

        Flush();
    }

    internal void DrawGizmos()
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
    internal void OnGui(Paper paper)
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
    internal bool Render(RenderTexture? target = null)
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
