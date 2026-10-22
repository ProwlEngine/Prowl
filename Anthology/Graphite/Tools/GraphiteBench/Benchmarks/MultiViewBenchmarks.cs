using System;
using System.Collections.Generic;
using System.Linq;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;

public readonly record struct MultiViewCase(int Views, int PassesPerView, bool History) : IComparable
{
    public int CompareTo(object? obj) => obj is MultiViewCase other ? (Views, PassesPerView, History).CompareTo((other.Views, other.PassesPerView, other.History)) : 0;

    public override string ToString() => $"{Views}v x{PassesPerView}{(History ? " History" : "")}";
}

public class MultiViewBenchmarks : GraphiteBenchmark
{
    public static IEnumerable<MultiViewCase> Cases =>
    [
        new(1, 4, false),
        new(8, 4, false),
        new(32, 4, false),
        new(8, 16, false),
        new(8, 4, true),
    ];

    [ParamsSource(nameof(Cases))]
    public MultiViewCase Case { get; set; }

    private int Views => Case.Views;
    private int PassesPerView => Case.PassesPerView;
    private bool History => Case.History;

    protected override (uint Width, uint Height)[] CreateViews()
        => Enumerable.Range(0, Views).Select(_ => (DefaultSize, DefaultSize)).ToArray();

    protected override IEnumerable<BenchPass> CreatePasses()
    {
        GraphicsProgram program = LoadProgram("Fullscreen.glsl");
        RenderResourceID[] ids = Enumerable.Range(0, PassesPerView).Select(i => RenderResourceID.Intern("ViewPass" + i)).ToArray();
        int history = History ? 1 : 0;

        for (int i = 0; i < PassesPerView; i++)
        {
            int index = i;
            PropertySet props = CreatePassProperties(new Float4(0, index / (float)PassesPerView, 0, 1), WhiteTexture);
            TextureHandle input = default;
            TextureHandle output = default;

            yield return new BenchPass("ViewPass" + index, b =>
            {
                if (index > 0)
                    input = b.DeclareInputTexture(ids[index - 1]);
                output = b.DeclareOutputTexture(ids[index], ColorDesc, index == 0 ? history : 0, ClearOps);
            }, (context, cmd) =>
            {
                Texture source = input.IsValid
                    ? context.Get(input).ColorTextures[0]
                    : history > 0 ? context.History(output, 1)!.ColorTextures[0] : WhiteTexture;
                props.SetTexture("Input", source, LinearSampler);
                DrawQuad(cmd, program, props);
            });
        }
    }
}
