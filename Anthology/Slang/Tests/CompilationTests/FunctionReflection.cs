namespace Prowl.Slang.Test;


public class FuncReflection
{

    // Test that the reflection API provides correct info about entry point and ordinary functions.
    [Fact]
    [DisplayTestMethodName]
    public void FunctionReflectionTest()
    {
        // Source for a module that contains an undecorated entrypoint.
        string userSourceBody =
"""
[__AttributeUsage(_AttributeTargets.Function)]
struct MyFuncPropertyAttribute { int v; }

[MyFuncProperty(1024)]
[Differentiable]
float ordinaryFunc(no_diff float x, int y) { return x + y; }

float4 fragMain(float4 pos:SV_Position) : SV_Position
{
    return pos;
}

float foo(float x) { return x; }
float foo(float x, uint i) { return x + i; }

int bar1(IFloat a, IFloat b) { return 0; }
int bar2<T>(T a, float3 b) { return 0; }
int bar3(float3 b) { return 0; }
int bar4<T:IFloat>(T a){ return 0; }

struct Foo { __init() { } }
""";

        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Hlsl,
            Profile = GlobalSession.FindProfile("sm_5_0"),
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

        EntryPoint entryPoint = module.FindAndCheckEntryPoint("fragMain", ShaderStage.Fragment, out _);

        var entryPointFuncReflection = entryPoint.GetFunctionReflection();
        Assert.Equal("fragMain", entryPointFuncReflection.Name);
        Assert.Equal(1U, entryPointFuncReflection.ParameterCount);
        Assert.Equal("pos", entryPointFuncReflection.GetParameterByIndex(0).Name);
        Assert.Equal("vector<float,4>", entryPointFuncReflection.GetParameterByIndex(0).Type.FullName);

        var funcReflection = module.GetLayout().FindFunctionByName("ordinaryFunc");

        Assert.Equal("float", funcReflection.ReturnType.FullName);
        Assert.Equal("ordinaryFunc", funcReflection.Name);
        Assert.Equal(2U, funcReflection.ParameterCount);
        Assert.Equal("x", funcReflection.GetParameterByIndex(0).Name);
        Assert.Equal("float", funcReflection.GetParameterByIndex(0).Type.FullName);

        Assert.Equal("y", funcReflection.GetParameterByIndex(1).Name);
        Assert.Equal("int", funcReflection.GetParameterByIndex(1).Type.FullName);

        Assert.Equal(1U, funcReflection.UserAttributeCount);
        var userAttribute = funcReflection.GetUserAttributeByIndex(0);
        Assert.Equal("MyFuncProperty", userAttribute.Name);
        Assert.Equal(1U, userAttribute.ArgumentCount);
        Assert.Equal("int", userAttribute.GetArgumentType(0).FullName);

        int? val = userAttribute.GetArgumentValueInt(0);
        Assert.Equal(val, 1024);
        Assert.Equal(funcReflection.FindAttributeByName("MyFuncProperty"), userAttribute);

        // Check overloaded method resolution
        var overloadReflection = module.GetLayout().FindFunctionByName("foo");
        Assert.True(overloadReflection.IsOverloaded);
        Assert.Equal(2U, overloadReflection.OverloadCount);

        var firstOverload = overloadReflection.GetOverload(0);
        Assert.Equal("foo", firstOverload.Name);
        Assert.Equal(2U, firstOverload.ParameterCount);
        Assert.Equal("x", firstOverload.GetParameterByIndex(0).Name);
        Assert.Equal("float", firstOverload.GetParameterByIndex(0).Type.FullName);
        Assert.Equal("i", firstOverload.GetParameterByIndex(1).Name);
        Assert.Equal("uint", firstOverload.GetParameterByIndex(1).Type.FullName);

        var secondOverload = overloadReflection.GetOverload(1);
        Assert.Equal("foo", secondOverload.Name);
        Assert.Equal(1U, secondOverload.ParameterCount);
        Assert.Equal("x", secondOverload.GetParameterByIndex(0).Name);

        // Check overload resolution via argument types.
        TypeReflection floatType = module.GetLayout().FindTypeByName("float");
        TypeReflection uIntType = module.GetLayout().FindTypeByName("uint");

        var resolvedFunctionReflection = overloadReflection.SpecializeWithArgTypes([floatType, uIntType]);
        Assert.Equal(resolvedFunctionReflection, firstOverload);

        //
        // More testing for SpecializeWithArgTypes
        //

        // bar1 (IFloat, IFloat) . int
        //
        var bar1Reflection = module.GetLayout().FindFunctionByName("bar1");
        Assert.False(bar1Reflection.IsOverloaded);
        Assert.Equal(2U, bar1Reflection.ParameterCount);

        var float3Type = module.GetLayout().FindTypeByName("float3");

        resolvedFunctionReflection = bar1Reflection.SpecializeWithArgTypes([float3Type, float3Type]);

        Assert.Equal(2U, resolvedFunctionReflection.ParameterCount);
        Assert.Equal("IFloat", resolvedFunctionReflection.GetParameterByIndex(0).Type.FullName);
        Assert.Equal("IFloat", resolvedFunctionReflection.GetParameterByIndex(1).Type.FullName);

        // bar2 (T : IFloat, float3) . int
        //
        var bar2Reflection = module.GetLayout().FindFunctionByName("bar2");
        Assert.False(bar2Reflection.IsOverloaded);
        Assert.Equal(2U, bar2Reflection.ParameterCount);

        resolvedFunctionReflection = bar2Reflection.SpecializeWithArgTypes([floatType, float3Type]);

        Assert.Equal(2U, resolvedFunctionReflection.ParameterCount);
        Assert.Equal("float", resolvedFunctionReflection.GetParameterByIndex(0).Type.FullName);
        Assert.Equal("vector<float,3>", resolvedFunctionReflection.GetParameterByIndex(1).Type.FullName);

        var float2Type = module.GetLayout().FindTypeByName("float2");


        // failure case
        try
        {
            resolvedFunctionReflection = bar2Reflection.SpecializeWithArgTypes([floatType, float2Type]);
            Assert.Fail();
        }
        catch
        {

        }


        // bar3 (float3) . int
        // (trivial case)
        var bar3Reflection = module.GetLayout().FindFunctionByName("bar3");
        Assert.False(bar3Reflection.IsOverloaded);
        Assert.Equal(1U, bar3Reflection.ParameterCount);

        resolvedFunctionReflection = bar3Reflection.SpecializeWithArgTypes([float3Type]);
        Assert.Equal(resolvedFunctionReflection, bar3Reflection);


        // GitHub issue #6317: bar2 is a function, not a type, so it should not be found.
        try
        {
            module.GetLayout().FindTypeByName("bar4");
            Assert.Fail();
        }
        catch
        {

        }


        var fooType = module.GetLayout().FindTypeByName("Foo");
        var ctor = module.GetLayout().FindFunctionByNameInType(fooType, "$init");
    }
}
