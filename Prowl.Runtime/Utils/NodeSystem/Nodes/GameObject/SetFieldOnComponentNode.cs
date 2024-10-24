// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Set Field On Component")]
[RequiresUnreferencedCode("Requires Unreferenced Code")]
public class SetFieldOnComponentNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Set Field On Component";
    public override float Width => 200;

    [Input] public string FieldName;

    [Input(ShowBackingValue.Never)] public MonoBehaviour _component;

    [Input(ShowBackingValue.Never)] public object _value;

    public FieldType _fieldType;

    public override void Execute(NodePort input)
    {
        MonoBehaviour component = GetInputValue<MonoBehaviour>(nameof(_component));
        if (component == null)
        {
            Error = "Component is null";
            return;
        }

        object value = GetInputValue<object>(nameof(_value));
        string fieldName = GetInputValue<string>(nameof(FieldName), FieldName);
        switch (_fieldType)
        {
            case FieldType.Field:
                FieldInfo? field = component.GetType().GetField(fieldName);
                field.SetValue(component, value);
                break;
            case FieldType.Property:
                PropertyInfo? property = component.GetType().GetProperty(fieldName);
                property.SetValue(component, value);
                break;
        }
    }
}
