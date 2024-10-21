// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Get Enabled")]
public class GetEnabledNode : Node
{
    public override bool ShowTitle => true;
    public override string Title => "Get Enabled";
    public override float Width => 100;

    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Output, SerializeIgnore] public bool Enabled;

    public override object GetValue(NodePort input)
    {
        return GetInputValue("Target", Target)?.enabled ?? false;
    }
}
