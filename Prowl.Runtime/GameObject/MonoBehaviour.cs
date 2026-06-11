// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Prowl.Echo;
using Prowl.PaperUI;
using Prowl.Runtime.Events;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Represents the base class for all scripts that attach to GameObjects in the Prowl Game Engine.
/// MonoBehaviour provides lifecycle methods for game object behaviors.
/// </summary>
public abstract class MonoBehaviour : EngineObject, ISerializationCallbackReceiver
{
    [SerializeField, HideInInspector]
    private Guid _identifier = Guid.NewGuid();

    [SerializeField, HideInInspector]
    protected internal bool _enabled = true;
    [SerializeField, HideInInspector]
    protected internal bool _enabledInHierarchy = true;

    private GameObject _go;

    /// <summary>
    /// Gets or sets the hide flags for this MonoBehaviour.
    /// </summary>
    [HideInInspector]
    public HideFlags HideFlags;

    [SerializeIgnore]
    private bool _hasStarted = false;

    [SerializeIgnore]
    private bool _hasBeenEnabled = false;

    [SerializeIgnore]
    private bool? _executeAlwaysCached;

    /// <summary>
    /// Whether this component's gameplay methods should execute.
    /// True when in play mode, or when the component has [ExecuteAlways].
    /// </summary>
    internal bool ShouldExecuteGameplay
    {
        get
        {
            _executeAlwaysCached ??= GetType().IsDefined(typeof(ExecuteAlwaysAttribute), true);
            return Application.IsPlaying || _executeAlwaysCached.Value;
        }
    }

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
    /// Gets whether the Start method has been called.
    /// </summary>
    public bool HasStarted { get => _hasStarted; internal set => _hasStarted = value; }

    /// <summary>
    /// Gets whether OnEnable has ever been called on this component.
    /// Used to determine if OnDispose should be called during cleanup.
    /// </summary>
    public bool HasBeenEnabled { get => _hasBeenEnabled; internal set => _hasBeenEnabled = value; }

    /// <summary>
    /// Gets the tag of the GameObject this MonoBehaviour is attached to.
    /// </summary>
    public string Tag => _go.Tag;

    #region Override Detection Cache

    [Flags]
    private enum OverrideFlags : byte
    {
        None            = 0,
        Update          = 1 << 0,
        LateUpdate      = 1 << 1,
        FixedUpdate     = 1 << 2,
        OnRenderCollect = 1 << 3,
        OnGui           = 1 << 4,
        DrawGizmos      = 1 << 5,
    }

    private static readonly ConcurrentDictionary<Type, OverrideFlags> s_overrideCache = new();

    private static OverrideFlags DetectOverrides(Type type)
    {
        return s_overrideCache.GetOrAdd(type, static t =>
        {
            OverrideFlags flags = OverrideFlags.None;
            Type baseType = typeof(MonoBehaviour);

            if (t.GetMethod(nameof(Update), BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)?.DeclaringType != baseType)
                flags |= OverrideFlags.Update;
            if (t.GetMethod(nameof(LateUpdate), BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)?.DeclaringType != baseType)
                flags |= OverrideFlags.LateUpdate;
            if (t.GetMethod(nameof(FixedUpdate), BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)?.DeclaringType != baseType)
                flags |= OverrideFlags.FixedUpdate;
            if (t.GetMethod(nameof(OnRenderCollect), BindingFlags.Instance | BindingFlags.Public, [typeof(SceneEvents.OnRenderCollectArgs)])?.DeclaringType != baseType)
                flags |= OverrideFlags.OnRenderCollect;
            if (t.GetMethod(nameof(OnGui), BindingFlags.Instance | BindingFlags.Public, [typeof(Paper)])?.DeclaringType != baseType)
                flags |= OverrideFlags.OnGui;
            if (t.GetMethod(nameof(DrawGizmos), BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)?.DeclaringType != baseType)
                flags |= OverrideFlags.DrawGizmos;

            return flags;
        });
    }

    [SerializeIgnore]
    private OverrideFlags _overrides;

    #endregion

    private ExecutionOrder _executionOrder;

    public ExecutionOrder ExecutionOrder
    {
        get
        {
            return _executionOrder;
        }
        set
        {
            _executionOrder = value;
            if (_eventsInitialized && Scene.IsValid())
            {
                DisposeSceneEvents();
                SubscribeSceneEvents(Scene);
            }
        }
    }

    /// <summary>
    /// Updates this component's <see cref="ExecutionOrder"/> based on its owning GameObject's
    /// order and this component's index. Uses discriminator <c>0</c> so components sort
    /// after their GO but before its children.
    /// </summary>
    public void UpdateExecutionOrder()
    {
        if (!GameObject.IsValid()) return;

        ReadOnlySpan<int> goLevels = GameObject.ExecutionOrder.Levels;
        int[] levels = new int[goLevels.Length + 2];
        goLevels.CopyTo(levels);
        levels[goLevels.Length] = 0; // discriminator: component
        levels[goLevels.Length + 1] = GameObject._components.IndexOf(this);
        ExecutionOrder = new ExecutionOrder(levels);
    }

    private EventDelegateContainer<SceneEvents.EventTypes, Unit> UpdateDelegate = null;
    private EventDelegateContainer<SceneEvents.EventTypes, Unit> LateUpdateDelegate = null;
    private EventDelegateContainer<SceneEvents.EventTypes, Unit> FixedUpdateDelegate = null;
    private EventDelegateContainer<SceneEvents.EventTypes, SceneEvents.OnRenderCollectArgs> OnRenderCollectDelegate = null;
    private EventDelegateContainer<SceneEvents.EventTypes, Paper> OnGuiDelegate = null;
    private EventDelegateContainer<SceneEvents.EventTypes, Unit> DrawGizmosDelegate = null;

    private bool _eventsInitialized = false;

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
                UpdateEventDelegateState(_enabled && _enabledInHierarchy);
            }
        }
    }

    /// <summary>
    /// Gets whether the MonoBehaviour is enabled in the hierarchy (considering parent objects).
    /// </summary>
    public bool EnabledInHierarchy => _enabledInHierarchy;

    /// <summary>
    /// The parent <see cref="Prowl.Runtime.Resources.Scene"/> to which this <see cref="Prowl.Runtime.MonoBehaviour"/> belongs.
    ///
    /// Note that this property is derived from the components <see cref="GameObject"/>, as a
    /// <see cref="Prowl.Runtime.MonoBehaviour"/> itself cannot be part of a <see cref="Prowl.Runtime.Resources.Scene"/> without a
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
    // Convenience component-access API mirrored onto the behaviour; it's generally recommended to use the GameObject instead
    /// <inheritdoc cref="GameObject.AddComponent{T}"/>"
    public T AddComponent<T>() where T : MonoBehaviour, new() => (T)AddComponent(typeof(T));
    /// <inheritdoc cref="GameObject.AddComponent(Type)"/>"
    public MonoBehaviour AddComponent([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type) => GameObject.AddComponent(type);
    /// <inheritdoc cref="GameObject.RemoveComponent{T}"/>"
    public void RemoveComponent<T>(T component) where T : MonoBehaviour => GameObject.RemoveComponent(component);
    /// <inheritdoc cref="GameObject.RemoveComponent(MonoBehaviour)"/>"
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
    public bool TryGetComponent<T>(out T component) where T : MonoBehaviour => (component = GetComponent<T>()).IsValid();
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
    /// <exception cref="Exception">Thrown if the Component is not found in its GameObject's Component list.</exception>
    public int? GetSiblingIndex()
    {
        if (GameObject.IsNotValid()) return null;

        for (int i = 0; i < GameObject._components.Count; i++)
            if (object.ReferenceEquals(GameObject._components[i], this))
                return i;

        throw new Exception($"This Component appears to be in Limbo, This should never happen!, The Component believes its a child of {GameObject.Name} but they don't have it as an attached component!");
    }

    /// <summary>
    /// Sets the index of this Component in its GameObject's Component list.
    /// </summary>
    /// <param name="index">The new index of this Component.</param>
    public void SetSiblingIndex(int index)
    {
        if (GameObject.IsNotValid()) return;

        // Remove this object from current position
        GameObject._components.Remove(this);

        // Ensure index is within bounds
        index = Maths.Max(0, Maths.Min(index, GameObject._components.Count));

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

        bool isEnabled = _enabled && _go.EnabledInHierarchy;
        _enabledInHierarchy = isEnabled;
    }

    /// <summary>
    /// Updates the enabled state based on changes in the hierarchy.
    /// OnEnable/OnDisable are only called if the GameObject is in an active Scene.
    /// </summary>
    internal void HierarchyStateChanged()
    {
        bool newState = _enabled && _go.EnabledInHierarchy;
        if (newState != _enabledInHierarchy)
        {
            _enabledInHierarchy = newState;

            // Only call OnEnable/OnDisable if we're in an active Scene
            Scene? scene = _go.Scene;
            if (scene.IsValid() && scene.IsActive)
            {
                if (newState)
                    InternalOnEnable();
                else
                    InternalOnDisable();
            }

            // Sync event subscriptions so disabled-in-hierarchy objects stop receiving Update/FixedUpdate etc.
            if (_eventsInitialized)
                UpdateEventDelegateState(newState);
        }
    }

    private void DisposeSceneEvents()
    {
        if (_eventsInitialized)
        {
            UpdateDelegate?.Dispose();
            LateUpdateDelegate?.Dispose();
            FixedUpdateDelegate?.Dispose();
            OnRenderCollectDelegate?.Dispose();
            OnGuiDelegate?.Dispose();
            DrawGizmosDelegate?.Dispose();

            _eventsInitialized = false;
        }
    }

    internal void SubscribeSceneEvents(Scene scene)
    {
        _overrides = DetectOverrides(GetType());

        if (_overrides.HasFlag(OverrideFlags.Update))
            UpdateDelegate = scene.Events.Update.Subscribe(InternalUpdate, ExecutionOrder);
        if (_overrides.HasFlag(OverrideFlags.LateUpdate))
            LateUpdateDelegate = scene.Events.LateUpdate.Subscribe(InternalLateUpdate, ExecutionOrder);
        if (_overrides.HasFlag(OverrideFlags.FixedUpdate))
            FixedUpdateDelegate = scene.Events.FixedUpdate.Subscribe(InternalFixedUpdate, ExecutionOrder);
        if (_overrides.HasFlag(OverrideFlags.OnRenderCollect))
            OnRenderCollectDelegate = scene.Events.OnRenderCollect.Subscribe(OnRenderCollect, ExecutionOrder);
        if (_overrides.HasFlag(OverrideFlags.OnGui))
            OnGuiDelegate = scene.Events.OnGui.Subscribe(OnGui, ExecutionOrder);
        if (_overrides.HasFlag(OverrideFlags.DrawGizmos))
            DrawGizmosDelegate = scene.Events.DrawGizmos.Subscribe(DrawGizmos, ExecutionOrder);

        _eventsInitialized = true;

        // Apply current effective state immediately (in case the MB or its GO ancestors are disabled at subscription time)
        UpdateEventDelegateState(_enabled && _enabledInHierarchy);
    }

    internal void UpdateEventDelegateState(bool enable)
    {
        if (!_eventsInitialized)
        {
            Scene?.ToSubscribe.Add(this);
            return;
        }

        if (enable)
        {
            UpdateDelegate?.Enable();
            LateUpdateDelegate?.Enable();
            FixedUpdateDelegate?.Enable();
            OnRenderCollectDelegate?.Enable();
            OnGuiDelegate?.Enable();
            DrawGizmosDelegate?.Enable();
        }
        else
        {
            UpdateDelegate?.Disable();
            LateUpdateDelegate?.Disable();
            FixedUpdateDelegate?.Disable();
            OnRenderCollectDelegate?.Disable();
            OnGuiDelegate?.Disable();
            DrawGizmosDelegate?.Disable();
        }
    }

    /// <summary>
    /// Checks if this MonoBehaviour can be destroyed.
    /// </summary>
    /// <returns>True if the MonoBehaviour can be destroyed, false otherwise.</returns>
    internal bool CanDestroy()
    {
        // Skip dependency check if the entire GameObject is being disposed
        if (_go.IsDisposed)
            return true;

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
    /// Called when the GameObject is added to a Scene.
    /// This happens before OnEnable and only once per scene add.
    /// </summary>
    public virtual void OnAddedToScene() { }

    /// <summary>
    /// Called when the GameObject is removed from a Scene.
    /// This happens after OnDisable and only once per scene remove.
    /// </summary>
    public virtual void OnRemovedFromScene() { }

    /// <summary>
    /// Called when the object becomes enabled and active.
    /// Only called when the GameObject is in a Scene and enabled.
    /// </summary>
    public virtual void OnEnable() { }

    /// <summary>
    /// Called when the object becomes disabled or inactive.
    /// Only called when the GameObject was in a Scene and becomes disabled.
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
    /// Called every frame per camera to collect render data.
    /// Always called regardless of play mode.
    /// Components add their renderables/lights to the provided lists.
    /// Camera is provided for LOD and distance-based decisions.
    /// </summary>
    public virtual void OnRenderCollect(SceneEvents.OnRenderCollectArgs onRenderCollectArgs) { }

    /// <summary>
    /// Called for rendering and handling GUI gizmos.
    /// </summary>
    public virtual void DrawGizmos() { }

    /// <summary>
    /// Called for rendering and handling GUI gizmos.
    /// </summary>
    public virtual void DrawGizmosSelected() { }

    /// <summary>
    /// Called for drawing and handling interaction with Runtime/Ingame UI
    /// </summary>
    /// <param name="paper"></param>
    public virtual void OnGui(Paper paper) { }

    /// <summary>Gated Start only runs in play mode or with [ExecuteAlways].</summary>
    internal void InternalStart()
    {
        if (HasStarted) return;
        if (!ShouldExecuteGameplay) return;
        HasStarted = true;
        try { Start(); }
        catch (Exception ex) { Debug.LogError($"[{Name}/{GetType().Name}] Start() threw: {ex.Message}\n{ex.StackTrace}"); }
    }

    /// <summary>Gated Update only runs in play mode or with [ExecuteAlways].</summary>
    internal void InternalUpdate()
    {
        if (!ShouldExecuteGameplay) return;
        try { Update(); }
        catch (Exception ex) { Debug.LogError($"[{Name}/{GetType().Name}] Update() threw: {ex.Message}\n{ex.StackTrace}"); }
    }

    /// <summary>Gated LateUpdate only runs in play mode or with [ExecuteAlways].</summary>
    internal void InternalLateUpdate()
    {
        if (!ShouldExecuteGameplay) return;
        try { LateUpdate(); }
        catch (Exception ex) { Debug.LogError($"[{Name}/{GetType().Name}] LateUpdate() threw: {ex.Message}\n{ex.StackTrace}"); }
    }

    /// <summary>Gated FixedUpdate only runs in play mode or with [ExecuteAlways].</summary>
    internal void InternalFixedUpdate()
    {
        if (!ShouldExecuteGameplay) return;
        try { FixedUpdate(); }
        catch (Exception ex) { Debug.LogError($"[{Name}/{GetType().Name}] FixedUpdate() threw: {ex.Message}\n{ex.StackTrace}"); }
    }

    /// <summary>Gated OnEnable only runs in play mode or with [ExecuteAlways].</summary>
    internal void InternalOnEnable()
    {
        _hasBeenEnabled = true;
        if (!ShouldExecuteGameplay) return;
        try { OnEnable(); }
        catch (Exception ex) { Debug.LogError($"[{Name}/{GetType().Name}] OnEnable() threw: {ex.Message}\n{ex.StackTrace}"); }
    }

    /// <summary>Gated OnDisable only runs in play mode or with [ExecuteAlways].</summary>
    internal void InternalOnDisable()
    {
        if (!ShouldExecuteGameplay) return;
        try { OnDisable(); }
        catch (Exception ex) { Debug.LogError($"[{Name}/{GetType().Name}] OnDisable() threw: {ex.Message}\n{ex.StackTrace}"); }
    }

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        // Always generate fresh identifier Scene restores them after deserialization
        _identifier = Guid.NewGuid();
    }

    /// <summary>
    /// Called when the MonoBehaviour will be destroyed.
    /// This is an override of EngineObject.OnDispose() and is also exposed as a virtual lifecycle method.
    /// </summary>
    public override void OnDispose()
    {
        DisposeSceneEvents();

        if (GameObject.IsValid())
            GameObject.RemoveComponent(this);
    }

    #endregion
}
