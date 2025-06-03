namespace Prowl.Slang.Test;

// Test that the compilation API can be used to produce CUDA source.

public class CudaCompile
{
    [Fact]
    [DisplayTestMethodName]
    public void CudaCompileTest()
    {
        // Source for a module that contains an undecorated entrypoint.
        const string userSourceBody =
    """
[CudaDeviceExport]
float testExportedFunc(float3 particleRayOrigin)
{
    return dot(particleRayOrigin, particleRayOrigin);
};
""";

        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.CudaSource
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

        ComponentType linkedProgram = module.Link(out _);

        Memory<byte> code = linkedProgram.GetTargetCode(0, out _);

        Assert.NotEqual(0, code.Length);

        string text = System.Text.Encoding.UTF8.GetString(code.Span);

        Assert.NotEqual(-1, text.IndexOf("testExportedFunc"));
    }
}
