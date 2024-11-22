// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Random/Double")]
public class RandomDoubleRangeNode : Node
{
    public override bool ShowTitle => false;
    public override string Title => "Random Double";
    public override float Width => 50;

    [Input] public double Min;
    [Input] public double Max;

    [Output, SerializeIgnore] public double Random;

    public override object GetValue(NodePort port)
    {
        double min = GetInputValue("Min", Min);
        double max = GetInputValue("Max", Max);
        return System.Random.Shared.NextDouble() * (max - min) + min;
    }
}
