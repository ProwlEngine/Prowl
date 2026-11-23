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
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public int X { get => 1; set { } } }",
        "PROWLNET001")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public static int X { get; set; } }",
        "PROWLNET001")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Broadcast] public int F() => 1; }",
        "PROWLNET002")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Host] public void F(ref int x) { } }",
        "PROWLNET002")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Broadcast] public async void F() { await System.Threading.Tasks.Task.Yield(); } }",
        "PROWLNET002")]
    [InlineData("public struct A { [Prowl.Runtime.Networking.Sync] public int X { get; set; } }", "PROWLNET003")]
    [InlineData("public class A { [Prowl.Runtime.Networking.Sync] public int X { get; set; } }", "PROWLNET003")]
    // Values
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Broadcast] public void F(int[,] x) { } }",
        "PROWLNET005")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public System.Collections.Generic.Dictionary<int, int> X { get; set; } }",
        "PROWLNET005")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public Prowl.Runtime.Networking.NetList<int[,]> X { get; } = new(); }",
        "PROWLNET005")]
    [InlineData(
        "public struct S { public int[,] A; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public S X { get; set; } }",
        "PROWLNET005")]
    [InlineData(
        "public class C { public C(int x) { } public int A; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Broadcast] public void F(C c) { } }",
        "PROWLNET005")]
    [InlineData(
        "public struct S<T> { public T A; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public S<int> X { get; set; } }",
        "PROWLNET005")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { private struct S { public int A; } [Prowl.Runtime.Networking.Sync] private S X { get; set; } }",
        "PROWLNET005")]
    [InlineData(
        "public struct S { private int _a; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public Prowl.Runtime.Networking.NetList<S> X { get; } = new(); }",
        "PROWLNET005")]
    [InlineData(
        "public abstract class B { public int A; } public class C : B { public System.Action F; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public B X { get; set; } }",
        "PROWLNET005")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public object X { get; set; } }",
        "PROWLNET005")]
    // Change hooks
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync, Prowl.Runtime.Networking.Change(\"F\")] public int X { get; set; } void F(string a, string b) { } }",
        "PROWLNET006")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync, Prowl.Runtime.Networking.Change(\"Missing\")] public int X { get; set; } }",
        "PROWLNET006")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync, Prowl.Runtime.Networking.Change(\"F\")] public Prowl.Runtime.Networking.NetList<int> X { get; } = new(); void F() { } }",
        "PROWLNET006")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Change(nameof(F))] public int X { get; set; } void F() { } }",
        "PROWLNET006")]
    // Collections
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public int X { get; } = 1; }",
        "PROWLNET007")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public Prowl.Runtime.Networking.NetList<int> X { get; set; } }",
        "PROWLNET007")]
    // Overrides
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Broadcast] public virtual void F() { } } public class B : A { public override void F() { } }",
        "PROWLNET010")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Host] public virtual void F() { } } public class B : A { [System.Obsolete] public override void F() { } }",
        "PROWLNET010")]
    [InlineData(
        "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public virtual int X { get; set; } } public class B : A { public override int X { get; set; } }",
        "PROWLNET010")]
    public void UnsupportedNetworkingIsACompileError(string script, string id) =>
        Assert.Contains(Scripts.Build(script).Errors, e => e.Id == id);

    private static string SyncProperties(int count, string prefix = "V") =>
        string.Concat(Enumerable.Range(0, count)
            .Select(i => $"[Prowl.Runtime.Networking.Sync] public int {prefix}{i} {{ get; set; }} "));

    [Fact]
    public void AComponentHoldsAtMost64SyncValuesCountingItsBaseTypes()
    {
        string within = $"public class A : Prowl.Runtime.Component {{ {SyncProperties(64)} }}";
        Assert.DoesNotContain(Scripts.Build(within).Errors, e => e.Id == "PROWLNET012");

        string over = $"public class A : Prowl.Runtime.Component {{ {SyncProperties(65)} }}";
        Assert.Contains(Scripts.Build(over).Errors, e => e.Id == "PROWLNET012");

        string inherited = $"public class A : Prowl.Runtime.Component {{ {SyncProperties(40)} }} " +
                           $"public class B : A {{ {SyncProperties(25, "W")} }}";
        Assert.Contains(Scripts.Build(inherited).Errors, e => e.Id == "PROWLNET012");
    }

    [Fact]
    public void AnOverrideInAFileWithoutNetworkingStillNeedsTheRpcAttribute() =>
        Assert.Contains(Scripts.Build(
            "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Broadcast] public virtual void F() { } }",
            "public class B : A { public override void F() { } }").Errors, e => e.Id == "PROWLNET010");

    [Fact]
    public void AFlagThatDoesNothingOnItsRpcIsAWarning()
    {
        const string script = """
                              using Prowl.Runtime.Networking;
                              public class A : Prowl.Runtime.Component
                              {
                                  [Rpc.Broadcast(RpcFlags.AnyCaller)] public void Shout() { }
                                  [Rpc.Host(RpcFlags.ExcludeOwner)] public void Ask() { }
                                  [Rpc.Owner(RpcFlags.HostOnly)] public void Tell() { }
                                  [Rpc.Host(RpcFlags.AnyCaller | RpcFlags.HostOnly)] public void Fine() { }
                                  [Rpc.Broadcast(RpcFlags.ExcludeOwner | RpcFlags.Unreliable)] public void AlsoFine() { }
                              }
                              """;
        string[] warnings = [.. Scripts.Warnings(script).Where(w => w.Id == "PROWLNET014").Select(w => w.GetMessage())];
        Assert.Equal(3, warnings.Length);
        Assert.Contains(warnings, w => w.Contains("AnyCaller") && w.Contains("'Shout'") && w.Contains("host RPC"));
        Assert.Contains(warnings, w => w.Contains("ExcludeOwner") && w.Contains("'Ask'"));
        Assert.Contains(warnings, w => w.Contains("HostOnly") && w.Contains("'Tell'"));
        Assert.NotNull(Scripts.Compile(script));
    }

    [Fact]
    public void AStaticRpcIsAnError() => Assert.Contains(Scripts.Build(
        "public class S : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Broadcast] public static void F() { } }").Errors,
        e => e.Id == "PROWLNET008");

    [Fact]
    public void AChannelOnAnUnreliableRpcIsAWarning()
    {
        const string script = """
                              using Prowl.Runtime.Networking;
                              public class A : Prowl.Runtime.Component
                              {
                                  [Rpc.Broadcast(RpcFlags.Unreliable, Channel = 1)] public void Loose() { }
                                  [Rpc.Broadcast(Channel = 1)] public void Ordered() { }
                              }
                              """;
        Assert.Contains("Loose", Assert.Single(Scripts.Warnings(script), w => w.Id == "PROWLNET009").GetMessage());
        Assert.NotNull(Scripts.Compile(script));
    }

    [Fact]
    public void AChangeHookCanBeNamedByAConstant() => Scripts.Compile(
        "public class A : Prowl.Runtime.Component { const string Name = \"F\"; [Prowl.Runtime.Networking.Sync, Prowl.Runtime.Networking.Change(Name)] public int X { get; set; } void F() { } }");
}
