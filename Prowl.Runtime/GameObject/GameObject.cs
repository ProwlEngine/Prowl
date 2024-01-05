using HexaEngine.ImGuizmoNET;
using Prowl.Runtime.SceneManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

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

    private MultiValueDictionary<Type, MonoBehaviour> _componentCache = new();

    private bool _enabled = true;
    private bool _enabledInHierarchy = true;

    internal WeakReference<Transform>? _transform;

    // We dont serialize parent, since if we want to serialize X object who is a child to Y object, we dont want to serialize Y object as well.
    // The parent is reconstructed when the object is deserialized for all children.
    internal GameObject? parent;

    #endregion

    #region Public Fields/Properties

    /// <summary> The Tag Index of this GameObject </summary>
    public int tagIndex;

    /// <summary> The Layer Index of this GameObject </summary>
    public int layerIndex;

    /// <summary> The Hide Flags of this GameObject, Used to hide the GameObject from a variety of places like Serializing, Inspector or Hierarchy </summary>
    public HideFlags hideFlags = HideFlags.None;

    /// <summary> Gets whether or not this gameobject is enabled explicitly </summary>
    public bool Enabled {
        get { return _enabled; }
        set { if (value != _enabled) { SetEnabled(value); } }
    }

    /// <summary> Gets whether this gameobejct is enabled in the hierarchy, so if its parent is disabled this will return false </summary>
    public bool EnabledInHierarchy => _enabledInHierarchy;

    /// <summary> The Tag of this GameObject </summary>
    public string tag {
        get => TagLayerManager.tags[tagIndex];
        set => tagIndex = TagLayerManager.GetTagIndex(value);
    }

    /// <summary> The Layer of this GameObject </summary>
    public string layer {
        get => TagLayerManager.layers[layerIndex];
        set => layerIndex = TagLayerManager.GetLayerIndex(value);
    }

    /// <summary> The Parent of this GameObject, Can be null </summary>
    public GameObject? Parent => parent;

    /// <summary> The Transform of this GameObject, If this GameObject has no Transform it will fallback to one in a parent, Can be null </summary>
    public Transform? Transform {
        get {
            _transform ??= new(GetComponentInParent<Transform>()); // Fallback to Parent transform if one exists
            return _transform.TryGetTarget(out var t) ? t : null;
        }
    }

    /// <summary> A List of all children of this GameObject </summary>
    public List<GameObject> Children = new List<GameObject>();

    #endregion

    public void SetParent(GameObject? newParent)
    {
        if (newParent == parent || newParent == this)
            return;

        parent?.Children.Remove(this);

        var parentToChild = false;
        if (Children.Count > 0)
        {
            var gameobjects = new Stack<GameObject>();
            gameobjects.Push(this);

            var currentTransform = gameobjects.Peek();
            while (gameobjects.Count > 0)
            {
                foreach (var child in currentTransform.Children)
                {
                    if (child == newParent)
                    {
                        parentToChild = true;
                        break;
                    }

                    gameobjects.Push(child);
                }
                if (parentToChild)
                    break;

                currentTransform = gameobjects.Pop();
            }
        }

        if (parentToChild)
        {
            var tempList = Children.ToList();
            foreach (var child in tempList)
                child.SetParent(parent);

            Children.Clear();
        }

        parent = newParent;
        newParent?.Children.Add(this);

        _transform = null; // Reset cached Transform
        Transform?.Recalculate();
        HierarchyStateChanged();
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

    /// <summary>
    /// 
    /// </summary>
    /// <param name="otherTag"></param>
    /// <returns></returns>
    public bool CompareTag(string otherTag) => tagIndex == TagLayerManager.GetTagIndex(otherTag);

    public static GameObject Find(string otherName) => FindObjectsOfType<GameObject>().FirstOrDefault(gameObject => gameObject.Name == otherName);

    public static GameObject FindGameObjectWithTag(string otherTag) => FindObjectsOfType<GameObject>().FirstOrDefault(gameObject => gameObject.CompareTag(otherTag));

    public static GameObject[] FindGameObjectsWithTag(string otherTag) => FindObjectsOfType<GameObject>().Where(gameObject => gameObject.CompareTag(otherTag)).ToArray();

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

        return newComponent;
    }

    public void AddComponentDirectly(MonoBehaviour comp)
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
    }

    public void RemoveAll<T>() where T : MonoBehaviour
    {
        IReadOnlyCollection<MonoBehaviour> components;
        if (_componentCache.TryGetValue(typeof(T), out components))
        {
            foreach (MonoBehaviour c in components)
                if (c.EnabledInHierarchy)
                    c.Internal_OnDisabled();
            foreach (MonoBehaviour c in components)
            {
                if (c.HasBeenEnabled) // OnDestroy is only called if the component has previously been active
                    c.Internal_OnDestroy();

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

        if (component.EnabledInHierarchy) component.Internal_OnDisabled();
        if (component.HasBeenEnabled) component.Internal_OnDestroy(); // OnDestroy is only called if the component has previously been active
    }

    public void RemoveComponent(MonoBehaviour component)
    {
        if (component.CanDestroy() == false) return;

        _components.Remove(component);
        _componentCache.Remove(component.GetType(), component);

        if (component.EnabledInHierarchy) component.Internal_OnDisabled();
        if (component.HasBeenEnabled) component.Internal_OnDestroy(); // OnDestroy is only called if the component has previously been active
    }

    public T GetComponent<T>() where T : MonoBehaviour => (T)GetComponent(typeof(T));

    public MonoBehaviour GetComponent(Type type)
    {
        if (_componentCache.TryGetValue(type, out var components))
            return components.First();
        else
            foreach (var comp in _components)
                if (comp.GetType().IsAssignableTo(type))
                    return comp;
        return null;
    }

    public IEnumerable<MonoBehaviour> GetComponents() => _components;

    public bool TryGetComponent<T>(out T component) where T : MonoBehaviour => (component = GetComponent<T>()) != null;

    public IEnumerable<T> GetComponents<T>() where T : MonoBehaviour
    {
        if (typeof(T) == typeof(MonoBehaviour))
        {
            // Special case for Component
            foreach (var comp in _components)
                yield return (T)comp;
        }
        else
        {
            if (!_componentCache.TryGetValue(typeof(T), out var components))
            {
                foreach (var kvp in _componentCache.ToArray())
                    if (kvp.Key.GetTypeInfo().IsAssignableTo(typeof(T)))
                        foreach (var comp in kvp.Value.ToArray())
                            yield return (T)comp;
            }
            else
            {
                foreach (var comp in components)
                    if (comp.GetType().IsAssignableTo(typeof(T)))
                        yield return (T)comp;
            }
        }
    }

    public T GetComponentInParent<T>(bool includeSelf = true) where T : MonoBehaviour => (T)GetComponentInParent(typeof(T), includeSelf);

    public MonoBehaviour GetComponentInParent(Type componentType, bool includeSelf = true)
    {
        // First check the current Object
        MonoBehaviour component;
        if (includeSelf) {
            component = GetComponent(componentType);
            if (component != null)
                return component;
        }
        // Now check all parents
        GameObject parent = this;
        while ((parent = parent.Parent) != null)
        {
            component = parent.GetComponent(componentType);
            if (component != null)
                return component;
        }
        return null;
    }

    public IEnumerable<T> GetComponentsInParent<T>(bool includeSelf = true) where T : MonoBehaviour
    {
        // First check the current Object
        if (includeSelf)
            foreach (var component in GetComponents<T>())
                yield return component;
        // Now check all parents
        GameObject parent = this;
        while ((parent = parent.Parent) != null) {
            foreach (var component in parent.GetComponents<T>())
                yield return component;
        }
    }

    public T GetComponentInChildren<T>(bool includeSelf = true) where T : MonoBehaviour => (T)GetComponentInChildren(typeof(T), includeSelf);

    public MonoBehaviour GetComponentInChildren(Type componentType, bool includeSelf = true)
    {
        // First check the current Object
        MonoBehaviour component;
        if (includeSelf) {
            component = GetComponent(componentType);
            if (component != null)
                return component;
        }
        // Now check all children
        foreach (var child in Children)
        {
            component = child.GetComponent(componentType) ?? child.GetComponentInChildren(componentType);
            if (component != null)
                return component;
        }
        return null;
    }


    public IEnumerable<T> GetComponentsInChildren<T>(bool includeSelf = true) where T : MonoBehaviour
    {
        // First check the current Object
        if (includeSelf)
            foreach (var component in GetComponents<T>())
                yield return component;
        // Now check all children
        foreach (var child in Children) {
            foreach (var component in child.GetComponentsInChildren<T>())
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

    public void DrawGizmos(System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 projection, bool isSelected)
    {
        if (hideFlags.HasFlag(HideFlags.NoGizmos)) return;

        Transform myTransform = GetComponent<Transform>();
        if (isSelected && myTransform != null) {
            var goMatrix = myTransform.Local;

            // Perform ImGuizmo manipulation
            var fmat = goMatrix.ToFloat();
            if (ImGuizmo.Manipulate(ref view, ref projection, SceneManager.GizmosOperation, SceneManager.GizmosSpace, ref fmat)) {
                goMatrix = fmat.ToDouble();
                myTransform.Local = goMatrix;
            }
        }

        foreach (var component in _components)
        {
            component.Internal_DrawGizmos();
            if(isSelected) component.Internal_DrawGizmosSelected();
        }
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
        var t = clone.AddComponent<Transform>();
        t.GlobalPosition = position;
        t.GlobalOrientation = rotation;
        clone.SetParent(parent);
        return clone;
    }

    public override void OnDispose()
    {
        // Internal_DestroyCommitted removes the child from the parent
        // Hense why we do a while loop on the first element instead of a foreach/for
        while(Children.Count > 0)
            Children[0].Dispose();

        foreach (var component in _components)
        {
            if (component.EnabledInHierarchy) component.Internal_OnDisabled();
            if (component.HasBeenEnabled) component.Internal_OnDestroy(); // OnDestroy is only called if the component has previously been active
            component.Dispose();
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
        _transform = null; // Reset cached Transform

        bool newState = _enabled && IsParentEnabled();
        if (_enabledInHierarchy != newState)
        {
            _enabledInHierarchy = newState;
            foreach (var component in GetComponents<MonoBehaviour>())
                component.HierarchyStateChanged();
        }

		foreach (var child in Children)
			child.HierarchyStateChanged();
	}

    private bool IsParentEnabled() => Parent == null || Parent.EnabledInHierarchy;

    public void DontDestroyOnLoad() => SceneManager._dontDestroyOnLoad.Add(InstanceID);

    /// <summary> Calls the method named methodName on every MonoBehaviour in this game object or any of its children. </summary>
    public void BroadcastMessage(string methodName, params object[] objs)
    {
        foreach (var component in GetComponents<MonoBehaviour>())
            component.SendMessage(methodName, objs);

        foreach (var child in Children)
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


    [OnDeserialized]
    internal void OnDeserializedMethod(StreamingContext context)
    {
        foreach (var child in Children)
            child.parent = this;

        // Update Component Cache
        _componentCache = new MultiValueDictionary<Type, MonoBehaviour>();
        foreach (var component in _components)
        {
            component.AttachToGameObject(this);
            _componentCache.Add(component.GetType(), component);
        }

        Transform?.Recalculate();
    }

    public CompoundTag Serialize(TagSerializer.SerializationContext ctx)
    {
        CompoundTag compoundTag = new CompoundTag();
        compoundTag.Add("Name", new StringTag(Name));

        compoundTag.Add("Enabled", new ByteTag((byte)(_enabled ? 1 : 0)));
        compoundTag.Add("EnabledInHierarchy", new ByteTag((byte)(_enabledInHierarchy ? 1 : 0)));

        compoundTag.Add("TagIndex", new IntTag(tagIndex));
        compoundTag.Add("LayerIndex", new IntTag(layerIndex));

        compoundTag.Add("HideFlags", new IntTag((int)hideFlags));

        if(AssetID != Guid.Empty)
            compoundTag.Add("AssetID", new StringTag(AssetID.ToString()));

        ListTag components = new ListTag();
        foreach (var comp in _components)
            components.Add(TagSerializer.Serialize(comp, ctx));
        compoundTag.Add("Components", components);

        ListTag children = new ListTag();
        foreach (var child in Children)
            children.Add(TagSerializer.Serialize(child, ctx));
        compoundTag.Add("Children", children);

        return compoundTag;
    }

    public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
    {
        Name = value["Name"].StringValue;
        _enabled = value["Enabled"].ByteValue == 1;
        _enabledInHierarchy = value["EnabledInHierarchy"].ByteValue == 1;
        tagIndex = value["TagIndex"].IntValue;
        layerIndex = value["LayerIndex"].IntValue;
        hideFlags = (HideFlags)value["HideFlags"].IntValue;
        if(value.TryGet("AssetID", out StringTag guid))
            AssetID = Guid.Parse(guid.Value);

        ListTag comps = (ListTag)value["Components"];
        _components = new();
        Transform? transform = null;
        foreach (CompoundTag compTag in comps.Tags)
        {
            MonoBehaviour? component = TagSerializer.Deserialize<MonoBehaviour>(compTag, ctx);
            if (component == null) continue;
            component.AttachToGameObject(this);
            _components.Add(component);
            _componentCache.Add(component.GetType(), component);

            if (component is Transform t)
                transform = t;
        }

        ListTag children = (ListTag)value["Children"];
        Children = new();
        foreach (CompoundTag childTag in children.Tags)
        {
            GameObject? child = TagSerializer.Deserialize<GameObject>(childTag, ctx);
            if (child == null) continue;
            child.parent = this;
            Children.Add(child);
        }

        transform?.Recalculate();
    }
}