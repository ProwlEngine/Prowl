namespace Prowl.Slang.Test;

public class AttributeReflection
{
    [Fact]
    public void AttributeReflectionTest()
    {
        const string userSourceBody =
"""
public enum E
{
    V0,
    V1,
};

[__AttributeUsage(_AttributeTargets.Struct)]
public struct NormalTextureAttribute
{
    public E Type;
    public float x;
};

[COM("042BE50B-CB01-4DBB-8367-3A9CDCBE2F49")]
interface IInterface { void f(); }

[NormalTexture(E.V1, 6)]

struct TS { };
""";

        string userSource = userSourceBody;

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

        Module module = session.LoadModuleFromSourceString(
            "m",
            "m.slang",
            userSource,
            out _);

        ShaderReflection reflection = module.GetLayout();

        TypeReflection interfaceType = reflection.FindTypeByName("IInterface");
        Attribute comAttribute = interfaceType.FindAttributeByName("COM");

        string? guid = comAttribute.GetArgumentValueString(0);
        Assert.Equal("042BE50B-CB01-4DBB-8367-3A9CDCBE2F49", guid);

        TypeReflection testType = reflection.FindTypeByName("TS");
        Attribute normalTextureAttribute = testType.FindAttributeByName("NormalTexture");

        int? value = normalTextureAttribute.GetArgumentValueInt(0);
        Assert.Equal(1, value);

        float? fvalue = normalTextureAttribute.GetArgumentValueFloat(1);
        Assert.Equal(6.0f, fvalue);
    }
}
