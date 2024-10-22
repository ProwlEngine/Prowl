// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Add Component")]
[RequiresUnreferencedCode("Requires Unreferenced Code")]
public class AddComponentNode : Node
{
    public override bool ShowTitle => true;
    public override string Title => "Add Component";
    public override float Width => 200;

    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Input] public string ComponentName;

    public override void OnValidate()
    {
        Error = "";
        string? compName = GetInputValue("ComponentName", ComponentName);
        if (string.IsNullOrEmpty(compName))
        {
            Error = "Component name is empty";
            ClearDynamicPorts(); // Clear the dynamic ports
            return;
        }

        Type? compType = RuntimeUtils.FindType(compName);
        if (compType == null)
        {
            Error = $"Component type {compName} not found";
            ClearDynamicPorts(); // Clear the dynamic ports
            return;
        }

        if (!compType.IsAssignableTo(typeof(MonoBehaviour)))
        {
            Error = $"Type {compName} is not a component";
            ClearDynamicPorts(); // Clear the dynamic ports
            return;
        }

        if (DynamicOutputs.Count() > 0 && DynamicOutputs.ElementAt(0).ValueType != compType)
            ClearDynamicPorts(); // Clear the dynamic ports if the type doesn't match

        if (DynamicOutputs.Count() <= 0) // Only add the input if it doesn't exist
            AddDynamicOutput(compType, ConnectionType.Multiple, TypeConstraint.None, "Result");
    }

    public override object GetValue(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        string compName = GetInputValue("ComponentName", ComponentName);

        if (t != null)
        {
            Type? compType = RuntimeUtils.FindType(compName);
            if (compType != null && compType.IsAssignableTo(typeof(MonoBehaviour)))
            {
                MonoBehaviour comp = t.AddComponent(compType);
                return comp;
            }
        }

        return null;
    }
}
