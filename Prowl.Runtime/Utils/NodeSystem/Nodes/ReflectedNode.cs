// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;
using System.Reflection;

using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.Utils.NodeSystem.Nodes;

public class ReflectedNode : FlowNode, ISerializationCallbackReceiver
{
    public override string Title => node_title;
    public override float Width => node_width;


    private MethodInfo cached_method;
    [SerializeField, HideInInspector] private string type_Name;
    [SerializeField, HideInInspector] private string method_Name;
    [SerializeField, HideInInspector] private string node_title;
    [SerializeField, HideInInspector] private float node_width;

    private string[] parameterNames;

    public static string GetNodeName(MethodInfo method_info)
    {
        ArgumentNullException.ThrowIfNull(method_info, nameof(method_info));

        string node_title = method_info.Name;
        if (node_title.Contains("get_"))
            node_title = node_title.Replace("get_", string.Empty);
        else if (node_title.Contains("set_"))
            node_title = node_title.Replace("set_", string.Empty);
        node_title = RuntimeUtils.Prettify(node_title);

        return node_title;
    }

    public void SetMethod(MethodInfo method_info)
    {
        ArgumentNullException.ThrowIfNull(method_info, nameof(method_info));

        if (method_info.GetParameters().Any(p => p.IsIn))
            throw new ArgumentException("ReflectedNode does not support methods with 'in' parameters");

        cached_method = method_info;
        node_title = GetNodeName(method_info);

        type_Name = method_info.ReflectedType.FullName ?? throw new InvalidOperationException();
        method_Name = method_info.Name;

        AddDynamicInput(typeof(FlowNode), ConnectionType.Override, TypeConstraint.Strict, "From", true);
        AddDynamicOutput(typeof(FlowNode), ConnectionType.Override, TypeConstraint.Strict, "To", true);

        if (method_info.ReturnType != typeof(void))
            AddDynamicOutput(method_info.ReturnType, ConnectionType.Multiple, TypeConstraint.None, "Get");

        if (!method_info.IsStatic)
            AddDynamicInput(method_info.ReflectedType, ConnectionType.Override, TypeConstraint.AssignableTo, "Target");

        parameterNames = method_info.GetParameters().Select(i => RuntimeUtils.Prettify(i.Name)).ToArray();
        foreach (ParameterInfo parameter in method_info.GetParameters())
        {
            //if (parameter.IsOut)
            //    AddDynamicOutput(method_info.ReturnType, ConnectionType.Multiple, TypeConstraint.None, RuntimeUtils.Prettify(parameter.Name));
            //else
            AddDynamicInput(parameter.ParameterType, ConnectionType.Override, TypeConstraint.AssignableTo, RuntimeUtils.Prettify(parameter.Name));
        }

        // Calculate width
        var inputsWidth = parameterNames.Select(i => Font.DefaultFont.CalcTextSize(i, 0).x + 20).Max();
        var titleWidth = Math.Max(inputsWidth, Font.DefaultFont.CalcTextSize(node_title, 0).x + 20);
        var outputWidth = method_info.ReturnType == typeof(void) ? 50 : 75;
        node_width = (float)MathD.Max(titleWidth, inputsWidth, outputWidth);
    }

    public override void Execute(NodePort input)
    {
        GetValue(input);
        ExecuteNext();
    }

    public override object GetValue(NodePort port)
    {
        var target = GetInputValue<object>("Target", null);
        var args = parameterNames.Select(i => GetInputValue<object>(i)).ToArray();
        return cached_method.Invoke(target, args);
    }

    public static bool IsSupported(MethodInfo method)
    {
        if (method.GetParameters().Any(p => p.IsIn))
            return false;

        if (method.IsGenericMethod)
            return false;

        return true;
    }

    public void OnBeforeSerialize()
    {
    }

    public void OnAfterDeserialize()
    {
        var type = Type.GetType(type_Name ?? "");
        if (type == null)
        {
            node_title = "Missing Type";
            node_width = 100;
            ClearDynamicPorts();
            return;
        }

        var method = type.GetMethod(method_Name ?? "");
        if (method == null)
        {
            node_title = "Missing Method";
            node_width = 100;
            ClearDynamicPorts();
            return;
        }

        cached_method = method;
    }
}
