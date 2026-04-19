// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

/// <summary>
/// Outputs a literal float value. Phase 5 will add a UI to edit the value inline.
/// </summary>
public sealed class FloatConstantNode : Node, IShaderGraphNode
{
    public float Value = 1.0f;

    public override string Title => $"Float ({Value:0.##})";
    public override string Category => "Input/Constants";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 180, 150, 60);

    protected override void DefineNode()
    {
        AddOutput<float>("Out");
    }
}

public sealed class Vector3ConstantNode : Node, IShaderGraphNode
{
    public Float3 Value = Float3.One;

    public override string Title => $"Vector3 ({Value.X:0.#}, {Value.Y:0.#}, {Value.Z:0.#})";
    public override string Category => "Input/Constants";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 100, 180, 100);

    protected override void DefineNode()
    {
        AddOutput<Float3>("Out");
    }
}

public sealed class MultiplyNode : Node, IShaderGraphNode
{
    public override string Category => "Math/Basic";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 90, 130, 200);

    protected override void DefineNode()
    {
        AddInput<float>("A", 1.0f);
        AddInput<float>("B", 1.0f);
        AddOutput<float>("Result");
    }
}

public sealed class AddNode : Node, IShaderGraphNode
{
    public override string Category => "Math/Basic";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 90, 130, 200);

    protected override void DefineNode()
    {
        AddInput<float>("A", 0.0f);
        AddInput<float>("B", 0.0f);
        AddOutput<float>("Result");
    }
}

public sealed class SinNode : Node, IShaderGraphNode
{
    public override string Category => "Math/Trig";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 90, 130, 200);

    protected override void DefineNode()
    {
        AddInput<float>("X", 0.0f);
        AddOutput<float>("Result");
    }
}

/// <summary>
/// Terminal node — what the graph evaluates to. A real shader graph would have a
/// dedicated MasterOutput node with multiple inputs (Albedo, Normal, Smoothness, ...).
/// </summary>
public sealed class FragmentOutputNode : Node, IShaderGraphNode
{
    public override string Title => "Fragment Output";
    public override string Category => "Output";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 200, 80, 100);

    protected override void DefineNode()
    {
        AddInput<Float3>("Albedo", Float3.One);
        AddInput<float>("Smoothness", 0.5f);
        AddInput<float>("Metallic", 0.0f);
        AddInput<Float3>("Normal", new Float3(0, 0, 1));
        AddInput<Float3>("Emission", Float3.Zero);
    }
}
