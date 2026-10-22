using System;
using System.Collections.Generic;
using System.Linq;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;

public enum GraphResourceScenario
{
    Transient,
    MixedFormats,
    History,
    Buffers,
}

public readonly record struct GraphResourceCase(int Resources, GraphResourceScenario Kind) : IComparable
{
    public int CompareTo(object? obj) => obj is GraphResourceCase other ? (Resources, Kind).CompareTo((other.Resources, other.Kind)) : 0;

    public override string ToString() => $"{Resources} {Kind}";
}

public class GraphResourceBenchmarks : GraphiteBenchmark
{
    private const uint BufferElements = 4096;

    private static readonly PixelFormat[] s_formats =
    [
        PixelFormat.R8_G8_B8_A8_UNorm,
        PixelFormat.R16_G16_B16_A16_Float,
        PixelFormat.R11_G11_B10_Float,
        PixelFormat.R32_G32_B32_A32_Float,
    ];

    private static readonly float[] s_scales = [1f, 0.5f, 0.25f];

    public static IEnumerable<GraphResourceCase> Cases =>
    [
        new(16, GraphResourceScenario.Transient),
        new(64, GraphResourceScenario.Transient),
        new(256, GraphResourceScenario.Transient),
        new(64, GraphResourceScenario.MixedFormats),
        new(64, GraphResourceScenario.History),
        new(64, GraphResourceScenario.Buffers),
    ];

    [ParamsSource(nameof(Cases))]
    public GraphResourceCase Case { get; set; }

    private int Resources => Case.Resources;
    private GraphResourceScenario Kind => Case.Kind;

    protected override IEnumerable<BenchPass> CreatePasses()
        => Kind == GraphResourceScenario.Buffers ? CreateBufferPasses() : CreateTexturePasses();

    private IEnumerable<BenchPass> CreateTexturePasses()
    {
        GraphicsProgram flat = LoadProgram("Flat.glsl");
        GraphicsProgram fullscreen = LoadProgram("Fullscreen.glsl");
        RenderResourceID[] ids = Enumerable.Range(0, Resources).Select(i => RenderResourceID.Intern("Resource" + i)).ToArray();
        RenderResourceID final = "Final";
        GraphResourceScenario kind = Kind;

        for (int i = 0; i < Resources; i++)
        {
            int index = i;
            BenchTexture desc = kind == GraphResourceScenario.MixedFormats
                ? new BenchTexture(s_formats[index % s_formats.Length], s_scales[index / s_formats.Length % s_scales.Length], Depth: index % 2 == 1)
                : ColorDesc;
            int history = kind == GraphResourceScenario.History ? 1 : 0;
            PropertySet props = CreatePassProperties(new Float4(index / (float)Resources, 1, 0, 1), WhiteTexture);
            TextureHandle output = default;

            yield return new BenchPass("Write" + index, b => output = b.DeclareOutputTexture(ids[index], desc, history, ClearOps), (context, cmd) =>
            {
                if (history == 0)
                {
                    DrawQuad(cmd, flat, props);
                    return;
                }

                props.SetTexture("Input", context.History(output, 1)!.ColorTextures[0], LinearSampler);
                DrawQuad(cmd, fullscreen, props);
            });
        }

        PropertySet finalProps = CreatePassProperties(Float4.One, WhiteTexture);
        TextureHandle first = default;

        yield return new BenchPass("ReadAll", b =>
        {
            foreach (RenderResourceID id in ids)
            {
                TextureHandle handle = b.DeclareInputTexture(id);
                if (!first.IsValid)
                    first = handle;
            }
            b.DeclareOutputTexture(final, ColorDesc, ops: ClearOps);
        }, (context, cmd) =>
        {
            finalProps.SetTexture("Input", context.Get(first).ColorTextures[0], LinearSampler);
            DrawQuad(cmd, fullscreen, finalProps);
        });
    }

    private IEnumerable<BenchPass> CreateBufferPasses()
    {
        ComputeProgram program = Own(BenchShaderLoader.CreateCompute(Device, "BufferChain.glsl"));
        DeviceBuffer source = Own(Device.ResourceFactory.CreateBuffer(new BufferDescription(BufferElements * sizeof(float), BufferUsage.StructuredBufferReadWrite)));
        RenderResourceID[] ids = Enumerable.Range(0, Resources).Select(i => RenderResourceID.Intern("Buffer" + i)).ToArray();
        RenderResourceID final = "FinalBuffer";
        GraphBufferDesc desc = GraphBufferDesc.Structured(BufferElements, sizeof(float));
        uint groups = BufferElements / 64;

        for (int i = 0; i < Resources; i++)
        {
            int index = i;
            PropertySet props = CreateComputeProperties(source);
            BufferHandle output = default;

            yield return new BenchPass("Write" + index, b => output = b.DeclareOutputBuffer(ids[index], desc), (context, cmd) =>
            {
                props.SetBuffer("Output", context.Get(output));
                cmd.SetComputeShader(program);
                cmd.SetProperties(props);
                cmd.Dispatch(groups, 1, 1);
            });
        }

        PropertySet finalProps = CreateComputeProperties(source);
        BufferHandle first = default;
        BufferHandle finalHandle = default;

        yield return new BenchPass("ReadAll", b =>
        {
            foreach (RenderResourceID id in ids)
            {
                BufferHandle handle = b.DeclareInputBuffer(id, BufferAccess.ShaderRead);
                if (!first.IsValid)
                    first = handle;
            }
            finalHandle = b.DeclareOutputBuffer(final, desc);
        }, (context, cmd) =>
        {
            finalProps.SetBuffer("Input", context.Get(first));
            finalProps.SetBuffer("Output", context.Get(finalHandle));
            cmd.SetComputeShader(program);
            cmd.SetProperties(finalProps);
            cmd.Dispatch(groups, 1, 1);
        });
    }

    private static PropertySet CreateComputeProperties(DeviceBuffer input)
    {
        PropertySet props = new();
        props.SetFloat("Scale", 0.5f);
        props.SetBuffer("Input", input);
        return props;
    }
}
