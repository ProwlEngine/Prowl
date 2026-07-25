// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Echo.Test.Collisions
{
    // A type whose simple name deliberately collides with Prowl.Echo.EchoObject, but lives in this test
    // assembly. Type resolution must tell the two apart by the assembly recorded in the name.
    public sealed class EchoObject { }
}

namespace Prowl.Echo.Test
{
    /// <summary>
    /// A serialized type name records which assembly declared the type. When two loaded assemblies each
    /// declare a type with the same simple name, resolution must honor the recorded assembly instead of
    /// binding to whichever same-named type happens to be enumerated first.
    /// </summary>
    public class TypeResolutionCollision_Tests
    {
        private static readonly string ThisAssembly = typeof(TypeResolutionCollision_Tests).Assembly.GetName().Name!;
        private static readonly string EchoAssembly = typeof(EchoObject).Assembly.GetName().Name!;

        public TypeResolutionCollision_Tests() => Serializer.ClearCache();

        [Fact]
        public void ResolveFullTypeName_HonorsRecordedAssembly_ForCollidingSimpleName()
        {
            // "EchoObject" exists in both this test assembly (Collisions.EchoObject) and Prowl.Echo.
            // Each query must resolve within the assembly its name records, not by enumeration order.
            Type? mine = TypeNameRegistry.ResolveFullTypeName($"EchoObject, {ThisAssembly}");
            Type? echos = TypeNameRegistry.ResolveFullTypeName($"EchoObject, {EchoAssembly}");

            Assert.Equal(typeof(Collisions.EchoObject), mine);
            Assert.Equal(typeof(EchoObject), echos);
            Assert.NotEqual(mine, echos);
        }

        [Fact]
        public void ResolveFullTypeName_NamespaceQualifiedNames_StillResolve()
        {
            Assert.Equal(typeof(Collisions.EchoObject),
                TypeNameRegistry.ResolveFullTypeName($"{typeof(Collisions.EchoObject).FullName}, {ThisAssembly}"));
            Assert.Equal(typeof(EchoObject),
                TypeNameRegistry.ResolveFullTypeName($"{typeof(EchoObject).FullName}, {EchoAssembly}"));
        }

        [Fact]
        public void ResolveFullTypeName_UnknownAssembly_FallsBackToLooseSimpleNameMatch()
        {
            // When the recorded assembly isn't loaded, resolution still falls back to a cross-assembly
            // simple-name match so renamed/moved assemblies keep working - it must find SOME "EchoObject".
            Type? t = TypeNameRegistry.ResolveFullTypeName("EchoObject, Some.Unloaded.Assembly");
            Assert.True(t == typeof(Collisions.EchoObject) || t == typeof(EchoObject));
        }
    }
}
