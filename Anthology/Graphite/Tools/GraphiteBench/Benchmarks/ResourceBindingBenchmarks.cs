using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite;

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

    protected override IEnumerable<BenchPass> CreatePasses()
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
        sb.AppendLine("Shared {");
        sb.AppendLine("layout(set = 0) uniform View { mat4 MatrixViewProjection; vec4 ViewTint; };");
        sb.AppendLine("layout(set = 1) uniform Model { mat4 MatrixModel; };");
        for (int i = 0; i < textures; i++)
            sb.AppendLine($"layout(set = 1) uniform sampler2D T{i};");
        sb.AppendLine("}");
        sb.AppendLine("Vertex {");
        sb.AppendLine("in vec3 POSITION0;");
        sb.AppendLine("in vec2 UV0;");
        sb.AppendLine("out vec2 uv;");
        sb.AppendLine("void main() {");
        sb.AppendLine("    gl_Position = MatrixViewProjection * (MatrixModel * vec4(POSITION0, 1.0));");
        sb.AppendLine("    uv = UV0;");
        sb.AppendLine("}");
        sb.AppendLine("}");
        sb.AppendLine("Fragment {");
        sb.AppendLine("in vec2 uv;");
        sb.AppendLine("out vec4 target;");
        sb.AppendLine("void main() {");
        sb.AppendLine("    vec4 sum = vec4(0.0);");
        for (int i = 0; i < textures; i++)
            sb.AppendLine($"    sum += texture(T{i}, uv);");
        sb.AppendLine("    target = sum * ViewTint;");
        sb.AppendLine("}");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
