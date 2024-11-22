// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Random/Int")]
public class RandomIntRangeNode : Node
{
    public override bool ShowTitle => false;
    public override string Title => "Random Int";
    public override float Width => 50;

    [Input] public int Min;
    [Input] public int Max;

    [Output, SerializeIgnore] public int Random;

    public override object GetValue(NodePort port)
    {
        int min = GetInputValue("Min", Min);
        int max = GetInputValue("Max", Max);
        return System.Random.Shared.Next(min, max);
    }
}
