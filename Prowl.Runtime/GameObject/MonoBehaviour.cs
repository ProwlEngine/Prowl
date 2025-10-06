// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Echo;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

/// <summary>
/// Represents the base class for all scripts that attach to GameObjects in the Prowl Game Engine.
/// MonoBehaviour provides lifecycle methods and coroutine functionality for game object behaviors.
/// </summary>
public abstract class MonoBehaviour : EngineObject
{
    [SerializeField]
    private Guid _identifier = Guid.NewGuid();

    [SerializeField]
    protected internal bool _enabled = true;
    [SerializeField]
    protected internal bool _enabledInHierarchy = true;

    private Dictionary<string, Coroutine> _coroutines = new();
    private Dictionary<string, Coroutine> _endOfFrameCoroutines = new();
    private Dictionary<string, Coroutine> _fixedUpdateCoroutines = new();

    private GameObject _go;

    /// <summary>
    /// Gets or sets the hide flags for this MonoBehaviour.
    /// </summary>
    public HideFlags hideFlags;

    [SerializeIgnore]
    private bool _executeAlways = false;
    [SerializeIgnore]
    private bool _hasAwoken = false;
    [SerializeIgnore]
    private bool _hasStarted = false;

    /// <summary>
    /// Gets the identifier for this MonoBehaviour.
    /// Generally shouldnt be set manually
    /// </summary>
    public Guid Identifier { get => _identifier; set => _identifier = value; }

    /// <summary>
    /// Gets the GameObject this MonoBehaviour is attached to.
    /// </summary>
    public GameObject GameObject => _go;

    /// <summary>
    /// Gets the Transform component of the GameObject this MonoBehaviour is attached to.
    /// </summary>
    public Transform Transform => _go.Transform;

    /// <summary>
    /// Gets or sets whether this MonoBehaviour should execute in edit mode.
    /// </summary>
    public bool ExecuteAlways { get => _executeAlways; internal set => _executeAlways = value; }
    /// <summary>
    /// Gets whether the Awake method has been called.
    /// </summary>
    public bool HasAwoken { get => _hasAwoken; internal set => _hasAwoken = value; }
    /// <summary>
    /// Gets whether the Start method has been called.
    /// </summary>
    public bool HasStarted { get => _hasStarted; internal set => _hasStarted = value; }
    /// <summary>
    /// Gets the tag of the GameObject this MonoBehaviour is attached to.
    /// </summary>
    public string Tag => _go.tag;

    /// <summary>
    /// Gets or sets whether the MonoBehaviour is enabled.
    /// </summary>
    public bool Enabled
    {
        get { return _enabled; }
        set
        {
            if (value != _enabled)
            {
                _enabled = value;
                HierarchyStateChanged();
            }
        }
    }

    /// <summary>
    /// Gets whether the MonoBehaviour is enabled in the hierarchy (considering parent objects).
    /// </summary>
    public bool EnabledInHierarchy => _enabledInHierarchy;

    /// <summary>
    /// The parent <see cref="Prowl.Runtime.Scene"/> to which this <see cref="Prowl.Runtime.MonoBehaviour"/> belongs.
    /// 
    /// Note that this property is derived from the components <see cref="GameObject"/>, as a
    /// <see cref="Prowl.Runtime.MonoBehaviour"/> itself cannot be part of a <see cref="Prowl.Runtime.Scene"/> without a 
    /// <see cref="GameObject"/>.
    /// </summary>
    public Scene? Scene => GameObject?.Scene ?? null;

    public MonoBehaviour() : base() { }

    /// <summary>
    /// Compares the tag of the GameObject this MonoBehaviour is attached to with the specified tag.
    /// </summary>
    /// <param name="otherTag">The tag to compare against.</param>
    /// <returns>True if the tags match, false otherwise.</returns>
    public bool CompareTag(string otherTag) => _go.CompareTag(otherTag);

    #region Component API
    // This is used to make the Component API more similar to Unity's, Its generally recommended to use the GameObject instead
    /// <inheritdoc cref="GameObject.AddComponent{T}"/>"
    public T AddComponent<T>() where T : MonoBehaviour, new() => (T)AddComponent(typeof(T));
    /// <inheritdoc cref="GameObject.AddComponent(Type)"/>"
    public MonoBehaviour AddComponent(Type type) => GameObject.AddComponent(type);
    /// <inheritdoc cref="GameObject.RemoveComponent{T}"/>"
    public void RemoveComponent<T>(T component) where T : MonoBehaviour => GameObject.RemoveComponent(component);
    /// <inheritdoc cref="GameObject.RemoveComponent(Type)"/>"
    public void RemoveComponent(MonoBehaviour component) => GameObject.RemoveComponent(component);
    /// <inheritdoc cref="GameObject.RemoveComponent(MonoBehaviour)"/>"
    public void RemoveSelf() => GameObject.RemoveComponent(this);
    /// <inheritdoc cref="GameObject.GetComponent{T}"/>"
    public T? GetComponent<T>() where T : MonoBehaviour => GameObject.GetComponent<T>();
    /// <inheritdoc cref="GameObject.GetComponent(Type)"/>"
    public MonoBehaviour? GetComponent(Type type) => GameObject.GetComponent(type);
    /// <inheritdoc cref="GameObject.GetComponentByIdentifier(Guid)"/>"
    public MonoBehaviour? GetComponentByIdentifier(Guid identifier) => GameObject.GetComponentByIdentifier(identifier);
    /// <inheritdoc cref="GameObject.TryGetComponent{T}(out T)"/>"
    public bool TryGetComponent<T>(out T component) where T : MonoBehaviour => (component = GetComponent<T>()) != null;
    /// <inheritdoc cref="GameObject.GetComponents{T}"/>"
    public IEnumerable<T> GetComponents<T>() where T : MonoBehaviour => GameObject.GetComponents<T>();
    /// <inheritdoc cref="GameObject.GetComponents(Type)"/>"
    public IEnumerable<MonoBehaviour> GetComponents(Type type) => GameObject.GetComponents(type);
    /// <inheritdoc cref="GameObject.GetComponentInParent{T}"/>"
    public T? GetComponentInParent<T>(bool includeSelf = true) where T : MonoBehaviour => GameObject.GetComponentInParent<T>(includeSelf);
    /// <inheritdoc cref="GameObject.GetComponentInParent(Type, bool, bool)"/>"
    public MonoBehaviour? GetComponentInParent(Type componentType, bool includeSelf = true) => GameObject.GetComponentInParent(componentType, includeSelf);
    /// <inheritdoc cref="GameObject.GetComponentsInParent{T}"/>"
    public IEnumerable<T> GetComponentsInParent<T>(bool includeSelf = true) where T : MonoBehaviour => GameObject.GetComponentsInParent<T>(includeSelf);
    /// <inheritdoc cref="GameObject.GetComponentsInParent(Type, bool, bool)"/>"
    public IEnumerable<MonoBehaviour> GetComponentsInParent(Type type, bool includeSelf = true) => GameObject.GetComponentsInParent(type, includeSelf);
    /// <inheritdoc cref="GameObject.GetComponentInChildren{T}"/>"
    public T? GetComponentInChildren<T>(bool includeSelf = true) where T : MonoBehaviour => GameObject.GetComponentInChildren<T>(includeSelf);
    /// <inheritdoc cref="GameObject.GetComponentInChildren(Type, bool, bool)"/>"
    public MonoBehaviour? GetComponentInChildren(Type componentType, bool includeSelf = true) => GameObject.GetComponentInChildren(componentType, includeSelf);
    /// <inheritdoc cref="GameObject.GetComponentsInChildren{T}"/>"
    public IEnumerable<T> GetComponentsInChildren<T>(bool includeSelf = true) where T : MonoBehaviour => GameObject.GetComponentsInChildren<T>(includeSelf);
    /// <inheritdoc cref="GameObject.GetComponentsInChildren(Type, bool, bool)"/>"
    public IEnumerable<MonoBehaviour> GetComponentsInChildren(Type type, bool includeSelf = true) => GameObject.GetComponentsInChildren(type, includeSelf);


    /// <summary>
    /// Gets the index of this Component in its GameObject's Component list.
    /// </summary>
    /// <returns>The index of this Component in its GameObject's Component list, or null if it has no GameObject.</returns>
    /// <exception cref="Exception">Thrown if the Component is not found in its GameObject's Component list.</exception>q
    public int? GetSiblingIndex()
    {
        if (GameObject == null) return null;

        for (int i = 0; i < GameObject._components.Count; i++)
            if (object.ReferenceEquals(GameObject._components[i], this))
                return i;

        throw new Exception($"This Component appears to be in Limbo, This should never happen!, The Component believes its a child of {GameObject.Name} but they don't have it as an attatched component!");
    }

    /// <summary>
    /// Sets the index of this Component in its GameObject's Component list.
    /// </summary>
    /// <param name="index">The new index of this Component.</param>
    public void SetSiblingIndex(int index)
    {
        if (GameObject == null) return;

        // Remove this object from current position
        GameObject._components.Remove(this);

        // Ensure index is within bounds
        index = Math.Max(0, Math.Min(index, GameObject._components.Count));

        // Insert at new position
        GameObject._components.Insert(index, this);
    }
    #endregion

    /// <summary>
    /// Attaches this MonoBehaviour to the specified GameObject.
    /// </summary>
    /// <param name="go">The GameObject to attach to.</param>
    internal void AttachToGameObject(GameObject go)
    {
        _go = go;

        bool isEnabled = _enabled && _go.enabledInHierarchy;
        _enabledInHierarchy = isEnabled;
    }

    /// <summary>
    /// Updates the enabled state based on changes in the hierarchy.
    /// </summary>
    internal void HierarchyStateChanged()
    {
        bool newState = _enabled && _go.enabledInHierarchy;
        if (newState != _enabledInHierarchy)
        {
            _enabledInHierarchy = newState;
            if (newState)
                Do(OnEnable);
            else
                Do(OnDisable);
        }
    }

    /// <summary>
    /// Checks if this MonoBehaviour can be destroyed.
    /// </summary>
    /// <returns>True if the MonoBehaviour can be destroyed, false otherwise.</returns>
    internal bool CanDestroy()
    {
        if (_go.IsComponentRequired(this, out Type dependentType))
        {
            Debug.LogError("Can't remove " + GetType().Name + " because " + dependentType.Name + " depends on it");
            return false;
        }
        return true;
    }

    #region Behaviour

    // Lifecycle methods

    /// <summary>
    /// Called when the script instance is being loaded.
    /// </summary>
    public virtual void Awake() { }

    /// <summary>
    /// Called when the object becomes enabled and active.
    /// </summary>
    public virtual void OnEnable() { }

    /// <summary>
    /// Called when the object becomes disabled or inactive.
    /// </summary>
    public virtual void OnDisable() { }

    /// <summary>
    /// Called before the first frame update.
    /// </summary>
    public virtual void Start() { }

    /// <summary>
    /// Called at fixed time intervals.
    /// </summary>
    public virtual void FixedUpdate() { }

    /// <summary>
    /// Called every frame.
    /// </summary>
    public virtual void Update() { }

    /// <summary>
    /// Called every frame after all Update functions have been called.
    /// </summary>
    public virtual void LateUpdate() { }

    /// <summary>
    /// Called for rendering and handling GUI gizmos.
    /// </summary>
    public virtual void DrawGizmos() { }

    ///// <summary>
    ///// Called for drawing and handling interaction with Runtime/Ingame UI
    ///// Executed on any camera with the GUILayer component
    ///// </summary>
    ///// <param name="gui"></param>
    //public virtual void OnGUI(Gui gui) { }

    /// <summary>
    /// Called when the MonoBehaviour will be destroyed.
    /// </summary>
    public virtual void OnDestroy() { }

    /// <summary>
    /// Internal method to handle the Awake lifecycle event.
    /// </summary>
    internal void InternalAwake()
    {
        if (HasAwoken) return;
        HasAwoken = true;
        Awake();

        if (EnabledInHierarchy)
            Do(OnEnable);
    }

    /// <summary>
    /// Internal method to handle the Start lifecycle event.
    /// </summary>
    internal void InternalStart()
    {
        if (HasStarted) return;
        HasStarted = true;
        Start();
    }

    /// <summary>
    /// Executes the specified action, considering the ExecuteAlways attribute.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    internal void Do(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error: {e.Message} \n StackTrace: {e.StackTrace}");
        }
    }

    public override void OnDispose()
    {
        if (GameObject != null)
            GameObject.RemoveComponent(this);
    }

    /// <summary>
    /// Starts a coroutine with the specified method name.
    /// </summary>
    /// <param name="methodName">The name of the coroutine method to start.</param>
    /// <returns>A Coroutine object representing the started coroutine.</returns>
    public Coroutine StartCoroutine(string methodName)
    {
        methodName = methodName.Trim();
        var method = GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (method == null)
        {
            Debug.LogError("Coroutine '" + methodName + "' couldn't be started, the method doesn't exist.");
            return null;
        }

        var coroutine = new Coroutine(method.Invoke(this, null) as IEnumerator);

        if (coroutine.Enumerator.Current is WaitForEndOfFrame)
            _endOfFrameCoroutines.Add(methodName, coroutine);
        else if (coroutine.Enumerator.Current is WaitForFixedUpdate)
            _fixedUpdateCoroutines.Add(methodName, coroutine);
        else
            _coroutines.Add(methodName, coroutine);

        return coroutine;
    }

    /// <summary>
    /// Stops all running coroutines on this MonoBehaviour.
    /// </summary>
    public void StopAllCoroutines()
    {
        _coroutines.Clear();
        _endOfFrameCoroutines.Clear();
        _fixedUpdateCoroutines.Clear();
    }

    /// <summary>
    /// Stops the coroutine with the specified method name.
    /// </summary>
    /// <param name="methodName">The name of the coroutine method to stop.</param>
    public void StopCoroutine(string methodName)
    {
        methodName = methodName.Trim();
        _coroutines.Remove(methodName);
        _endOfFrameCoroutines.Remove(methodName);
        _fixedUpdateCoroutines.Remove(methodName);
    }

    /// <summary>
    /// Base class for all yield instructions used in coroutines.
    /// </summary>
    public class YieldInstruction
    {
    }

    /// <summary>
    /// Suspends the coroutine execution for the given amount of seconds.
    /// </summary>
    public class WaitForSeconds : YieldInstruction
    {
        public double Duration { get; private set; }
        public WaitForSeconds(float seconds)
        {
            Duration = Time.time + seconds;
        }
    }

    /// <summary>
    /// Waits until the end of the frame after all cameras and GUI is rendered, just before displaying the frame on screen.
    /// </summary>
    public class WaitForEndOfFrame : YieldInstruction
    {
    }

    /// <summary>
    /// Waits until the next fixed frame rate update function.
    /// </summary>
    public class WaitForFixedUpdate : YieldInstruction
    {
    }

    /// <summary>
    /// Represents a coroutine in the Prowl Game Engine.
    /// </summary>
    public sealed class Coroutine : YieldInstruction
    {
        internal bool isDone { get; private set; }
        internal IEnumerator Enumerator { get; private set; }
        internal Coroutine(IEnumerator routine)
        {
            Enumerator = routine;
        }

        internal bool CanRun
        {
            get
            {
                object current = Enumerator.Current;

                if (current is Coroutine)
                {
                    Coroutine dep = current as Coroutine;
                    return dep.isDone;
                }
                else if (current is WaitForSeconds)
                {
                    WaitForSeconds wait = current as WaitForSeconds;
                    return wait.Duration <= Time.time;
                }
                else
                {
                    return true;
                }
            }
        }

        internal void Run()
        {
            if (CanRun)
            {
                isDone = !Enumerator.MoveNext();
            }
        }
    }

    internal void UpdateCoroutines()
    {
        _coroutines ??= new Dictionary<string, Coroutine>();
        var tempList = new Dictionary<string, Coroutine>(_coroutines);
        _coroutines.Clear();
        foreach (var coroutine in tempList)
        {
            coroutine.Value.Run();
            if (coroutine.Value.isDone)
            {
                if (coroutine.Value.Enumerator.Current is WaitForEndOfFrame)
                    _endOfFrameCoroutines.Add(coroutine.Key, coroutine.Value);
                else if (coroutine.Value.Enumerator.Current is WaitForFixedUpdate)
                    _fixedUpdateCoroutines.Add(coroutine.Key, coroutine.Value);
                else
                    _coroutines.Add(coroutine.Key, coroutine.Value);
            }
        }
    }

    internal void UpdateEndOfFrameCoroutines()
    {
        _endOfFrameCoroutines ??= new Dictionary<string, Coroutine>();
        var tempList = new Dictionary<string, Coroutine>(_endOfFrameCoroutines);
        _endOfFrameCoroutines.Clear();
        foreach (var coroutine in tempList)
        {
            coroutine.Value.Run();
            if (coroutine.Value.isDone)
            {
                if (coroutine.Value.Enumerator.Current is WaitForEndOfFrame)
                    _endOfFrameCoroutines.Add(coroutine.Key, coroutine.Value);
                else
                    _coroutines.Add(coroutine.Key, coroutine.Value);
            }
        }
    }

    internal void UpdateFixedUpdateCoroutines()
    {
        _fixedUpdateCoroutines ??= new Dictionary<string, Coroutine>();
        var tempList = new Dictionary<string, Coroutine>(_fixedUpdateCoroutines);
        _fixedUpdateCoroutines.Clear();
        foreach (var coroutine in tempList)
        {
            coroutine.Value.Run();
            if (coroutine.Value.isDone)
            {
                if (coroutine.Value.Enumerator.Current is WaitForFixedUpdate)
                    _fixedUpdateCoroutines.Add(coroutine.Key, coroutine.Value);
                else
                    _coroutines.Add(coroutine.Key, coroutine.Value);
            }
        }
    }

    /// <summary>
    /// Calls the method named methodName on every MonoBehaviour in this game object or any of its children.
    /// </summary>
    /// <param name="methodName">The name of the method to call.</param>
    /// <param name="objs">Optional parameters to pass to the method.</param>
    public void BroadcastMessage(string methodName, params object[] objs) => GameObject.BroadcastMessage(methodName, objs);

    /// <summary>
    /// Calls the method named methodName on every MonoBehaviour in this game object.
    /// </summary>
    /// <param name="methodName">The name of the method to call.</param>
    /// <param name="objs">Optional parameters to pass to the method.</param>
    public void SendMessage(string methodName, params object[] objs) => GameObject.SendMessage(methodName, objs);

    #endregion
}
