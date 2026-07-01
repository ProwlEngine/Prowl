namespace Prowl.Slang.Test;

// Test that the getBindingRangeImageFormat API works.

public class ImageFormatReflection
{
    [Fact]
    [DisplayTestMethodName]
    public void ImageFormatReflectionTest()
    {
        string userSourceBody =
"""
Texture2D<uint4> g_tex : register(t0);
float4 fragMain(float4 pos:SV_Position) : SV_Position
{
    return pos;
}
""";

        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Hlsl,
            Profile = GlobalSession.FindProfile("sm_5_0")
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

        EntryPoint entryPoint = module.FindAndCheckEntryPoint("fragMain", ShaderStage.Fragment, out _);

        ComponentType compositeProgram = session.CreateCompositeComponentType([module, entryPoint], out _);

        ShaderReflection layout = compositeProgram.GetLayout();
        ImageFormat format = layout.GlobalParamsTypeLayout.GetBindingRangeImageFormat(0);

        Assert.Equal(ImageFormat.RGBA32UInt, format);
    }
}
