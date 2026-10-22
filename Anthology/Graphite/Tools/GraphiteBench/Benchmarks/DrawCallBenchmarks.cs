using System;
using System.Collections.Generic;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;

public enum DrawMode
{
    Identical,
    UniqueStatic,
    UniqueAnimated,
    MeshRebind,
    ShaderRebind,
}

public readonly record struct DrawCase(int Draws, DrawMode Mode) : IComparable
{
    public int CompareTo(object? obj) => obj is DrawCase other ? (Draws, Mode).CompareTo((other.Draws, other.Mode)) : 0;

    public override string ToString() => $"{Draws} {Mode}";
}

public class DrawCallBenchmarks : GraphiteBenchmark
{
    public static IEnumerable<DrawCase> Cases =>
    [
        new(1024, DrawMode.UniqueStatic),
        new(8192, DrawMode.UniqueStatic),
        new(32768, DrawMode.UniqueStatic),
        new(8192, DrawMode.Identical),
        new(8192, DrawMode.UniqueAnimated),
        new(8192, DrawMode.MeshRebind),
        new(8192, DrawMode.ShaderRebind),
    ];

    [ParamsSource(nameof(Cases))]
    public DrawCase Case { get; set; }

    private int Draws => Case.Draws;
    private DrawMode Mode => Case.Mode;

    protected override IEnumerable<IPass> CreatePasses()
    {
        GraphicsProgram program = LoadProgram("BenchShader.glsl");
        PropertySet[] models = Mode is DrawMode.Identical or DrawMode.MeshRebind or DrawMode.ShaderRebind
            ? [CreateModelProperties(Float4.One), CreateModelProperties(Float4.One)]
            : CreateModels(Draws);
        PropertyID matrixModel = "MatrixModel";
        RenderResourceID color = "Color";
        int draws = Draws;
        DrawMode mode = Mode;
        float spin = 0f;

        yield return new BenchPass("Draws", b => b.DeclareOutputTexture(color, ColorDesc, ops: ClearOps), (context, cmd) =>
        {
            spin += 0.01f;
            cmd.SetFullViewport();
            cmd.SetShader(program);
            cmd.SetProperties(ViewProperties);
            cmd.SetVertexSource(Cube.Source);

            for (int i = 0; i < draws; i++)
            {
                PropertySet model = models[i % models.Length];
                switch (mode)
                {
                    case DrawMode.UniqueAnimated:
                        model.SetMatrix(matrixModel, Float4x4.CreateTranslation(new Float3(spin + i, 0, 0)));
                        break;
                    case DrawMode.MeshRebind:
                        cmd.SetVertexSource(Cube.Source);
                        break;
                    case DrawMode.ShaderRebind:
                        cmd.SetShader(program);
                        break;
                }

                cmd.SetProperties(model);
                cmd.DrawIndexed();
            }
        });
    }
}
