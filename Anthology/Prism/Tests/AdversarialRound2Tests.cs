using Prowl.Prism.Compiler;
using Prowl.Prism.SpirV;
using Xunit;

namespace Prowl.Prism.Tests;

// Valid GLSL that Prism rejects, found by the second adversarial review round.
public sealed class AdversarialRound2Tests
{
    static void CompilesAndValidates(string source, ShaderStage stage = ShaderStage.Compute,
                                     SpirVVersion target = SpirVVersion.V1_0)
    {
        byte[] bytes = GlslCompiler.CompileToBytes(source, stage, target, optimize: false);
        StructValidator.Valid(bytes);
    }

    const string Compute = "#version 460\nlayout(local_size_x=1) in;\n";

    const string Buffer = "layout(std430,set=1,binding=0) buffer U { int ii; float ff; vec3 v3; vec4 v4; mat2x3 m23; };\n";

    [Theory]
    [InlineData("textureSamples(tm)")]
    [InlineData("textureQueryLevels(t)")]
    public void SamplerlessQueriesOnTextureTypes(string expr) =>
        CompilesAndValidates(
            Compute +
            "#extension GL_EXT_samplerless_texture_functions : require\n" +
            "layout(set=0,binding=0) uniform texture2DArray t;\n" +
            "layout(set=0,binding=1) uniform texture2DMS tm;\n" +
            "layout(std430,set=0,binding=2) buffer B { int o; };\n" +
            "void main() { o = " + expr + "; }\n");

    [Theory]
    [InlineData("v3.length()")]
    [InlineData("m23.length()")]
    [InlineData("m23[0].length()")]
    [InlineData("v4.xyz.length()")]
    [InlineData("vec3(1).length()")]
    public void LengthMethodOnVectorsAndMatrices(string expr) =>
        CompilesAndValidates(Compute + Buffer + "void main() { ii = " + expr + "; }\n");

    [Theory]
    [InlineData("v3 = g(ff.xxx);")]
    [InlineData("ff.x = 2.0;")]
    [InlineData("ff.x += 1.0;")]
    public void ScalarSwizzle(string stmt) =>
        CompilesAndValidates(Compute + Buffer + "vec3 g(vec3 x) { return x; }\nvoid main() { " + stmt + " }\n");

    [Theory]
    [InlineData("int32_t r = ii;")]
    [InlineData("uint32_t r = uint(ii);")]
    [InlineData("float32_t r = ff;")]
    [InlineData("float64_t r = double(ff);")]
    [InlineData("i32vec2 r = ivec2(ii);")]
    [InlineData("u32vec3 r = uvec3(1u);")]
    [InlineData("f32vec4 r = v4;")]
    [InlineData("f64vec2 r = dvec2(ff);")]
    [InlineData("f32mat2 r = mat2(ff);")]
    [InlineData("f64mat2x3 r = dmat2x3(ff);")]
    public void ExplicitArithmeticSizedTypeAliases(string decl) =>
        CompilesAndValidates(
            "#version 460\n#extension GL_EXT_shader_explicit_arithmetic_types : require\nlayout(local_size_x=1) in;\n" + Buffer +
            "void main() { " + decl + " ii = int(r == r); }\n");

    [Theory]
    [InlineData("uint r = packFloat2x16(f16vec2(1.0hf));")]
    [InlineData("f16vec2 r = unpackFloat2x16(1u);")]
    [InlineData("int64_t r = doubleBitsToInt64(double(ff));")]
    [InlineData("uint64_t r = doubleBitsToUint64(double(ff));")]
    [InlineData("double r = int64BitsToDouble(int64_t(ii));")]
    [InlineData("double r = uint64BitsToDouble(uint64_t(ii));")]
    [InlineData("int r = packInt2x16(i16vec2(1));")]
    [InlineData("uint r = packUint2x16(u16vec2(1));")]
    [InlineData("i16vec2 r = unpackInt2x16(ii);")]
    [InlineData("int64_t r = packInt4x16(i16vec4(1));")]
    [InlineData("uint64_t r = packUint4x16(u16vec4(1));")]
    [InlineData("u16vec4 r = unpackUint4x16(uint64_t(1));")]
    [InlineData("int16_t r = float16BitsToInt16(float16_t(1.0));")]
    [InlineData("u16vec2 r = float16BitsToUint16(f16vec2(1.0));")]
    [InlineData("float16_t r = int16BitsToFloat16(int16_t(1));")]
    [InlineData("f16vec2 r = uint16BitsToFloat16(u16vec2(1));")]
    [InlineData("int r = pack32(i8vec4(1));")]
    [InlineData("u8vec4 r = unpack8(1u);")]
    [InlineData("uint16_t r = pack16(u8vec2(1));")]
    [InlineData("uint64_t r = pack64(u16vec4(1));")]
    public void ExplicitArithmeticPackAndBitcastBuiltins(string decl) =>
        CompilesAndValidates(
            "#version 460\n#extension GL_EXT_shader_explicit_arithmetic_types : require\nlayout(local_size_x=1) in;\n" + Buffer +
            "void main() { " + decl + " ii = int(r == r); }\n");

    const string CoopMat =
        "#version 460\n" +
        "#extension GL_KHR_cooperative_matrix : require\n" +
        "#extension GL_KHR_memory_scope_semantics : require\n" +
        "layout(local_size_x=32) in;\n" +
        "layout(std430,set=0,binding=0) buffer D { float data[]; };\n";

    const string Acc = "coopmat<float, gl_ScopeSubgroup, 16, 16, gl_MatrixUseAccumulator>";

    const string LoadCD =
        "coopMatLoad(c, data, 0, 16, gl_CooperativeMatrixLayoutRowMajor); " +
        "coopMatLoad(d, data, 256, 16, gl_CooperativeMatrixLayoutRowMajor); ";

    const string StoreC = "coopMatStore(c, data, 0, 16, gl_CooperativeMatrixLayoutRowMajor);";

    [Fact]
    public void CoopMatFunctionParameter() =>
        CompilesAndValidates(CoopMat +
            "float first(" + Acc + " m) { return m[0]; }\n" +
            "void main() { " + Acc + " c; " + Acc + " d; " + LoadCD + "data[0] = first(c); }\n", ShaderStage.Compute, SpirVVersion.V1_4);

    [Fact]
    public void CoopMatFunctionReturnType() =>
        CompilesAndValidates(CoopMat +
            Acc + " load() { " + Acc + " c; coopMatLoad(c, data, 0, 16, gl_CooperativeMatrixLayoutRowMajor); return c; }\n" +
            "void main() { " + Acc + " c = load(); " + StoreC + " }\n", ShaderStage.Compute, SpirVVersion.V1_4);

    [Theory]
    [InlineData("c = " + Acc + "(0.0);")]
    [InlineData("c = c + d;")]
    [InlineData("c = c - d;")]
    [InlineData("c += d;")]
    [InlineData("c = c * 2.0;")]
    [InlineData("data[1] = float(c.length());")]
    public void CoopMatExpressions(string stmt) =>
        CompilesAndValidates(CoopMat +
            "void main() { " + Acc + " c; " + Acc + " d; " + LoadCD + stmt + " " + StoreC + " }\n", ShaderStage.Compute, SpirVVersion.V1_4);

    [Theory]
    [InlineData("Vec e = pc.base + 2;")]
    [InlineData("Vec e = pc.base - 1u;")]
    [InlineData("Vec e = pc.base[2];")]
    public void BufferReferenceArithmeticAndIndexing(string decl) =>
        CompilesAndValidates(
            "#version 460\n" +
            "#extension GL_EXT_buffer_reference : require\n" +
            "#extension GL_EXT_buffer_reference2 : require\n" +
            "layout(local_size_x=1) in;\n" +
            "layout(buffer_reference, std430, buffer_reference_align=16) buffer Vec { vec4 v; };\n" +
            "layout(push_constant) uniform PC { Vec base; } pc;\n" +
            "void main() { " + decl + " e.v += vec4(1); }\n");

    [Theory]
    [InlineData("Vec e = pc.base + pc.i;")]
    [InlineData("Vec e = pc.base - pc.u;")]
    [InlineData("Vec e = pc.base + pc.l;")]
    [InlineData("Vec e = pc.base + pc.ul;")]
    [InlineData("Vec e = pc.base + (-1);")]
    [InlineData("Vec e = 3 + pc.base;")]
    [InlineData("Vec e = pc.base[pc.i];")]
    public void BufferReferenceRuntimeAndConstantCounts(string decl) =>
        CompilesAndValidates(
            "#version 460\n" +
            "#extension GL_EXT_buffer_reference : require\n" +
            "#extension GL_EXT_buffer_reference2 : require\n" +
            "#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require\n" +
            "layout(local_size_x=1) in;\n" +
            "layout(buffer_reference, std430, buffer_reference_align=16) buffer Vec { vec4 v; };\n" +
            "layout(push_constant) uniform PC { Vec base; int i; uint u; int64_t l; uint64_t ul; } pc;\n" +
            "void main() { " + decl + " e.v += vec4(1); }\n");

    [Theory]
    [InlineData("c = " + Acc + "(data[1]);")]
    [InlineData("c = 2.0 * c;")]
    [InlineData("c = c / d;")]
    [InlineData("int n = c.length(); data[3] = float(n);")]
    public void CoopMatRuntimeExpressions(string stmt) =>
        CompilesAndValidates(CoopMat +
            "void main() { " + Acc + " c; " + Acc + " d; " + LoadCD + stmt + " " + StoreC + " }\n", ShaderStage.Compute, SpirVVersion.V1_4);

    [Fact]
    public void MeshPerPrimitiveBlockRedeclaration() =>
        CompilesAndValidates(
            "#version 460\n" +
            "#extension GL_EXT_mesh_shader : require\n" +
            "layout(local_size_x=32) in;\n" +
            "layout(triangles, max_vertices=64, max_primitives=32) out;\n" +
            "out gl_MeshPerPrimitiveEXT { perprimitiveEXT int gl_PrimitiveID; perprimitiveEXT int gl_Layer; perprimitiveEXT bool gl_CullPrimitiveEXT; } gl_MeshPrimitivesEXT[];\n" +
            "void main() {\n" +
            "  SetMeshOutputsEXT(3u, 1u);\n" +
            "  gl_MeshPrimitivesEXT[0].gl_PrimitiveID = 3;\n" +
            "  gl_MeshPrimitivesEXT[0].gl_CullPrimitiveEXT = gl_MeshPrimitivesEXT[0].gl_PrimitiveID > 2;\n" +
            "  gl_PrimitiveTriangleIndicesEXT[0] = uvec3(0, 1, 2);\n" +
            "}\n",
            ShaderStage.Mesh, SpirVVersion.V1_5);

    [Theory]
    [InlineData("const vec4 C = vec4(1, 2, 3, 4);\n", "ff = C[ii];")]
    [InlineData("const ivec3 C = ivec3(1, 2, 3);\n", "ii = C[ii];")]
    [InlineData("const mat2 C = mat2(1, 2, 3, 4);\n", "ff = C[ii].y;")]
    [InlineData("const mat2 C = mat2(1, 2, 3, 4);\n", "ff = C[1][ii];")]
    [InlineData("", "const vec4 C = vec4(1, 2, 3, 4); ff = C[ii];")]
    public void ConstVectorAndMatrixDynamicIndex(string global, string stmt) =>
        CompilesAndValidates(Compute + Buffer + global + "void main() { " + stmt + " }\n");

    [Theory]
    [InlineData("const mat2 M = -mat2(1);\n", "ff = M[0][0];")]
    [InlineData("const bool B = mat2(1) == mat2(1);\n", "ii = B ? 1 : 0;")]
    [InlineData("const bool B = float[2](1, 2) != float[2](1, 3);\n", "ii = B ? 1 : 0;")]
    [InlineData("struct S { int a; float b; };\nconst bool B = S(1, 2.0) == S(1, 2.0);\n", "ii = B ? 1 : 0;")]
    public void ConstFoldMatrixNegationAndAggregateEquality(string global, string stmt) =>
        CompilesAndValidates(Compute + Buffer + global + "void main() { " + stmt + " }\n");
}
