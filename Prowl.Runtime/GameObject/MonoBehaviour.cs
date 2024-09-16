// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime;

public abstract class MonoBehaviour : EngineObject
{
    [SerializeField, HideInInspector]
    internal protected bool _enabled = true;
    [SerializeField, HideInInspector]
    internal protected bool _enabledInHierarchy = true;

    [HideInInspector]
    public HideFlags hideFlags;

    private Dictionary<string, Coroutine> _coroutines = new();
    private Dictionary<string, Coroutine> _endOfFrameCoroutines = new();
    private Dictionary<string, Coroutine> _fixedUpdateCoroutines = new();


    private GameObject _go;

    public GameObject GameObject => _go;
    public Transform Transform => _go.Transform;

    public bool ExecuteAlways { get; internal set; } = false;
    public bool HasAwoken { get; internal set; } = false;
    public bool HasStarted { get; internal set; } = false;

    public string Tag => _go.tag;

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

    public bool EnabledInHierarchy => _enabledInHierarchy;

    public MonoBehaviour() : base() { }

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
    public bool TryGetComponent<T>(out T? component) where T : MonoBehaviour => (component = GetComponent<T>()) != null;
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

    internal void AttachToGameObject(GameObject go)
    {
        _go = go;

        bool isEnabled = _enabled && _go.enabledInHierarchy;
        _enabledInHierarchy = isEnabled;
    }

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

    public virtual void Awake() { }
    public virtual void OnEnable() { }
    public virtual void OnDisable() { }
    public virtual void Start() { }
    public virtual void FixedUpdate() { }
    public virtual void Update() { }
    public virtual void LateUpdate() { }
    public virtual void DrawGizmos() { }
    public virtual void DrawGizmosSelected() { }
    public virtual void OnLevelWasLoaded() { }
    public virtual void OnDestroy() { }

    internal void InternalAwake()
    {
        if (HasAwoken) return;
        HasAwoken = true;
        Awake();

        if (EnabledInHierarchy)
            Do(OnEnable);
    }
    internal void InternalStart()
    {
        if (HasStarted) return;
        HasStarted = true;
        Start();
    }

    private static readonly Dictionary<Type, bool> CachedExecuteAlways = new();
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

    [OnAssemblyUnload]
    public static void ClearCache() => CachedExecuteAlways.Clear();

    public Coroutine StartCoroutine(string methodName)
    {
        methodName = methodName.Trim();
        var method = GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (method == null)
        {
            Debug.LogError("Coroutine '" + methodName + "' couldn't be started, the method doesn't exist!");
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

    public void StopAllCoroutines()
    {
        _coroutines.Clear();
        _endOfFrameCoroutines.Clear();
        _fixedUpdateCoroutines.Clear();
    }

    public void StopCoroutine(string methodName)
    {
        methodName = methodName.Trim();
        _coroutines.Remove(methodName);
        _endOfFrameCoroutines.Remove(methodName);
        _fixedUpdateCoroutines.Remove(methodName);
    }

    public class YieldInstruction
    {
    }

    public class WaitForSeconds : YieldInstruction
    {
        public double Duration { get; private set; }
        public WaitForSeconds(float seconds)
        {
            Duration = Time.time + seconds;
        }
    }

    public class WaitForEndOfFrame : YieldInstruction
    {
    }

    public class WaitForFixedUpdate : YieldInstruction
    {
    }

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

    /// <summary> Calls the method named methodName on every MonoBehaviour in this game object or any of its children. </summary>
    public void BroadcastMessage(string methodName, params object[] objs) => GameObject.BroadcastMessage(methodName, objs);

    /// <summary> Calls the method named methodName on every MonoBehaviour in this game object. </summary>
    public void SendMessage(string methodName, params object[] objs) => GameObject.SendMessage(methodName, objs);


    #endregion
}
