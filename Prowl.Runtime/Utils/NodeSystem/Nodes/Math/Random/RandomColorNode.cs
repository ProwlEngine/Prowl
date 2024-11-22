// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Random/Color")]
public class RandomColorNode : Node
{
    public override bool ShowTitle => false;
    public override string Title => "Random Color";
    public override float Width => 50;

    [Output, SerializeIgnore] public Color Random;

    public override object GetValue(NodePort port)
    {
        return Prowl.Runtime.Random.Color;
    }
}
