// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Prowl.Runtime.Cloning;

namespace Prowl.Editor.Utilities;

public class SetMember : AbstractAction
{
    private readonly object _target;
    private readonly MemberInfo _field;
    private readonly object _newValue;
    private object? _oldValue;

    public SetMember(object target, MemberInfo field, object newValue)
    {
       _target = target;
       _field = field;
       _newValue = newValue;
    }

    protected override void Do()
    {
        if (_target == null) return;

        if (_field is FieldInfo field)
        {
            _oldValue = field.GetValue(_target);
            field.SetValue(_target, _newValue);
        }
        else if (_field is PropertyInfo property)
        {
            _oldValue = property.GetValue(_target);
            property.SetValue(_target, _newValue);
        }
    }

    protected override void Undo()
    {
        if (_field is FieldInfo field)
            field.SetValue(_target, _oldValue);
        else if (_field is PropertyInfo property)
            property.SetValue(_target, _oldValue);
    }
}
