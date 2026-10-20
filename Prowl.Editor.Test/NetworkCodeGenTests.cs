// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Prowl.Editor.Projects.Scripting;
using Prowl.Runtime;
using Prowl.Warp;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The script rewrite behind [Sync] and RPCs: what it generates compiles and runs, it keeps every
/// line where it was, and it explains what it can't handle.
/// </summary>
[Trait("Category", "Build")]
public class NetworkCodeGenTests : EditorTestHarness
{
    private const string ShipScript = """
        using System.Collections.Generic;
        using Prowl.Runtime;
        using Prowl.Vector;

        public class Ship : Component
        {
            [Sync] public int Health { get; set; } = 100;

            [Sync(SyncFlags.FromHost)]
            public string Callsign
            {
                get;
                private set;
            } = "Ship " +
                "One"; // spans lines

            public List<string> Log = new();

            [Rpc.Broadcast]
            public void Fire(int power, Float3 at)
            {
                Log.Add($"fire {power} at {at.X}");
            }

            [Rpc.Host(RpcFlags.Unreliable)] public void Ping() => Log.Add("ping");

            [Rpc.Owner]
            public void Steer(float amount)
                =>
                Log.Add("steer"); // MARKER-STEER

            public void SetCallsign(string value) => Callsign = value;

            #region Nested
            public class Turret : Component
            {
                [Sync] public float Angle { get; set; }
            }
            #endregion
        }

        public class After { } // MARKER-AFTER
        """;

    private Assembly CompileGame()
    {
        ScriptCompiler.CompileResult result = ScriptCompiler.CompileAll(Project);
        Assert.True(result.Success, result.Errors);
        return Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
    }

    private static List<uint> Slots(Component component) => NetworkMembers.Of(component).Syncs.Select(s => s.Slot).ToList();

    [Fact]
    public void GeneratedCodeCompilesAndBehavesNormallyWithoutASession()
    {
        WriteScript("Ship.cs", ShipScript);
        Type shipType = CompileGame().GetType("Ship")!;

        GameObject go = new("Ship");
        try
        {
            object ship = go.AddComponent(shipType)!;
            PropertyInfo health = shipType.GetProperty("Health")!;
            Assert.Equal(100, health.GetValue(ship));
            health.SetValue(ship, 5);
            Assert.Equal(5, health.GetValue(ship));
            Assert.Equal("Ship One", shipType.GetProperty("Callsign")!.GetValue(ship));

            shipType.GetMethod("Fire")!.Invoke(ship, [3, new Prowl.Vector.Float3(1, 0, 0)]);
            shipType.GetMethod("Ping")!.Invoke(ship, []);
            Assert.Equal(new[] { "fire 3 at 1", "ping" }, (List<string>)shipType.GetField("Log")!.GetValue(ship)!);
        }
        finally
        {
            go.Dispose();
        }
    }

    [Fact]
    public void GeneratedMembersDescribeTheNetworkedMembers()
    {
        WriteScript("Ship.cs", ShipScript);
        Assembly game = CompileGame();
        Type shipType = game.GetType("Ship")!;

        GameObject go = new("Ship");
        try
        {
            Component ship = (Component)go.AddComponent(shipType)!;
            NetworkMembers members = NetworkMembers.Of(ship);

            Assert.Equal(new[] { NetworkCodeGen.NetworkId("Health"), NetworkCodeGen.NetworkId("Callsign") }, Slots(ship));
            SyncMember health = members.Sync(NetworkCodeGen.NetworkId("Health"))!;
            Assert.Equal("Health", health.Name);
            Assert.Equal(SyncFlags.FromHost, members.Sync(NetworkCodeGen.NetworkId("Callsign"))!.Flags);
            Assert.Null(members.Sync(0));
            Assert.Equal("100", health.FormatValue(ship));

            using NetWriter writer = new();
            health.Write(ship, writer);
            NetReader reader = new(writer.WrittenSpan);
            Assert.Equal(100, NetSerializer<int>.Read(ref reader));

            RpcMember ping = members.Rpc(NetworkCodeGen.NetworkId("Ping()"))!;
            Assert.Equal(RpcMode.Host, ping.Mode);
            Assert.Equal(RpcFlags.Unreliable, ping.Flags);

            RpcMember fire = members.Rpc(NetworkCodeGen.NetworkId("Fire(int,Float3)"))!;
            Assert.Equal("Fire(int,Float3)", fire.Signature);
            Assert.Equal(RpcMode.Broadcast, fire.Mode);

            // Called the way an incoming call is: arguments read, then the body runs.
            using NetWriter arguments = new();
            NetSerializer<int>.Write(arguments, 7);
            NetSerializer<Prowl.Vector.Float3>.Write(arguments, new Prowl.Vector.Float3(2, 0, 0));
            NetReader argumentReader = new(arguments.WrittenSpan);
            fire.Invoke(ship, ref argumentReader);
            Assert.Equal(new[] { "fire 7 at 2" }, (List<string>)shipType.GetField("Log")!.GetValue(ship)!);

            // A nested component gets its own.
            Component turret = (Component)go.AddComponent(game.GetType("Ship+Turret")!)!;
            Assert.Equal(new[] { NetworkCodeGen.NetworkId("Angle") }, Slots(turret));
        }
        finally
        {
            go.Dispose();
        }
    }

    [Fact]
    public void RewriteKeepsEveryLineWhereItWas()
    {
        SyntaxTree original = CSharpSyntaxTree.ParseText(ShipScript);
        SyntaxTree rewritten = NetworkCodeGen.Rewrite([original]).Trees[0];

        Assert.NotSame(original, rewritten);
        string[] before = original.GetText().ToString().Split('\n');
        string[] after = rewritten.GetText().ToString().Split('\n');
        Assert.Equal(before.Length, after.Length);

        // Lines the rewrite has no business with are untouched, and marked ones are still on their line.
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i].Contains("Log.Add($\"fire") || before[i].Contains("#region") || before[i].Contains("#endregion")
                || before[i].Contains("public List<string> Log") || before[i].Contains("MARKER-AFTER"))
                Assert.Equal(before[i], after[i]);
            if (before[i].Contains("MARKER"))
                Assert.Contains("MARKER", after[i]);

            // Statements in RPC bodies, block or expression bodied, stay on their line for errors and breakpoints.
            int statement = before[i].IndexOf("Log.Add(", StringComparison.Ordinal);
            if (statement >= 0)
                Assert.Contains(before[i][statement..before[i].IndexOf(';', statement)], after[i]);
        }
        Assert.Contains(after, line => line.Contains("__Sync_Set(ref this.__sync_Health"));
    }

    [Fact]
    public void FilesWithoutNetworkingAreLeftAlone()
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText("public class Plain { public int Value { get; set; } }");
        Assert.Same(tree, NetworkCodeGen.Rewrite([tree]).Trees[0]);
    }

    private const string ShipPartA = """
        using Prowl.Runtime;

        namespace Fleet;

        public partial class Ship : Component
        {
            [Sync] public int Health { get; set; } = 100;

            [Rpc.Broadcast] public void Fire(int power) => Fired += power;

            public int Fired;
        }
        """;

    private const string ShipPartB = """
        using Prowl.Runtime;

        namespace Fleet
        {
            public partial class Ship
            {
                [Sync(SyncFlags.FromHost)] public int Ammo { get; set; } = 30;

                [Rpc.Host] public void Reload() => Ammo = 30;
            }
        }
        """;

    [Fact]
    public void PartialClassesNetworkMembersFromEveryPart()
    {
        WriteScript("ShipA.cs", ShipPartA);
        WriteScript("ShipB.cs", ShipPartB);
        WriteScript("ShipC.cs", "namespace Fleet; public partial class Ship { public int Plain; }");
        Type shipType = CompileGame().GetType("Fleet.Ship")!;

        GameObject go = new("Ship");
        try
        {
            Component ship = (Component)go.AddComponent(shipType)!;
            Assert.Equal(new[] { NetworkCodeGen.NetworkId("Health"), NetworkCodeGen.NetworkId("Ammo") }, Slots(ship));
            Assert.Equal(30, shipType.GetProperty("Ammo")!.GetValue(ship));

            NetworkMembers members = NetworkMembers.Of(ship);
            Assert.Equal(RpcMode.Host, members.Rpc(NetworkCodeGen.NetworkId("Reload()"))!.Mode);
            Assert.Equal(SyncFlags.FromHost, members.Sync(NetworkCodeGen.NetworkId("Ammo"))!.Flags);
            Assert.NotNull(members.Rpc(NetworkCodeGen.NetworkId("Fire(int)")));

            // Offline, both parts' RPCs are plain calls.
            shipType.GetMethod("Fire")!.Invoke(ship, [2]);
            Assert.Equal(2, shipType.GetField("Fired")!.GetValue(ship));
        }
        finally
        {
            go.Dispose();
        }
    }

    [Fact]
    public void EditingOnePartUpdatesTheGeneratedMembersInAnother()
    {
        WriteScript("ShipA.cs", ShipPartA);
        WriteScript("ShipB.cs", "namespace Fleet { public partial class Ship { } }");
        Assert.Equal(new[] { NetworkCodeGen.NetworkId("Health") }, SlotsOfFreshShip(CompileGame()));

        // ShipA.cs is untouched; only the other part gains a [Sync].
        WriteScript("ShipB.cs", ShipPartB);
        Assert.Equal(new[] { NetworkCodeGen.NetworkId("Health"), NetworkCodeGen.NetworkId("Ammo") }, SlotsOfFreshShip(CompileGame()));
    }

    /// <summary>
    /// A [Sync] type and initializer only the declaring file's usings resolve, in a part that doesn't host
    /// the generated overrides: the field has to stay where those usings apply.
    /// </summary>
    [Fact]
    public void PartsKeepTheirOwnUsings()
    {
        WriteScript("Stats.cs", "namespace Fleet.Stats { public enum Rank { Cadet, Captain } }");
        WriteScript("ShipA.cs", ShipPartA);
        WriteScript("ShipZ.cs", """
            using Prowl.Runtime;
            using Fleet.Stats;

            namespace Fleet;

            public partial class Ship
            {
                [Sync] public Rank Rank { get; set; } = Rank.Captain;

                [Rpc.Broadcast] public void Promote(Rank to) => Rank = to;
            }
            """);
        Type shipType = CompileGame().GetType("Fleet.Ship")!;

        GameObject go = new("Ship");
        try
        {
            Component ship = (Component)go.AddComponent(shipType)!;
            Assert.Equal("Captain", shipType.GetProperty("Rank")!.GetValue(ship)!.ToString());
        }
        finally
        {
            go.Dispose();
        }
    }

    /// <summary>Descriptors on a generic component are found on whichever instantiation is in use, and hot reload has nothing to warn about.</summary>
    [Fact]
    public void GenericComponentsNetwork()
    {
        WriteScript("Holder.cs", """
            using Prowl.Runtime;

            public class Holder<T> : Component
            {
                [Sync] public int Count { get; set; } = 3;

                [Rpc.Broadcast] public void Bump(int by) => Count += by;
            }

            public class IntHolder : Holder<int> { }
            """);

        ScriptCompiler.CompileResult result = ScriptCompiler.CompileAll(Project);
        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain("EMBA", result.Output);

        Type holderType = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath)).GetType("IntHolder")!;
        GameObject go = new("Holder");
        try
        {
            Component holder = (Component)go.AddComponent(holderType)!;
            Assert.Equal(new[] { NetworkCodeGen.NetworkId("Count") }, Slots(holder));
            Assert.NotNull(NetworkMembers.Of(holder).Rpc(NetworkCodeGen.NetworkId("Bump(int)")));
        }
        finally
        {
            go.Dispose();
        }
    }

    private static List<uint> SlotsOfFreshShip(Assembly game)
    {
        GameObject go = new("Ship");
        try
        {
            return Slots((Component)go.AddComponent(game.GetType("Fleet.Ship")!)!);
        }
        finally
        {
            go.Dispose();
        }
    }

    [Fact]
    public void PartialRewritesKeepLinesInEveryPart()
    {
        SyntaxTree a = CSharpSyntaxTree.ParseText(ShipPartA, path: "A.cs");
        SyntaxTree b = CSharpSyntaxTree.ParseText(ShipPartB, path: "B.cs");
        NetworkCodeGen.Result result = NetworkCodeGen.Rewrite([b, a]);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(ShipPartB.Split('\n').Length, result.Trees[0].GetText().ToString().Split('\n').Length);
        Assert.Equal(ShipPartA.Split('\n').Length, result.Trees[1].GetText().ToString().Split('\n').Length);

        // Each part carries the descriptors of its own members, and only those.
        Assert.Contains("__syncnet_Ammo", result.Trees[0].GetText().ToString());
        Assert.DoesNotContain("__syncnet_Health", result.Trees[0].GetText().ToString());
        Assert.Contains("__syncnet_Health", result.Trees[1].GetText().ToString());
    }

    [Fact]
    public void UnchangedFilesKeepTheirRewrittenTree()
    {
        SyntaxTree a = CSharpSyntaxTree.ParseText(ShipPartA, path: "A.cs");
        SyntaxTree b = CSharpSyntaxTree.ParseText(ShipPartB, path: "B.cs");
        SyntaxTree first = NetworkCodeGen.Rewrite([a, b]).Trees[0];
        Assert.Same(first, NetworkCodeGen.Rewrite([a, b]).Trees[0]);

        // A file's rewrite depends on nothing else, so a new [Sync] in another part leaves it alone.
        SyntaxTree b2 = CSharpSyntaxTree.ParseText(ShipPartB.Replace("public int Ammo", "public int Ammo2 { get; set; } [Sync] public int Ammo"), path: "B.cs");
        Assert.Same(first, NetworkCodeGen.Rewrite([a, b2]).Trees[0]);
    }

    [Theory]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Sync] public int X { get => 1; set { } } }", "PROWLNET001")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Sync] public static int X { get; set; } }", "PROWLNET001")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Rpc.Broadcast] public int F() => 1; }", "PROWLNET002")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Rpc.Host] public void F(ref int x) { } }", "PROWLNET002")]
    [InlineData("public struct A { [Prowl.Runtime.Sync] public int X { get; set; } }", "PROWLNET003")]
    [InlineData("public class A { [Prowl.Runtime.Sync] public int X { get; set; } }", "PROWLNET003")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Rpc.Broadcast] public void F(int[] x) { } }", "PROWLNET005")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Sync] public System.Collections.Generic.List<int> X { get; set; } }", "PROWLNET005")]
    public void UnsupportedShapesAreCompileErrors(string script, string id)
    {
        WriteScript("Bad.cs", script);

        ScriptCompiler.CompileResult result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success);
        Assert.Contains(id, result.Errors + result.Output);
    }
}
