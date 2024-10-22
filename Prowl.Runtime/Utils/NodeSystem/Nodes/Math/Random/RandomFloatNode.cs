// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Random/Float")]
public class RandomFloatRangeNode : Node
{
    public override bool ShowTitle => false;
    public override string Title => "Random Float";
    public override float Width => 50;

    [Input] public float Min;
    [Input] public float Max;

    [Output, SerializeIgnore] public float Random;

    public override object GetValue(NodePort port)
    {
        float min = GetInputValue("Min", Min);
        float max = GetInputValue("Max", Max);
        return (float)System.Random.Shared.NextDouble() * (max - min) + min;
    }
}
