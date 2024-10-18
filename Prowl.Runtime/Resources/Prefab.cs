// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Runtime.Cloning;

namespace Prowl.Runtime;

/// <summary>
/// Prefab is short for "prefabricated object" and encapsulates a single <see cref="GameObject"/> that can serve as a template.
/// When creating a GameObject out of a Prefab, it maintains a connection to it using a <see cref="PrefabLink"/> object. This
/// ensures that changes made to the Prefab propagate to all of its instances as well. It also keeps track of Properties that
/// have been deliberately modified in the editor and restores them after re-applying the original Prefabs data.
/// </summary>
public class Prefab : EngineObject
{
    private static readonly CloneProvider s_sharedPrefabProvider = new();

    [SerializeField]
    private GameObject _objTree = null;

    /// <summary> Returns whether this Prefab contains any data. </summary>
    public bool ContainsData => _objTree != null;

    /// <summary> Creates a new, empty Prefab. </summary>
    public Prefab() : this(null) { }

    /// <summary> Creates a new Prefab out of a GameObject. </summary>
    public Prefab(GameObject obj) { Inject(obj); }

    /// <summary>
    /// Discards previous data and injects the specified <see cref="GameObject"/> into the Prefab.
    /// The GameObject itself will not be affected, instead a <see cref="GameObject.Clone"/> of it
    /// will be used for the Prefab.
    /// </summary>
    /// <param name="obj">The object to inject as Prefab root object.</param>
    public void Inject(GameObject obj)
    {
        // Dispose old content
        if (obj == null)
        {
            if (_objTree != null)
            {
                _objTree.DestroyImmediate();
                _objTree = null;
            }
        }
        // Inject new content
        else
        {
            // Copy the new content into the Prefabs internal object
            if (_objTree != null)
                obj.CopyTo(_objTree);
            else
                _objTree = obj.Clone();

            // Cleanup any leftover prefab links that might have been copied
            _objTree.BreakPrefabLink();

            // Prevent recursion
            foreach (GameObject child in _objTree.GetChildrenDeep())
                if (child.PrefabLink != null && child.PrefabLink.Prefab == this)
                    child.BreakPrefabLink();
        }
    }

    /// <summary> Creates a new instance of the Prefab. You will need to add it to a Scene in most cases. </summary>
    public GameObject Instantiate() => _objTree == null ? new GameObject() : new GameObject(new AssetRef<Prefab>(this));

    public GameObject Instantiate(Vector3 position) => Instantiate(position, Quaternion.identity, Vector3.one);
    public GameObject Instantiate(Vector3 position, Quaternion rotation) => Instantiate(position, rotation, Vector3.one);
    public GameObject Instantiate(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        GameObject obj = Instantiate();
        Transform transform = obj.Transform;
        if (transform != null)
        {
            transform.position = position;
            transform.rotation = rotation;
            transform.localScale = scale;
        }
        return obj;
    }

    /// <summary> Copies this Prefabs data to a GameObject without linking itself to it. </summary>
    /// <param name="obj">The GameObject to which the Prefabs data is copied.</param>
    public void CopyTo(GameObject obj)
    {
        if (_objTree == null) return;
        s_sharedPrefabProvider.CopyObject(_objTree, obj);
    }

    /// <summary>
    /// Copies a subset of this Prefabs data to a specific Component.
    /// </summary>
    /// <param name="baseObjAddress">The GameObject IndexPath to locate the source Component</param>
    /// <param name="componentAddress">The Component address to locate the source Component</param>
    /// <param name="target">The Component to which the Prefabs data is copied.</param>
    public void CopyTo(IEnumerable<int> baseObjAddress, int componentAddress, MonoBehaviour target)
    {
        if (_objTree == null) return;

        GameObject baseObj = _objTree.GetChildAtIndexPath(baseObjAddress);
        if (baseObj == null) return;

        MonoBehaviour baseCmp = baseObj.GetComponentAtAddress(componentAddress);
        if (baseCmp == null) return;

        s_sharedPrefabProvider.CopyObject(baseCmp, target);
    }

    /// <summary>
    /// Returns whether this Prefab contains a <see cref="GameObject"/> with the specified <see cref="GameObject.GetIndexPathOfChild">index path</see>.
    /// It is based on this Prefabs root GameObject.
    /// </summary>
    /// <param name="indexPath">The <see cref="GameObject.GetIndexPathOfChild">index path</see> at which to search for a GameObject.</param>
    /// <returns>True, if such child GameObjects exists, false if not.</returns>
    public bool HasGameObject(IEnumerable<int> indexPath) => _objTree != null && _objTree.GetChildAtIndexPath(indexPath) != null;

    /// <summary>
    /// Returns whether this Prefab contains a <see cref="MonoBehaviour"/> inside a GameObject with the specified <see cref="GameObject.GetIndexPathOfChild">index path</see>.
    /// It is based on this Prefabs root GameObject.
    /// </summary>
    /// <param name="gameObjIndexPath">The <see cref="GameObject.GetIndexPathOfChild">index path</see> at which to search for a GameObject.</param>
    /// <param name="cmpType">The Component type to search for inside the found GameObject.</param>
    public bool HasComponent(IEnumerable<int> gameObjIndexPath, Type cmpType)
    {
        if (_objTree == null) return false;

        GameObject child = _objTree.GetChildAtIndexPath(gameObjIndexPath);
        if (child == null) return false;
        return child.GetComponent(cmpType) != null;
    }
}

/// <summary>
/// Represents a <see cref="GameObject">GameObjects</see> connection to the <see cref="Prefab"/> it has been instantiated from.
/// </summary>
public sealed class PrefabLink
{
    private struct VarMod
    {
        public class ComponentAddress
        {
            public int index;

            public ComponentAddress(int i) => index = i;
        }

        public FieldInfo prop;
        public ComponentAddress component;
        public List<int> childIndex;
        public object val;

        public override string ToString()
        {
            string childStr;
            string propStr;
            string valueStr;

            if (childIndex == null)
                childStr = "null";
            else
                childStr = childIndex.Any() ? "(" + string.Join(", ", childIndex) + ")" : "Root";

            if (component != null)
                childStr += "." + component;

            if (prop != null)
                propStr = prop.Name;
            else
                propStr = "null";

            if (val != null)
                valueStr = val.ToString();
            else
                valueStr = "null";

            return string.Format("VarMod: {0}.{1} = {2}", childStr, propStr, valueStr);
        }
    }


    [SerializeField] private AssetRef<Prefab> _prefab;
    [SerializeField] private GameObject _obj;
    [SerializeField] private List<VarMod> _changes;


    /// <summary>
    /// The GameObject this PrefabLink belongs to.
    /// </summary>
    public GameObject Obj { get => _obj; internal set => _obj = value; }

    /// <summary>
    /// The Prefab to which the GameObject is connected to.
    /// </summary>
    public AssetRef<Prefab> Prefab => _prefab;

    /// <summary>
    /// If the connected GameObject is itself contained within a hierarchy
    /// of GameObjects which is affected by a higher PrefabLink, this link will be
    /// returned.
    /// </summary>
    /// <seealso cref="GameObject.AffectedByPrefabLink"/>
    public PrefabLink ParentLink => _obj.parent != null ? _obj.parent.AffectedByPrefabLink : null; // EngineObject, Cannot use Null Coalescing Operator


    public PrefabLink() : this(null, null) { }

    /// <summary>
    /// Creates a new PrefabLink, connecting a GameObject to a Prefab.
    /// </summary>
    /// <param name="obj">The GameObject to link.</param>
    /// <param name="prefab">The Prefab to connect the GameObject with.</param>
    public PrefabLink(GameObject obj, AssetRef<Prefab> prefab)
    {
        _obj = obj;
        _prefab = prefab;
    }

    /// <summary>
    /// Relocates the internal change list from this PrefabLink to a different, hierarchically lower PrefabLink.
    /// </summary>
    /// <param name="other">
    /// The PrefabLink to which to relocate changes. It needs to be hierarchically lower than
    /// this one for the relocation to succeed.
    /// </param>
    /// <remarks>
    /// <para>
    /// In general, each PrefabLink is responsible for all hierarchically lower GameObjects. If one of them has
    /// a PrefabLink on its own, then the higher PrefabLinks responsibility ends there.
    /// </para>
    /// <para>
    /// Change relocation is done when linking an existing GameObject to a Prefab although it is already affected by a
    /// hierarchically higher PrefabLink. In order to prevent both PrefabLinks to interfere with each other, 
    /// all higher PrefabLink change list entries referring to that GameObject are relocated to the new, lower 
    /// PrefabLink that is specifically targeting it.
    /// </para>
    /// <para>
    /// This way, the above responsibility guideline remains applicable.
    /// </para>
    /// </remarks>
    public void RelocateChanges(PrefabLink other)
    {
        if (_changes == null || _changes.Count == 0) return;
        if (!other._obj.IsChildOf(_obj)) return;
        List<int> childPath = _obj.GetIndexPathOfChild(other._obj);

        for (int i = _changes.Count - 1; i >= 0; i--)
        {
            if (_changes[i].childIndex.Take(childPath.Count).SequenceEqual(childPath))
            {
                object target;

                GameObject targetObj = _obj.GetChildAtIndexPath(_changes[i].childIndex);
                if (_changes[i].component != null)
                    target = targetObj.GetComponentAtAddress(_changes[i].component.index);
                else
                    target = targetObj;

                other.PushChange(target, _changes[i].prop, _changes[i].val);
                _changes.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Clones the PrefabLink, but targets a different GameObject and Prefab.
    /// </summary>
    /// <param name="newObj">The GameObject which the clone is connected to.</param>
    /// <param name="newPrefab">The Prefab which the clone will connect its GameObject to.</param>
    /// <returns>A cloned version of this PrefabLink</returns>
    public PrefabLink Clone(GameObject newObj, AssetRef<Prefab> newPrefab)
    {
        PrefabLink clone = Clone();
        clone._obj = newObj;
        clone._prefab = newPrefab;
        return clone;
    }

    /// <summary>
    /// Clones the PrefabLink, but targets a different GameObject.
    /// </summary>
    /// <param name="newObj">The GameObject which the clone is connected to.</param>
    /// <returns>A cloned version of this PrefabLink</returns>
    public PrefabLink Clone(GameObject newObj)
    {
        PrefabLink clone = Clone();
        clone._obj = newObj;
        return clone;
    }
    /// <summary>
    /// Clones the PrefabLink.
    /// </summary>
    /// <returns>A cloned version of this PrefabLink</returns>
    public PrefabLink Clone() => this.DeepClone();

    /// <summary> Applies both Prefab and change list to this PrefabLinks GameObject. </summary>
    public void Apply(bool deep = true)
    {
        ApplyPrefab();
        ApplyChanges();

        // Lower prefab links later
        if (deep)
        {
            foreach (GameObject child in _obj.GetChildrenDeep())
            {
                if (child.PrefabLink != null && child.PrefabLink.ParentLink == this)
                    child.PrefabLink.Apply(true);
            }
        }
    }

    /// <summary>
    /// Applies the Prefab to this PrefabLinks GameObject. This will overwrite
    /// all of its existing data and establish the state as defined in the Prefab.
    /// </summary>
    public void ApplyPrefab()
    {
        if (!_prefab.IsAvailable) return;
        if (!_prefab.Res.ContainsData) return;
        _prefab.Res.CopyTo(_obj);
    }

    /// <summary>
    /// Applies this PrefabLinks change list to its GameObject. This will restore
    /// all deliberate modifications (made in the editor) of the GameObjects Fields 
    /// after linking it to the Prefab.
    /// </summary>
    public void ApplyChanges()
    {
        if (_changes == null || _changes.Count == 0) return;

        for (int i = 0; i < _changes.Count; i++)
        {
            GameObject targetObj = _obj.GetChildAtIndexPath(_changes[i].childIndex);
            object target;
            if (_changes[i].component != null)
                target = targetObj.GetComponentAtAddress(_changes[i].component.index);
            else
                target = targetObj;

            if (_changes[i].prop != null && target != null)
            {
                try
                {
                    CloneType cloneType = CloneProvider.GetCloneType(_changes[i].prop.FieldType);
                    object applyVal;
                    if (cloneType.Type.IsValueType || cloneType.DefaultCloneBehavior != CloneBehavior.ChildObject)
                        applyVal = _changes[i].val;
                    else
                        applyVal = _changes[i].val.DeepClone();

                    _changes[i].prop.SetValue(target, applyVal);
                }
                catch (Exception e)
                {
                    Debug.LogError(string.Format("Error applying PrefabLink changes in {0}, field  {1}:\n{2}", _obj.Name, _changes[i].prop.Name, e));
                }
            }
            else
            {
                _changes.RemoveAt(i);
                i--;
                continue;
            }
        }
    }

    /// <summary> Updates all existing change list entries by the GameObjects current Field values. </summary>
    public void UpdateChanges()
    {
        if (_changes == null || _changes.Count == 0) return;

        // Remove empty change list entries
        ClearEmptyChanges();

        // Update change list values from fields
        for (int i = 0; i < _changes.Count; i++)
        {
            GameObject targetObj = _obj.GetChildAtIndexPath(_changes[i].childIndex);
            object target;
            if (_changes[i].component != null)
                target = targetObj.GetComponentAtAddress(_changes[i].component.index);
            else
                target = targetObj;

            VarMod modTmp = _changes[i];
            modTmp.val = _changes[i].prop.GetValue(target);
            _changes[i] = modTmp;
        }
    }

    /// <summary>
    /// Creates a new change list entry.
    /// </summary>
    /// <param name="target">The target object in which the change has been made. Must be a GameObject or Component.</param>
    /// <param name="prop">The target objects <see cref="System.Reflection.FieldInfo">Field</see> that has been changed.</param>
    /// <param name="value">The value to which the specified Field has been changed to.</param>
    public void PushChange(object target, FieldInfo prop, object value)
    {
        //if (prop.IsEquivalent(typeof(GameObject).GetInstanceField(nameof(GameObject.parent)))) return; // Reject changing "Parent" as it would destroy the PrefabLink - Changing parent doesnt send a change event so this is not needed
        //if (!prop.CanWrite) return;
        _changes ??= [];

        GameObject targetObj = target as GameObject;
        MonoBehaviour targetComp = target as MonoBehaviour;
        if (targetObj == null && targetComp != null) targetObj = targetComp.GameObject;

        if (targetObj == null)
            throw new ArgumentException("Target object is not a valid child of this PrefabLinks GameObject", nameof(target));
        if (value == null && prop.FieldType.GetTypeInfo().IsValueType)
            throw new ArgumentException("Target field cannot be assigned from null value.", nameof(value));
        if (value != null && !prop.FieldType.GetTypeInfo().IsInstanceOfType(value))
            throw new ArgumentException("Target field not assignable from Type " + value.GetType().Name + ".", nameof(value));

        VarMod change;
        change.childIndex = _obj.GetIndexPathOfChild(targetObj);
        change.component = (targetComp != null) ? new(targetComp.GameObject.GetComponentAddress(targetComp)) : null; // Is EngineObject, Cannot use Null Coalescing Operator
        change.prop = prop;
        change.val = value;

        PopChange(change.childIndex, prop, change.component);
        _changes.Add(change);
    }

    /// <summary>
    /// Creates a new change list entry.
    /// </summary>
    /// <param name="target">The target object in which the change has been made. Must be a GameObject or Component.</param>
    /// <param name="prop">The target objects <see cref="System.Reflection.FieldInfo">Field</see> that has been changed.</param>
    public void PushChange(object target, FieldInfo prop)
    {
        //if (!prop.CanWrite || !prop.CanRead) return;
        object changeVal = prop.GetValue(target);

        // Clone the change list entry value, if required
        if (changeVal != null)
        {
            CloneType cloneType = CloneProvider.GetCloneType(changeVal.GetType());
            if (!cloneType.Type.IsValueType && cloneType.DefaultCloneBehavior == CloneBehavior.ChildObject)
            {
                changeVal = changeVal.DeepClone();
            }
        }

        PushChange(target, prop, changeVal);
    }

    /// <summary>
    /// Removes an existing change list entry.
    /// </summary>
    /// <param name="target">The target object in which the change has been made. Must be a GameObject or Component.</param>
    /// <param name="prop">The target objects <see cref="System.Reflection.FieldInfo">Field</see> that has been changed.</param>
    public void PopChange(object target, FieldInfo prop)
    {
        GameObject targetObj = target as GameObject;
        MonoBehaviour targetComp = target as MonoBehaviour;
        if (targetObj == null && targetComp != null) targetObj = targetComp.GameObject;

        if (targetObj == null)
            throw new ArgumentException("Target object is not a valid child of this PrefabLinks GameObject", nameof(target));

        PopChange(_obj.GetIndexPathOfChild(targetObj), prop);
    }

    private void PopChange(IEnumerable<int> indexPath, FieldInfo prop, VarMod.ComponentAddress component)
    {
        if (_changes == null || _changes.Count == 0) return;
        for (int i = _changes.Count - 1; i >= 0; i--)
        {
            if (_changes[i].prop == prop && _changes[i].childIndex.SequenceEqual(indexPath))
            {
                if (component != null && _changes[i].component.index != component.index)
                    continue;

                _changes.RemoveAt(i);
                break;
            }
        }
    }

    /// <summary>
    /// Returns whether there is a specific change list entry.
    /// </summary>
    /// <param name="target">The target object in which the change has been made. Must be a GameObject or Component.</param>
    /// <param name="prop">The target objects <see cref="System.Reflection.FieldInfo">Field</see> that has been changed.</param>
    /// <returns>True, if such change list entry exists, false if not.</returns>
    public bool HasChange(object target, FieldInfo prop)
    {
        if (_changes == null || _changes.Count == 0) return false;

        GameObject targetObj = target as GameObject;
        MonoBehaviour targetComp = target as MonoBehaviour;
        if (targetObj == null && targetComp != null) targetObj = targetComp.GameObject;

        if (targetObj == null)
            throw new ArgumentException("Target object is not a valid child of this PrefabLinks GameObject", nameof(target));

        List<int> indexPath = _obj.GetIndexPathOfChild(targetObj);
        for (int i = 0; i < _changes.Count; i++)
        {
            if (_changes[i].childIndex.SequenceEqual(indexPath) && _changes[i].prop == prop)
                return true;
        }

        return false;
    }

    /// <summary> Clears the change list. </summary>
    public void ClearChanges() => _changes?.Clear();

    /// <summary> Clears the change list for certain objects </summary>
    public void ClearChanges(GameObject targetObj, int? cmpAddress, FieldInfo prop)
    {
        if (_changes == null || _changes.Count == 0) return;

        IEnumerable<int> indexPath = targetObj != null ? _obj.GetIndexPathOfChild(targetObj) : null;
        for (int i = _changes.Count - 1; i >= 0; i--)
        {
            if (indexPath != null && !_changes[i].childIndex.SequenceEqual(indexPath)) continue;
            if (cmpAddress != null && (_changes[i].component?.index ?? int.MinValue) != cmpAddress) continue;
            if (prop != null && prop != _changes[i].prop) continue;
            _changes.RemoveAt(i);
        }
    }

    private void ClearEmptyChanges()
    {
        for (int i = _changes.Count - 1; i >= 0; i--)
            if (_changes[i].prop == null)
                _changes.RemoveAt(i);
    }

    /// <summary> Returns whether a specific object is affected by this PrefabLink. </summary>

    /// <summary> Returns whether a specific object is affected by this PrefabLink. </summary>
    public bool AffectsObject(GameObject obj) => _prefab.IsAvailable && _prefab.Res.HasGameObject(this._obj.GetIndexPathOfChild(obj));

    /// <summary>
    /// Applies all PrefabLinks in a set of GameObjects. 
    /// </summary>
    /// <param name="objEnum">An enumeration of all GameObjects containing PrefabLinks that are to <see cref="Apply()">apply</see>.</param>
    /// <param name="predicate">An optional predicate. If set, only PrefabLinks meeting its requirements are applied.</param>
    /// <returns>A List of all PrefabLinks that have been applied.</returns>
    public static HashSet<PrefabLink> ApplyAllLinks(IEnumerable<GameObject> objEnum, Predicate<PrefabLink> predicate = null)
    {
        predicate ??= p => true;
        HashSet<PrefabLink> appliedLinks = [];

        var sortedQuery = from obj in objEnum
                          where obj.PrefabLink != null && predicate(obj.PrefabLink)
                          group obj by GetObjectHierarchyLevel(obj) into g
                          orderby g.Key
                          select g;

        foreach (var group in sortedQuery)
            foreach (GameObject obj in group)
            {
                obj.PrefabLink.Apply();
                appliedLinks.Add(obj.PrefabLink);
            }

        return appliedLinks;
    }

    private static int GetObjectHierarchyLevel(GameObject obj)
    {
        if (obj.parent == null)
            return 0;
        else
            return GetObjectHierarchyLevel(obj.parent) + 1;
    }
}
