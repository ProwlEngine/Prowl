namespace Prowl.Slang.Test;

// Test metal argument buffer tier2 layout rules.

public class MetalArgumentBufferTier2Reflection
{
    [Fact]
    public void MetalArgumentBufferTier2ReflectionTest()
    {
        const string userSourceBody =
"""
struct A
{
    float3 one;
    float3 two;
    float three;
}

struct Args
{
    ParameterBlock<A> a;
}

ParameterBlock<Args> argument_buffer;
RWStructuredBuffer<float> outputBuffer;

[numthreads(1, 1, 1)]
void computeMain()
{
    outputBuffer[0] = argument_buffer.a.two.x;
}
""";

        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Spirv,
            Profile = GlobalSession.FindProfile("spirv_1_5")
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString(
            "m",
            "m.slang",
            userSourceBody,
            out _);

        ShaderReflection layout = module.GetLayout();

        TypeReflection type = layout.FindTypeByName("A");
        TypeLayoutReflection typeLayout = layout.GetTypeLayout(type, LayoutRules.MetalArgumentBufferTier2);

        Assert.Equal(0U, typeLayout.GetFieldByIndex(0).GetOffset());
        Assert.Equal(16U, typeLayout.GetFieldByIndex(0).TypeLayout.GetSize());
        Assert.Equal(16U, typeLayout.GetFieldByIndex(1).GetOffset());
        Assert.Equal(16U, typeLayout.GetFieldByIndex(1).TypeLayout.GetSize());
        Assert.Equal(32U, typeLayout.GetFieldByIndex(2).GetOffset());
        Assert.Equal(4U, typeLayout.GetFieldByIndex(2).TypeLayout.GetSize());
    }
}
