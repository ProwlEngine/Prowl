using System;
using System.Collections.Generic;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;

public enum ShaderKind
{
    Flat,
    Alu,
    Texture,
    Branch,
    Vertex,
}

public readonly record struct ShaderCase(ShaderKind Shader, int Resolution, int Overdraw) : IComparable
{
    public int CompareTo(object? obj) => obj is ShaderCase other ? (Shader, Resolution, Overdraw).CompareTo((other.Shader, other.Resolution, other.Overdraw)) : 0;

    public override string ToString() => $"{Shader} {Resolution}px x{Overdraw}";
}

public class ShaderComplexityBenchmarks : GraphiteBenchmark
{
    private const int GridCells = 256;

    public static IEnumerable<ShaderCase> Cases =>
    [
        new(ShaderKind.Flat, 2048, 1),
        new(ShaderKind.Alu, 2048, 1),
        new(ShaderKind.Texture, 2048, 1),
        new(ShaderKind.Branch, 2048, 1),
        new(ShaderKind.Vertex, 2048, 1),
        new(ShaderKind.Alu, 512, 1),
        new(ShaderKind.Alu, 2048, 4),
    ];

    [ParamsSource(nameof(Cases))]
    public ShaderCase Case { get; set; }

    private ShaderKind Shader => Case.Shader;
    private int Resolution => Case.Resolution;
    private int Overdraw => Case.Overdraw;

    protected override (uint Width, uint Height)[] CreateViews() => [((uint)Resolution, (uint)Resolution)];

    protected override IEnumerable<BenchPass> CreatePasses()
    {
        (string file, int iterations) = Shader switch
        {
            ShaderKind.Alu => ("Alu.glsl", 64),
            ShaderKind.Texture => ("TextureHeavy.glsl", 32),
            ShaderKind.Branch => ("Branch.glsl", 64),
            ShaderKind.Vertex => ("VertexHeavy.glsl", 64),
            _ => ("Flat.glsl", 0),
        };

        GraphicsProgram program = LoadProgram(file);
        IVertexSource mesh = Shader == ShaderKind.Vertex ? Own(BenchMesh.CreateGrid(Device, GridCells)).Source : Quad.Source;

        PropertySet props = new();
        props.SetFloat4("Tint", new Float4(1, 1, 1, 1));
        if (iterations > 0)
            props.SetInt("Iterations", iterations);
        if (Shader == ShaderKind.Texture)
            props.SetTexture("Noise", CreateTexture(1024, 7), LinearSampler);

        RenderResourceID color = "Color";
        int overdraw = Overdraw;

        yield return new BenchPass("Shade", b => b.DeclareOutputTexture(color, ColorDesc, ops: ClearOps), (context, cmd) =>
        {
            cmd.SetFullViewport();
            cmd.SetShader(program);
            cmd.SetVertexSource(mesh);
            cmd.SetProperties(props);
            for (int i = 0; i < overdraw; i++)
                cmd.DrawIndexed();
        });
    }
}
