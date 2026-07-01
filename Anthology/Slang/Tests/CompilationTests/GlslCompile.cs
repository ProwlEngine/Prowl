namespace Prowl.Slang.Test;

// Test the compilation API for cross-compiling glsl source to SPIRV.

public class GlslCompile
{
    [Fact]
    [DisplayTestMethodName]
    public void GlslCompileTest()
    {
        string userSourceBody =
"""
# version 450 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec4 aColor;
layout(location = 0) out vec4 vColor;
void main()
{
    vColor = aColor;
    gl_Position = vec4(aPosition, 0, 1);
}
""";

        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Spirv,
            Profile = GlobalSession.FindProfile("spirv_1_5")
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc]
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

        EntryPoint entryPoint = module.FindAndCheckEntryPoint("main", ShaderStage.Vertex, out _);

        ComponentType compositeProgram = session.CreateCompositeComponentType([module, entryPoint], out _);

        ComponentType linkedProgram = compositeProgram.Link(out _);

        Memory<byte> code = linkedProgram.GetEntryPointCode(0, 0, out _);

        Assert.NotEqual(0, code.Length);
    }
}
