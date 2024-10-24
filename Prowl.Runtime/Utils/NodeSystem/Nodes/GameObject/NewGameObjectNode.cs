// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/New GameObject")]
public class NewGameObjectNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "New GameObject";
    public override float Width => 70;

    [Input] public string Name;

    [Output, SerializeIgnore] public GameObject GameObject;

    private GameObject _result;

    public override void Execute(NodePort input)
    {
        _result = new GameObject(GetInputValue<string>(nameof(Name), Name) ?? "New Gameobject");
    }

    public override object GetValue(NodePort port)
    {
        return _result;
    }
}
