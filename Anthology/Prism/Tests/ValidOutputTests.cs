using Prowl.Prism.Compiler;
using Prowl.Prism.SpirV;
using Xunit;

namespace Prowl.Prism.Tests;

// Guards for constructs that Prism compiles to structurally sound SPIR-V but does not yet byte-match
// glslang on, so they cannot live in the parity corpus. Each one previously failed outright; the
// assertion here is that it still compiles and still produces a structurally sound module.
public sealed class ValidOutputTests
{
    static void CompilesAndValidates(string source, ShaderStage stage = ShaderStage.Compute,
                                     SpirVVersion target = SpirVVersion.V1_0)
    {
        byte[] bytes = GlslCompiler.CompileToBytes(source, stage, target, optimize: false);
        StructValidator.Valid(bytes);
    }

    // f16mat* were unknown type names, so any float16 matrix was rejected outright. Byte parity with
    // glslang is not reached yet (it orders the float32 type and constants differently), but the output
    // must remain structurally sound.
    [Theory]
    [InlineData("f16mat2")]
    [InlineData("f16mat3")]
    [InlineData("f16mat4")]
    [InlineData("f16mat2x3")]
    [InlineData("f16mat3x2")]
    [InlineData("f16mat4x3")]
    public void Float16MatrixTypesCompile(string type) =>
        CompilesAndValidates(
            "#version 450\n" +
            "#extension GL_EXT_shader_explicit_arithmetic_types_float16 : require\n" +
            "layout(local_size_x=1) in;\n" +
            "layout(std430,set=0,binding=0) buffer B { float o; };\n" +
            "void main() { " + type + " m = " + type + "(float16_t(1.5)); o = float(m[0][0]); }\n");

    [Theory]
    [InlineData("textureSize(s, 0).x - 1")]
    [InlineData("textureSize(sampler2D(t, p), 0).x - 1")]
    [InlineData("textureSize(s, 0).y * 2")]
    [InlineData("imageSize(img).x - 1")]
    public void TextureSizeMemberInArithmeticCompiles(string expr) =>
        CompilesAndValidates(
            "#version 450\n" +
            "layout(local_size_x=1) in;\n" +
            "layout(set=0,binding=0) uniform sampler2D s;\n" +
            "layout(set=0,binding=1) uniform texture2D t;\n" +
            "layout(set=0,binding=2) uniform sampler p;\n" +
            "layout(std430,set=0,binding=3) buffer B { int o; };\n" +
            "layout(set=0,binding=4,r32f) uniform image2D img;\n" +
            "void main() { int last = " + expr + "; o = last; }\n");

    [Fact]
    public void Float16MathCompiles() =>
        CompilesAndValidates(
            "#version 450\n" +
            "#extension GL_EXT_shader_explicit_arithmetic_types_float16 : require\n" +
            "layout(local_size_x=1) in;\n" +
            "layout(std430,set=0,binding=0) buffer B { float o; };\n" +
            "void main() {\n" +
            "  float16_t a = float16_t(1.5);\n" +
            "  f16vec3 v = f16vec3(a);\n" +
            "  f16vec3 r = v * a + v / a - v;\n" +
            "  float16_t d = dot(v, r) + length(v) + a;\n" +
            "  f16vec3 n = normalize(v) + abs(v) + floor(v) + max(v, r) + mix(v, r, float16_t(0.5));\n" +
            "  o = float(d) + float(n.x);\n" +
            "}\n");
}
