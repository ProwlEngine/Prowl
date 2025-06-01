
namespace Prowl.Slang.Test;

// Test metal argument buffer tier2 layout rules.

public class MetalArgumentBufferTier2ReflectionTest
{
    [Fact]
    public void MetalArgumentBufferTier2Reflection()
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

        TargetDescription targetDesc = new TargetDescription
        {
            Format = CompileTarget.Spirv,
            Profile = GlobalSession.FindProfile("spirv_1_5")
        };

        SessionDescription sessionDesc = new SessionDescription
        {
            Targets = [targetDesc],
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString(
            "m",
            "m.slang",
            userSourceBody,
            out _);

        ShaderReflection layout = module.GetLayout(0, out _);

        TypeReflection type = layout.FindTypeByName("A");
        TypeLayoutReflection typeLayout = layout.GetTypeLayout(type, LayoutRules.MetalArgumentBufferTier2);

        Assert.Equal((uint)0, typeLayout.GetFieldByIndex(0).GetOffset(ParameterCategory.None));
        Assert.Equal((uint)16, typeLayout.GetFieldByIndex(0).TypeLayout.GetSize(ParameterCategory.));
        Assert.Equal((uint)16, typeLayout.GetFieldByIndex(1).GetOffset(ParameterCategory.None));
        Assert.Equal((uint)16, typeLayout.GetFieldByIndex(1).TypeLayout.GetSize(ParameterCategory.None));
        Assert.Equal((uint)32, typeLayout.GetFieldByIndex(2).GetOffset(ParameterCategory.None));
        Assert.Equal((uint)4, typeLayout.GetFieldByIndex(2).TypeLayout.GetSize(ParameterCategory.None));
    }
}
