using HexaEngine.ImGuizmoNET;
using Prowl.Runtime.Components;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Serialization;

namespace Prowl.Runtime;

public class GameObject : EngineObject, ISerializable
{
    internal static event Action<GameObject>? Internal_Constructed;
    internal static event Action<GameObject>? Internal_DestroyCommitted;
    
    private List<MonoBehaviour> _components = new();

    private MultiValueDictionary<Type, MonoBehaviour> _componentCache = new();

    private bool _enabled = true;
    private bool _enabledInHierarchy = true;
     
    public bool Enabled
    {
        get { return _enabled; }
        set { if (value != _enabled) { SetEnabled(value); } }
    }

    public bool EnabledInHierarchy => _enabledInHierarchy;

    public int tagIndex;
    public int layerIndex;

    public HideFlags hideFlags = HideFlags.None;

    public string tag
    {
        get => TagLayerManager.tags[tagIndex];
        set => tagIndex = TagLayerManager.GetTagIndex(value);
    }

    public string layer
    {
        get => TagLayerManager.layers[layerIndex];
        set => layerIndex = TagLayerManager.GetLayerIndex(value);
    }

    #region Transform


    protected Vector3 position;
    protected Vector3 rotation;
    protected Vector3 scale = Vector3.One;
    protected Vector3 globalPosition;
    protected Vector3 velocity, oldpos;
    protected Vector3 globalScale;
    protected Quaternion orientation = Quaternion.Identity, globalOrientation;
    protected Vector3 forward, backward, left, right, up, down;
    protected Matrix4x4 globalPrevious, global, globalInverse;
    protected Matrix4x4 local, localInverse;

    // We dont serialize parent, since if we want to serialize X object who is a child to Y object, we dont want to serialize Y object as well.
    // The parent is reconstructed when the object is deserialized for all children.
    internal GameObject? parent;

    public List<GameObject> Children = new List<GameObject>();

    public GameObject? Parent => parent;

    /// <summary>Gets or sets the local position.</summary>
    public Vector3 Position
    {
        get => position;
        set
        {
            if (position == value) return;
            oldpos = position;
            position = value;
            velocity = position - oldpos;
            Recalculate();
        }
    }

    /// <summary>Gets or sets the local rotation.</summary>
    /// <remarks>The rotation is in space euler from 0° to 360°(359°)</remarks>
    public Vector3 Rotation
    {
        get => rotation;
        set
        {
            if (rotation == value) return;
            rotation = value;
            orientation = value.NormalizeEulerAngleDegrees().ToRad().GetQuaternion();
            Recalculate();
        }
    }

    /// <summary>Gets or sets the local scale.</summary>
    public Vector3 Scale
    {
        get => scale;
        set
        {
            if (scale == value) return;
            scale = value;
            Recalculate();
        }
    }

    /// <summary>Gets or sets the local orientation.</summary>
    public Quaternion Orientation
    {
        get => orientation;
        set
        {
            if (orientation == value) return;
            orientation = value;
            rotation = value.GetRotation().ToDeg().NormalizeEulerAngleDegrees();
            Recalculate();
        }
    }

    /// <summary>Gets or sets the global (world space) position.</summary>
    public Vector3 GlobalPosition
    {
        get => globalPosition;
        set
        {
            if (globalPosition == value)return;
            if (parent == null)
                position = value;
            else // Transform because the rotation could modify the position of the child.
                position = Vector3.Transform(value, parent.globalInverse);
            Recalculate();
        }
    }

    /// <summary>Gets or sets the global (world space) orientation.</summary>
    public Quaternion GlobalOrientation
    {
        get => globalOrientation;
        set
        {
            if (globalOrientation == value) return;
            if (parent == null)
                orientation = value;
            else // Divide because quaternions are like matrices.
                orientation = value / parent.globalOrientation;
            Recalculate();
        }
    }

    /// <summary>The forward vector in global orientation space</summary>
     public Vector3 Forward => forward;

    /// <summary>The backward vector in global orientation space</summary>
     public Vector3 Backward => backward;

    /// <summary>The left vector in global orientation space</summary>
     public Vector3 Left => left;

    /// <summary>The right vector in global orientation space</summary>
     public Vector3 Right => right;

    /// <summary>The up vector in global orientation space</summary>
     public Vector3 Up => up;

    /// <summary>The down vector in global orientation space</summary>
     public Vector3 Down => down;

    /// <summary>The global transformation matrix of the previous frame.</summary>
     public Matrix4x4 GlobalPrevious => globalPrevious;

    /// <summary>The global transformation matrix</summary>
     public Matrix4x4 Global { get => global; }

    /// <summary>The inverse global transformation matrix</summary>
     public Matrix4x4 GlobalInverse => globalInverse;

    /// <summary>The local transformation matrix</summary>
     public Matrix4x4 Local { get => local; set => SetMatrix(value); }

    /// <summary>The local inverse transformation matrix</summary>
     public Matrix4x4 LocalInverse => localInverse;

    /// <summary>The velocity of the object only useful for 3d sound.</summary>
     public Vector3 Velocity => velocity;

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

        Recalculate();
        HierarchyStateChanged();
    }

    /// <summary>Recalculates all values of the <see cref="Transform"/>.</summary>
    public void Recalculate()
    {
        globalPrevious = global;

        var s = Matrix4x4.CreateScale(scale);
        var r = Matrix4x4.CreateFromQuaternion(orientation);
        var t = Matrix4x4.CreateTranslation(position);
        local = s * r * t;
        //local = Matrix4.CreateScale(scale) * Matrix4.CreateFromQuaternion(orientation) * Matrix4.CreateTranslation(position);
        Matrix4x4.Invert(local, out localInverse);
        if (parent == null)
            global = local;
        else
            global = local * parent.global;

        Matrix4x4.Invert(global, out globalInverse);

        Matrix4x4.Decompose(global, out globalScale, out globalOrientation, out globalPosition);
        forward = Vector3.Transform(Vector3.UnitZ, globalOrientation);
        backward = Vector3.Transform(-Vector3.UnitZ, globalOrientation);
        right = Vector3.Transform(Vector3.UnitX, globalOrientation);
        left = Vector3.Transform(-Vector3.UnitX, globalOrientation);
        up = Vector3.Transform(Vector3.UnitY, globalOrientation);
        down = Vector3.Transform(-Vector3.UnitY, globalOrientation);

        foreach (var child in Children)
            child.Recalculate();
    }

    /// <summary>
    /// Reverse calculates the local params from a local matrix.
    /// </summary>
    /// <param name="matrix">Local space matrix</param>
    private void SetMatrix(Matrix4x4 matrix)
    {
        local = matrix;
        Matrix4x4.Invert(local, out localInverse);
        Matrix4x4.Decompose(local, out scale, out orientation, out position);
        rotation = orientation.GetRotation().ToDeg();
        Recalculate();
    }

    public static implicit operator Matrix4x4(GameObject go) => go.global;

    #endregion

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
    private GameObject(int dummy) : base("New GameObject") { Recalculate(); }

    /// <summary>Creates a new gameobject with tbe name 'New GameObject'.</summary>
    public GameObject() : base("New GameObject")
    {
        Recalculate();
        Internal_Constructed?.Invoke(this);
    }

    /// <summary>Creates a new gameobject.</summary>
    /// <param name="name">The name of the gameobject.</param>
    public GameObject(string name = "New GameObject") : base(name)
    {
        Recalculate();
        Internal_Constructed?.Invoke(this);
    }

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
            Debug.LogError("Can't Add the Same Component Multiple Times" + "The component of type " + type.Name + " does not allow multiple instances");
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
        if (component.EnabledInHierarchy) component.Internal_OnDisabled();
        if (component.HasBeenEnabled) component.Internal_OnDestroy(); // OnDestroy is only called if the component has previously been active
        _components.Remove(component);
        _componentCache.Remove(typeof(T), component);
    }

    public void RemoveComponent(MonoBehaviour component)
    {
        if (component.CanDestroy() == false) return;
        if (component.EnabledInHierarchy) component.Internal_OnDisabled();
        if (component.HasBeenEnabled) component.Internal_OnDestroy(); // OnDestroy is only called if the component has previously been active
        _components.Remove(component);
        _componentCache.Remove(component.GetType(), component);
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

    public T GetComponentInParent<T>() where T : MonoBehaviour => (T)GetComponentInParent(typeof(T));

    public MonoBehaviour GetComponentInParent(Type componentType)
    {
        // First check the current Object
        MonoBehaviour component = GetComponent(componentType);
        if (component != null)
            return component;
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

    public IEnumerable<T> GetComponentsInParent<T>() where T : MonoBehaviour
    {
        // First check the current Object
        foreach (var component in GetComponents<T>())
            yield return component;
        // Now check all parents
        GameObject parent = this;
        while ((parent = parent.Parent) != null)
        {
            foreach (var component in parent.GetComponents<T>())
                yield return component;
        }
    }

    public T GetComponentInChildren<T>() where T : MonoBehaviour => (T)GetComponentInChildren(typeof(T));

    public MonoBehaviour GetComponentInChildren(Type componentType)
    {
        // First check the current Object
        MonoBehaviour component = GetComponent(componentType);
        if (component != null)
            return component;
        // Now check all children
        foreach (var child in Children)
        {
            component = child.GetComponent(componentType) ?? child.GetComponentInChildren(componentType);
            if (component != null)
                return component;
        }
        return null;
    }


    public IEnumerable<T> GetComponentsInChildren<T>() where T : MonoBehaviour
    {
        // First check the current Object
        foreach (var component in GetComponents<T>())
            yield return component;
        // Now check all children
        foreach (var child in Children)
        {
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

    public void DrawGizmos(Matrix4x4 view, Matrix4x4 projection, bool isSelected)
    {
        if (hideFlags.HasFlag(HideFlags.NoGizmos)) return;

        if (isSelected)
        {
            if (GameObjectManager.GizmosSpace == ImGuizmoMode.Local)
            {
                var goMatrix = Local;
                if (ImGuizmo.Manipulate(ref view.M11, ref projection.M11, GameObjectManager.GizmosOperation, ImGuizmoMode.Local, ref goMatrix.M11))
                    Local = goMatrix;
            }
            else
            {
                var goMatrix = Global;
                if (ImGuizmo.Manipulate(ref view.M11, ref projection.M11, GameObjectManager.GizmosOperation, ImGuizmoMode.World, ref goMatrix.M11))
                    global = goMatrix;
            }
        }

        foreach (var component in _components)
        {
            component.Internal_DrawGizmos(view, projection);
            if(isSelected) component.Internal_DrawGizmosSelected(view, projection);
        }
    }

    public static GameObject Instantiate(GameObject original) => Instantiate(original, original.position, original.orientation, null);
    public static GameObject Instantiate(GameObject original, GameObject? parent) => Instantiate(original, original.position, original.orientation, parent);
    public static GameObject Instantiate(GameObject original, GameObject? parent, Vector3 position, Quaternion rotation) => Instantiate(original, position, rotation, parent);
    public static GameObject Instantiate(GameObject original, Vector3 position, Quaternion rotation, GameObject? parent) 
    {
        GameObject clone = (GameObject)EngineObject.Instantiate(original, false);
        clone.position = position;
        clone.orientation = rotation;
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

    private bool IsParentEnabled()
    {
        return Parent == null || Parent.EnabledInHierarchy;
    }

    public void DontDestroyOnLoad()
    {
        GameObjectManager._dontDestroyOnLoad.Add(InstanceID);
    }

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

        Recalculate();
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

        compoundTag.Add("PosX", new FloatTag(position.X));
        compoundTag.Add("PosY", new FloatTag(position.Y));
        compoundTag.Add("PosZ", new FloatTag(position.Z));

        compoundTag.Add("RotX", new FloatTag(rotation.X));
        compoundTag.Add("RotY", new FloatTag(rotation.Y));
        compoundTag.Add("RotZ", new FloatTag(rotation.Z));

        compoundTag.Add("ScalX", new FloatTag(scale.X));
        compoundTag.Add("ScalY", new FloatTag(scale.Y));
        compoundTag.Add("ScalZ", new FloatTag(scale.Z));

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
        position = new Vector3(value["PosX"].FloatValue, value["PosY"].FloatValue, value["PosZ"].FloatValue);
        rotation = new Vector3(value["RotX"].FloatValue, value["RotY"].FloatValue, value["RotZ"].FloatValue);
        scale = new Vector3(value["ScalX"].FloatValue, value["ScalY"].FloatValue, value["ScalZ"].FloatValue);

        ListTag comps = (ListTag)value["Components"];
        _components = new();
        foreach (CompoundTag compTag in comps.Tags)
        {
            MonoBehaviour? component = TagSerializer.Deserialize<MonoBehaviour>(compTag, ctx);
            if (component == null) continue;
            component.AttachToGameObject(this);
            _components.Add(component);
            _componentCache.Add(component.GetType(), component);
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

        Recalculate();
    }
}