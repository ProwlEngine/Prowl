// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime;

/// <summary>
/// Represents the base class for all scripts that attach to GameObjects in the Prowl Game Engine.
/// MonoBehaviour provides lifecycle methods and coroutine functionality for game object behaviors.
/// </summary>
public abstract class MonoBehaviour : EngineObject
[ManuallyCloned]
[CloneBehavior(CloneBehavior.Reference)]
{
    private static readonly Dictionary<Type, bool> CachedExecuteAlways = new();

    [SerializeField, HideInInspector]
    protected internal bool _enabled = true;
    [SerializeField, HideInInspector]
    protected internal bool _enabledInHierarchy = true;

    private Dictionary<string, Coroutine> _coroutines = new();
    private Dictionary<string, Coroutine> _endOfFrameCoroutines = new();
    private Dictionary<string, Coroutine> _fixedUpdateCoroutines = new();

    private GameObject _go;

    /// <summary>
    /// Gets or sets the hide flags for this MonoBehaviour.
    /// </summary>
    [HideInInspector]
    public HideFlags hideFlags;

    [SerializeIgnore, CloneField(CloneFieldFlags.Skip)]
    private bool _executeAlways = false;
    [SerializeIgnore, CloneField(CloneFieldFlags.Skip)]
    private bool _hasAwoken = false;
    [SerializeIgnore, CloneField(CloneFieldFlags.Skip)]
    private bool _hasStarted = false;

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

    public MonoBehaviour() : base() { }

    /// <summary>
    /// Compares the tag of the GameObject this MonoBehaviour is attached to with the specified tag.
    /// </summary>
    /// <param name="otherTag">The tag to compare against.</param>
    /// <returns>True if the tags match, false otherwise.</returns>
    public bool CompareTag(string otherTag) => _go.CompareTag(otherTag);

    #region Component API
    // This is used to make the Component API more similar to Unity's, Its generally recommended to use the GameObject instead
    public T AddComponent<T>() where T : MonoBehaviour, new() => (T)AddComponent(typeof(T));
    public MonoBehaviour AddComponent(Type type) => GameObject.AddComponent(type);
    public void RemoveAll<T>() where T : MonoBehaviour => GameObject.RemoveAll<T>();
    public void RemoveComponent<T>(T component) where T : MonoBehaviour => GameObject.RemoveComponent(component);
    public void RemoveComponent(MonoBehaviour component) => GameObject.RemoveComponent(component);
    public void RemoveSelf() => GameObject.RemoveComponent(this);
    public T? GetComponent<T>() where T : MonoBehaviour => GameObject.GetComponent<T>();
    public MonoBehaviour? GetComponent(Type type) => GameObject.GetComponent(type);
    public bool TryGetComponent<T>(out T component) where T : MonoBehaviour => (component = GetComponent<T>()) != null;
    public IEnumerable<T> GetComponents<T>() where T : MonoBehaviour => GameObject.GetComponents<T>();
    public IEnumerable<MonoBehaviour> GetComponents(Type type) => GameObject.GetComponents(type);
    public T? GetComponentInParent<T>(bool includeSelf = true) where T : MonoBehaviour => GameObject.GetComponentInParent<T>(includeSelf);
    public MonoBehaviour? GetComponentInParent(Type componentType, bool includeSelf = true) => GameObject.GetComponentInParent(componentType, includeSelf);
    public IEnumerable<T> GetComponentsInParent<T>(bool includeSelf = true) where T : MonoBehaviour => GameObject.GetComponentsInParent<T>(includeSelf);
    public IEnumerable<MonoBehaviour> GetComponentsInParent(Type type, bool includeSelf = true) => GameObject.GetComponentsInParent(type, includeSelf);
    public T? GetComponentInChildren<T>(bool includeSelf = true) where T : MonoBehaviour => GameObject.GetComponentInChildren<T>(includeSelf);
    public MonoBehaviour? GetComponentInChildren(Type componentType, bool includeSelf = true) => GameObject.GetComponentInChildren(componentType, includeSelf);
    public IEnumerable<T> GetComponentsInChildren<T>(bool includeSelf = true) where T : MonoBehaviour => GameObject.GetComponentsInChildren<T>(includeSelf);
    public IEnumerable<MonoBehaviour> GetComponentsInChildren(Type type, bool includeSelf = true) => GameObject.GetComponentsInChildren(type, includeSelf);
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
#warning "Need to apply this to Component Deletion in Inspector, to make sure not to delete dependant Components"
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
    /// Called for rendering and handling GUI events.
    /// </summary>
    public virtual void DrawGizmos() { }

    /// <summary>
    /// Called for rendering and handling GUI events when the object is selected.
    /// </summary>
    public virtual void DrawGizmosSelected() { }

    /// <summary>
    /// Called when a new level is loaded.
    /// </summary>
    public virtual void OnLevelWasLoaded() { }

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
        bool always;
        if (CachedExecuteAlways.TryGetValue(GetType(), out bool value))
            always = value;
        else
        {
            always = GetType().GetCustomAttribute<ExecuteAlwaysAttribute>() != null;
            CachedExecuteAlways[GetType()] = always;
        }

        try
        {
            if (Application.IsPlaying || always)
                action();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error: {e.Message} \n StackTrace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Clears the cached ExecuteAlways attributes when the assembly is unloaded.
    /// </summary>
    [OnAssemblyUnload]
    public static void ClearCache() => CachedExecuteAlways.Clear();

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

    /// <summary>
    /// Creates a deep copy of this Component.
    /// </summary>
    /// <returns>A reference to a newly created deep copy of this Component.</returns>
    public MonoBehaviour Clone()
    {
        return this.DeepClone();
    }
    /// <summary>
    /// Deep-copies this Components data to the specified target Component. If source and 
    /// target Component Type do not match, the operation will fail.
    /// </summary>
    /// <param name="target">The target Component to copy to.</param>
    public void CopyTo(MonoBehaviour target)
    {
        this.DeepCopyTo(target);
    }

    void ICloneExplicit.SetupCloneTargets(object targetObj, ICloneTargetSetup setup)
    {
        MonoBehaviour target = targetObj as MonoBehaviour;
        this.OnSetupCloneTargets(targetObj, setup);
    }

    void ICloneExplicit.CopyDataTo(object targetObj, ICloneOperation operation)
    {
        MonoBehaviour target = targetObj as MonoBehaviour;
        target._enabled = _enabled;
        target._enabledInHierarchy = _enabledInHierarchy;
        this.OnCopyDataTo(targetObj, operation);
    }

    /// <summary>
    /// This method prepares the <see cref="CopyTo"/> operation for custom Component Types.
    /// It uses reflection to prepare the cloning operation automatically, but you can implement
    /// this method in order to handle certain fields and cases manually. See <see cref="ICloneExplicit.SetupCloneTargets"/>
    /// for a more thorough explanation.
    /// </summary>
    protected virtual void OnSetupCloneTargets(object target, ICloneTargetSetup setup)
    {
        setup.HandleObject(this, target);
    }

    /// <summary>
    /// This method performs the <see cref="CopyTo"/> operation for custom Component Types.
    /// It uses reflection to perform the cloning operation automatically, but you can implement
    /// this method in order to handle certain fields and cases manually. See <see cref="ICloneExplicit.CopyDataTo"/>
    /// for a more thorough explanation.
    /// </summary>
    /// <param name="target">The target Component where this Components data is copied to.</param>
    /// <param name="operation"></param>
    protected virtual void OnCopyDataTo(object target, ICloneOperation operation)
    {
        operation.HandleObject(this, target);
    }

    #endregion
}
