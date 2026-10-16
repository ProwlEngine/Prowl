using System;
using System.Collections.Generic;
using System.Linq;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite.Bench;

public enum ComputeTopology
{
    SinglePass,
    IndependentPasses,
    ChainPasses,
}

public readonly record struct ComputeCase(int Dispatches, ComputeTopology Topology) : IComparable
{
    public int CompareTo(object? obj) => obj is ComputeCase other ? (Dispatches, Topology).CompareTo((other.Dispatches, other.Topology)) : 0;

    public override string ToString() => $"{Dispatches} {Topology}";
}

public class ComputeBenchmarks : GraphiteBenchmark
{
    private const uint Elements = 4096;

    public static IEnumerable<ComputeCase> Cases =>
    [
        new(16, ComputeTopology.ChainPasses),
        new(128, ComputeTopology.ChainPasses),
        new(512, ComputeTopology.ChainPasses),
        new(128, ComputeTopology.SinglePass),
        new(128, ComputeTopology.IndependentPasses),
    ];

    [ParamsSource(nameof(Cases))]
    public ComputeCase Case { get; set; }

    private int Dispatches => Case.Dispatches;
    private ComputeTopology Topology => Case.Topology;

    protected override IEnumerable<IPass> CreatePasses()
    {
        ComputeProgram program = Own(BenchShaderLoader.CreateCompute(Device, "BufferChain.glsl"));
        DeviceBuffer a = CreateBuffer();
        DeviceBuffer b = CreateBuffer();
        uint groups = Elements / 64;

        if (Topology == ComputeTopology.SinglePass)
        {
            PropertySet forward = CreateProperties(a, b);
            PropertySet backward = CreateProperties(b, a);
            int dispatches = Dispatches;

            yield return new BenchPass("Dispatches", _ => { }, (context, cmd) =>
            {
                cmd.SetComputeShader(program);
                for (int i = 0; i < dispatches; i++)
                {
                    cmd.SetProperties((i & 1) == 0 ? forward : backward);
                    cmd.Dispatch(groups, 1, 1);
                }
            });
            yield break;
        }

        RenderResourceID[] ids = Enumerable.Range(0, Dispatches).Select(i => RenderResourceID.Intern("Compute" + i)).ToArray();
        GraphBufferDesc desc = GraphBufferDesc.Structured(Elements, sizeof(float));
        bool chain = Topology == ComputeTopology.ChainPasses;

        for (int i = 0; i < Dispatches; i++)
        {
            int index = i;
            PropertySet props = CreateProperties(a, null);
            BufferHandle input = default;
            BufferHandle output = default;

            yield return new BenchPass("Dispatch" + index, builder =>
            {
                if (chain && index > 0)
                    input = builder.DeclareInputBuffer(ids[index - 1], BufferAccess.ShaderRead);
                output = builder.DeclareOutputBuffer(ids[index], desc);
            }, (context, cmd) =>
            {
                if (input.IsValid)
                    props.SetBuffer("Input", context.GetRenderBuffer(input));
                props.SetBuffer("Output", context.GetRenderBuffer(output));
                cmd.SetComputeShader(program);
                cmd.SetProperties(props);
                cmd.Dispatch(groups, 1, 1);
            });
        }
    }

    private DeviceBuffer CreateBuffer()
        => Own(Device.ResourceFactory.CreateBuffer(new BufferDescription(Elements * sizeof(float), BufferUsage.StructuredBufferReadWrite)));

    private static PropertySet CreateProperties(DeviceBuffer input, DeviceBuffer? output)
    {
        PropertySet props = new();
        props.SetFloat("Scale", 0.5f);
        props.SetBuffer("Input", input);
        if (output != null)
            props.SetBuffer("Output", output);
        return props;
    }
}
