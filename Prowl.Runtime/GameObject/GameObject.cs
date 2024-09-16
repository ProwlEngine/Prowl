// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Runtime.SceneManagement;

namespace Prowl.Runtime;

/// <summary>
/// The Base Class for all Object/Entities in a Scene.
/// Holds a collection of Components that contain the logic for this Object/Entity
/// </summary>
public class GameObject : EngineObject, ISerializable
{
    #region Static Fields/Properties

    internal static event Action<GameObject>? Internal_Constructed;
    internal static event Action<GameObject>? Internal_DestroyCommitted;

    #endregion

    #region Private Fields/Properties

    private List<MonoBehaviour> _components = new();

    private readonly MultiValueDictionary<Type, MonoBehaviour> _componentCache = new();

    private bool _enabled = true;
    private bool _enabledInHierarchy = true;

    // We dont serialize parent, since if we want to serialize X object who is a child to Y object, we dont want to serialize Y object as well.
    // The parent is reconstructed when the object is deserialized for all children.
    private GameObject? _parent;

    [SerializeField]
    private Transform _transform = new();

    #endregion

    #region Public Fields/Properties

    /// <summary> The Tag Index of this GameObject </summary>
    public byte tagIndex;

    /// <summary> The Layer Index of this GameObject </summary>
    public byte layerIndex;

    /// <summary> The Hide Flags of this GameObject, Used to hide the GameObject from a variety of places like Serializing, Inspector or Hierarchy </summary>
    public HideFlags hideFlags = HideFlags.None;

    /// <summary> Gets whether or not this gameobject is enabled explicitly </summary>
    public bool enabled
    {
        get { return _enabled; }
        set { if (value != _enabled) { SetEnabled(value); } }
    }

    /// <summary> Gets whether this gameobejct is enabled in the hierarchy, so if its parent is disabled this will return false </summary>
    public bool enabledInHierarchy => _enabledInHierarchy;

    /// <summary> The Tag of this GameObject </summary>
    public string tag
    {
        get => TagLayerManager.GetTag(tagIndex);
        set => tagIndex = TagLayerManager.GetTagIndex(value);
    }

    /// <summary> The Layer of this GameObject </summary>
    public string layer
    {
        get => TagLayerManager.GetLayer(layerIndex);
        set => layerIndex = TagLayerManager.GetLayerIndex(value);
    }

    /// <summary> The Parent of this GameObject, Can be null </summary>
    public GameObject? parent => _parent;

    /// <summary> A List of all children of this GameObject </summary>
    public List<GameObject> children = new List<GameObject>();

    public int childCount => children.Count;

    public bool IsPrefab => AssetID != Guid.Empty;

    public bool IsPartOfPrefab
    {
        get
        {
            GameObject? parent = this;
            while (parent != null)
            {
                if (parent.IsPrefab) return true;
                parent = parent.parent;
            }
            return false;
        }
    }

    public SerializedProperty? PrefabMods { get; set; } = null;

    #endregion

    public Transform Transform
    {
        get
        {
            _transform.gameObject = this; // ensure gameobject is this
            return _transform;
        }
    }

    public bool IsChildOrSameTransform(GameObject transform, GameObject inParent)
    {
        GameObject child = transform;
        while (child != null)
        {
            if (child == inParent)
                return true;
            child = child._parent;
        }
        return false;
    }

    public bool IsChildOf(GameObject parent)
    {
        if (InstanceID == parent.InstanceID) return false; // Not a child their the same object

        GameObject child = this;
        while (child != null)
        {
            if (child == parent)
                return true;
            child = child._parent;
        }
        return false;
    }

    public bool SetParent(GameObject NewParent, bool worldPositionStays = true)
    {
        if (NewParent == _parent)
            return true;

        // Make sure that the new father is not a child of this transform.
        if (IsChildOrSameTransform(NewParent, this))
            return false;

        // Save the old position in worldspace
        Vector3 worldPosition = new Vector3();
        Quaternion worldRotation = new Quaternion();
        Matrix4x4 worldScale = new Matrix4x4();

        if (worldPositionStays)
        {
            worldPosition = Transform.position;
            worldRotation = Transform.rotation;
            worldScale = Transform.GetWorldRotationAndScale();
        }

        if (NewParent != _parent)
        {
            // If it already has an father, remove this from fathers children
            if (_parent != null)
                _parent.children.Remove(this);

            if (NewParent != null)
                NewParent.children.Add(this);

            _parent = NewParent;
        }

        if (worldPositionStays)
        {
            if (_parent != null)
            {
                Transform.localPosition = _parent.Transform.InverseTransformPoint(worldPosition);
                Transform.localRotation = Quaternion.NormalizeSafe(Quaternion.Inverse(_parent.Transform.rotation) * worldRotation);
            }
            else
            {
                Transform.localPosition = worldPosition;
                Transform.localRotation = Quaternion.NormalizeSafe(worldRotation);
            }

            Transform.localScale = Vector3.one;
            Matrix4x4 inverseRS = Transform.GetWorldRotationAndScale().Invert() * worldScale;
            Transform.localScale = new Vector3(inverseRS[0, 0], inverseRS[1, 1], inverseRS[2, 2]);
        }

        HierarchyStateChanged();

        return true;
    }

    #region Constructors

    /// <summary>
    /// A special method to create a new GameObject without triggering the global Constructed event.
    /// This prevents the gameobject from, for example being loaded into the scene.
    /// </summary>
    /// <param name="dummy"></param>
    public static GameObject CreateSilently()
    {
        var go = new GameObject(0);
        return go;
    }

    /// <summary>
    /// A Special constructed used internally for the <see cref="GameObject.CreateSilently"/>() method, this prevents the GameObject from being added to the scene.
    /// </summary>
    /// <param name="dummy">Does nothing.</param>
    private GameObject(int dummy) : base("New GameObject") { }

    /// <summary>Creates a new gameobject with tbe name 'New GameObject'.</summary>
    public GameObject() : base("New GameObject")
    {
        Internal_Constructed?.Invoke(this);
    }

    /// <summary>Creates a new gameobject.</summary>
    /// <param name="name">The name of the gameobject.</param>
    public GameObject(string name = "New GameObject") : base(name)
    {
        Internal_Constructed?.Invoke(this);
    }

    #endregion


    /// <summary> Recursive function to check if this GameObject is a parent of another GameObject </summary>
    public bool IsParentOf(GameObject go)
    {
        if (go.parent?.InstanceID == InstanceID)
            return true;

        foreach (var child in children)
            if (child.IsParentOf(go))
                return true;

        return false;
    }

    public bool CompareTag(string otherTag) => TagLayerManager.GetTag(tagIndex).Equals(otherTag, StringComparison.OrdinalIgnoreCase);

    public static GameObject Find(string otherName) => FindObjectsOfType<GameObject>().FirstOrDefault(gameObject => gameObject.Name == otherName);

    public static GameObject FindGameObjectWithTag(string otherTag) => FindObjectsOfType<GameObject>().FirstOrDefault(gameObject => gameObject.CompareTag(otherTag));

    public static GameObject[] FindGameObjectsWithTag(string otherTag) => FindObjectsOfType<GameObject>().Where(gameObject => gameObject.CompareTag(otherTag)).ToArray();

    internal void PreUpdate()
    {
        foreach (var component in _components)
        {
            if (!component.HasAwoken)
                component.Do(component.InternalAwake);

            if (!component.HasStarted)
                if (component.EnabledInHierarchy)
                {
                    component.Do(component.InternalStart);
                }
        }
    }

    public T AddComponent<T>() where T : MonoBehaviour, new() => AddComponent(typeof(T)) as T;

    public MonoBehaviour AddComponent(Type type)
    {
        if (!typeof(MonoBehaviour).IsAssignableFrom(type)) return null;

        var requireComponentAttribute = type.GetCustomAttribute<RequireComponentAttribute>();
        if (requireComponentAttribute != null)
        {
            foreach (var requiredComponentType in requireComponentAttribute.types)
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(requiredComponentType))
                    continue;

                // If there is already a component on the object
                if (GetComponent(requiredComponentType) != null)
                    continue;

                // Recursive call to attempt to add the new component
                AddComponent(requiredComponentType);
            }
        }

        if (type.GetCustomAttribute<DisallowMultipleComponentAttribute>() != null && GetComponent(type) != null)
        {
            Debug.LogError("Can't Add the Same Component Multiple Times The component of type " + type.Name + " does not allow multiple instances");
            return null;
        }

        var newComponent = Activator.CreateInstance(type) as MonoBehaviour;
        if (newComponent == null) return null;

        newComponent.AttachToGameObject(this);
        _components.Add(newComponent);
        _componentCache.Add(type, newComponent);

        if (enabledInHierarchy)
        {
            newComponent.Do(newComponent.InternalAwake);
        }

        return newComponent;
    }

    public void AddComponent(MonoBehaviour comp)
    {
        var type = comp.GetType();
        var requireComponentAttribute = type.GetCustomAttribute<RequireComponentAttribute>();
        if (requireComponentAttribute != null)
        {
            foreach (var requiredComponentType in requireComponentAttribute.types)
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(requiredComponentType))
                    continue;

                // If there is already a component on the object
                if (GetComponent(requiredComponentType) != null)
                    continue;

                // Recursive call to attempt to add the new component
                AddComponent(requiredComponentType);
            }
        }

        if (type.GetCustomAttribute<DisallowMultipleComponentAttribute>() != null && GetComponent(type) != null)
        {
            Debug.LogError("Can't Add the Same Component Multiple Times" + "The component of type " + type.Name + " does not allow multiple instances");
            return;
        }

        comp.AttachToGameObject(this);
        _components.Add(comp);
        _componentCache.Add(comp.GetType(), comp);
        if (enabledInHierarchy)
        {
            comp.Do(comp.InternalAwake);
        }
    }

    public void RemoveAll<T>() where T : MonoBehaviour
    {
        IReadOnlyCollection<MonoBehaviour> components;
        if (_componentCache.TryGetValue(typeof(T), out components))
        {
            foreach (MonoBehaviour c in components)
                if (c.EnabledInHierarchy)
                    c.Do(c.OnDisable);
            foreach (MonoBehaviour c in components)
            {
                if (c.HasStarted) // OnDestroy is only called if the component has previously been active
                    c.Do(c.OnDestroy);

                _components.Remove(c);
            }
            _componentCache.Remove(typeof(T));
        }
    }

    public void RemoveComponent<T>(T component) where T : MonoBehaviour
    {
        if (component.CanDestroy() == false) return;

        _components.Remove(component);
        _componentCache.Remove(typeof(T), component);

        if (component.EnabledInHierarchy) component.Do(component.OnDisable);
        if (component.HasStarted) component.Do(component.OnDestroy); // OnDestroy is only called if the component has previously been active
    }

    public void RemoveComponent(MonoBehaviour component)
    {
        if (component.CanDestroy() == false) return;

        _components.Remove(component);
        _componentCache.Remove(component.GetType(), component);

        if (component.EnabledInHierarchy) component.Do(component.OnDisable);
        if (component.HasStarted) component.Do(component.OnDestroy); // OnDestroy is only called if the component has previously been active
    }

    public T? GetComponent<T>() where T : MonoBehaviour => (T?)GetComponent(typeof(T));

    public MonoBehaviour? GetComponent(Type type)
    {
        if (type == null) return null;
        if (_componentCache.TryGetValue(type, out var components))
            return components.First();
        else
            foreach (var comp in _components)
                if (comp.GetType().IsAssignableTo(type))
                    return comp;
        return null;
    }

    public IEnumerable<MonoBehaviour> GetComponents() => _components;

    public bool TryGetComponent<T>(out T? component) where T : MonoBehaviour => (component = GetComponent<T>()) != null;

    public IEnumerable<T> GetComponents<T>() where T : MonoBehaviour => GetComponents(typeof(T)).Cast<T>();
    public IEnumerable<MonoBehaviour> GetComponents(Type type)
    {
        if (type == typeof(MonoBehaviour))
        {
            // Special case for Component
            foreach (var comp in _components)
                yield return comp;
        }
        else
        {
            if (!_componentCache.TryGetValue(type, out var components))
            {
                foreach (var kvp in _componentCache.ToArray())
                    if (kvp.Key.GetTypeInfo().IsAssignableTo(type))
                        foreach (var comp in kvp.Value.ToArray())
                            yield return comp;
            }
            else
            {
                foreach (var comp in components)
                    if (comp.GetType().IsAssignableTo(type))
                        yield return comp;
            }
        }
    }

    public T? GetComponentInParent<T>(bool includeSelf = true, bool includeInactive = false) where T : MonoBehaviour => (T)GetComponentInParent(typeof(T), includeSelf, includeInactive);

    public MonoBehaviour? GetComponentInParent(Type componentType, bool includeSelf = true, bool includeInactive = false)
    {
        if (componentType == null) return null;
        // First check the current Object
        MonoBehaviour component;
        if (includeSelf && enabledInHierarchy)
        {
            component = GetComponent(componentType);
            if (component != null)
                return component;
        }
        // Now check all parents
        GameObject parent = this;
        while ((parent = parent.parent) != null)
        {
            if (parent.enabledInHierarchy || includeInactive)
            {
                component = parent.GetComponent(componentType);
                if (component != null)
                    return component;
            }
        }
        return null;
    }

    public IEnumerable<T> GetComponentsInParent<T>(bool includeSelf = true, bool includeInactive = false) where T : MonoBehaviour => GetComponentsInParent(typeof(T), includeSelf, includeInactive).Cast<T>();
    public IEnumerable<MonoBehaviour> GetComponentsInParent(Type type, bool includeSelf = true, bool includeInactive = false)
    {
        // First check the current Object
        if (includeSelf && enabledInHierarchy)
            foreach (var component in GetComponents(type))
                yield return component;
        // Now check all parents
        GameObject parent = this;
        while ((parent = parent.parent) != null)
        {
            if (parent.enabledInHierarchy || includeInactive)
                foreach (var component in parent.GetComponents(type))
                    yield return component;
        }
    }

    public T? GetComponentInChildren<T>(bool includeSelf = true, bool includeInactive = false) where T : MonoBehaviour => (T)GetComponentInChildren(typeof(T), includeSelf, includeInactive);

    public MonoBehaviour GetComponentInChildren(Type componentType, bool includeSelf = true, bool includeInactive = false)
    {
        if (componentType == null) return null;
        // First check the current Object
        MonoBehaviour component;
        if (includeSelf && enabledInHierarchy)
        {
            component = GetComponent(componentType);
            if (component != null)
                return component;
        }
        // Now check all children
        foreach (var child in children)
        {
            if (enabledInHierarchy || includeInactive)
            {
                component = child.GetComponent(componentType) ?? child.GetComponentInChildren(componentType);
                if (component != null)
                    return component;
            }
        }
        return null;
    }


    public IEnumerable<T> GetComponentsInChildren<T>(bool includeSelf = true, bool includeInactive = false) where T : MonoBehaviour => GetComponentsInChildren(typeof(T), includeSelf, includeInactive).Cast<T>();
    public IEnumerable<MonoBehaviour> GetComponentsInChildren(Type type, bool includeSelf = true, bool includeInactive = false)
    {
        // First check the current Object
        if (includeSelf && enabledInHierarchy)
            foreach (var component in GetComponents(type))
                yield return component;
        // Now check all children
        foreach (var child in children)
        {
            if (enabledInHierarchy || includeInactive)
                foreach (var component in child.GetComponentsInChildren(type, true, includeInactive))
                    yield return component;
        }
    }

    internal bool IsComponentRequired(MonoBehaviour requiredComponent, out Type dependentType)
    {
        var componentType = requiredComponent.GetType();
        foreach (var component in _components)
        {
            var requireComponentAttribute =
                component.GetType().GetCustomAttribute<RequireComponentAttribute>();
            if (requireComponentAttribute == null)
                continue;

            if (requireComponentAttribute.types.All(type => type != componentType))
                continue;

            dependentType = component.GetType();
            return true;
        }
        dependentType = null;
        return false;
    }

    public static GameObject Instantiate(GameObject original) => Instantiate(original, null);
    public static GameObject Instantiate(GameObject original, GameObject? parent)
    {
        GameObject clone = (GameObject)EngineObject.Instantiate(original, false);
        clone.SetParent(parent);
        return clone;
    }
    public static GameObject Instantiate(GameObject original, GameObject? parent, Vector3 position, Quaternion rotation) => Instantiate(original, position, rotation, parent);
    public static GameObject Instantiate(GameObject original, Vector3 position, Quaternion rotation, GameObject? parent)
    {
        GameObject clone = (GameObject)EngineObject.Instantiate(original, false);
        clone.Transform.position = position;
        clone.Transform.rotation = rotation;
        clone.SetParent(parent, true);
        return clone;
    }

    public override void OnDispose()
    {
        // Internal_DestroyCommitted removes the child from the parent
        // Hense why we do a while loop on the first element instead of a foreach/for
        while (children.Count > 0)
            children[0].Destroy();

        foreach (var component in _components)
        {
            if (component.EnabledInHierarchy) component.Do(component.OnDisable);
            if (component.HasStarted) component.Do(component.OnDestroy); // OnDestroy is only called if the component has previously been active
            component.Destroy();
        }
        _components.Clear();
        _componentCache.Clear();

        Internal_DestroyCommitted?.Invoke(this);
    }

    private void SetEnabled(bool state)
    {
        _enabled = state;
        HierarchyStateChanged();
    }

    private void HierarchyStateChanged()
    {
        bool newState = _enabled && IsParentEnabled();
        if (_enabledInHierarchy != newState)
        {
            _enabledInHierarchy = newState;
            foreach (var component in GetComponents<MonoBehaviour>())
                component.HierarchyStateChanged();
        }

        foreach (var child in children)
            child.HierarchyStateChanged();
    }

    private bool IsParentEnabled() => parent == null || parent.enabledInHierarchy;

    public void DontDestroyOnLoad() => SceneManager._dontDestroyOnLoad.Add(InstanceID);

    /// <summary> Calls the method named methodName on every MonoBehaviour in this game object or any of its children. </summary>
    public void BroadcastMessage(string methodName, params object[] objs)
    {
        foreach (var component in GetComponents<MonoBehaviour>())
            component.SendMessage(methodName, objs);

        foreach (var child in children)
            child.BroadcastMessage(methodName, objs);
    }

    /// <summary> Calls the method named methodName on every MonoBehaviour in this game object. </summary>
    public void SendMessage(string methodName, params object[] objs)
    {
        foreach (var c in GetComponents<MonoBehaviour>())
        {
            MethodInfo method = c.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            method?.Invoke(c, objs);
        }
    }

    public SerializedProperty Serialize(Serializer.SerializationContext ctx)
    {
        SerializedProperty compoundTag = SerializedProperty.NewCompound();
        compoundTag.Add("Name", new SerializedProperty(Name));

        compoundTag.Add("Enabled", new SerializedProperty((byte)(_enabled ? 1 : 0)));
        compoundTag.Add("EnabledInHierarchy", new SerializedProperty((byte)(_enabledInHierarchy ? 1 : 0)));

        compoundTag.Add("TagIndex", new SerializedProperty(tagIndex));
        compoundTag.Add("LayerIndex", new SerializedProperty(layerIndex));

        compoundTag.Add("HideFlags", new SerializedProperty((int)hideFlags));

        compoundTag.Add("Transform", Serializer.Serialize(_transform, ctx));

        if (AssetID != Guid.Empty)
        {
            compoundTag.Add("AssetID", new SerializedProperty(AssetID.ToString()));
            if (FileID != 0)
                compoundTag.Add("FileID", new SerializedProperty(FileID));
        }

        SerializedProperty components = SerializedProperty.NewList();
        foreach (var comp in _components)
            components.ListAdd(Serializer.Serialize(comp, ctx));
        compoundTag.Add("Components", components);

        SerializedProperty children = SerializedProperty.NewList();
        foreach (var child in this.children)
            children.ListAdd(Serializer.Serialize(child, ctx));
        compoundTag.Add("Children", children);

        return compoundTag;
    }

    public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
    {
        Name = value["Name"].StringValue;
        _enabled = value["Enabled"].ByteValue == 1;
        _enabledInHierarchy = value["EnabledInHierarchy"].ByteValue == 1;
        tagIndex = value["TagIndex"].ByteValue;
        layerIndex = value["LayerIndex"].ByteValue;
        hideFlags = (HideFlags)value["HideFlags"].IntValue;

        _transform = Serializer.Deserialize<Transform>(value["Transform"], ctx) ?? new Transform();
        _transform.gameObject = this;

        if (value.TryGet("AssetID", out var guid))
            AssetID = Guid.Parse(guid.StringValue);
        if (value.TryGet("FileID", out var fileID))
            FileID = fileID.UShortValue;

        var children2 = value["Children"];
        this.children = new();
        foreach (var childTag in children2.List)
        {
            GameObject? child = Serializer.Deserialize<GameObject>(childTag, ctx);
            if (child == null) continue;
            child._parent = this;
            this.children.Add(child);
        }

        var comps = value["Components"];
        _components = new();
        foreach (var compTag in comps.List)
        {
            // Fallback for Missing Type
            SerializedProperty? typeProperty = compTag.Get("$type");
            // If the type is missing or string null/whitespace something is wrong, so just let the deserializer handle it, maybe it knows what to do
            if (typeProperty != null && !string.IsNullOrWhiteSpace(typeProperty.StringValue))
            {
                // Look for Monobehaviour Type
                Type oType = RuntimeUtils.FindType(typeProperty.StringValue);
                if (oType == null)
                {
                    Debug.LogWarning("Missing Monobehaviour Type: " + typeProperty.StringValue + " On " + Name);
                    MissingMonobehaviour missing = new MissingMonobehaviour();
                    missing.ComponentData = compTag;
                    _components.Add(missing);
                    _componentCache.Add(typeof(MissingMonobehaviour), missing);
                    continue;
                }
                else if (oType == typeof(MissingMonobehaviour))
                {
                    HandleMissingComponent(compTag, ctx);
                    continue;
                }
            }

            MonoBehaviour? component = Serializer.Deserialize<MonoBehaviour>(compTag, ctx);
            if (component == null) continue;
            _components.Add(component);
            _componentCache.Add(component.GetType(), component);
        }
        // Attach all components
        foreach (var comp in _components)
            comp.AttachToGameObject(this);
    }

    private void HandleMissingComponent(SerializedProperty compTag, Serializer.SerializationContext ctx)
    {
        // Were missing! see if we can recover
        MissingMonobehaviour missing = Serializer.Deserialize<MissingMonobehaviour>(compTag, ctx);
        var oldData = missing.ComponentData;
        // Try to recover the component
        if (oldData.TryGet("$type", out var typeProp))
        {
            Type oType = RuntimeUtils.FindType(typeProp.StringValue);
            if (oType != null)
            {
                // We have the type! Deserialize it and add it to the components
                MonoBehaviour? component = Serializer.Deserialize<MonoBehaviour>(oldData);
                if (component != null)
                {
                    _components.Add(component);
                    _componentCache.Add(component.GetType(), component);
                }
            }
        }
    }
}
