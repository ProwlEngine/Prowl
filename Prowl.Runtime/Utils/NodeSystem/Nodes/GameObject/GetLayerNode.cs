// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Get Layer")]
public class GetLayerNode : Node
{
    public override bool ShowTitle => true;
    public override string Title => "Get Layer";
    public override float Width => 100;

    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Output] public string Layer;

    public override object GetValue(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        return t != null ? t.layer : null;
    }
}
