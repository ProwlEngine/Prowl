// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("General/Log")]
public class LogNode : InOutFlowNode
{
    public override string Title => "Log";
    public override float Width => 150;

    [Input] public string Message;

    public enum LogType
    {
        Log,
        Warning,
        Error
    }
    public LogType Type;

    public override void Execute(NodePort input)
    {
        var message = GetInputValue("Message", Message);

        switch (Type)
        {
            case LogType.Log:
                Debug.Log(message);
                break;
            case LogType.Warning:
                Debug.LogWarning(message);
                break;
            case LogType.Error:
                Debug.LogError(message);
                break;
        }

        ExecuteNext();
    }
}
