using Prowl.Prism.Compiler;
using Prowl.Prism.SpirV;
using Xunit;

namespace Prowl.Prism.Tests;

// A const declared in a function or block must not leak its value into later functions or outer scopes.
public sealed class ConstScopeTests
{
    // The int constants each function's body references, keyed by the function's OpName.
    static Dictionary<string, HashSet<int>> IntConstantsByFunction(string source, ShaderStage stage)
    {
        byte[] bytes = GlslCompiler.CompileToBytes(source, stage, SpirVVersion.V1_0, optimize: false);
        StructValidator.Valid(bytes);
        uint[] words = new uint[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, words, 0, bytes.Length);

        var names = new Dictionary<uint, string>();
        var intTypes = new HashSet<uint>();
        var intConstants = new Dictionary<uint, int>();
        var result = new Dictionary<string, HashSet<int>>();
        HashSet<int>? current = null;
        foreach (var (op, o) in SpirVReader.Read(words))
        {
            switch (op)
            {
                case Op.Name:
                    names[o[0]] = System.Text.Encoding.UTF8.GetString(o.Skip(1).SelectMany(BitConverter.GetBytes).TakeWhile(b => b != 0).ToArray());
                    break;
                case Op.TypeInt when o[2] == 1: intTypes.Add(o[0]); break;
                case Op.Constant when intTypes.Contains(o[0]): intConstants[o[1]] = (int)o[2]; break;
                case Op.Function:
                    string name = names[o[1]];
                    result[name[..(name.IndexOf('(') is int p and >= 0 ? p : name.Length)]] = current = [];
                    break;
                case Op.FunctionEnd: current = null; break;
                default:
                    if (current is not null)
                        foreach (uint id in o)
                            if (intConstants.TryGetValue(id, out int value)) current.Add(value);
                    break;
            }
        }
        return result;
    }

    const string Compute = "#version 450\nlayout(local_size_x=1) in;\nlayout(std430,set=0,binding=0) buffer B { int o; };\n";

    [Fact]
    public void SameNamedFunctionConstsKeepTheirOwnValues()
    {
        var consts = IntConstantsByFunction(Compute +
            "int light() { const int N = 6; int s = 0; for (int i = 0; i < N; i++) s += i; return s; }\n" +
            "int view() { const int N = 20; int s = 0; for (int i = 0; i < N; i++) s += light(); return s / N; }\n" +
            "void main() { o = view(); }\n", ShaderStage.Compute);

        Assert.Contains(6, consts["light"]);
        Assert.DoesNotContain(20, consts["light"]);
        Assert.Contains(20, consts["view"]);
        Assert.DoesNotContain(6, consts["view"]);
    }

    [Fact]
    public void FunctionConstDoesNotShadowGlobalInLaterFunction()
    {
        var consts = IntConstantsByFunction(Compute +
            "const int N = 4;\n" +
            "int a() { const int N = 6; return N; }\n" +
            "int b() { return N; }\n" +
            "void main() { o = a() + b(); }\n", ShaderStage.Compute);

        Assert.Contains(6, consts["a"]);
        Assert.Contains(4, consts["b"]);
        Assert.DoesNotContain(6, consts["b"]);
    }

    [Fact]
    public void BlockConstDoesNotShadowGlobalAfterTheBlock()
    {
        var consts = IntConstantsByFunction(Compute +
            "const int N = 4;\n" +
            "int f(int x) { { const int N = 6; x += N; } return x * N; }\n" +
            "void main() { o = f(o); }\n", ShaderStage.Compute);

        Assert.Contains(6, consts["f"]);
        Assert.Contains(4, consts["f"]);
    }

    // The Cosmos atmosphere shape: two helpers each declare `const int N`, one calls the other inside its loop.
    [Fact]
    public void NestedLoopHelpersWithSameNamedConstsUseTheirOwnBounds()
    {
        var consts = IntConstantsByFunction(
            "#version 450\n" +
            "layout(location=0) in vec3 ro;\n" +
            "layout(location=0) out vec4 target;\n" +
            "vec3 lightDepth(vec3 p) { const int N = 6; float ds = 1.0 / float(N); vec3 d = vec3(0.0);\n" +
            "  for (int i = 0; i < N; i++) d += p * (float(i) + 0.5) * ds; return d; }\n" +
            "void integrate(vec3 p, out vec3 inscatter, out vec3 transmittance) {\n" +
            "  inscatter = vec3(0.0); transmittance = vec3(1.0);\n" +
            "  if (p.x < 0.0) return;\n" +
            "  const int N = 20; float ds = 1.0 / float(N); vec3 depth = vec3(0.0);\n" +
            "  for (int i = 0; i < N; i++) { depth += p * ds; inscatter += exp(-(depth + lightDepth(p))) * ds; }\n" +
            "  transmittance = exp(-depth); }\n" +
            "void main() { vec3 a, b; integrate(ro, a, b); target = vec4(a + b, 1.0); }\n", ShaderStage.Fragment);

        Assert.Contains(6, consts["lightDepth"]);
        Assert.Contains(20, consts["integrate"]);
        Assert.DoesNotContain(6, consts["integrate"]);
    }
}
