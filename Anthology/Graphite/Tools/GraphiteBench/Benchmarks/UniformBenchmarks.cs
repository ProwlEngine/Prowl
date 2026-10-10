using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;

public enum UniformUpdate
{
    Static,
    OneFieldPerDraw,
    AllFieldsPerDraw,
}

public readonly record struct UniformCase(int Fields, int Draws, UniformUpdate Update) : IComparable
{
    public int CompareTo(object? obj) => obj is UniformCase other ? (Fields, Draws, Update).CompareTo((other.Fields, other.Draws, other.Update)) : 0;

    public override string ToString() => $"{Fields}f {Draws} {Update}";
}

public class UniformBenchmarks : GraphiteBenchmark
{
    public static IEnumerable<UniformCase> Cases =>
    [
        new(4, 8192, UniformUpdate.Static),
        new(64, 8192, UniformUpdate.Static),
        new(256, 8192, UniformUpdate.Static),
        new(64, 8192, UniformUpdate.OneFieldPerDraw),
        new(64, 8192, UniformUpdate.AllFieldsPerDraw),
        new(256, 8192, UniformUpdate.AllFieldsPerDraw),
    ];

    [ParamsSource(nameof(Cases))]
    public UniformCase Case { get; set; }

    private int Fields => Case.Fields;
    private int Draws => Case.Draws;
    private UniformUpdate Update => Case.Update;

    protected override IEnumerable<IPass> CreatePasses()
    {
        GraphicsProgram program = Own(BenchShaderLoader.CreateFromSource(Device, GenerateShader(Fields)));
        PropertyID[] fields = Enumerable.Range(0, Fields).Select(i => (PropertyID)("F" + i)).ToArray();
        PropertySet[] models = new PropertySet[Draws];
        for (int d = 0; d < models.Length; d++)
        {
            models[d] = new PropertySet(fields.Length + 1);
            models[d].SetMatrix("MatrixModel", Float4x4.CreateTranslation(new Float3(d, 0, 0)));
            foreach (PropertyID field in fields)
                models[d].SetFloat4(field, new Float4(d, 0, 0, 1));
        }

        RenderResourceID color = "Color";
        UniformUpdate update = Update;
        float time = 0f;

        yield return new BenchPass("Uniforms", b => b.DeclareOutputTexture(color, ColorDesc, ops: ClearOps), (context, cmd) =>
        {
            time += 0.01f;
            cmd.SetFullViewport();
            cmd.SetShader(program);
            cmd.SetProperties(ViewProperties);
            cmd.SetVertexSource(Cube.Source);

            for (int d = 0; d < models.Length; d++)
            {
                PropertySet model = models[d];
                if (update == UniformUpdate.OneFieldPerDraw)
                {
                    model.SetFloat4(fields[d % fields.Length], new Float4(time, d, 0, 1));
                }
                else if (update == UniformUpdate.AllFieldsPerDraw)
                {
                    for (int f = 0; f < fields.Length; f++)
                        model.SetFloat4(fields[f], new Float4(time, d, f, 1));
                }

                cmd.SetProperties(model);
                cmd.DrawIndexed();
            }
        });
    }

    private static string GenerateShader(int fields)
    {
        StringBuilder sb = new();
        sb.AppendLine($"module Uniforms{fields};");
        sb.AppendLine("struct VertexInput { float3 position : POSITION; float2 uv : UV0; }");
        sb.AppendLine("struct VertexOutput { float4 clipPosition : SV_Position; float4 color : COLOR; }");
        sb.AppendLine("struct ViewData { float4x4 MatrixViewProjection; float4 ViewTint; };");
        sb.AppendLine("ParameterBlock<ViewData> View;");
        sb.AppendLine("struct ModelData {");
        sb.AppendLine("    float4x4 MatrixModel;");
        for (int i = 0; i < fields; i++)
            sb.AppendLine($"    float4 F{i};");
        sb.AppendLine("};");
        sb.AppendLine("ParameterBlock<ModelData> Model;");
        sb.AppendLine("[shader(\"vertex\")]");
        sb.AppendLine("VertexOutput vertex(VertexInput input) {");
        sb.AppendLine("    float4 sum = 0.0;");
        for (int i = 0; i < fields; i++)
            sb.AppendLine($"    sum += Model.F{i};");
        sb.AppendLine("    VertexOutput output;");
        sb.AppendLine("    output.clipPosition = mul(View.MatrixViewProjection, mul(Model.MatrixModel, float4(input.position, 1.0)));");
        sb.AppendLine("    output.color = sum * View.ViewTint;");
        sb.AppendLine("    return output;");
        sb.AppendLine("}");
        sb.AppendLine("[shader(\"fragment\")]");
        sb.AppendLine("float4 fragment(VertexOutput input) : SV_Target { return input.color; }");
        return sb.ToString();
    }
}
