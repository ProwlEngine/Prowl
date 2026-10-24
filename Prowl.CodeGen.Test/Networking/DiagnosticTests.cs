// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.CodeGen.Test.Networking;

/// <summary>Networking a script can't do is a compile error that says what's wrong, never a surprise at run time.</summary>
public class DiagnosticTests
{
    [Theory]
    // Shapes
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync] public int X { get => 1; set { } } }",
        "PROWLNET001")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync] public static int X { get; set; } }",
        "PROWLNET001")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Rpc.Broadcast] public int F() => 1; }",
        "PROWLNET002")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Rpc.Host] public void F(ref int x) { } }",
        "PROWLNET002")]
    [InlineData("public struct A { [Prowl.Runtime.Networking.Attributes.Sync] public int X { get; set; } }", "PROWLNET003")]
    [InlineData("public class A { [Prowl.Runtime.Networking.Attributes.Sync] public int X { get; set; } }", "PROWLNET003")]
    // Values
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Rpc.Broadcast] public void F(int[] x) { } }",
        "PROWLNET005")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync] public System.Collections.Generic.List<int> X { get; set; } }",
        "PROWLNET005")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync] public Prowl.Runtime.Networking.Collections.NetList<int[]> X { get; } = new(); }",
        "PROWLNET005")]
    [InlineData(
        "public struct S { public int[] A; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync] public S X { get; set; } }",
        "PROWLNET005")]
    [InlineData(
        "public class C { public C(int x) { } public int A; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Rpc.Broadcast] public void F(C c) { } }",
        "PROWLNET005")]
    [InlineData(
        "public struct S<T> { public T A; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync] public S<int> X { get; set; } }",
        "PROWLNET005")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { private struct S { public int A; } [Prowl.Runtime.Networking.Attributes.Sync] private S X { get; set; } }",
        "PROWLNET005")]
    [InlineData(
        "public struct S { private int _a; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync] public Prowl.Runtime.Networking.Collections.NetList<S> X { get; } = new(); }",
        "PROWLNET005")]
    [InlineData(
        "public abstract class B { public int A; } public class C : B { public System.Action F; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync] public B X { get; set; } }",
        "PROWLNET005")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync] public object X { get; set; } }",
        "PROWLNET005")]
    // Change hooks
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync, Prowl.Runtime.Networking.Attributes.Change(\"F\")] public int X { get; set; } void F(string a, string b) { } }",
        "PROWLNET006")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync, Prowl.Runtime.Networking.Attributes.Change(\"Missing\")] public int X { get; set; } }",
        "PROWLNET006")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync, Prowl.Runtime.Networking.Attributes.Change(\"F\")] public Prowl.Runtime.Networking.Collections.NetList<int> X { get; } = new(); void F() { } }",
        "PROWLNET006")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Change(nameof(F))] public int X { get; set; } void F() { } }",
        "PROWLNET006")]
    // Collections
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync] public int X { get; } = 1; }",
        "PROWLNET007")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync] public Prowl.Runtime.Networking.Collections.NetList<int> X { get; set; } }",
        "PROWLNET007")]
    public void UnsupportedNetworkingIsACompileError(string script, string id) =>
        Assert.Contains(Scripts.Build(script).Errors, e => e.Id == id);

    [Fact]
    public void AChangeHookCanBeNamedByAConstant() => Scripts.Compile(
        "public class A : Prowl.Runtime.Component { const string Name = \"F\"; [Prowl.Runtime.Networking.Attributes.Sync, Prowl.Runtime.Networking.Attributes.Change(Name)] public int X { get; set; } void F() { } }");
}
