// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Get Name")]
public class GetNameNode : Node
{
    public override bool ShowTitle => true;
    public override string Title => "Get Name";
    public override float Width => 100;

    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Output, SerializeIgnore] public string Name;

    public override object GetValue(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        return t != null ? t.Name : null;
    }
}
