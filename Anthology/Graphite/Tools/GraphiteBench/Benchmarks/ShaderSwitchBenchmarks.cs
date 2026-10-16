using System;
using System.Collections.Generic;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;

public enum SwitchOrder
{
    Sorted,
    Interleaved,
}

public readonly record struct SwitchCase(int Programs, int Draws, SwitchOrder Order) : IComparable
{
    public int CompareTo(object? obj) => obj is SwitchCase other ? (Programs, Draws, Order).CompareTo((other.Programs, other.Draws, other.Order)) : 0;

    public override string ToString() => $"{Programs}p {Draws} {Order}";
}

public class ShaderSwitchBenchmarks : GraphiteBenchmark
{
    public static IEnumerable<SwitchCase> Cases =>
    [
        new(1, 4096, SwitchOrder.Interleaved),
        new(16, 4096, SwitchOrder.Interleaved),
        new(64, 4096, SwitchOrder.Interleaved),
        new(64, 4096, SwitchOrder.Sorted),
    ];

    [ParamsSource(nameof(Cases))]
    public SwitchCase Case { get; set; }

    private int Programs => Case.Programs;
    private int Draws => Case.Draws;
    private SwitchOrder Order => Case.Order;

    protected override IEnumerable<IPass> CreatePasses()
    {
        GraphicsProgram[] programs = BenchShaderLoader.CreateVariants(Device, "Variants.glsl", Programs);
        foreach (GraphicsProgram program in programs)
            Own(program);

        PropertySet[] models = CreateModels(Draws);
        RenderResourceID color = "Color";
        int draws = Draws;
        int perProgram = draws / programs.Length;
        bool interleaved = Order == SwitchOrder.Interleaved;

        yield return new BenchPass("Switches", b => b.DeclareOutputTexture(color, ColorDesc, ops: ClearOps), (context, cmd) =>
        {
            cmd.SetFullViewport();
            cmd.SetVertexSource(Cube.Source);

            for (int d = 0; d < draws; d++)
            {
                int index = interleaved ? d % programs.Length : d / perProgram % programs.Length;
                cmd.SetShader(programs[index]);
                cmd.SetProperties(ViewProperties);
                cmd.SetProperties(models[d]);
                cmd.DrawIndexed();
            }
        });
    }
}
