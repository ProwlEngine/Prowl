// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Prowl.Echo;
using Prowl.PaperUI;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Represents the base class for all scripts that attach to GameObjects in the Prowl Game Engine.
/// MonoBehaviour provides lifecycle methods for game object behaviors.
/// </summary>
public abstract class MonoBehaviour : EngineObject
{
    [SerializeField]
    private Guid _identifier = Guid.NewGuid();

    [SerializeField]
    protected internal bool _enabled = true;
    [SerializeField]
    protected internal bool _enabledInHierarchy = true;

    private GameObject _go;

    /// <summary>
    /// Gets or sets the hide flags for this MonoBehaviour.
    /// </summary>
    public HideFlags HideFlags;

    [SerializeIgnore]
    private bool _hasStarted = false;

    [SerializeIgnore]
    private bool _hasBeenEnabled = false;

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
                    OnDisable();
            }
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
    /// Called for rendering and handling GUI gizmos.
    /// </summary>
    public virtual void DrawGizmos() { }

    /// <summary>
    /// Called for drawing and handling interaction with Runtime/Ingame UI
    /// </summary>
    /// <param name="paper"></param>
    public virtual void OnGui(Paper paper) { }

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
    /// Internal method to handle the OnEnable lifecycle event.
    /// Sets HasBeenEnabled flag before calling the virtual OnEnable method.
    /// </summary>
    internal void InternalOnEnable()
    {
        _hasBeenEnabled = true;
        OnEnable();
    }

    /// <summary>
    /// Called when the MonoBehaviour will be destroyed.
    /// This is an override of EngineObject.OnDispose() and is also exposed as a virtual lifecycle method.
    /// </summary>
    public override void OnDispose()
    {
        if (GameObject.IsValid())
            GameObject.RemoveComponent(this);
    }

    #endregion
}
