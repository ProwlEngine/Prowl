// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Runtime.Cloning;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Runtime;

/// <summary>
/// Prefab is short for "prefabricated object" and encapsulates a single <see cref="GameObject"/> that can serve as a template.
/// When creating a GameObject out of a Prefab, it maintains a connection to it using a <see cref="PrefabLink"/> object. This
/// ensures that changes made to the Prefab propagate to all of its instances as well. It also keeps track of Properties that
/// have been deliberately modified in the editor and restores them after re-applying the original Prefabs data.
/// </summary>
public class Prefab : EngineObject
{
    private static readonly ApplyPrefabContext s_prefabContext = new ApplyPrefabContext();
    private static readonly CloneProvider s_sharedPrefabProvider = new CloneProvider(s_prefabContext);

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
    /// Returns whether this Prefab contains a <see cref="GameObject"/> with the specified <see cref="GameObject.GetIndexPathOfChild">index path</see>.
    /// It is based on this Prefabs root GameObject.
    /// </summary>
    /// <param name="indexPath">The <see cref="GameObject.GetIndexPathOfChild">index path</see> at which to search for a GameObject.</param>
    /// <returns>True, if such child GameObjects exists, false if not.</returns>
    public bool HasGameObject(Guid identifier) => _objTree != null && _objTree.FindChildByIdentifier(identifier) != null;

    /// <summary>
    /// Returns whether this Prefab contains a <see cref="MonoBehaviour"/> inside a GameObject with the specified <see cref="GameObject.GetIndexPathOfChild">index path</see>.
    /// It is based on this Prefabs root GameObject.
    /// </summary>
    /// <param name="gameObjIndexPath">The <see cref="GameObject.GetIndexPathOfChild">index path</see> at which to search for a GameObject.</param>
    /// <param name="cmpType">The component type to search for.</param>
    public bool HasComponent(Guid identifier)
    {
        if (_objTree == null) return false;

        return _objTree.GetComponentInChildrenByIdentifier(identifier) != null;
    }


    public static string PrefabLinkInfo = "prefabLink";

    /// <summary>
    /// Used to push a change to the PrefabLink
    /// If the PrefabLink itself is what's changed (They)
    /// </summary>
    /// <param name="o"></param>
    /// <param name="info"></param>
    /// <returns></returns>
    public static void OnFieldChange(object o, string? info)
    {
        if (info != null)
        {
            HashSet<PrefabLink> changedLinks = new HashSet<PrefabLink>();

            MonoBehaviour cmp = o as MonoBehaviour;
            GameObject obj = o as GameObject;
            if (cmp == null && obj == null) return;

            PrefabLink link = null;
            if (obj != null) link = obj.AffectedByPrefabLink;
            else if (cmp != null && cmp.GameObject != null) link = cmp.GameObject.AffectedByPrefabLink;

            if (link == null) return;
            if (cmp != null && !link.IsSource(cmp)) return;
            if (obj != null && !link.IsSource(obj)) return;

            // Handle property changes regarding affected prefab links change lists
            if (PushPrefabLinkFieldChange(link, o, info))
                changedLinks.Add(link);

            foreach (PrefabLink l in changedLinks)
            {
                OnFieldChange(l.Obj, PrefabLinkInfo);
            }
        }

        if (o is Prefab)
        {
            // Prefab was modified, Update all instances
            Prefab prefab = o as Prefab;
            HashSet<PrefabLink> appliedLinks = PrefabLink.ApplyAllLinks(SceneManager.Scene.AllObjects, p => p.Prefab.AssetID == prefab.AssetID);
            foreach (PrefabLink l in appliedLinks)
                OnFieldChange(l.Obj, PrefabLinkInfo);
        }
    }

    private static bool PushPrefabLinkFieldChange(PrefabLink link, object target, string? info)
    {
        if (link == null) return false;

        if (info == "prefabLink" && target is GameObject)
        {
            GameObject obj = target as GameObject;
            if (obj == null) return false;

            PrefabLink parentLink;
            if (obj.PrefabLink == link && (parentLink = link.ParentLink) != null)
            {
                parentLink.PushChange(obj, info, obj.PrefabLink.Clone());
            }
            return false;
        }
        else
        {
            link.PushChange(target, info);
            return true;
        }
    }
}

/// <summary>
/// Represents a <see cref="GameObject">GameObjects</see> connection to the <see cref="Prefab"/> it has been instantiated from.
/// </summary>
public sealed class PrefabLink
{
    public struct VarMod
    {
        public string fieldName;
        public Guid identifier;
        public object val;

        public override string ToString()
        {
            string childStr = identifier.ToString();
            string propStr;
            string valueStr;

            if (fieldName != null)
                propStr = fieldName;
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

        for (int i = _changes.Count - 1; i >= 0; i--)
        {
            VarMod change = _changes[i];

            object target = _obj.FindChildByIdentifier(change.identifier);
            target ??= _obj.GetComponentByIdentifier(change.identifier);

            other.PushChange(target, change.fieldName, change.val);

            _changes.RemoveAt(i);
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
            object target = _obj.FindChildByIdentifier(_changes[i].identifier);
            target ??= _obj.GetComponentByIdentifier(_changes[i].identifier);
            
            if (_changes[i].fieldName != null && target != null)
            {
                try
                {
                    var field = target.GetType().GetInstanceField(_changes[i].fieldName);
                    if (field == null) continue;

                    CloneType cloneType = CloneProvider.GetCloneType(field.FieldType);
                    object applyVal;
                    if (cloneType.Type.IsValueType || cloneType.DefaultCloneBehavior != CloneBehavior.ChildObject)
                        applyVal = _changes[i].val;
                    else
                        applyVal = _changes[i].val.DeepClone();

                    field.SetValue(target, applyVal);
                }
                catch (Exception e)
                {
                    Debug.LogError(string.Format("Error applying PrefabLink changes in {0}, field  {1}:\n{2}", _obj.Name, _changes[i].fieldName, e));
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
    public void UpdateFieldChanges()
    {
        if (_changes == null || _changes.Count == 0) return;

        // Remove empty change list entries
        ClearEmptyChanges();

        // Update change list values from fields
        for (int i = 0; i < _changes.Count; i++)
        {
            object target = _obj.FindChildByIdentifier(_changes[i].identifier);
            target ??= _obj.GetComponentByIdentifier(_changes[i].identifier);

            var field = target.GetType().GetInstanceField(_changes[i].fieldName);
            if (field == null) continue;

            VarMod modTmp = _changes[i];
            modTmp.val = field.GetValue(target);
            _changes[i] = modTmp;
        }
    }

    /// <summary>
    /// Creates a new change list entry.
    /// </summary>
    /// <param name="target">The target object in which the change has been made. Must be a GameObject or Component.</param>
    /// <param name="fieldName">The target objects <see cref="System.Reflection.FieldInfo">Field's</see> name that has been changed.</param>
    /// <param name="value">The value to which the specified Field has been changed to.</param>
    public void PushChange(object target, string fieldName, object value)
    {
        //if (prop.IsEquivalent(typeof(GameObject).GetInstanceField(nameof(GameObject.parent)))) return; // Reject changing "Parent" as it would destroy the PrefabLink - Changing parent doesnt send a change event so this is not needed
        //if (!prop.CanWrite) return;
        _changes ??= [];

        GameObject targetObj = target as GameObject;
        MonoBehaviour targetComp = target as MonoBehaviour;
        if (targetObj == null && targetComp != null) targetObj = targetComp.GameObject;

        if (targetObj == null)
            throw new ArgumentException("Target object is not a valid child of this PrefabLinks GameObject", nameof(target));

        FieldInfo field = (target is GameObject) ? targetObj.GetType().GetInstanceField(fieldName) : targetComp.GetType().GetInstanceField(fieldName);

        if (value == null && field.FieldType.GetTypeInfo().IsValueType)
            throw new ArgumentException("Target field cannot be assigned from null value.", nameof(value));
        if (value != null && !field.FieldType.GetTypeInfo().IsInstanceOfType(value))
            throw new ArgumentException("Target field not assignable from Type " + value.GetType().Name + ".", nameof(value));

        VarMod change;
        change.identifier = (target is GameObject) ? targetObj.Identifier : targetComp.Identifier;
        change.fieldName = fieldName;
        change.val = value;

        PopChange(target, fieldName);
        _changes.Add(change);
    }

    /// <summary>
    /// Creates a new change list entry.
    /// </summary>
    /// <param name="target">The target object in which the change has been made. Must be a GameObject or Component.</param>
    /// <param name="fieldName">The target objects <see cref="System.Reflection.FieldInfo">Field's</see> name that has been changed.</param>
    public void PushChange(object target, string fieldName)
    {
        //if (!prop.CanWrite || !prop.CanRead) return;
        var field = target.GetType().GetInstanceField(fieldName);
        if (field == null)
            throw new ArgumentException("Target field not found in Target Object", nameof(fieldName));
        object changeVal = field.GetValue(target);

        // Clone the change list entry value, if required
        if (changeVal != null)
        {
            CloneType cloneType = CloneProvider.GetCloneType(changeVal.GetType());
            if (!cloneType.Type.IsValueType && cloneType.DefaultCloneBehavior == CloneBehavior.ChildObject)
            {
                changeVal = changeVal.DeepClone();
            }
        }

        PushChange(target, fieldName, changeVal);
    }

    /// <summary>
    /// Removes an existing change list entry.
    /// </summary>
    /// <param name="target">The target object in which the change has been made. Must be a GameObject or Component.</param>
    /// <param name="fieldName">The target objects <see cref="System.Reflection.FieldInfo">Field's</see> name that has been changed.</param>
    public void PopChange(object target, string fieldName)
    {
        GameObject targetObj = target as GameObject;
        MonoBehaviour targetComp = target as MonoBehaviour;

        PopChange((targetComp != null) ? targetComp.Identifier : targetObj.Identifier, fieldName);
    }

    private void PopChange(Guid identifier, string fieldName)
    {
        if (_changes == null || _changes.Count == 0) return;
        for (int i = _changes.Count - 1; i >= 0; i--)
        {
            if (_changes[i].fieldName == fieldName && _changes[i].identifier == identifier)
            {
                _changes.RemoveAt(i);
                break;
            }
        }
    }

    /// <summary>
    /// Returns whether there is a specific change list entry.
    /// </summary>
    /// <param name="target">The target object in which the change has been made. Must be a GameObject or Component.</param>
    /// <param name="fieldName">The target objects <see cref="System.Reflection.FieldInfo">Field's</see> name that has been changed.</param>
    /// <returns>True, if such change list entry exists, false if not.</returns>
    public bool HasChange(object target, string fieldName)
    {
        if (_changes == null || _changes.Count == 0) return false;

        GameObject targetObj = target as GameObject;
        MonoBehaviour targetComp = target as MonoBehaviour;

        Guid identifier = (targetComp != null) ? targetComp.Identifier : targetObj.Identifier;
        for (int i = 0; i < _changes.Count; i++)
        {
            if (_changes[i].identifier == identifier && _changes[i].fieldName == fieldName)
                return true;
        }

        return false;
    }

    /// <summary> Clears the change list. </summary>
    public void ClearChanges() => _changes?.Clear();

    /// <summary> Clears the change list for certain objects </summary>
    public void ClearChanges(object targetObj, string? fieldName = null)
    {
        if (_changes == null || _changes.Count == 0) return;

        for (int i = _changes.Count - 1; i >= 0; i--)
        {
            if (targetObj is GameObject go && _changes[i].identifier != go.Identifier) continue;
            if (targetObj is MonoBehaviour comp && _changes[i].identifier != comp.Identifier) continue;
            if (fieldName != null && string.Equals(_changes[i].fieldName, fieldName, StringComparison.Ordinal)) continue;
            _changes.RemoveAt(i);
        }
    }

    private void ClearEmptyChanges()
    {
        for (int i = _changes.Count - 1; i >= 0; i--)
            if (_changes[i].fieldName == null)
                _changes.RemoveAt(i);
    }

    /// <summary> Returns whether a specific object is affected by this PrefabLink. </summary>
    public bool IsSource(MonoBehaviour cmp) => _prefab.IsAvailable && _prefab.Res.HasComponent(cmp.Identifier);

    /// <summary> Returns whether a specific object is affected by this PrefabLink. </summary>
    public bool IsSource(GameObject obj) => _prefab.IsAvailable && _prefab.Res.HasGameObject(obj.Identifier);

    /// <summary>
    /// Applies all PrefabLinks in a set of GameObjects.
    /// </summary>
    /// <param name="objEnum">An enumeration of all GameObjects containing PrefabLinks that are to apply.</param>
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

public class ApplyPrefabContext : CloneProviderContext
{
    public ApplyPrefabContext() : base(false)
    {

    }
}
