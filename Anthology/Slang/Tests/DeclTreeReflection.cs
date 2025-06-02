namespace Prowl.Slang.Test;


public class DeclTreeReflection
{
    // Test that the reflection API provides correct info about entry point and ordinary functions.

    [Fact]
    public void DeclTreeReflectionTest()
    {
        // Source for a module that contains an undecorated entrypoint.
        string userSourceBody =
"""
[__AttributeUsage(_AttributeTargets.Function)]
struct MyFuncPropertyAttribute
{
    int v;
}

[MyFuncProperty(1024)]
[Differentiable]
float ordinaryFunc(no_diff float x, int y) { return x + y; }

float4 fragMain(float4 pos:SV_Position) : SV_Position
{
    return pos;
}

uint f(uint y) { return y; }

struct MyType
{
    int x;
    float f(float x) { return x; }
}

struct MyGenericType<T : IArithmetic & IFloat>
{
    T z;

    __init(T _z) { z = _z; }

    T g() { return z; }
    U h<U>(U x, out T y) { y = z; return x; }

    T j<let N : int>(T x, out int o) { o = N; return x; }

    U q<U>(U x, T y) { return x; }
}

namespace MyNamespace
{
    struct MyStruct
    {
        int x;
    }
}

T foo<T, U>(T t, U u) { return t; }

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

        Module module = session.LoadModuleFromSourceString(
            "m",
            "m.slang",
            userSourceBody,
            out _);

        EntryPoint entryPoint = module.FindAndCheckEntryPoint(
            "fragMain",
            ShaderStage.Fragment,
            out _);

        ComponentType compositeProgram = session.CreateCompositeComponentType(
            [module, entryPoint],
            out _);

        DeclReflection moduleDeclReflection = module.GetModuleReflection();

        Assert.Equal(DeclKind.Module, moduleDeclReflection.Kind);
        Assert.Equal(9U, moduleDeclReflection.ChildrenCount);

        // First declaration should be a struct with 1 variable and a synthesized constructor
        DeclReflection firstDecl = moduleDeclReflection.GetChild(0);

        Assert.Equal(DeclKind.Struct, firstDecl.Kind);
        Assert.Equal(2U, firstDecl.ChildrenCount);

        {
            TypeReflection type = firstDecl.Type;

            Assert.Equal("MyFuncPropertyAttribute", type.FullName);

            // Check the field of the struct.
            Assert.Equal(1U, type.FieldCount);

            VariableReflection field = type.GetFieldByIndex(0);
            Assert.Equal("v", field.Name);
            Assert.Equal("int", field.Type.FullName);
        }

        // Second declaration should be a function
        DeclReflection secondDecl = moduleDeclReflection.GetChild(1);

        Assert.Equal(DeclKind.Func, secondDecl.Kind);
        Assert.Equal(2U, secondDecl.ChildrenCount);

        {
            FunctionReflection funcReflection = secondDecl.AsFunction();

            Assert.True(funcReflection.HasModifier(ModifierID.Differentiable));
            Assert.Equal("float", funcReflection.ReturnType.FullName);
            Assert.Equal("ordinaryFunc", funcReflection.Name);
            Assert.Equal(2U, funcReflection.ParameterCount);
            Assert.Equal("x", funcReflection.GetParameterByIndex(0).Name);
            Assert.Equal("float", funcReflection.GetParameterByIndex(0).Type.FullName);
            Assert.True(funcReflection.GetParameterByIndex(0).HasModifier(ModifierID.NoDiff));

            Assert.Equal("y", funcReflection.GetParameterByIndex(1).Name);
            Assert.Equal("int", funcReflection.GetParameterByIndex(1).Type.FullName);

            Assert.Equal(1U, funcReflection.UserAttributeCount);
            Attribute userAttribute = funcReflection.GetUserAttributeByIndex(0);
            Assert.Equal("MyFuncProperty", userAttribute.Name);
            Assert.Equal(1U, userAttribute.ArgumentCount);
            Assert.Equal("int", userAttribute.GetArgumentType(0).FullName);

            Assert.Equal(1024, userAttribute.GetArgumentValueInt(0));
            Assert.Equal(userAttribute, funcReflection.FindAttributeByName("MyFuncProperty"));
        }

        // Third declaration should also be a function
        DeclReflection thirdDecl = moduleDeclReflection.GetChild(2);
        Assert.Equal(DeclKind.Func, thirdDecl.Kind);
        Assert.Equal(1U, thirdDecl.ChildrenCount);

        {
            FunctionReflection funcReflection = thirdDecl.AsFunction();
            Assert.Equal("vector<float,4>", funcReflection.ReturnType.FullName);
            Assert.Equal("fragMain", funcReflection.Name);
            Assert.Equal(1U, funcReflection.ParameterCount);
            Assert.Equal("pos", funcReflection.GetParameterByIndex(0).Name);
            Assert.Equal("vector<float,4>", funcReflection.GetParameterByIndex(0).Type.FullName);
        }

        // Sixth declaration should be a generic struct
        DeclReflection sixthDecl = moduleDeclReflection.GetChild(5);
        Assert.Equal(DeclKind.Generic, sixthDecl.Kind);

        GenericReflection genericReflection = sixthDecl.AsGeneric();
        Assert.Equal(1U, genericReflection.TypeParameterCount);

        VariableReflection typeParamT = genericReflection.GetTypeParameter(0);
        Assert.Equal("T", typeParamT.Name);

        uint typeParamTConstraintCount = genericReflection.GetTypeParameterConstraintCount(typeParamT);
        Assert.Equal(2U, typeParamTConstraintCount);

        TypeReflection typeParamTConstraintType1 = genericReflection.GetTypeParameterConstraintType(typeParamT, 1);
        Assert.Equal("IFloat", typeParamTConstraintType1.FullName);

        TypeReflection typeParamTConstraintType2 = genericReflection.GetTypeParameterConstraintType(typeParamT, 0);
        Assert.Equal("IArithmetic", typeParamTConstraintType2.FullName);

        DeclReflection innerStruct = genericReflection.InnerDecl;
        Assert.Equal(DeclKind.Struct, innerStruct.Kind);

        // Check that the seventh declaration is a namespace
        DeclReflection seventhDecl = moduleDeclReflection.GetChild(6);

        Assert.Equal(DeclKind.Namespace, seventhDecl.Kind);
        Assert.Equal("MyNamespace", seventhDecl.Name);

        // Check type-lookup-by-name
        {
            TypeReflection type = compositeProgram.GetLayout().FindTypeByName("MyType");
            Assert.Equal("MyType", type.Name);

            FunctionReflection funcReflection = compositeProgram.GetLayout().FindFunctionByNameInType(type, "f");
            Assert.Equal("f", funcReflection.Name);
            Assert.Equal("float", funcReflection.ReturnType.FullName);
            Assert.Equal(1U, funcReflection.ParameterCount);
            Assert.Equal("x", funcReflection.GetParameterByIndex(0).Name);
            Assert.Equal("float", funcReflection.GetParameterByIndex(0).Type.FullName);
        }

        // Check type-lookup-by-name for generic type
        {
            TypeReflection type = compositeProgram.GetLayout().FindTypeByName("MyGenericType<half>");
            Assert.Equal("MyGenericType<half>", type.FullName);

            FunctionReflection funcReflection = compositeProgram.GetLayout().FindFunctionByNameInType(type, "g");
            Assert.Equal("g", funcReflection.Name);
            Assert.Equal("half", funcReflection.ReturnType.FullName);
            Assert.Equal(0U, funcReflection.ParameterCount);

            VariableReflection varReflection = compositeProgram.GetLayout().FindVarByNameInType(type, "z");
            Assert.Equal("z", varReflection.Name);
            Assert.Equal("half", varReflection.Type.FullName);

            funcReflection = compositeProgram.GetLayout().FindFunctionByNameInType(type, "h<float>");
            Assert.Equal("h", funcReflection.Name);
            Assert.Equal("float", funcReflection.ReturnType.FullName);
            Assert.Equal(2U, funcReflection.ParameterCount);
            Assert.Equal("x", funcReflection.GetParameterByIndex(0).Name);
            Assert.Equal("float", funcReflection.GetParameterByIndex(0).Type.FullName);
            Assert.Equal("y", funcReflection.GetParameterByIndex(1).Name);
            Assert.Equal("half", funcReflection.GetParameterByIndex(1).Type.FullName);

            // Access parent generic container from a specialized method.
            GenericReflection specializationInfo = funcReflection.GenericContainer;
            Assert.Equal("h", specializationInfo.Name);
            Assert.Equal(DeclKind.Generic, specializationInfo.AsDecl().Kind);
            // Check type parameters
            Assert.Equal(1U, specializationInfo.TypeParameterCount);

            VariableReflection typeParam = specializationInfo.GetTypeParameter(0);
            Assert.Equal("U", typeParam.Name); // generic name
            Assert.Equal("float", specializationInfo.GetConcreteType(typeParam).FullName); // specialized type name under the context in which the generic is obtained
            Assert.Equal(0U, specializationInfo.GetTypeParameterConstraintCount(typeParam));

            // Go up another level to the generic struct
            specializationInfo = specializationInfo.OuterGenericContainer;
            Assert.Equal("MyGenericType", specializationInfo.Name);
            Assert.Equal(DeclKind.Generic, specializationInfo.AsDecl().Kind);
            // Check type parameters
            Assert.Equal(1U, specializationInfo.TypeParameterCount);
            typeParam = specializationInfo.GetTypeParameter(0);
            Assert.Equal("T", typeParam.Name); // generic name
            Assert.Equal("half", specializationInfo.GetConcreteType(typeParam).FullName); // specialized type name under the context in which the generic is obtained
            Assert.Equal(2U, specializationInfo.GetTypeParameterConstraintCount(typeParam));

            // Query 'j' on the type 'half'
            funcReflection = compositeProgram.GetLayout().FindFunctionByNameInType(type, "j<10>");
            Assert.Equal("j", funcReflection.Name);

            // Check the generic parameters
            specializationInfo = funcReflection.GenericContainer;
            Assert.Equal("j", specializationInfo.Name);
            Assert.Equal(DeclKind.Generic, specializationInfo.AsDecl().Kind);
            Assert.Equal(1U, specializationInfo.ValueParameterCount);

            VariableReflection valueParam = specializationInfo.GetValueParameter(0);
            Assert.Equal("N", valueParam.Name); // generic name
            Assert.Equal(10U, specializationInfo.GetConcreteIntVal(valueParam));
        }

        // Check specializeGeneric() and applySpecializations()
        {
            TypeReflection unspecializedType = compositeProgram.GetLayout().FindTypeByName("MyGenericType");
            TypeReflection halfType = compositeProgram.GetLayout().FindTypeByName("half");

            GenericReflection genericContainer = unspecializedType.GenericContainer;
            // TypeReflection typeParamT = genericContainer.GetTypeParameter(0);

            GenericReflection specializedContainer = compositeProgram.GetLayout().SpecializeGeneric(
                genericContainer,
                [halfType], out _);

            TypeReflection specializedType = unspecializedType.ApplySpecializations(specializedContainer);
            Assert.Equal("MyGenericType<half>", specializedType.FullName);
        }

        // Check specializeGeneric() and applySpecializations() on multiple levels (generic function
        // nested in a generic struct)
        {
            TypeReflection unspecializedType = compositeProgram.GetLayout().FindTypeByName("MyGenericType");
            FunctionReflection unspecializedFunc =
                compositeProgram.GetLayout().FindFunctionByNameInType(unspecializedType, "j");

            TypeReflection halfType = compositeProgram.GetLayout().FindTypeByName("half");

            GenericReflection genericFuncContainer = unspecializedFunc.GenericContainer;
            GenericReflection genericStructContainer = genericFuncContainer.OuterGenericContainer;

            GenericReflection specializedStructContainer = compositeProgram.GetLayout().SpecializeGeneric(
                genericStructContainer,
                [halfType], out _);

            // apply T=half. N is still left unspecialized.
            genericFuncContainer = genericFuncContainer.ApplySpecializations(specializedStructContainer);

            GenericReflection specializedFuncContainer = compositeProgram.GetLayout().SpecializeGeneric(
                genericFuncContainer,
                [10], out _);

            FunctionReflection specializedFunc = unspecializedFunc.ApplySpecializations(specializedFuncContainer);

            // ------ check the specialized function
            GenericReflection specializationInfo = specializedFunc.GenericContainer;
            Assert.Equal("j", specializationInfo.Name);
            Assert.Equal(DeclKind.Generic, specializationInfo.AsDecl().Kind);
            Assert.Equal(1U, specializationInfo.ValueParameterCount);

            VariableReflection valueParam = specializationInfo.GetValueParameter(0);
            Assert.Equal("N", valueParam.Name); // generic name
            Assert.Equal(10, specializationInfo.GetConcreteIntVal(valueParam));

            // check outer container
            specializationInfo = specializationInfo.OuterGenericContainer;
            Assert.Equal("MyGenericType", specializationInfo.Name);
            Assert.Equal(DeclKind.Generic, specializationInfo.AsDecl().Kind);

            // Check type parameters
            Assert.Equal(1U, specializationInfo.TypeParameterCount);
            VariableReflection typeParam = specializationInfo.GetTypeParameter(0);
            Assert.Equal("T", typeParam.Name); // generic name
            Assert.Equal("half", specializationInfo.GetConcreteType(typeParam).FullName);
        }

        // Check sub-type relations
        {
            TypeReflection floatType = compositeProgram.GetLayout().FindTypeByName("float");
            TypeReflection diffType = compositeProgram.GetLayout().FindTypeByName("IDifferentiable");

            Assert.True(compositeProgram.GetLayout().IsSubType(floatType, diffType));

            TypeReflection uintType = compositeProgram.GetLayout().FindTypeByName("uint");
            Assert.False(compositeProgram.GetLayout().IsSubType(uintType, diffType));
        }

        // Check specializeWithArgTypes()
        {
            FunctionReflection unspecializedFoo = compositeProgram.GetLayout().FindFunctionByName("foo");

            TypeReflection floatType = compositeProgram.GetLayout().FindTypeByName("float");
            TypeReflection uintType = compositeProgram.GetLayout().FindTypeByName("uint");

            FunctionReflection specializedFoo = unspecializedFoo.SpecializeWithArgTypes([floatType, uintType]);

            Assert.Equal("float", specializedFoo.ReturnType.FullName);
            Assert.Equal(2U, specializedFoo.ParameterCount);

            Assert.Equal("t", specializedFoo.GetParameterByIndex(0).Name);
            Assert.Equal("float", specializedFoo.GetParameterByIndex(0).Type.FullName);

            Assert.Equal("u", specializedFoo.GetParameterByIndex(1).Name);
            Assert.Equal("uint", specializedFoo.GetParameterByIndex(1).Type.FullName);
        }

        // Check specializeArgTypes on member method looked up through a specialized type
        {
            TypeReflection specializedType = compositeProgram.GetLayout().FindTypeByName("MyGenericType<half>");

            FunctionReflection unspecializedMethod = compositeProgram.GetLayout().FindFunctionByNameInType(specializedType, "h");

            // Specialize the method with float
            TypeReflection floatType = compositeProgram.GetLayout().FindTypeByName("float");
            TypeReflection halfType = compositeProgram.GetLayout().FindTypeByName("half");

            FunctionReflection specializedMethodWithFloat = unspecializedMethod.SpecializeWithArgTypes([floatType, halfType]);
            Assert.Equal("float", specializedMethodWithFloat.ReturnType.FullName);
        }

        // Check getTypeFullName() on nested objects.
        {
            TypeReflection structType = compositeProgram.GetLayout().FindTypeByName("MyNamespace::MyStruct");
            Assert.Equal("MyNamespace.MyStruct", structType.FullName);
        }

        // Check iterators
        {
            int count = moduleDeclReflection.Children.Count();
            Assert.Equal(9, count);

            count = moduleDeclReflection.GetChildrenOfKind(DeclKind.Func).Count();
            Assert.Equal(3, count);

            count = moduleDeclReflection.GetChildrenOfKind(DeclKind.Struct).Count();
            Assert.Equal(2, count);

            count = moduleDeclReflection.GetChildrenOfKind(DeclKind.Generic).Count();
            Assert.Equal(2, count);

            count = moduleDeclReflection.GetChildrenOfKind(DeclKind.Namespace).Count();
            Assert.Equal(1, count);
        }
    }
}
