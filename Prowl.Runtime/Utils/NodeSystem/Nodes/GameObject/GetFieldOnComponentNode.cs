// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Get Field On Component")]
[RequiresUnreferencedCode("Requires Unreferenced Code")]
public class GetFieldOnComponentNode : Node
{
    public override bool ShowTitle => true;
    public override string Title => "Get Field On Component";
    public override float Width => 200;

    public object Output { get => output; set => output = value; }

    [Input(connectionType = ConnectionType.Override)] public string FieldName;

    [Input(ShowBackingValue.Never, ConnectionType.Override)] public MonoBehaviour Component;

    public FieldType FieldType;

    [Output, SerializeIgnore] private object output;

    public override object GetValue(NodePort port)
    {
        MonoBehaviour component = GetInputValue<MonoBehaviour>(nameof(Component));
        if (component == null)
        {
            Error = "Component is null";
            return null;
        }

        string name = GetInputValue<string>(nameof(FieldName), FieldName);

        Type type = component.GetType();
        return FieldType == FieldType.Field ? component.GetType().GetField(name).GetValue(component) : component.GetType().GetProperty(name).GetValue(component);
    }
}
