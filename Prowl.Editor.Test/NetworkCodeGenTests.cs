// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Prowl.Editor.Projects.Scripting;
using Prowl.Runtime;
using Prowl.Runtime.Networking;
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
        using Prowl.Runtime.Networking;
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

    private static readonly Lazy<MetadataReference[]> s_references = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToArray());

    /// <summary>Generates the networking of <paramref name="trees"/>, bound the way the script compiler binds them.</summary>
    private static NetworkCodeGen.Result Generate(params SyntaxTree[] trees) =>
        NetworkCodeGen.Generate(CSharpCompilation.Create("Game", trees, s_references.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)), trees);

    private Assembly CompileGame()
    {
        ScriptCompiler.CompileResult result = ScriptCompiler.CompileAll(Project);
        Assert.True(result.Success, result.Errors);
        return Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
    }

    private static List<uint> Slots(Component component) => NetworkMembers.Of(component).Syncs.Select(s => s.Slot).ToList();

    [Fact]
    public void RpcStatsScopeDoesNotIntroduceALocalNameCollision()
    {
        WriteScript("StatsNames.cs", """
            public class StatsNames : Prowl.Runtime.Component
            {
                [Prowl.Runtime.Networking.Rpc.Broadcast]
                public void WithParameter(int __rpc_stats) { }

                [Prowl.Runtime.Networking.Rpc.Broadcast]
                public void WithLocal() { int __rpc_stats = 1; }
            }
            """);

        Assert.NotNull(CompileGame().GetType("StatsNames"));
    }

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
        SyntaxTree rewritten = Generate(original).Trees[0];

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
        Assert.Same(tree, Generate(tree).Trees[0]);
    }

    private const string ShipPartA = """
        using Prowl.Runtime;
        using Prowl.Runtime.Networking;

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
        using Prowl.Runtime.Networking;

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
            using Prowl.Runtime.Networking;
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
            using Prowl.Runtime.Networking;

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
        NetworkCodeGen.Result result = Generate(b, a);

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
        SyntaxTree first = Generate(a, b).Trees[0];
        Assert.Same(first, Generate(a, b).Trees[0]);

        // A file's rewrite depends on nothing else, so a new [Sync] in another part leaves it alone.
        SyntaxTree b2 = CSharpSyntaxTree.ParseText(ShipPartB.Replace("public int Ammo", "public int Ammo2 { get; set; } [Sync] public int Ammo"), path: "B.cs");
        Assert.Same(first, Generate(a, b2).Trees[0]);
    }

    private const string FleetScript = """
        using System.Collections.Generic;
        using System.Linq;
        using Prowl.Echo;
        using Prowl.Runtime;
        using Prowl.Runtime.Networking;
        using Prowl.Warp;

        public struct Loadout
        {
            public int Ammo;
            [SerializeField] private string _weapon;
            [SerializeIgnore] public int Local;

            public string Weapon { get => _weapon; set => _weapon = value; }
        }

        public class Person
        {
            public string Name;
            protected Person() { }
        }

        public class Crew : Person
        {
            public Loadout Kit;
            public int Rank { get; init; }
            public Crew Mentor;
            public string Note => "not sent";
        }

        public readonly record struct Badge(string Title, int Level);

        public abstract class Weapon { public string Name; }
        public class Sword : Weapon { public int Edge; }
        public class Bow : Weapon { public int Range; }

        public interface ITrinket { }
        public class Ring : ITrinket { public int Power; }

        public struct Packed : INetSerializable<Packed>
        {
            public byte Value;
            public static void Write(NetWriter writer, Packed value) => writer.WriteByte((byte)(value.Value + 1));
            public static Packed Read(ref NetReader reader) => new() { Value = (byte)(reader.ReadByte() - 1) };
        }

        public class Fleet : Component
        {
            [Sync, Change(nameof(OnHealthChanged))] public int Health { get; set; } = 100;

            [Sync, Change("Ping")] public int Shield { get; set; }

            [Sync] public NetList<Crew> Crew { get; }
                = new(); // MARKER-CREW

            [Sync(SyncFlags.FromHost)] public NetDictionary<string, Loadout> Kits { get; } = new();

            [Sync] public NetSet<Packed> Codes { get; } = new();

            [Sync] public Loadout Kit { get; set; }

            [Sync] public Badge Badge { get; set; }

            [Sync] public Weapon Gear { get; set; }

            [Sync] public NetList<ITrinket> Trinkets { get; } = new();

            public List<string> Log = new();

            private void OnHealthChanged(int oldValue, int newValue) => Log.Add($"{oldValue}->{newValue}");

            private void Ping() => Log.Add("ping");

            [Rpc.Broadcast] public void Hire(Crew who) => Crew.Add(who);

            public void Fill()
            {
                Kit = new Loadout { Ammo = 3, Weapon = "rifle", Local = 9 };
                Crew ada = new() { Name = "Ada", Kit = Kit, Rank = 2 };
                Crew bo = new() { Name = "Bo", Kit = Kit, Rank = 1, Mentor = ada };
                ada.Mentor = bo;
                Hire(bo);
                Crew.Add(null);
                Kits["main"] = Kit;
                Codes.Add(new Packed { Value = 7 });
                Badge = new Badge("Ace", 4);
                Gear = new Bow { Name = "Longbow", Range = 30 };
                Trinkets.Add(new Ring { Power = 2 });
            }

            public string Describe() =>
                $"{Kit.Ammo} {Kit.Weapon} {Kit.Local} | {string.Join(",", Crew.Select(c => c == null ? "null" : $"{c.Name}:{c.Kit.Weapon}:{c.Rank}<{c.Mentor?.Name}{(ReferenceEquals(c.Mentor?.Mentor, c) ? "<loop" : "")}"))}"
                + $" | {string.Join(",", Kits.Select(k => k.Key + "=" + k.Value.Ammo))} | {string.Join(",", Codes.Select(c => c.Value))} | {Badge.Title} {Badge.Level}"
                + $" | {Gear?.GetType().Name}:{Gear?.Name}:{(Gear as Bow)?.Range} | {string.Join(",", Trinkets.Select(t => t.GetType().Name + ((Ring)t).Power))}";
        }
        """;

    [Fact]
    public void CollectionsHooksAndTheGamesOwnTypesNetwork()
    {
        WriteScript("Fleet.cs", FleetScript);
        Type fleetType = CompileGame().GetType("Fleet")!;

        GameObject go = new("Fleet");
        try
        {
            Component fleet = (Component)go.AddComponent(fleetType)!;
            Component copy = (Component)go.AddComponent(fleetType)!;
            List<string> log = (List<string>)fleetType.GetField("Log")!.GetValue(fleet)!;

            // Hooks run on a change, with or without the values, and not for the same value.
            fleetType.GetProperty("Health")!.SetValue(fleet, 5);
            fleetType.GetProperty("Health")!.SetValue(fleet, 5);
            fleetType.GetProperty("Shield")!.SetValue(fleet, 1);
            Assert.Equal(new[] { "100->5", "ping" }, log);

            NetworkMembers members = NetworkMembers.Of(fleet);
            Assert.Equal(typeof(SyncCollectionMember<,>), members.Sync(NetworkCodeGen.NetworkId("Crew"))!.GetType().GetGenericTypeDefinition());
            Assert.Equal(SyncFlags.FromHost, members.Sync(NetworkCodeGen.NetworkId("Kits"))!.Flags);

            // Everything, generated serializers and the hand-written one alike, goes across as it was.
            fleetType.GetMethod("Fill")!.Invoke(fleet, []);
            foreach (SyncMember sync in members.Syncs)
            {
                using NetWriter writer = new();
                sync.Write(fleet, writer);
                NetReader reader = new(writer.WrittenSpan);
                sync.Read(copy, ref reader, initial: true);
                Assert.True(reader.IsAtEnd, sync.Name);
            }

            Assert.Equal("3 rifle 9 | Bo:rifle:1<Ada<loop,null | main=3 | 7 | Ace 4 | Bow:Longbow:30 | Ring2", fleetType.GetMethod("Describe")!.Invoke(fleet, []));
            Assert.Equal("3 rifle 0 | Bo:rifle:1<Ada<loop,null | main=3 | 7 | Ace 4 | Bow:Longbow:30 | Ring2", fleetType.GetMethod("Describe")!.Invoke(copy, []));
        }
        finally
        {
            go.Dispose();
        }
    }

    [Fact]
    public void CollectionRewriteKeepsEveryLineWhereItWas()
    {
        SyntaxTree original = CSharpSyntaxTree.ParseText(FleetScript);
        NetworkCodeGen.Result result = Generate(original);
        Assert.Empty(result.Diagnostics);

        string[] before = original.GetText().ToString().Split('\n');
        string[] after = result.Trees[0].GetText().ToString().Split('\n');
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i].Contains("public int Ammo;") || before[i].Contains("Log.Add("))
                Assert.Equal(before[i].Trim(), after[i].Trim());
            if (before[i].Contains("MARKER"))
                Assert.Contains("MARKER", after[i]);
        }
        Assert.Contains(after, line => line.Contains("__Sync_Collection(this.__sync_Crew, __syncnet_Crew)"));
    }

    [Theory]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public int X { get; } = 1; }", "PROWLNET007")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public Prowl.Runtime.Networking.NetList<int> X { get; set; } }", "PROWLNET007")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public Prowl.Runtime.Networking.NetList<int[]> X { get; } = new(); }", "PROWLNET005")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync, Prowl.Runtime.Networking.Change(\"F\")] public int X { get; set; } void F(string a, string b) { } }", "PROWLNET006")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync, Prowl.Runtime.Networking.Change(\"Missing\")] public int X { get; set; } }", "PROWLNET006")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync, Prowl.Runtime.Networking.Change(\"F\")] public Prowl.Runtime.Networking.NetList<int> X { get; } = new(); void F() { } }", "PROWLNET006")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Change(nameof(F))] public int X { get; set; } void F() { } }", "PROWLNET006")]
    [InlineData("public struct S { public int[] A; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public S X { get; set; } }", "PROWLNET005")]
    [InlineData("public class C { public C(int x) { } public int A; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Broadcast] public void F(C c) { } }", "PROWLNET005")]
    [InlineData("public struct S<T> { public T A; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public S<int> X { get; set; } }", "PROWLNET005")]
    [InlineData("public class A : Prowl.Runtime.Component { private struct S { public int A; } [Prowl.Runtime.Networking.Sync] private S X { get; set; } }", "PROWLNET005")]
    [InlineData("public struct S { private int _a; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public Prowl.Runtime.Networking.NetList<S> X { get; } = new(); }", "PROWLNET005")]
    [InlineData("public abstract class B { public int A; } public class C : B { public System.Action F; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public B X { get; set; } }", "PROWLNET005")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public object X { get; set; } }", "PROWLNET005")]
    [InlineData("public struct Outer { public Inner I; } public struct Inner { public System.Action A; } public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public Outer X { get; set; } }", "PROWLNET005")]
    public void UnsupportedNetworkTypesAreCompileErrors(string script, string id) => UnsupportedShapesAreCompileErrors(script, id);

    [Fact]
    public void UnsendableTypesSayWhy()
    {
        WriteScript("Bad.cs", """
            public struct Outer { public Inner I; }
            public struct Inner { public System.Action A; }
            public struct Hidden { private int _a; }
            public class A : Prowl.Runtime.Component
            {
                [Prowl.Runtime.Networking.Sync] public Outer X { get; set; }
                [Prowl.Runtime.Networking.Rpc.Broadcast] public void F(Hidden h) { }
            }
            """);

        ScriptCompiler.CompileResult result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success);
        string errors = result.Errors + result.Output;
        Assert.Contains("'X' can't be sent over the network: X.I.A is a System.Action, which isn't a type the network can send", errors);
        Assert.Contains("'h' can't be sent over the network: h is a Hidden, which has nothing to send", errors);
    }

    [Theory]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public int X { get => 1; set { } } }", "PROWLNET001")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public static int X { get; set; } }", "PROWLNET001")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Broadcast] public int F() => 1; }", "PROWLNET002")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Host] public void F(ref int x) { } }", "PROWLNET002")]
    [InlineData("public struct A { [Prowl.Runtime.Networking.Sync] public int X { get; set; } }", "PROWLNET003")]
    [InlineData("public class A { [Prowl.Runtime.Networking.Sync] public int X { get; set; } }", "PROWLNET003")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Rpc.Broadcast] public void F(int[] x) { } }", "PROWLNET005")]
    [InlineData("public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Sync] public System.Collections.Generic.List<int> X { get; set; } }", "PROWLNET005")]
    public void UnsupportedShapesAreCompileErrors(string script, string id)
    {
        WriteScript("Bad.cs", script);

        ScriptCompiler.CompileResult result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success);
        Assert.Contains(id, result.Errors + result.Output);
    }
}
