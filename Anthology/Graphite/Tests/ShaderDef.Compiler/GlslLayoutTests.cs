using System.Linq;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Compiler.Tests;


public class GlslLayoutTests
{
    static string FragmentOnly(string declarations, string body) => $$"""
        Vertex { void main() { gl_Position = vec4(0); } }
        Fragment
        {
            out vec4 color;
            {{declarations}}
            void main() { color = vec4(0); {{body}} }
        }
        """;


    [Fact]
    public void LooseUniforms_PackStd140()
    {
        ShaderDescription d = Glsl.Describe(FragmentOnly(
            "uniform float _A; uniform vec3 _B; uniform float _C; uniform vec2 _D;",
            "color.x = _A + _B.x + _C + _D.x;"));

        Assert.Equal(new UniformBlockField("_A", 0, 4, UniformScalarType.Float1), Glsl.Field(d, "_A"));
        Assert.Equal(new UniformBlockField("_B", 16, 12, UniformScalarType.Float3), Glsl.Field(d, "_B"));
        Assert.Equal(new UniformBlockField("_C", 28, 4, UniformScalarType.Float1), Glsl.Field(d, "_C"));
        Assert.Equal(new UniformBlockField("_D", 32, 8, UniformScalarType.Float2), Glsl.Field(d, "_D"));
    }


    [Fact]
    public void LooseUniforms_OfEveryStage_ShareOneLayout()
    {
        const string source = """
            Vertex
            {
                uniform vec4 _V;
                void main() { gl_Position = _V; }
            }
            Fragment
            {
                uniform vec4 _F;
                uniform vec4 _V;
                out vec4 color;
                void main() { color = _F + _V; }
            }
            """;

        string[] stages = Glsl.Rewritten(source);
        const string block = "uniform _GraphiteGlobal { vec4 _V; vec4 _F; };";
        Assert.All(stages, s => Assert.Contains(block, s));

        ShaderDescription d = Glsl.Describe(source);
        Assert.Equal(ShaderStages.Vertex | ShaderStages.Fragment, Glsl.Element(d, "$Global").Stages);
        Assert.Equal(0u, Glsl.Field(d, "_V").Offset);
        Assert.Equal(16u, Glsl.Field(d, "_F").Offset);
    }


    [Fact]
    public void LooseUniform_WithDifferentTypesAcrossStages_Fails()
    {
        string message = Glsl.Fails("""
            Vertex { uniform vec4 _X; void main() { gl_Position = _X; } }
            Fragment { uniform float _X; out vec4 c; void main() { c = vec4(_X); } }
            """).Message;

        Assert.Contains("'_X'", message);
    }


    [Fact]
    public void LooseUniform_WithAnInitializer_Fails()
    {
        Assert.Contains("initializers", Glsl.Fails(FragmentOnly("uniform float _A = 1.0;", "color.x = _A;")).Message);
    }


    [Fact]
    public void StructUniforms_FlattenWithDottedNames()
    {
        ShaderDescription d = Glsl.Describe("""
            Shared
            {
                struct Light { vec3 color; float range; };
            }
            Vertex { void main() { gl_Position = vec4(0); } }
            Fragment
            {
                uniform Light _Light;
                uniform vec4 _After;
                out vec4 color;
                void main() { color = vec4(_Light.color * _Light.range, 1) + _After; }
            }
            """);

        Assert.Equal(new UniformBlockField("_Light.color", 0, 12, UniformScalarType.Float3), Glsl.Field(d, "_Light.color"));
        Assert.Equal(new UniformBlockField("_Light.range", 12, 4, UniformScalarType.Float1), Glsl.Field(d, "_Light.range"));
        Assert.Equal(16u, Glsl.Field(d, "_After").Offset);
    }


    [Fact]
    public void StructUniform_DeclaredAfterTheFirstUniform_Fails()
    {
        string message = Glsl.Fails(FragmentOnly(
            "uniform vec4 _First; struct Late { vec4 v; }; uniform Late _Late;",
            "color = _First + _Late.v;")).Message;

        Assert.Contains("Shared", message);
    }


    [Fact]
    public void ArrayUniforms_CarryLengthAndStride()
    {
        ShaderDescription d = Glsl.Describe(FragmentOnly(
            "uniform vec4 _Colors[3]; uniform float _Weights[4]; uniform float _Tail;",
            "for (int i = 0; i < 3; i++) color += _Colors[i] * _Weights[i]; color.x += _Tail;"));

        Assert.Equal(new UniformBlockField("_Colors", 0, UniformScalarType.Float4, 3, 16), Glsl.Field(d, "_Colors"));
        Assert.Equal(new UniformBlockField("_Weights", 48, UniformScalarType.Float1, 4, 16), Glsl.Field(d, "_Weights"));
        Assert.Equal(112u, Glsl.Field(d, "_Tail").Offset);
    }


    [Fact]
    public void MatrixUniforms_IncludeColumnPadding()
    {
        ShaderDescription d = Glsl.Describe(FragmentOnly(
            "uniform mat4 _M; uniform mat3 _N; uniform float _F;",
            "color = _M * vec4(_N * vec3(_F), 1);"));

        Assert.Equal(new UniformBlockField("_M", 0, 64, UniformScalarType.Float4x4), Glsl.Field(d, "_M"));
        Assert.Equal(new UniformBlockField("_N", 64, 48, UniformScalarType.Float3x3), Glsl.Field(d, "_N"));
        Assert.Equal(112u, Glsl.Field(d, "_F").Offset);
    }


    [Fact]
    public void IntegerAndBoolUniforms_ReflectTheirTypes()
    {
        ShaderDescription d = Glsl.Describe(FragmentOnly(
            "uniform bool _B; uniform uint _U; uniform ivec3 _I; uniform uvec2 _U2;",
            "if (_B) color = vec4(_U + uint(_I.x) + _U2.x);"));

        Assert.Equal(new UniformBlockField("_B", 0, 4, UniformScalarType.UInt1), Glsl.Field(d, "_B"));
        Assert.Equal(new UniformBlockField("_U", 4, 4, UniformScalarType.UInt1), Glsl.Field(d, "_U"));
        Assert.Equal(new UniformBlockField("_I", 16, 12, UniformScalarType.Int3), Glsl.Field(d, "_I"));
        Assert.Equal(new UniformBlockField("_U2", 32, 8, UniformScalarType.UInt2), Glsl.Field(d, "_U2"));
    }


    [Fact]
    public void Bindings_AreAssignedByName_TheSameInEveryStage()
    {
        ShaderDescription d = Glsl.Describe("""
            Vertex
            {
                uniform sampler2D _A;
                void main() { gl_Position = textureLod(_A, vec2(0), 0); }
            }
            Fragment
            {
                uniform sampler2D _B;
                uniform sampler2D _A;
                in vec2 uv;
                out vec4 color;
                void main() { color = texture(_A, uv) + texture(_B, uv); }
            }
            """);

        ResourceLayoutElementDescription a = Glsl.Element(d, "_A");
        ResourceLayoutElementDescription b = Glsl.Element(d, "_B");
        Assert.Equal(0, a.BindingIndex);
        Assert.Equal(1, b.BindingIndex);
        Assert.Equal(ShaderStages.Vertex | ShaderStages.Fragment, a.Stages);
        Assert.Equal(ShaderStages.Fragment, b.Stages);
        Assert.Equal(ResourceKind.TextureReadOnly, a.Kind);
        Assert.Equal(ResourceLayoutElementOptions.CombinedImageSampler, a.Options);
    }


    [Fact]
    public void ExplicitBindings_AreKept_AndSkippedByAutomaticOnes()
    {
        ShaderDescription d = Glsl.Describe(FragmentOnly(
            "layout(binding = 0) uniform sampler2D _Fixed; uniform sampler2D _Auto; uniform vec4 _Tint;",
            "color = texture(_Fixed, vec2(0)) + texture(_Auto, vec2(0)) + _Tint;"));

        Assert.Equal(0, Glsl.Element(d, "_Fixed").BindingIndex);
        Assert.Equal(1, Glsl.Element(d, "_Auto").BindingIndex);
        Assert.Equal(2, Glsl.Element(d, "$Global").BindingIndex);
    }


    [Fact]
    public void ExplicitSets_AreKept()
    {
        ShaderDescription d = Glsl.Describe(FragmentOnly(
            "layout(set = 1) uniform sampler2D _Other; uniform sampler2D _Main;",
            "color = texture(_Other, vec2(0)) + texture(_Main, vec2(0));"));

        Assert.Equal(1u, Glsl.SetOf(d, "_Other"));
        Assert.Equal(0, Glsl.Element(d, "_Other").BindingIndex);
        Assert.Equal(0u, Glsl.SetOf(d, "_Main"));
        Assert.Equal(0, Glsl.Element(d, "_Main").BindingIndex);
    }


    [Fact]
    public void ExplicitBindings_ThatCollide_Fail()
    {
        Assert.Contains("binding 0", Glsl.Fails(FragmentOnly(
            "layout(binding = 0) uniform sampler2D _A; layout(binding = 0) uniform sampler2D _B;",
            "color = texture(_A, vec2(0)) + texture(_B, vec2(0));")).Message);
    }


    [Fact]
    public void SeveralVariablesInOneDeclaration_GetTheirOwnBindings()
    {
        ShaderDescription d = Glsl.Describe(FragmentOnly(
            "uniform sampler2D _A, _B;",
            "color = texture(_A, vec2(0)) + texture(_B, vec2(0));"));

        Assert.Equal(0, Glsl.Element(d, "_A").BindingIndex);
        Assert.Equal(1, Glsl.Element(d, "_B").BindingIndex);
    }


    [Fact]
    public void SeparateTexturesAndSamplers_ReflectAsTheirOwnKinds()
    {
        ShaderDescription d = Glsl.Describe(FragmentOnly(
            "uniform texture2D _Texture; uniform sampler _Sampler;",
            "color = texture(sampler2D(_Texture, _Sampler), vec2(0));"));

        Assert.Equal(ResourceKind.TextureReadOnly, Glsl.Element(d, "_Texture").Kind);
        Assert.Equal(ResourceLayoutElementOptions.None, Glsl.Element(d, "_Texture").Options);
        Assert.Equal(ResourceKind.Sampler, Glsl.Element(d, "_Sampler").Kind);
    }


    [Fact]
    public void StorageBuffers_ReflectReadOnlyOrReadWrite_ByBlockName()
    {
        ShaderDescription d = Glsl.Describe("""
            Compute
            {
                layout(local_size_x = 64) in;
                layout(std430) readonly buffer Lights { vec4 lights[]; };
                layout(std430) buffer Counters { uint counts[]; } counters;
                void main() { counters.counts[gl_GlobalInvocationID.x] = uint(lights[gl_GlobalInvocationID.x].x); }
            }
            """);

        Assert.Equal(ResourceKind.StructuredBufferReadOnly, Glsl.Element(d, "Lights").Kind);
        Assert.Equal(ResourceKind.StructuredBufferReadWrite, Glsl.Element(d, "Counters").Kind);
        Assert.NotEqual(Glsl.Element(d, "Lights").BindingIndex, Glsl.Element(d, "Counters").BindingIndex);
    }


    [Fact]
    public void UniformBlocks_ReflectTheirMembers()
    {
        ShaderDescription d = Glsl.Describe("""
            Vertex
            {
                uniform Camera { mat4 view; vec4 position; } camera;
                void main() { gl_Position = camera.view * camera.position; }
            }
            """);

        Assert.Equal(ResourceKind.UniformBuffer, Glsl.Element(d, "Camera").Kind);
        Assert.Equal(new UniformBlockField("view", 0, 64, UniformScalarType.Float4x4), Glsl.Field(d, "view", "Camera"));
        Assert.Equal(new UniformBlockField("position", 64, 16, UniformScalarType.Float4), Glsl.Field(d, "position", "Camera"));
    }


    [Fact]
    public void UnusedResources_AreLeftOut()
    {
        ShaderDescription d = Glsl.Describe(FragmentOnly("uniform sampler2D _Unused; uniform sampler2D _Used;", "color = texture(_Used, vec2(0));"));

        Assert.Equal(["_Used"], Glsl.Elements(d).Select(e => PropertyID.ToString(e.Name)));
    }


    [Fact]
    public void PushConstants_Fail()
    {
        Assert.Contains("push constants", Glsl.Fails(FragmentOnly("layout(push_constant) uniform P { vec4 x; } p;", "color = p.x;")).Message);
    }


    [Fact]
    public void Varyings_MatchByName_NotByOrder()
    {
        string[] stages = Glsl.Rewritten("""
            Vertex
            {
                out vec2 uv;
                out vec3 normal;
                void main() { uv = vec2(0); normal = vec3(0); gl_Position = vec4(0); }
            }
            Fragment
            {
                in vec3 normal;
                in vec2 uv;
                out vec4 color;
                void main() { color = vec4(normal, uv.x); }
            }
            """);

        Assert.Contains("layout(location = 0) out vec2 uv;", stages[0]);
        Assert.Contains("layout(location = 1) out vec3 normal;", stages[0]);
        Assert.Contains("layout(location = 1) in vec3 normal;", stages[1]);
        Assert.Contains("layout(location = 0) in vec2 uv;", stages[1]);
    }


    [Fact]
    public void Varyings_TakeAnExplicitLocationFromEitherSide()
    {
        string[] stages = Glsl.Rewritten("""
            Vertex
            {
                layout(location = 3) out vec2 uv;
                void main() { uv = vec2(0); gl_Position = vec4(0); }
            }
            Fragment
            {
                in vec2 uv;
                out vec4 color;
                void main() { color = vec4(uv, 0, 1); }
            }
            """);

        Assert.Contains("layout(location = 3) in vec2 uv;", stages[1]);
    }


    [Fact]
    public void Varyings_ReserveOneLocationPerMatrixColumn()
    {
        string[] stages = Glsl.Rewritten("""
            Vertex
            {
                out mat4 basis;
                out vec2 uv;
                void main() { basis = mat4(1); uv = vec2(0); gl_Position = vec4(0); }
            }
            Fragment
            {
                in mat4 basis;
                in vec2 uv;
                out vec4 color;
                void main() { color = basis[0] + vec4(uv, 0, 0); }
            }
            """);

        Assert.Contains("layout(location = 4) out vec2 uv;", stages[0]);
        Assert.Contains("layout(location = 4) in vec2 uv;", stages[1]);
    }


    [Fact]
    public void InterfaceBlocks_MatchByBlockName()
    {
        string[] stages = Glsl.Rewritten("""
            Vertex
            {
                out float fade;
                out Varyings { vec2 uv; vec3 normal; } o;
                void main() { fade = 1.0; o.uv = vec2(0); o.normal = vec3(0); gl_Position = vec4(0); }
            }
            Fragment
            {
                in Varyings { vec2 uv; vec3 normal; } i;
                in float fade;
                out vec4 color;
                void main() { color = vec4(i.normal, i.uv.x * fade); }
            }
            """);

        Assert.Contains("layout(location = 1) out Varyings", stages[0]);
        Assert.Contains("layout(location = 1) in Varyings", stages[1]);
        Assert.Contains("layout(location = 0) in float fade;", stages[1]);
    }


    [Fact]
    public void SeveralVaryingsInOneDeclaration_GetTheirOwnLocations()
    {
        string[] stages = Glsl.Rewritten("""
            Vertex
            {
                out vec2 a, b;
                void main() { a = vec2(0); b = vec2(1); gl_Position = vec4(0); }
            }
            Fragment
            {
                in vec2 a, b;
                out vec4 color;
                void main() { color = vec4(a, b); }
            }
            """);

        Assert.Contains("layout(location = 0) out vec2 a;", stages[0]);
        Assert.Contains("layout(location = 1) out vec2 b;", stages[0]);
        Assert.Contains("layout(location = 1) in vec2 b;", stages[1]);
    }


    [Fact]
    public void FragmentOutputs_GetLocationsInOrder()
    {
        string[] stages = Glsl.Rewritten("""
            Vertex { void main() { gl_Position = vec4(0); } }
            Fragment
            {
                out vec4 albedo;
                out vec4 normal;
                void main() { albedo = vec4(1); normal = vec4(0); }
            }
            """);

        Assert.Contains("layout(location = 0) out vec4 albedo;", stages[1]);
        Assert.Contains("layout(location = 1) out vec4 normal;", stages[1]);
    }


    [Fact]
    public void VertexInputs_ReflectByLocationWithTheirNames()
    {
        ShaderDescription d = Glsl.Describe("""
            Vertex
            {
                in vec3 vertexPosition;
                in vec2 vertexUV;
                in uvec4 vertexBones;
                out vec2 uv;
                void main() { uv = vertexUV + vec2(vertexBones.xy); gl_Position = vec4(vertexPosition, 1); }
            }
            """);

        Assert.Equal([0u, 1u, 2u], d.VertexLayouts.Select(l => l.Location));
        Assert.Equal(["vertexPosition", "vertexUV", "vertexBones"], d.VertexLayouts.Select(l => VertexAttributeID.ToString(l.Elements[0].Name)));
        Assert.Equal([VertexElementFormat.Float3, VertexElementFormat.Float2, VertexElementFormat.UInt4], d.VertexLayouts.Select(l => l.Elements[0].Format));
        Assert.Equal([12u, 8u, 16u], d.VertexLayouts.Select(l => l.Stride));
    }


    [Fact]
    public void OpenGlVertexBuiltins_MapToVulkan()
    {
        string[] stages = Glsl.Rewritten("Vertex { void main() { gl_Position = vec4(gl_VertexID, gl_InstanceID, 0, 1); } }");

        Assert.Contains("gl_VertexIndex", stages[0]);
        Assert.Contains("(gl_InstanceIndex - gl_BaseInstance)", stages[0]);
        Glsl.Compile("Vertex { void main() { gl_Position = vec4(gl_VertexID, gl_InstanceID, 0, 1); } }");
    }
}
