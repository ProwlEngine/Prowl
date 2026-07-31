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
    public void ResolveType_IsRepeatable()
    {
        string aqn = typeof(RuntimeUtils).AssemblyQualifiedName!;

        Assert.Equal(typeof(RuntimeUtils), RuntimeUtils.ResolveType(aqn)); // resolves + caches
        Assert.Equal(typeof(RuntimeUtils), RuntimeUtils.ResolveType(aqn)); // cache hit, same result
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

    // A hot reload leaves the outgoing script assembly loaded under the same simple name as the incoming one,
    // and the domain lists it first. Resolving a component's persisted $type has to reach the current build,
    // or the scene comes back running the code the user just replaced.
    [Fact]
    public void FindType_TwoBuildsOfOneAssembly_ResolvesAgainstTheLiveOne()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return;

        Type stale = DefineNamedType("HotReloadDuplicate", "Comp");
        Type live = DefineNamedType("HotReloadDuplicate", "Comp");
        Assert.NotSame(stale, live);

        var previous = RuntimeUtils.AssemblySource;
        try
        {
            // Live first, exactly as the editor orders it. The stale build is still enumerable.
            RuntimeUtils.AssemblySource = () => new[] { live.Assembly, stale.Assembly };
            Assert.Same(live, RuntimeUtils.FindType("Comp, HotReloadDuplicate"));

            // Load order alone must not decide it, which is the bug: the domain would hand back the stale one.
            RuntimeUtils.AssemblySource = () => new[] { stale.Assembly, live.Assembly };
            Assert.Same(stale, RuntimeUtils.FindType("Comp, HotReloadDuplicate"));
        }
        finally
        {
            RuntimeUtils.AssemblySource = previous;
        }
    }

    private static Type DefineNamedType(string assemblyName, string typeName)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule(assemblyName);
        return module.DefineType(typeName, TypeAttributes.Public).CreateType()!;
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
