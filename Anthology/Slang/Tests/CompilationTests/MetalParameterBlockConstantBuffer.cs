namespace Prowl.Slang.Test;

public class MetalParameterBlockConstantBuffer
{
    [Fact]
    [DisplayTestMethodName]
    public void MetalConstantBufferInParameterBlockLayoutTest()
    {
        string userSourceBody =
"""
struct T
{
    float4 m0;
    float m1;
    float3 m2;
};

ParameterBlock<ConstantBuffer<T>> params;
""";

        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Metal,
            Profile = GlobalSession.FindProfile("metal")
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc]
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

        ShaderReflection reflection = module.GetLayout();

        // Collect our layouts
        TypeReflection paramBlockType = reflection.FindTypeByName("ParameterBlock<ConstantBuffer<T>>");
        TypeLayoutReflection paramBlockLayout = reflection.GetTypeLayout(paramBlockType);
        TypeLayoutReflection cbufferLayout = paramBlockLayout.ElementTypeLayout;
        TypeLayoutReflection structLayout = cbufferLayout.ElementTypeLayout;

        // Check offsets follow constant buffer rules (uniform alignment)
        // m0 : float4 should be at offset 0
        // m1 : float  should be at offset 16 (after float4)
        // m2 : float3 should be at offset 32 (aligned to 16-byte boundary)
        Assert.Equal(3U, structLayout.FieldCount);
        Assert.Equal(0U, structLayout.GetFieldByIndex(0).GetOffset());
        Assert.Equal(16U, structLayout.GetFieldByIndex(1).GetOffset());
        Assert.Equal(32U, structLayout.GetFieldByIndex(2).GetOffset());
    }

    [Fact]
    [DisplayTestMethodName]
    public void MetalArgumentBufferLayoutTest()
    {
        string testSource =
"""
struct T
{
    float4 m0;
    float m1;
    float3 m2;
};

// Using ParameterBlock directly without ConstantBuffer wrapper
ParameterBlock<T> params;
""";

        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Metal,
            Profile = GlobalSession.FindProfile("metal")
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc]
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString("m", "m.slang", testSource, out _);

        ShaderReflection reflection = module.GetLayout();

        // Collect our layouts
        TypeReflection paramBlockType = reflection.FindTypeByName("ParameterBlock<T>");
        TypeLayoutReflection paramBlockLayout = reflection.GetTypeLayout(paramBlockType);
        TypeLayoutReflection structLayout = paramBlockLayout.ElementTypeLayout;

        // Check that offsets follow Metal argument buffer rules
        // Fields should have 0 offset and meaningful binding indices
        Assert.Equal(3U, structLayout.FieldCount);
        Assert.Equal(0U, structLayout.GetFieldByIndex(0).GetOffset());
        Assert.Equal(0U, structLayout.GetFieldByIndex(1).GetOffset());
        Assert.Equal(0U, structLayout.GetFieldByIndex(2).GetOffset());
        Assert.Equal(0U, structLayout.GetFieldByIndex(0).BindingIndex);
        Assert.Equal(1U, structLayout.GetFieldByIndex(1).BindingIndex);
        Assert.Equal(2U, structLayout.GetFieldByIndex(2).BindingIndex);
    }
}
