namespace Prowl.Slang.Test;

// Test that the IModule::findAndCheckEntryPoint API supports discovering
// entrypoints without a [shader] attribute.


public class FindCheckEntrypoint
{
    [Fact]
    [DisplayTestMethodName]
    public void FindAndCheckEntryPointTest()
    {
        // Source for a module that contains an undecorated entrypoint.
        const string userSourceBody =
"""
float4 fragMain(float4 pos:SV_Position) : SV_Target
{
    return pos;
}
""";
        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Spirv,
            Profile = GlobalSession.FindProfile("spirv_1_5"),
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

        EntryPoint entryPoint = module.FindAndCheckEntryPoint("fragMain", ShaderStage.Fragment, out _);

        ComponentType compositeProgram = session.CreateCompositeComponentType([module, entryPoint], out _);

        ComponentType linkedProgram = compositeProgram.Link(out _);

        Memory<byte> code = linkedProgram.GetEntryPointCode(0, 0, out _);

        Assert.NotEqual(0, code.Length);
    }

    /*
        // This test reproduces issue #6507, where it was noticed that compilation of
        // tests/compute/simple.slang for PTX target generates invalid code.
        // TODO: Remove this when issue #4760 is resolved, because at that point
        // tests/compute/simple.slang should cover the same issue.
        [Fact]
        public void CudaCodeGenBugTest()
        {
            // Source for a module that contains an undecorated entrypoint.
            string userSourceBody =
    """
    RWStructuredBuffer<float> outputBuffer;

    [numthreads(4, 1, 1)]
    void computeMain(uint3 dispatchThreadID : SV_DispatchThreadID)
    {
        outputBuffer[dispatchThreadID.x] = float(dispatchThreadID.x);
    }
    """;

            TargetDescription targetDesc = new()
            {
                Format = CompileTarget.Ptx,
            };

            SessionDescription sessionDesc = new()
            {
                Targets = [targetDesc],
            };

            Session session = GlobalSession.CreateSession(sessionDesc);

            Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

            EntryPoint entryPoint = module.FindAndCheckEntryPoint("computeMain", ShaderStage.Fragment, out _);

            ComponentType compositeProgram = session.CreateCompositeComponentType([module, entryPoint], out _);

            ComponentType linkedProgram = compositeProgram.Link(out _);

            Memory<byte> code = linkedProgram.GetEntryPointCode(0, 0, out _);

            Assert.NotEqual(0, code.Length);
        }
    */
}
