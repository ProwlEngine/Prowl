namespace Prowl.Slang.Test;

// Test that the isParameterLocationUsed API works.

public class IsParameterLocationUsedReflection
{
    [Fact]
    [DisplayTestMethodName]
    public void IsParameterLocationUsedReflectionTest()
    {
        string userSourceBody =
"""
Texture2D g_tex : register(t0);
struct Params
{
    Texture2D tex2;
    Texture2D tex3;
};

struct Material
{
    float2 uvScale;
    float2 uvBias;
}

ParameterBlock<Params> gParams;
ConstantBuffer<Material> gcMaterial;
ParameterBlock<Material> gMaterial;
[shader("fragment")]
float4 fragMain(float4 pos:SV_Position, float unused:COLOR0, float4 used:COLOR1) : SV_Target
{
    return g_tex.Load(int3(0, 0, 0)) + gParams.tex3.Load(int3(0)) + used + gMaterial.uvScale.x + gcMaterial.uvBias.x;
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

        Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

        EntryPoint entryPoint = module.FindAndCheckEntryPoint("fragMain", ShaderStage.Fragment, out _);

        ComponentType compositeProgram = session.CreateCompositeComponentType([module, entryPoint], out _);

        ComponentType linkedProgram = compositeProgram.Link(out _);

        Metadata metadata = linkedProgram.GetTargetMetadata(0, out _);

        bool isUsed = false;
        isUsed = metadata.IsParameterLocationUsed(ParameterCategory.DescriptorTableSlot, 0, 0);
        Assert.True(isUsed);

        isUsed = metadata.IsParameterLocationUsed(ParameterCategory.DescriptorTableSlot, 0, 1);
        Assert.True(isUsed);

        isUsed = metadata.IsParameterLocationUsed(ParameterCategory.DescriptorTableSlot, 0, 2);
        Assert.False(isUsed);

        isUsed = metadata.IsParameterLocationUsed(ParameterCategory.DescriptorTableSlot, 1, 0);
        Assert.False(isUsed);

        isUsed = metadata.IsParameterLocationUsed(ParameterCategory.DescriptorTableSlot, 1, 1);
        Assert.True(isUsed);

        isUsed = metadata.IsParameterLocationUsed(ParameterCategory.DescriptorTableSlot, 2, 0);
        Assert.True(isUsed);

        isUsed = metadata.IsParameterLocationUsed(ParameterCategory.VaryingInput, 0, 0);
        Assert.False(isUsed);

        isUsed = metadata.IsParameterLocationUsed(ParameterCategory.VaryingInput, 0, 1);
        Assert.True(isUsed);
    }
}
