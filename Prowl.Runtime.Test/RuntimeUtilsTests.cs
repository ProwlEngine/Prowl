// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Tests for <see cref="RuntimeUtils.ResolveType"/> assembly-qualified name binding.</summary>
public class RuntimeUtilsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveType_NullOrBlank_ReturnsNull(string? name)
    {
        Assert.Null(RuntimeUtils.ResolveType(name!));
    }

    [Fact]
    public void ResolveType_DefaultContextType_RoundTrips()
    {
        Assert.Equal(typeof(RuntimeUtils), RuntimeUtils.ResolveType(typeof(RuntimeUtils).AssemblyQualifiedName!));
        Assert.Equal(typeof(int), RuntimeUtils.ResolveType(typeof(int).AssemblyQualifiedName!));
        Assert.Equal(typeof(List<string>), RuntimeUtils.ResolveType(typeof(List<string>).AssemblyQualifiedName!));
    }

    [Fact]
    public void ResolveType_WithoutAssemblyQualifier_ResolvesByFullName()
    {
        Assert.Equal(typeof(int), RuntimeUtils.ResolveType("System.Int32"));
        Assert.Equal(typeof(RuntimeUtils), RuntimeUtils.ResolveType("Prowl.Runtime.RuntimeUtils"));
    }

    // These names reach the editor from a persisted asset database, so a corrupt or truncated entry
    // must degrade to "unresolved" rather than throw out of a property getter inside a draw loop.
    [Theory]
    [InlineData("Foo,")]                                                                    // empty assembly name
    [InlineData("Foo, ==")]                                                                 // invalid assembly name
    [InlineData("Prowl.Runtime.RuntimeUtils[")]                                             // truncated
    [InlineData("Ns.T, Missing.Asm, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")] // assembly not loaded
    public void ResolveType_MalformedOrUnloadable_ReturnsNullWithoutThrowing(string name)
    {
        Assert.Null(RuntimeUtils.ResolveType(name));
    }

    // Must not degrade into a loose simple-name search: the assembly recorded in the name is honored,
    // so two assemblies declaring the same type name can never be confused for one another.
    [Fact]
    public void ResolveType_TypeNameQualifiedWithWrongAssembly_ReturnsNull()
    {
        string wrong = $"Prowl.Runtime.RuntimeUtils, {typeof(int).Assembly.GetName().Name}";
        Assert.Null(RuntimeUtils.ResolveType(wrong));
    }

    [Fact]
    public void ResolveType_SurvivesCacheClear()
    {
        string aqn = typeof(RuntimeUtils).AssemblyQualifiedName!;

        Assert.Equal(typeof(RuntimeUtils), RuntimeUtils.ResolveType(aqn));
        RuntimeUtils.ClearCache();
        Assert.Equal(typeof(RuntimeUtils), RuntimeUtils.ResolveType(aqn));
    }

    // The reason ResolveType exists: user scripts (and their EngineObject/asset types) live in a
    // separate, collectible load context, which Type.GetType is not permitted to bind into. A dynamic
    // assembly stands in for that context here - it is reachable by reflection but not by Type.GetType.
    [Fact]
    public void ResolveType_TypeOutsideDefaultContext_ResolvesWhereTypeGetTypeCannot()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return;

        Type outOfContext = DefineOutOfContextType();
        string aqn = outOfContext.AssemblyQualifiedName!;

        Assert.Null(Type.GetType(aqn, throwOnError: false));
        Assert.Same(outOfContext, RuntimeUtils.ResolveType(aqn));
    }

    // Generic arguments and array element types carry their own assembly qualifiers, so they have to
    // be bound out-of-context too - a naive "split on the first comma" never gets this right.
    [Fact]
    public void ResolveType_GenericArgumentAndArrayOfOutOfContextType_Resolve()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return;

        Type outOfContext = DefineOutOfContextType();

        Type list = typeof(List<>).MakeGenericType(outOfContext);
        Type resolvedList = RuntimeUtils.ResolveType(list.AssemblyQualifiedName!)!;
        Assert.NotNull(resolvedList);
        Assert.Same(outOfContext, resolvedList.GetGenericArguments()[0]);

        Type array = outOfContext.MakeArrayType();
        Type resolvedArray = RuntimeUtils.ResolveType(array.AssemblyQualifiedName!)!;
        Assert.NotNull(resolvedArray);
        Assert.Same(outOfContext, resolvedArray.GetElementType());
    }

    private static Type? s_outOfContextType;

    private static Type DefineOutOfContextType()
    {
        // Cached: an assembly name can only be defined once per process.
        if (s_outOfContextType != null)
            return s_outOfContextType;

        AssemblyBuilder asm = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Prowl.Runtime.Test.OutOfContext"), AssemblyBuilderAccess.Run);
        ModuleBuilder module = asm.DefineDynamicModule("Main");
        TypeBuilder type = module.DefineType("Prowl.Runtime.Test.OutOfContextAsset", TypeAttributes.Public | TypeAttributes.Class);

        return s_outOfContextType = type.CreateType();
    }
}
