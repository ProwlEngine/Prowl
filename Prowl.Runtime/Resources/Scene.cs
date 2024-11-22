// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Cloning;
using Prowl.Runtime.Utilities;

using Vortice.Direct3D11;

namespace Prowl.Runtime;

public class Scene : EngineObject, ISerializationCallbackReceiver
{
    [SerializeField, HideInInspector]
    private GameObject[] serializeObj = null;

    [SerializeIgnore]
    [CloneField(CloneFieldFlags.DontSkip)]
    [CloneBehavior(typeof(GameObject), CloneBehavior.ChildObject)]
    private HashSet<GameObject> _allObj = new HashSet<GameObject>(ReferenceEqualityComparer.Instance);

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
        [ShowIf(nameof(IsFogLinear))] public float Start = 20;
        [ShowIf(nameof(IsFogLinear))] public float End = 100;
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
        [ShowIf(nameof(UseHemisphere), true)] public Vector4 Color = new(0.2f, 0.2f, 0.2f, 1.0f);

        // Hemisphere ambient
        [ShowIf(nameof(UseHemisphere))] public Vector4 SkyColor = new(0.3f, 0.3f, 0.4f, 1.0f);
        [ShowIf(nameof(UseHemisphere))] public Vector4 GroundColor = new(0.2f, 0.2f, 0.2f, 1.0f);

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

    public void HandlePrefabs()
    {
        // When in the editor, apply prefab links
        if (Application.IsEditor)
            PrefabLink.ApplyAllLinks(AllObjects);
        
        // When running the game, break prefab links
        if (Application.IsPlaying)
            foreach (GameObject obj in AllObjects)
                PrefabUtility.BreakPrefabLink(obj);
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
}
