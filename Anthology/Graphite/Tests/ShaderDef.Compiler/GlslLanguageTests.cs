using Xunit;

namespace Prowl.Graphite.ShaderDef.Compiler.Tests;


public class GlslLanguageTests
{
    [Fact]
    public void MaterialSampler_PassesToAFunction()
    {
        Glsl.Compile("""
            Vertex
            {
                out vec2 vUV;
                void main() { vUV = vec2(0); gl_Position = vec4(0, 0, 0, 1); }
            }
            Fragment
            {
                uniform sampler2D _Atlas;
                in vec2 vUV;
                out vec4 color;
                vec4 Read(sampler2D atlas, vec2 uv) { return texture(atlas, uv); }
                void main() { color = Read(_Atlas, vUV); }
            }
            """);
    }

    [Fact]
    public void SwizzleOfALocal_CanBeAssigned()
    {
        Glsl.Compile("""
            Vertex
            {
                void main() { gl_Position = vec4(0, 0, 0, 1); }
            }
            Fragment
            {
                uniform sampler2D _Atlas;
                out vec4 color;
                void main()
                {
                    vec4 tex = texture(_Atlas, vec2(0.5));
                    tex.rgb = mix(tex.rgb, vec3(1), 0.5);
                    tex.a += 0.1;
                    color = tex;
                }
            }
            """);
    }
}
