namespace Prowl.Slang.Test;

// Test that the IModule::findAndCheckEntryPoint API works with modules that
// defines two entrypoints, where one entrypoint calls the other.


public class FindEntrypointNested
{
    [Fact]
    [DisplayTestMethodName]
    public void FindEntryPointNestedTest()
    {
        // Source for a module that contains an undecorated entrypoint.
        string userSourceBody =
"""
[shader("raygeneration")]
void inner()
{
    AllMemoryBarrier();
}
[shader("raygeneration")]
void outer()
{
    inner();
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

        EntryPoint entryPoint = module.FindAndCheckEntryPoint("outer", ShaderStage.RayGeneration, out _);

        ComponentType compositeProgram = session.CreateCompositeComponentType([module, entryPoint], out _);

        ComponentType linkedProgram = compositeProgram.Link(out _);

        Memory<byte> code = linkedProgram.GetEntryPointCode(0, 0, out _);

        Assert.NotEqual(0, code.Length);
    }
}
