// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Runtime;
using Prowl.Runtime.Cloning;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Utilities;

public class AddGameObjectToSceneAction : AbstractAction
{
    private readonly GameObject GameObject;
    private readonly Guid Parent;
    private GameObject backup;
    private Guid? identifier;

    public AddGameObjectToSceneAction(GameObject gameObject, GameObject parent) =>
        (GameObject, Parent) = (gameObject, parent?.Identifier ?? Guid.Empty);


    protected override void Do()
    {
        if(backup == null)
            backup = GameObject.DeepClone(new(false));

        var go = backup.DeepClone(new(false));
        GameObject parent = EngineObject.FindObjectByIdentifier<GameObject>(Parent);
        if (parent != null)
        {
            go.SetParent(parent);
        }
        else
        {
            SceneManager.Scene.Add(go);
        }

        identifier = go.Identifier;
    }

    protected override void Undo()
    {
        GameObject go = EngineObject.FindObjectByIdentifier<GameObject>(identifier.Value);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        go.DestroyImmediate();
    }
}

public class CloneGameObjectAction : AbstractAction
{
    private readonly Guid GameObject;
    private GameObject? _gameObject;
    private Guid _parent;
    private Guid _clone;

    public CloneGameObjectAction(Guid gameObject) =>
        (GameObject) = (gameObject);

    protected override void Do()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(GameObject);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        _gameObject = go.DeepClone();
        _parent = go.parent?.Identifier ?? Guid.Empty;

        var clone = _gameObject.DeepClone(new(false));
        _clone = clone.Identifier;
        GameObject? parent = EngineObject.FindObjectByIdentifier<GameObject>(_parent);
        if (parent != null)
        {
            clone.SetParent(parent);
        }
        else
        {
            SceneManager.Scene.Add(clone);
        }
    }

    protected override void Undo()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(_clone);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + _clone);

        go.DestroyImmediate();
    }
}

public class DeleteGameObjectAction : AbstractAction
{
    private readonly Guid GameObject;
    private GameObject? _gameObject;
    private Guid _parent;

    public DeleteGameObjectAction(Guid gameObject) =>
        (GameObject) = (gameObject);

    protected override void Do()
    {
        var go = EngineObject.FindObjectByIdentifier<GameObject>(GameObject);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        _gameObject = go.DeepClone(new(false));
        _parent = go.parent?.Identifier ?? Guid.Empty;

        go.DestroyImmediate();
    }

    protected override void Undo()
    {
        if (_gameObject == null)
            throw new InvalidOperationException("GameObject is null");

        var go = _gameObject.DeepClone(new(false));
        GameObject? parent = EngineObject.FindObjectByIdentifier<GameObject>(_parent);
        if (parent != null)
        {
            go.SetParent(parent);
        }
        else
        {
            SceneManager.Scene.Add(go);
        }
    }
}

public class SetParentAction : AbstractAction
{
    private readonly Guid GameObject;
    private readonly Guid Parent;
    private Guid OldParent;

    public SetParentAction(Guid gameObject, Guid parent) =>
        (GameObject, Parent) = (gameObject, parent);

    protected override void Do()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(GameObject);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        GameObject? parent = EngineObject.FindObjectByIdentifier<GameObject>(Parent);

        OldParent = go.parent?.Identifier ?? Guid.Empty;
        go.SetParent(parent);
    }

    protected override void Undo()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(GameObject);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        GameObject? parent = EngineObject.FindObjectByIdentifier<GameObject>(OldParent);
        go.SetParent(parent);
    }
}

public class ChangeFieldOnGameObjectAction : AbstractAction
{
    private readonly Guid _target;
    private readonly MemberInfo _field;
    private readonly object _newValue;
    private object? _oldValue;

    public ChangeFieldOnGameObjectAction(GameObject target, MemberInfo field, object newValue)
    {
        _target = target?.Identifier ?? Guid.Empty;
        _field = field;
        _newValue = newValue;
    }

    public ChangeFieldOnGameObjectAction(GameObject target, string field, object newValue)
    {
        _target = target?.Identifier ?? Guid.Empty;
        _field = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new InvalidOperationException($"Field '{field}' not found on '{target}'");
        _newValue = newValue;
    }

    protected override void Do()
    {
        if (_target == Guid.Empty) return;

        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(_target);
        if (go == null)
            throw new InvalidOperationException("Could not find gameobject with identifier: " + _target);

        if (_field is FieldInfo field)
        {
            _oldValue = field.GetValue(go);
            field.SetValue(go, _newValue);
        }
        else if (_field is PropertyInfo property)
        {
            _oldValue = property.GetValue(go);
            property.SetValue(go, _newValue);
        }
    }

    protected override void Undo()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(_target);
        if (go == null)
            throw new InvalidOperationException("Could not find gameobject with identifier: " + _target);

        if (_field is FieldInfo field)
            field.SetValue(go, _oldValue);
        else if (_field is PropertyInfo property)
            property.SetValue(go, _oldValue);
    }
}

public class ChangeTransformAction : AbstractAction
{
    private readonly Guid _target;

    private Vector3 _position;
    private Quaternion _rotation;
    private Vector3 _scale;

    private Vector3 _oldPosition;
    private Quaternion _oldRotation;
    private Vector3 _oldScale;

    public ChangeTransformAction(GameObject target, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        _target = target?.Identifier ?? Guid.Empty;
        (_position, _rotation, _scale) = (position, rotation, scale);
    }

    protected override void Do()
    {
        if (_target == Guid.Empty) return;

        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(_target);
        if (go == null)
            throw new InvalidOperationException("Could not find gameobject with identifier: " + _target);

        var transform = go.Transform;

        _oldPosition = transform.localPosition;
        _oldRotation = transform.localRotation;
        _oldScale = transform.localScale;

        transform.localPosition = _position;
        transform.localRotation = _rotation;
        transform.localScale = _scale;

        // Trigger OnValidate on all components
        foreach (var comp in go.GetComponents())
            comp.OnValidate();
    }

    protected override void Undo()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(_target);
        if (go == null)
            throw new InvalidOperationException("Could not find gameobject with identifier: " + _target);

        var transform = go.Transform;

        transform.localPosition = _oldPosition;
        transform.localRotation = _oldRotation;
        transform.localScale = _oldScale;

        // Trigger OnValidate on all components
        foreach (var comp in go.GetComponents())
            comp.OnValidate();
    }

    public override bool TryMerge(IAction action)
    {
        if (action is ChangeTransformAction next && next._target == _target)
        {
            _position = next._position;
            _rotation = next._rotation;
            _scale = next._scale;

            GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(_target);
            if (go == null)
                throw new InvalidOperationException("Could not find gameobject with identifier: " + _target);

            var transform = go.Transform;

            transform.localPosition = _position;
            transform.localRotation = _rotation;
            transform.localScale = _scale;

            // Trigger OnValidate on all components
            foreach (var comp in go.GetComponents())
                comp.OnValidate();
            return true;
        }
        return false;
    }
}

public class BreakPrefabLinkAction : AbstractAction
{
    private readonly Guid _target;
    private PrefabLink _prefab;

    public BreakPrefabLinkAction(GameObject target)
    {
        _target = target?.Identifier ?? throw new ArgumentNullException(nameof(target));
    }

    protected override void Do()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(_target);
        if (go == null)
            throw new InvalidOperationException("Could not find gameobject with identifier: " + _target);

        _prefab ??= go.AffectedByPrefabLink.DeepClone(new(false));

        PrefabUtility.BreakPrefabLink(go);
    }

    protected override void Undo()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(_target);
        if (go == null)
            throw new InvalidOperationException("Could not find gameobject with identifier: " + _target);

        PrefabUtility.LinkToPrefab(go, _prefab.Prefab);
        _prefab.DeepCopyTo(go.PrefabLink);
    }
}

// TODO: Implement ApplyPrefabLinkAction
//public class ApplyPrefabLinkAction : AbstractAction
//{
//}

public class ResetPrefabLinkAction : AbstractAction
{
    private readonly Guid _target;
    private GameObject _backup;
    private PrefabLink _prefab;

    public ResetPrefabLinkAction(GameObject target)
    {
        if(target.PrefabLink == null)
            throw new InvalidOperationException("GameObject does not have a prefab link");
        _target = target?.Identifier ?? throw new ArgumentNullException(nameof(target));
    }

    protected override void Do()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(_target);
        if (go == null)
            throw new InvalidOperationException("Could not find gameobject with identifier: " + _target);

        _backup ??= go.DeepClone(new(false));
        _prefab ??= go.AffectedByPrefabLink.DeepClone(new(false));

        go.PrefabLink.ClearChanges();
        PrefabLink.ApplyAllLinks([go]);
    }

    protected override void Undo()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(_target);
        if (go == null)
            throw new InvalidOperationException("Could not find gameobject with identifier: " + _target);

        _backup.DeepCopyTo(go);
        _prefab.DeepCopyTo(go.PrefabLink);
    }
}
