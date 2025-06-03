namespace Prowl.Slang.Test;

// Test that the IComponentType::getTargetCode API supports
// compiling a program with multiple entrypoints and retrieving a single
// compiled module that contains all the entrypoints.
//
public class GetTargetCode
{
    [Fact]
    [DisplayTestMethodName]
    public void GetTargetCodeTest()
    {
        // Source for a module that contains an undecorated entrypoint.
        string userSourceBody =
"""
[shader("fragment")]
float4 fragMain(float4 pos:SV_Position) : SV_Target
{
    return pos;
}

[shader("vertex")]
float4 vertMain(float4 pos) : SV_Position
{
    return pos;
}
""";

        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.SpirvAsm,
            Profile = GlobalSession.FindProfile("sm_5_0")
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc]
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

        ComponentType linkedProgram = module.Link(out _);

        Memory<byte> code = linkedProgram.GetTargetCode(0, out _);

        Assert.NotEqual(0, code.Length);
        Assert.Contains("fragMain", System.Text.Encoding.UTF8.GetString(code.Span));
        Assert.Contains("vertMain", System.Text.Encoding.UTF8.GetString(code.Span));
    }
}
