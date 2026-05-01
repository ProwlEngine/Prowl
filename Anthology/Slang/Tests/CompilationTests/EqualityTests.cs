namespace Prowl.Slang.Test;

public class EqualityTests
{
    // Source covering: Attribute, DeclReflection, EntryPointReflection, FunctionReflection,
    // GenericReflection, ShaderReflection, TypeLayoutReflection, TypeReflection,
    // VariableLayoutReflection, and VariableReflection.
    //
    // Note: Slang strips the "Attribute" suffix from struct names when registering attributes,
    // so struct "MyTestAttribute" is accessed as [MyTest(...)].
    private const string Source =
"""
[__AttributeUsage(_AttributeTargets.Struct)]
struct MyTestAttribute { int value; }

[MyTest(42)]
struct MyStruct { float x; float y; }

struct MyGenericStruct<T> { T field; }

uniform float cbValue;

[shader("fragment")]
float4 fragMain(float4 pos : SV_Position) : SV_Target { return cbValue * pos; }

float globalFunc(float x) { return x; }
""";

    private static (Session session, Module module, ComponentType composite, ShaderReflection layout) Setup()
    {
        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Hlsl,
            Profile = GlobalSession.FindProfile("sm_5_0")
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc]
        };

        Session session = GlobalSession.CreateSession(sessionDesc);
        Module module = session.LoadModuleFromSourceString("eq-test", "eq-test.slang", Source, out _);
        EntryPoint entryPoint = module.FindAndCheckEntryPoint("fragMain", ShaderStage.Fragment, out _);
        ComponentType composite = session.CreateCompositeComponentType([module, entryPoint], out _);
        ShaderReflection layout = composite.GetLayout();

        return (session, module, composite, layout);
    }


    [Fact]
    public void ShaderReflection_TwoInstancesSamePointer_AreEqual()
    {
        (_, _, ComponentType composite, _) = Setup();

        ShaderReflection a = composite.GetLayout();
        ShaderReflection b = composite.GetLayout();

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }


    [Fact]
    public void TypeReflection_TwoInstancesSamePointer_AreEqual()
    {
        (_, _, _, ShaderReflection layout) = Setup();

        TypeReflection a = layout.FindTypeByName("MyStruct");
        TypeReflection b = layout.FindTypeByName("MyStruct");

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }


    [Fact]
    public void TypeLayoutReflection_TwoInstancesSamePointer_AreEqual()
    {
        (_, _, _, ShaderReflection layout) = Setup();

        TypeReflection type = layout.FindTypeByName("MyStruct");
        TypeLayoutReflection a = layout.GetTypeLayout(type);
        TypeLayoutReflection b = layout.GetTypeLayout(type);

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }


    [Fact]
    public void VariableReflection_TwoInstancesSamePointer_AreEqual()
    {
        (_, _, _, ShaderReflection layout) = Setup();

        TypeReflection type = layout.FindTypeByName("MyStruct");
        VariableReflection a = type.GetFieldByIndex(0);
        VariableReflection b = type.GetFieldByIndex(0);

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }


    [Fact]
    public void VariableLayoutReflection_TwoInstancesSamePointer_AreEqual()
    {
        (_, _, _, ShaderReflection layout) = Setup();

        Assert.True(layout.ParameterCount > 0);

        VariableLayoutReflection a = layout.GetParameterByIndex(0);
        VariableLayoutReflection b = layout.GetParameterByIndex(0);

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }


    [Fact]
    public void EntryPointReflection_TwoInstancesSamePointer_AreEqual()
    {
        (_, _, _, ShaderReflection layout) = Setup();

        EntryPointReflection a = layout.GetEntryPointByIndex(0);
        EntryPointReflection b = layout.GetEntryPointByIndex(0);

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }


    [Fact]
    public void FunctionReflection_TwoInstancesSamePointer_AreEqual()
    {
        (_, _, _, ShaderReflection layout) = Setup();

        FunctionReflection a = layout.FindFunctionByName("globalFunc");
        FunctionReflection b = layout.FindFunctionByName("globalFunc");

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }


    [Fact]
    public void GenericReflection_TwoInstancesSamePointer_AreEqual()
    {
        (_, _, _, ShaderReflection layout) = Setup();

        TypeReflection genericType = layout.FindTypeByName("MyGenericStruct");
        GenericReflection a = genericType.GenericContainer;
        GenericReflection b = genericType.GenericContainer;

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }


    [Fact]
    public void DeclReflection_TwoInstancesSamePointer_AreEqual()
    {
        (_, Module module, _, _) = Setup();

        DeclReflection moduleDecl = module.GetModuleReflection();
        DeclReflection a = moduleDecl.GetChild(0);
        DeclReflection b = moduleDecl.GetChild(0);

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }


    [Fact]
    public void Attribute_TwoInstancesSamePointer_AreEqual()
    {
        (_, _, _, ShaderReflection layout) = Setup();

        TypeReflection type = layout.FindTypeByName("MyStruct");
        Attribute a = type.FindAttributeByName("MyTest");
        Attribute b = type.FindAttributeByName("MyTest");

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }


    [Fact]
    public void TypeParameterReflection_TwoInstancesSamePointer_AreEqual()
    {
        // An unspecialized generic entry point produces program-level type parameters.
        const string typeParamSource =
"""
interface IProcessor { float process(float x); }
[shader("compute")]
[numthreads(1,1,1)]
void computeMain<T : IProcessor>(uniform T proc) { }
""";

        TargetDescription targetDesc = new() { Format = CompileTarget.Spirv };
        SessionDescription sessionDesc = new() { Targets = [targetDesc] };
        Session session = GlobalSession.CreateSession(sessionDesc);
        Module module = session.LoadModuleFromSourceString("tp-test", "tp-test.slang", typeParamSource, out _);
        EntryPoint entryPoint = module.FindAndCheckEntryPoint("computeMain", ShaderStage.Compute, out _);
        ComponentType composite = session.CreateCompositeComponentType([module, entryPoint], out _);
        ShaderReflection layout = composite.GetLayout();

        Assert.True(layout.TypeParameterCount > 0);

        TypeParameterReflection a = layout.GetTypeParameterByIndex(0);
        TypeParameterReflection b = layout.GetTypeParameterByIndex(0);

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }


    [Fact]
    public void Module_TwoManagedWrappersAroundSameNativeModule_AreEqual()
    {
        // Slang caches modules by name within a session; reloading the same module
        // returns the same underlying native pointer.
        const string source = "float globalX = 1.0;";

        TargetDescription targetDesc = new() { };
        SessionDescription sessionDesc = new() { Targets = [targetDesc] };
        Session session = GlobalSession.CreateSession(sessionDesc);

        Module m1 = session.LoadModuleFromSourceString("eq-module", "eq-module.slang", source, out _);
        Module m2 = session.LoadModuleFromSourceString("eq-module", "eq-module.slang", source, out _);

        Assert.True(m1.Equals(m2));
    }


    [Fact]
    public void ComponentType_TwoManagedWrappersAroundSameNativeModule_AreEqual()
    {
        const string source = "float globalX = 1.0;";

        TargetDescription targetDesc = new() { };
        SessionDescription sessionDesc = new() { Targets = [targetDesc] };
        Session session = GlobalSession.CreateSession(sessionDesc);

        Module m1 = session.LoadModuleFromSourceString("eq-ct-module", "eq-ct-module.slang", source, out _);
        Module m2 = session.LoadModuleFromSourceString("eq-ct-module", "eq-ct-module.slang", source, out _);

        Assert.True(((ComponentType)m1).Equals(m2));
    }


    [Fact]
    public void Session_SameManagedInstance_EqualsItself()
    {
        TargetDescription targetDesc = new() { };
        SessionDescription sessionDesc = new() { Targets = [targetDesc] };
        Session session = GlobalSession.CreateSession(sessionDesc);

        Assert.True(session.Equals(session));
    }


    [Fact]
    public void Diagnostic_TwoInstancesSameValues_AreEqual()
    {
        Diagnostic a = new()
        {
            Severity = Severity.Warning,
            ErrorCode = 100,
            Message = "test message",
            FilePath = "test.slang",
            LineNumber = 5
        };

        Diagnostic b = new()
        {
            Severity = Severity.Warning,
            ErrorCode = 100,
            Message = "test message",
            FilePath = "test.slang",
            LineNumber = 5
        };

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }


    [Fact]
    public void Metadata_SameManagedInstance_EqualsItself()
    {
        const string source =
"""
[shader("compute")]
[numthreads(1,1,1)]
void computeMain() { }
""";

        TargetDescription targetDesc = new() { Format = CompileTarget.Spirv };
        SessionDescription sessionDesc = new() { Targets = [targetDesc] };
        Session session = GlobalSession.CreateSession(sessionDesc);
        Module module = session.LoadModuleFromSourceString("meta-test", "meta-test.slang", source, out _);
        EntryPoint entryPoint = module.FindAndCheckEntryPoint("computeMain", ShaderStage.Compute, out _);
        ComponentType composite = session.CreateCompositeComponentType([module, entryPoint], out _);
        ComponentType linked = composite.Link(out _);

        Metadata m = linked.GetTargetMetadata(0, out _);

        Assert.True(m.Equals(m));
    }
}
