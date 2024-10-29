// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Call Method On Component")]
[RequiresUnreferencedCode("Requires Unreferenced Code")]
public class CallMethodOnComponentNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Call Method On Component";
    public override float Width => 200;

    [Input(connectionType = ConnectionType.Override)] public string MethodName;

    [Input(ShowBackingValue.Never, ConnectionType.Override)] public MonoBehaviour Component;

    [Input(ShowBackingValue.Never, ConnectionType.Multiple)] public object Params;

    public BindingFlags _bindingFlags = BindingFlags.InvokeMethod;

    [Output, SerializeIgnore] public object Output;

    private object _outputData;

    public override void Execute(NodePort input)
    {
        MonoBehaviour component = GetInputValue<MonoBehaviour>(nameof(Component));
        if (component == null)
        {
            Error = "Component is null";
            return;
        }

        string methodName = GetInputValue<string>(nameof(MethodName), MethodName);

        NodePort inputParams = GetInputPort(nameof(Params));
        object[] paramsArray = null;
        if (inputParams.Connection != null)
        {
            System.Collections.Generic.List<NodePort> connections = inputParams.GetConnections();
            paramsArray = new object[connections.Count];

            for (int i = 0; i < connections.Count; i++)
            {
                NodePort connection = connections[i];
                paramsArray[i] = connection.GetOutputValue();
            }

        }

        _outputData = component.GetType().InvokeMember(methodName, _bindingFlags, null, component, paramsArray);
    }

    public override object GetValue(NodePort port)
    {
        return _outputData;
    }
}
