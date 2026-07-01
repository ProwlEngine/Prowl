namespace Prowl.Slang.Test;

// Test the compilation API for compiling a specialized generic entrypoint.

public class GenericEntrypoint
{
    [Fact]
    [DisplayTestMethodName]
    public void GenericEntryPointCompile()
    {
        string userSourceBody =
"""
interface I { int getValue(); }
struct X : I { int getValue() { return 100; } }
float4 vertMain<T:I>(uniform T o)
{
    return float4(o.getValue(), 0, 0, 1);
}
""";

        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Glsl,
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

        EntryPoint entryPoint = module.FindAndCheckEntryPoint("vertMain<X>", ShaderStage.Vertex, out _);

        ComponentType compositeProgram = session.CreateCompositeComponentType([module, entryPoint], out _);

        ComponentType linkedProgram = compositeProgram.Link(out _);

        Memory<byte> code = linkedProgram.GetEntryPointCode(0, 0, out _);

        Assert.NotEqual(0, code.Length);
        Assert.Contains("vec4(float(X_getValue", System.Text.Encoding.UTF8.GetString(code.Span));
    }
}
