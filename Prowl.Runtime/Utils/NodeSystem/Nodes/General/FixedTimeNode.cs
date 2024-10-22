// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("General/Fixed Delta Time")]
public class FixedTimeNode : Node
{
    public override bool ShowTitle => false;
    public override string Title => "Fixed Delta Time";
    public override float Width => 50;

    [Output, SerializeIgnore] public double DeltaTime;

    public override object GetValue(NodePort port) => Runtime.Time.fixedDeltaTime;
}
