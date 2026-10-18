using Prowl.Prism.Compiler;
using Prowl.Prism.SpirV;
using Xunit;

namespace Prowl.Prism.Tests;

// Valid GLSL that Prism rejects or mistypes because its expression type inference disagrees with lowering.
public sealed class InferenceGapTests
{
    static void CompilesAndValidates(string source, ShaderStage stage = ShaderStage.Compute,
                                     SpirVVersion target = SpirVVersion.V1_0)
    {
        byte[] bytes = GlslCompiler.CompileToBytes(source, stage, target, optimize: false);
        StructValidator.Valid(bytes);
    }

    const string Compute = "#version 450\nlayout(local_size_x=1) in;\n";

    [Fact]
    public void TextureQueryLodMemberAccess() =>
        CompilesAndValidates(
            "#version 450\n" +
            "layout(set=0,binding=0) uniform sampler2D s;\n" +
            "layout(location=0) in vec2 uv;\n" +
            "layout(location=0) out vec4 o;\n" +
            "void main() { float lod = textureQueryLod(s, uv).y + 1.0; o = vec4(lod); }\n",
            ShaderStage.Fragment);

    [Theory]
    [InlineData("imageAtomicAdd(img, ivec2(0), 1u)")]
    [InlineData("imageAtomicMax(img, ivec2(0), 1u)")]
    [InlineData("imageAtomicExchange(img, ivec2(0), 1u)")]
    [InlineData("imageAtomicCompSwap(img, ivec2(0), 1u, 2u)")]
    public void ImageAtomicResultPicksOverload(string expr) =>
        CompilesAndValidates(
            Compute +
            "layout(set=0,binding=0,r32ui) uniform uimage2D img;\n" +
            "layout(std430,set=0,binding=1) buffer B { uint o; };\n" +
            "vec2 f(float x) { return vec2(x); }\n" +
            "uint f(uint x) { return x + 1u; }\n" +
            "void main() { o = f(" + expr + "); }\n");

    [Fact]
    public void ImageAtomicResultScalarSwizzle() =>
        CompilesAndValidates(
            Compute +
            "layout(set=0,binding=0,r32ui) uniform uimage2D img;\n" +
            "layout(std430,set=0,binding=1) buffer B { uint o; };\n" +
            "void main() { o = imageAtomicAdd(img, ivec2(0), 1u).x + 1u; }\n");

    [Theory]
    [InlineData("sparseTexelFetchARB(s, ivec2(0), 0, t)")]
    [InlineData("sparseTextureLodARB(s, vec2(0.5), 0.0, t)")]
    [InlineData("sparseImageLoadARB(img, ivec2(0), t)")]
    public void SparseResidencyCodePicksOverload(string expr) =>
        CompilesAndValidates(
            "#version 450\n" +
            "#extension GL_ARB_sparse_texture2 : require\n" +
            "layout(local_size_x=1) in;\n" +
            "layout(set=0,binding=0) uniform sampler2D s;\n" +
            "layout(set=0,binding=1,rgba8) uniform image2D img;\n" +
            "layout(std430,set=0,binding=2) buffer B { int o; };\n" +
            "vec2 f(float x) { return vec2(x); }\n" +
            "int f(int x) { return x + 1; }\n" +
            "void main() { vec4 t; o = f(" + expr + "); }\n");

    [Fact]
    public void TextureClampMemberAccess() =>
        CompilesAndValidates(
            "#version 450\n" +
            "#extension GL_ARB_sparse_texture_clamp : require\n" +
            "layout(set=0,binding=0) uniform sampler2D s;\n" +
            "layout(location=0) in vec2 uv;\n" +
            "layout(location=0) out vec4 o;\n" +
            "void main() { o = vec4(textureClampARB(s, uv, 1.0).x - 1.0); }\n",
            ShaderStage.Fragment);

    [Fact]
    public void TextureGradClampMemberAccess() =>
        CompilesAndValidates(
            "#version 450\n" +
            "#extension GL_ARB_sparse_texture_clamp : require\n" +
            "layout(local_size_x=1) in;\n" +
            "layout(set=0,binding=0) uniform sampler2D s;\n" +
            "layout(std430,set=0,binding=1) buffer B { float o; };\n" +
            "void main() { o = textureGradClampARB(s, vec2(0.5), vec2(0.0), vec2(0.0), 1.0).x - 1.0; }\n");

    [Fact]
    public void BallotArbResultPicksOverload() =>
        CompilesAndValidates(
            "#version 450\n" +
            "#extension GL_ARB_shader_ballot : require\n" +
            "#extension GL_ARB_gpu_shader_int64 : require\n" +
            "layout(local_size_x=1) in;\n" +
            "layout(std430,set=0,binding=0) buffer B { uint64_t o; };\n" +
            "bool f(bool x) { return x; }\n" +
            "uint64_t f(uint64_t x) { return x; }\n" +
            "void main() { o = f(ballotARB(true)); }\n");

    [Fact]
    public void AllInvocationsEqualArbAsTernaryCondition() =>
        CompilesAndValidates(
            "#version 450\n" +
            "#extension GL_ARB_shader_group_vote : require\n" +
            "layout(local_size_x=1) in;\n" +
            "layout(std430,set=0,binding=0) buffer B { float v; uint o; };\n" +
            "void main() { o = allInvocationsEqualARB(v) ? 1u : 0u; }\n");

    [Fact]
    public void ReportIntersectionAsTernaryCondition() =>
        CompilesAndValidates(
            "#version 460\n" +
            "#extension GL_EXT_ray_tracing : require\n" +
            "hitAttributeEXT vec2 attr;\n" +
            "void main() { attr = reportIntersectionEXT(1.0, 0u) ? vec2(1.0) : vec2(0.0); }\n",
            ShaderStage.Intersection, SpirVVersion.V1_4);

    [Fact]
    public void TransposeNonSquareColumnPicksOverload() =>
        CompilesAndValidates(
            Compute +
            "layout(std430,set=0,binding=0) buffer B { float o; };\n" +
            "vec4 f(vec3 v) { return vec4(v, 1.0); }\n" +
            "float f(vec2 v) { return v.y; }\n" +
            "void main() { mat2x3 m = mat2x3(1.0); o = f(transpose(m)[0]); }\n");

    [Fact]
    public void OuterProductColumnPicksOverload() =>
        CompilesAndValidates(
            Compute +
            "layout(std430,set=0,binding=0) buffer B { float o; };\n" +
            "float f(float v) { return v; }\n" +
            "float f(vec2 v) { return v.y; }\n" +
            "void main() { o = f(outerProduct(vec2(1.0), vec3(2.0))[0]); }\n");

    [Theory]
    [InlineData("vec2", "c ? 1 : 2.5")]
    [InlineData("uvec2", "c ? 1 : 2u")]
    [InlineData("vec3", "c ? ivec2(1) : vec2(2.5)")]
    public void TernaryImplicitConversionPicksOverload(string resultType, string expr) =>
        CompilesAndValidates(
            Compute +
            "layout(std430,set=0,binding=0) buffer B { bool c; float o; };\n" +
            "int f(int x) { return x; }\n" +
            "int f(ivec2 x) { return x.x; }\n" +
            "vec2 f(float x) { return vec2(x); }\n" +
            "uvec2 f(uint x) { return uvec2(x); }\n" +
            "vec3 f(vec2 x) { return vec3(x, 1.0); }\n" +
            "void main() { " + resultType + " r = f(" + expr + "); o = float(r.x); }\n");

    const string CoopMat =
        "#version 460\n" +
        "#extension GL_KHR_memory_scope_semantics : require\n" +
        "#extension GL_KHR_cooperative_matrix : require\n" +
        "#extension GL_EXT_shader_explicit_arithmetic_types_float16 : require\n" +
        "layout(local_size_x = 64) in;\n" +
        "layout(std430, set = 0, binding = 0) buffer Data { float16_t values[]; };\n";

    [Fact]
    public void CoopMatElementPicksOverload() =>
        CompilesAndValidates(
            CoopMat +
            "float16_t f(float16_t x) { return x; }\n" +
            "float16_t f(int x) { return float16_t(1.0); }\n" +
            "void main() {\n" +
            "  coopmat<float16_t, gl_ScopeSubgroup, 16, 16, gl_MatrixUseA> m;\n" +
            "  coopMatLoad(m, values, 0, 16, gl_CooperativeMatrixLayoutRowMajor);\n" +
            "  values[0] = f(m[0]);\n" +
            "}\n", ShaderStage.Compute, SpirVVersion.V1_4);

    [Fact]
    public void TextureFootprintAsTernaryCondition() =>
        CompilesAndValidates(
            "#version 460 core\n" +
            "#extension GL_NV_shader_texture_footprint : require\n" +
            "layout(set=0,binding=0) uniform sampler2D tex;\n" +
            "layout(location=0) in vec2 uv;\n" +
            "layout(location=0) out vec4 o;\n" +
            "void main() { gl_TextureFootprint2DNV fp; o = textureFootprintNV(tex, uv, 2, false, fp) ? vec4(1.0) : vec4(0.0); }\n",
            ShaderStage.Fragment);

    [Theory]
    [InlineData("vec2 p = f(gl_MeshVerticesEXT[0].gl_Position.xy);")]
    [InlineData("uint p = gl_PrimitiveTriangleIndicesEXT[0].x + 1u;")]
    public void MeshOutputArrayRead(string stmt) =>
        CompilesAndValidates(
            "#version 460\n" +
            "#extension GL_EXT_mesh_shader : require\n" +
            "layout(local_size_x=1) in;\n" +
            "layout(triangles, max_vertices=3, max_primitives=1) out;\n" +
            "float f(float x) { return x; }\n" +
            "vec2 f(vec2 x) { return x; }\n" +
            "void main() {\n" +
            "  SetMeshOutputsEXT(3u, 1u);\n" +
            "  gl_MeshVerticesEXT[0].gl_Position = vec4(0.0);\n" +
            "  gl_PrimitiveTriangleIndicesEXT[0] = uvec3(0u, 1u, 2u);\n" +
            "  " + stmt + "\n" +
            "}\n", ShaderStage.Mesh, SpirVVersion.V1_4);
}
