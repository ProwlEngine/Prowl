namespace Prowl.Slang.Test;



public class PointerTypeLayout
{
    [Fact]
    [DisplayTestMethodName]
    public void PointerTypeLayoutTest()
    {
        string testSource =
"""
struct TestStruct
{
    int3 member0;
    float member1;
};
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

        Module module = session.LoadModuleFromSourceString("m", "m.slang", testSource, out _);

        ShaderReflection reflection = module.GetLayout();
        TypeReflection testStruct = reflection.FindTypeByName("Ptr<TestStruct>");
        TypeLayoutReflection ptrLayout = reflection.GetTypeLayout(testStruct);
        TypeLayoutReflection valueLayout = ptrLayout.ElementTypeLayout;

        Assert.Equal(2U, valueLayout.FieldCount);
        Assert.Equal(12U, valueLayout.GetFieldByIndex(1).GetOffset());

    }
}
