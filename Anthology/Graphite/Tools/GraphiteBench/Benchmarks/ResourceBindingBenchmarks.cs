using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;

public enum BindingPattern
{
    Shared,
    UniquePerDraw,
    SwappedPerFrame,
}

public readonly record struct BindingCase(int Textures, int Draws, BindingPattern Pattern) : IComparable
{
    public int CompareTo(object? obj) => obj is BindingCase other ? (Textures, Draws, Pattern).CompareTo((other.Textures, other.Draws, other.Pattern)) : 0;

    public override string ToString() => $"{Textures}t {Draws} {Pattern}";
}

public class ResourceBindingBenchmarks : GraphiteBenchmark
{
    private const int TexturePool = 64;

    public static IEnumerable<BindingCase> Cases =>
    [
        new(1, 8192, BindingPattern.UniquePerDraw),
        new(8, 8192, BindingPattern.UniquePerDraw),
        new(16, 8192, BindingPattern.UniquePerDraw),
        new(8, 8192, BindingPattern.Shared),
        new(8, 8192, BindingPattern.SwappedPerFrame),
    ];

    [ParamsSource(nameof(Cases))]
    public BindingCase Case { get; set; }

    private int Textures => Case.Textures;
    private int Draws => Case.Draws;
    private BindingPattern Pattern => Case.Pattern;

    protected override IEnumerable<IPass> CreatePasses()
    {
        GraphicsProgram program = Own(BenchShaderLoader.CreateFromSource(Device, GenerateShader(Textures)));
        Texture[] pool = Enumerable.Range(1, TexturePool).Select(seed => CreateTexture(4, seed)).ToArray();
        PropertyID[] slots = Enumerable.Range(0, Textures).Select(i => (PropertyID)("T" + i)).ToArray();

        int setCount = Pattern == BindingPattern.Shared ? 1 : Draws;
        PropertySet[] models = new PropertySet[setCount];
        for (int d = 0; d < setCount; d++)
        {
            models[d] = new PropertySet();
            models[d].SetMatrix("MatrixModel", Float4x4.CreateTranslation(new Float3(d, 0, 0)));
            for (int t = 0; t < slots.Length; t++)
                models[d].SetTexture(slots[t], pool[(d + t) % pool.Length], PointSampler);
        }

        RenderResourceID color = "Color";
        BindingPattern pattern = Pattern;
        int draws = Draws;
        int frame = 0;

        yield return new BenchPass("Bindings", b => b.DeclareOutputTexture(color, ColorDesc, ops: ClearOps), (context, cmd) =>
        {
            frame++;
            cmd.SetFullViewport();
            cmd.SetShader(program);
            cmd.SetProperties(ViewProperties);
            cmd.SetVertexSource(Cube.Source);

            for (int d = 0; d < draws; d++)
            {
                PropertySet model = models[d % models.Length];
                if (pattern == BindingPattern.SwappedPerFrame)
                    model.SetTexture(slots[0], pool[(d + frame) % pool.Length], PointSampler);

                cmd.SetProperties(model);
                cmd.DrawIndexed();
            }
        });
    }

    private static string GenerateShader(int textures)
    {
        StringBuilder sb = new();
        sb.AppendLine($"module Textures{textures};");
        sb.AppendLine("struct VertexInput { float3 position : POSITION; float2 uv : UV0; }");
        sb.AppendLine("struct VertexOutput { float4 clipPosition : SV_Position; float2 uv : UV; }");
        sb.AppendLine("struct ViewData { float4x4 MatrixViewProjection; float4 ViewTint; };");
        sb.AppendLine("ParameterBlock<ViewData> View;");
        sb.AppendLine("struct ModelData {");
        sb.AppendLine("    float4x4 MatrixModel;");
        for (int i = 0; i < textures; i++)
            sb.AppendLine($"    Sampler2D<float4> T{i};");
        sb.AppendLine("};");
        sb.AppendLine("ParameterBlock<ModelData> Model;");
        sb.AppendLine("[shader(\"vertex\")]");
        sb.AppendLine("VertexOutput vertex(VertexInput input) {");
        sb.AppendLine("    VertexOutput output;");
        sb.AppendLine("    output.clipPosition = mul(View.MatrixViewProjection, mul(Model.MatrixModel, float4(input.position, 1.0)));");
        sb.AppendLine("    output.uv = input.uv;");
        sb.AppendLine("    return output;");
        sb.AppendLine("}");
        sb.AppendLine("[shader(\"fragment\")]");
        sb.AppendLine("float4 fragment(VertexOutput input) : SV_Target {");
        sb.AppendLine("    float4 sum = 0.0;");
        for (int i = 0; i < textures; i++)
            sb.AppendLine($"    sum += Model.T{i}.Sample(input.uv);");
        sb.AppendLine("    return sum * View.ViewTint;");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
