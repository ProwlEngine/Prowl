using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime.Rendering;

namespace Prowl.Runtime.Resources
{
    public class Scene : EngineObject, ISerializationCallbackReceiver
    {
        [SerializeField]
        private GameObject[] serializeObj = null;

        [SerializeIgnore]
        private HashSet<GameObject> _allObj = new HashSet<GameObject>(ReferenceEqualityComparer.Instance);

        private PhysicsWorld _physics = new PhysicsWorld();

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
            public Vector4 Color = new(0.5, 0.5, 0.5, 1.0);
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

            // Uniform ambient
            public Vector4 Color = new(0.2f, 0.2f, 0.2f, 1.0f);

            // Hemisphere ambient
            public Vector4 SkyColor = new(0.3f, 0.3f, 0.4f, 1.0f);
            public Vector4 GroundColor = new(0.2f, 0.2f, 0.2f, 1.0f);

            public bool UseHemisphere => Mode == AmbientMode.Hemisphere;

            public AmbientLightParams()
            {
            }
        }

        // Add this to your Scene class
        public AmbientLightParams Ambient = new();

        /// <summary> The number of registered objects. </summary>
        public int Count => _allObj.Count;

        /// <summary> Enumerates all registered objects. </summary>
        public IEnumerable<GameObject> AllObjects => _allObj.Where(o => !o.IsDestroyed);

        /// <summary> Enumerates all registered objects that are currently active and saveable. </summary>
        public IEnumerable<GameObject> SaveableObjects => _allObj.Where(o => !o.IsDestroyed && !o.hideFlags.HasFlag(HideFlags.DontSave) && !o.hideFlags.HasFlag(HideFlags.HideAndDontSave));

        /// <summary> Enumerates all registered objects that are currently active. </summary>
        public IEnumerable<GameObject> ActiveObjects => _allObj.Where(o => !o.IsDestroyed && o.enabledInHierarchy);

        /// <summary> Enumerates all root GameObjects, i.e. all GameObjects without a parent object. </summary>
        public IEnumerable<GameObject> RootObjects => _allObj.Where(o => !o.IsDestroyed && o.Transform.parent == null);

        /// <summary> Enumerates all <see cref="RootObjects"/> that are currently active. </summary>
        public IEnumerable<GameObject> ActiveRootObjects => _allObj.Where(o => !o.IsDestroyed && o.Transform.parent == null && o.enabledInHierarchy);

        /// <summary> Returns whether this Scene is completely empty. </summary>
        public bool IsEmpty => !AllObjects.Any();

        /// <summary>
        /// Creates a new, empty scene which does not contain any <see cref="GameObject">GameObjects</see>.
        /// </summary>
        public Scene()
        {
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
            if (obj.Scene != null && obj.Scene != this) obj.Scene.Remove(obj);
            AddObject(obj);
        }

        /// <summary>
        /// Unregisters a GameObject and all of its children
        /// </summary>
        public void Remove(GameObject obj)
        {
            if (obj.Scene != this) return;
            if (obj.parent != null && obj.parent.Scene == this)
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
            }
            foreach (GameObject child in obj.children)
                AddObject(child);
        }

        private void RemoveObject(GameObject obj)
        {
            foreach (GameObject child in obj.children)
                RemoveObject(child);
            if (_allObj.Remove(obj))
            {
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
            return objects.ToArray();
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
            foreach (GameObject obj in _allObj)
                obj.Scene = null;
            _allObj.Clear();
        }

        /// <summary> Unregisters all dead / disposed GameObjects </summary>
        public void Flush()
        {
            List<GameObject> removed = [];
            foreach (GameObject obj in _allObj)
            {
                if (obj.IsDestroyed)
                    removed.Add(obj);
            }

            _allObj.RemoveWhere(obj => obj.IsDestroyed);

            foreach (GameObject obj in removed)
                obj.Scene = null;
        }

        public override void OnDispose()
        {
            base.OnDispose();

            foreach (GameObject g in AllObjects)
                g.OnDispose();

            Clear();
        }

        public void OnBeforeSerialize()
        {
            serializeObj = AllObjects.ToArray();
        }

        public void OnAfterDeserialize()
        {
            foreach (GameObject obj in serializeObj)
                Add(obj);
        }

        /// <summary>
        /// Updates all active GameObjects and their components in this scene.
        /// Calls PreUpdate, Update, UpdateCoroutines, LateUpdate, and EndOfFrameCoroutines.
        /// </summary>
        public void Update()
        {
            // Clear render tracking at the start of each update
            ClearRenderTracking();

            List<GameObject> activeGOs = ActiveObjects.ToList();
            foreach (GameObject go in activeGOs)
                go.PreUpdate();

            ForeachComponent(activeGOs, (x) =>
            {
                x.Do(x.UpdateCoroutines);
                x.Do(x.Update);
            });

            ForeachComponent(activeGOs, (x) => x.Do(x.LateUpdate));
            ForeachComponent(activeGOs, (x) => x.Do(x.UpdateEndOfFrameCoroutines));
        }

        /// <summary>
        /// Executes physics update on all active GameObjects and their components.
        /// Calls Physics.Update, FixedUpdate and UpdateFixedUpdateCoroutines.
        /// </summary>
        public void FixedUpdate()
        {
            Physics.Update();

            List<GameObject> activeGOs = ActiveObjects.ToList();
            ForeachComponent(activeGOs, (x) =>
            {
                x.Do(x.FixedUpdate);
                x.Do(x.UpdateFixedUpdateCoroutines);
            });
        }

        /// <summary>
        /// Renders all cameras in this scene, sorted by depth.
        /// </summary>
        /// <param name="target">Optional render target to render into</param>
        /// <returns>True if any cameras were rendered, false otherwise</returns>
        public bool RenderScene(RenderTexture? target = null)
        {
            var Cameras = ActiveObjects.SelectMany(x => x.GetComponentsInChildren<Camera>()).ToList();

            Cameras.Sort((a, b) => a.Depth.CompareTo(b.Depth));

            if (Cameras.Count == 0)
                return false;

            foreach (Camera? cam in Cameras)
            {
                RenderPipeline pipeline = cam.Pipeline ?? DefaultRenderPipeline.Default;

                // If we have a target and the Camera doesnt, draw into the target
                if (target != null && cam.Target == null)
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
            foreach (var go in objs)
                foreach (var comp in go.GetComponents(typeof(MonoBehaviour)))
                    if (comp.EnabledInHierarchy)
                        action.Invoke(comp);
        }
    }
}
