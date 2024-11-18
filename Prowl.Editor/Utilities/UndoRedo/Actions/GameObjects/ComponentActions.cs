// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Runtime;
using Prowl.Runtime.Cloning;

namespace Prowl.Editor.Utilities;

public class AddComponentAction<T> : AbstractAction
{
    private readonly Guid GameObject;
    private readonly Type Component;
    private Guid? identifier;

    public AddComponentAction(Guid gameObject, Type component) =>
        (GameObject, Component) = (gameObject, component);


    protected override void Do()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(GameObject);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        var comp = go.AddComponent(Component);
        identifier ??= comp.Identifier;

        // Ensure its always identified as the same component
        comp.Identifier = identifier.Value;
    }

    protected override void Undo()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(GameObject);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        if (identifier == null)
            throw new InvalidOperationException("Identifier is null");

        go.RemoveComponent(identifier.Value);
    }
}

public class AddComponentAction : AbstractAction
{
    private readonly Guid GameObject;
    private MonoBehaviour Component;
    private Guid? identifier;

    public AddComponentAction(Guid gameObject, MonoBehaviour component) =>
        (GameObject, Component) = (gameObject, component);


    protected override void Do()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(GameObject);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        identifier ??= Component.Identifier;
        go.AddComponent(Component.DeepClone(new(false)));
    }

    protected override void Undo()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(GameObject);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        go.RemoveComponent(identifier.Value);
    }
}

public class RemoveComponentAction : AbstractAction
{
    private readonly Guid GameObject;
    private readonly Guid Component;
    private MonoBehaviour backup;

    public RemoveComponentAction(Guid gameObject, Guid component) =>
        (GameObject, Component) = (gameObject, component);

    protected override void Do()
    {
        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(GameObject);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        backup = go.GetComponentByIdentifier(Component).DeepClone(new(false));
        if (backup == null)
            throw new InvalidOperationException("Could not find component with identifier: " + Component);
          
        go.RemoveComponent(Component);
    }

    protected override void Undo()
    {
        if (backup == null)
            throw new InvalidOperationException("Backup is null");

        GameObject? go = EngineObject.FindObjectByIdentifier<GameObject>(GameObject);
        if (go == null)
            throw new InvalidOperationException("Could not find GameObject with identifier: " + GameObject);

        var comp = backup.DeepClone(new(false));
        go.AddComponent(comp);
        comp.OnValidate();
    }
}

public class ChangeFieldOnComponentAction : AbstractAction
{
    private readonly Guid _target;
    private readonly MemberInfo _field;
    private readonly object _newValue;
    private object? _oldValue;

    public ChangeFieldOnComponentAction(MonoBehaviour target, MemberInfo field, object newValue)
    {
        _target = target?.Identifier ?? Guid.Empty;
        _field = field;
        _newValue = newValue;
    }

    protected override void Do()
    {
        if (_target == Guid.Empty) return;

        MonoBehaviour? comp = EngineObject.FindObjectByIdentifier<MonoBehaviour>(_target);
        if(comp == null)
            throw new InvalidOperationException("Could not find component with identifier: " + _target);

        if (_field is FieldInfo field)
        {
            _oldValue = field.GetValue(comp);
            field.SetValue(comp, _newValue);
        }
        else if (_field is PropertyInfo property)
        {
            _oldValue = property.GetValue(comp);
            property.SetValue(comp, _newValue);
        }

        comp.OnValidate();
    }

    protected override void Undo()
    {
        MonoBehaviour? comp = EngineObject.FindObjectByIdentifier<MonoBehaviour>(_target);
        if (comp == null)
            throw new InvalidOperationException("Could not find component with identifier: " + _target);

        if (_field is FieldInfo field)
            field.SetValue(comp, _oldValue);
        else if (_field is PropertyInfo property)
            property.SetValue(comp, _oldValue);

        comp.OnValidate();
    }
}
