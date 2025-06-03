namespace Prowl.Slang.Test;


public class DefaultMatrixLayout
{
    [Fact]
    [DisplayTestMethodName]
    public void DefaultMatrixLayoutTest()
    {
        // Source for a module that contains an undecorated entrypoint.
        const string userSourceBody =
    """
RWStructuredBuffer<float> output;

[numthreads(1, 1, 1)][shader("compute")]
void main(uniform float3x4 m)
{
    output[0] = m[0][0];
}
""";

        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Glsl,
            Profile = GlobalSession.FindProfile("glsl_460"),
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],
            DefaultMatrixLayoutMode = MatrixLayoutMode.ColumnMajor
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

        EntryPoint entry = module.FindEntryPointByName("main");

        ComponentType program = session.CreateCompositeComponentType([module, entry], out _);

        ComponentType linkedProgram = program.Link(out _);

        Memory<byte> outCode = linkedProgram.GetEntryPointCode(0, 0, out _);

        string code = System.Text.Encoding.UTF8.GetString(outCode.Span);

        Assert.Contains("row_major", code);
    }
}
