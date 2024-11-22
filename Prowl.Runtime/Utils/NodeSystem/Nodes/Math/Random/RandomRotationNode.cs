// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Random/Rotation")]
public class RandomRotationNode : Node
{
    public override bool ShowTitle => false;
    public override string Title => "Random Rotation";
    public override float Width => 50;

    [Output, SerializeIgnore] public Quaternion Random;

    public override object GetValue(NodePort port)
    {
        return Prowl.Runtime.Random.Rotation;
    }
}
