using System;
using System.Collections.Generic;
using System.Linq;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;

public enum PassTopology
{
    Undeclared,
    SharedTarget,
    Chain,
    Dead,
}

public readonly record struct PassCase(int Passes, int DrawsPerPass, PassTopology Topology) : IComparable
{
    public int CompareTo(object? obj) => obj is PassCase other ? (Passes, DrawsPerPass, Topology).CompareTo((other.Passes, other.DrawsPerPass, other.Topology)) : 0;

    public override string ToString() => $"{Passes}x{DrawsPerPass} {Topology}";
}

public class PassBenchmarks : GraphiteBenchmark
{
    public static IEnumerable<PassCase> Cases =>
    [
        new(16, 1, PassTopology.Chain),
        new(128, 1, PassTopology.Chain),
        new(512, 1, PassTopology.Chain),
        new(128, 1, PassTopology.Undeclared),
        new(128, 1, PassTopology.SharedTarget),
        new(128, 1, PassTopology.Dead),
        new(128, 16, PassTopology.Chain),
    ];

    [ParamsSource(nameof(Cases))]
    public PassCase Case { get; set; }

    private int Passes => Case.Passes;
    private int DrawsPerPass => Case.DrawsPerPass;
    private PassTopology Topology => Case.Topology;

    protected override IEnumerable<BenchPass> CreatePasses()
    {
        GraphicsProgram program = LoadProgram("Fullscreen.glsl");
        RenderResourceID shared = "Shared";
        RenderResourceID[] ids = Enumerable.Range(0, Passes).Select(i => RenderResourceID.Intern("Pass" + i)).ToArray();
        Framebuffer? framebuffer = Topology == PassTopology.Undeclared ? CreateFramebuffer() : null;
        PassTopology topology = Topology;
        int draws = DrawsPerPass;

        for (int i = 0; i < Passes; i++)
        {
            int index = i;
            PropertySet props = CreatePassProperties(new Float4(index / (float)Passes, 0, 0, 1), WhiteTexture);
            TextureHandle input = default;

            yield return new BenchPass("Pass" + index, b =>
            {
                switch (topology)
                {
                    case PassTopology.SharedTarget:
                        b.DeclareOutputTexture(shared, ColorDesc, ops: index == 0 ? ClearOps : LoadOps);
                        break;
                    case PassTopology.Chain:
                        if (index > 0)
                            input = b.DeclareInputTexture(ids[index - 1]);
                        b.DeclareOutputTexture(ids[index], ColorDesc, ops: ClearOps);
                        break;
                    case PassTopology.Dead:
                        b.DeclareOutputTexture(ids[index], ColorDesc, ops: ClearOps);
                        break;
                }
            }, (context, cmd) =>
            {
                if (framebuffer != null)
                    cmd.SetFramebuffer(framebuffer, index == 0 ? ClearOps : LoadOps);

                if (input.IsValid)
                    props.SetTexture("Input", context.Get(input).ColorTextures[0], LinearSampler);

                for (int d = 0; d < draws; d++)
                    DrawQuad(cmd, program, props);
            });
        }
    }

    private Framebuffer CreateFramebuffer()
    {
        Texture target = Own(Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            DefaultSize, DefaultSize, 1, 1, ColorFormat, TextureUsage.RenderTarget | TextureUsage.Sampled)));
        return Own(Device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, target)));
    }
}
