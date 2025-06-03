namespace Prowl.Slang.Test;

// Test that the IModule::findAndCheckEntryPoint API supports discovering
// entrypoints without a [shader] attribute.

public class GenericInterfaceConformance
{
    [Fact]
    [DisplayTestMethodName]
    public void GenericInterfaceConformanceTest()
    {
        // Source for a module that contains an undecorated entrypoint.
        string userSourceBody =
"""
        public interface ITestInterface<Real : IFloat> {
        Real sample();
    }

    struct TestInterfaceImpl<Real : IFloat> : ITestInterface<Real> {
            Real sample()
    {
        return x;
    }
    Real x;
        }

        //TEST_INPUT: set data = new StructuredBuffer<ITestInterface<float> >[new TestInterfaceImpl<float>{1.0}];
        StructuredBuffer<ITestInterface<float>> data;

    //TEST_INPUT: set outputBuffer = out ubuffer(data=[0 0 0 0], stride=4);
    RWStructuredBuffer<int> outputBuffer;

    //TEST_INPUT: type_conformance TestInterfaceImpl<float>:ITestInterface<float> = 3

    [numthreads(1, 1, 1)]
    void computeMain()
    {
        let obj = data[0];
        // CHECK: 1
        outputBuffer[0] = int(obj.sample());
    }
""";

        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Hlsl,
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

        EntryPoint entryPoint = module.FindAndCheckEntryPoint("computeMain", ShaderStage.Compute, out _);

        ComponentType compositeProgram = session.CreateCompositeComponentType([module, entryPoint], out _);

        ComponentType typeConformance = session.CreateTypeConformanceComponentType(
            compositeProgram.GetLayout().FindTypeByName("TestInterfaceImpl<float>"),
            compositeProgram.GetLayout().FindTypeByName("ITestInterface<float>"),
            3,
            out _);

        ComponentType compositeProgram2 = session.CreateCompositeComponentType([compositeProgram, typeConformance], out _);

        ComponentType linkedProgram = compositeProgram2.Link(out _);

        Memory<byte> code = linkedProgram.GetEntryPointCode(0, 0, out _);

        Assert.NotEqual(0, code.Length);
        Assert.Contains("computeMain", System.Text.Encoding.UTF8.GetString(code.Span));
    }
}
