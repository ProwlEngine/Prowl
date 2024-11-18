// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Runtime;
using Prowl.Runtime.Cloning;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Editor.Utilities;

public class AddGameObjectToSceneAction : AbstractAction
{
    private readonly GameObject GameObject;
    private Guid? identifier;

    public AddGameObjectToSceneAction(GameObject gameObject) =>
        (GameObject) = (gameObject);


    protected override void Do()
    {
        SceneManager.Scene.Add(GameObject.DeepClone(new(false)));
        identifier = GameObject.Identifier;
    }

    protected override void Undo()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(identifier.Value);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        go.DestroyImmediate();
    }
}

public class DeleteGameObjectAction : AbstractAction
{
    private readonly Guid GameObject;
    private GameObject? _gameObject;

    public DeleteGameObjectAction(Guid gameObject) =>
        (GameObject) = (gameObject);

    protected override void Do()
    {
        _gameObject = EngineObject.FindObjectByIdentifier<GameObject>(GameObject);
        if (_gameObject == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        _gameObject.DestroyImmediate();
    }

    protected override void Undo()
    {
        if (_gameObject == null)
            throw new InvalidOperationException("GameObject is null");

        SceneManager.Scene.Add(_gameObject.DeepClone(new(false)));
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

    private readonly Vector3 _position;
    private readonly Quaternion _rotation;
    private readonly Vector3 _scale;

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
    }
}
