// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Random/In Unit Sphere")]
public class RandomInUnitSphereNode : Node
{
    public override bool ShowTitle => false;
    public override string Title => "Random In Unit Sphere";
    public override float Width => 50;

    [Output, SerializeIgnore] public Vector3 Random;

    public override object GetValue(NodePort port)
    {
        return Prowl.Runtime.Random.InUnitSphere;
    }
}
