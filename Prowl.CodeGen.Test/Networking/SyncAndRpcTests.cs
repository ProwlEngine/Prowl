// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.CodeGen.Networking;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Networking.Serialization;
using Prowl.Runtime.Networking.Utilities;
using Prowl.Vector;
using Prowl.Warp;

using Xunit;

namespace Prowl.CodeGen.Test.Networking;

/// <summary>
///     What [Sync] properties and RPCs compile to: plain members without a session, and descriptors the runtime
///     networks them by.
/// </summary>
public class SyncAndRpcTests
{
    [Fact]
    public void GeneratedCodeBehavesNormallyWithoutASession()
    {
        Type shipType = Scripts.Compile(TestScripts.Ship).GetType("Ship")!;
        Scripts.WithComponent(shipType, ship =>
        {
            PropertyInfo health = shipType.GetProperty("Health")!;
            Assert.Equal(100, health.GetValue(ship));
            health.SetValue(ship, 5);
            Assert.Equal(5, health.GetValue(ship));
            Assert.Equal("Ship One", shipType.GetProperty("Callsign")!.GetValue(ship));

            shipType.GetMethod("Fire")!.Invoke(ship, [3, new Float3(1, 0, 0)]);
            shipType.GetMethod("Ping")!.Invoke(ship, []);
            Assert.Equal(new[] { "fire 3 at 1", "ping" }, (List<string>)shipType.GetField("Log")!.GetValue(ship)!);
        });
    }

    [Fact]
    public void GeneratedMembersDescribeTheNetworkedMembers()
    {
        Assembly game = Scripts.Compile(TestScripts.Ship);
        Type shipType = game.GetType("Ship")!;
        Scripts.WithComponent(shipType, ship =>
        {
            NetworkMembers members = NetworkMembers.Of(ship);
            Assert.Equal(new[] { NetworkCodeGen.NetworkId("Health"), NetworkCodeGen.NetworkId("Callsign") },
                Scripts.Slots(ship));
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
            NetSerializer<Float3>.Write(arguments, new Float3(2, 0, 0));
            NetReader argumentReader = new(arguments.WrittenSpan);
            fire.Invoke(ship, ref argumentReader);
            Assert.Equal(new[] { "fire 7 at 2" }, (List<string>)shipType.GetField("Log")!.GetValue(ship)!);
        });

        // A nested component gets its own.
        Scripts.WithComponent(game.GetType("Ship+Turret")!, turret =>
            Assert.Equal(new[] { NetworkCodeGen.NetworkId("Angle") }, Scripts.Slots(turret)));
    }

    [Fact]
    public void PartialClassesNetworkMembersFromEveryPart()
    {
        Type shipType = Scripts.Compile(TestScripts.ShipPartA, TestScripts.ShipPartB,
                "namespace Fleet; public partial class Ship { public int Plain; }")
            .GetType("Fleet.Ship")!;
        Scripts.WithComponent(shipType, ship =>
        {
            Assert.Equal(new[] { NetworkCodeGen.NetworkId("Health"), NetworkCodeGen.NetworkId("Ammo") },
                Scripts.Slots(ship));
            Assert.Equal(30, shipType.GetProperty("Ammo")!.GetValue(ship));

            NetworkMembers members = NetworkMembers.Of(ship);
            Assert.Equal(RpcMode.Host, members.Rpc(NetworkCodeGen.NetworkId("Reload()"))!.Mode);
            Assert.Equal(SyncFlags.FromHost, members.Sync(NetworkCodeGen.NetworkId("Ammo"))!.Flags);
            Assert.NotNull(members.Rpc(NetworkCodeGen.NetworkId("Fire(int)")));

            // Offline, both parts' RPCs are plain calls.
            shipType.GetMethod("Fire")!.Invoke(ship, [2]);
            Assert.Equal(2, shipType.GetField("Fired")!.GetValue(ship));
        });
    }

    /// <summary>
    ///     A [Sync] type and initializer only the declaring file's usings resolve: the field has to stay where those
    ///     usings apply.
    /// </summary>
    [Fact]
    public void PartsKeepTheirOwnUsings()
    {
        Type shipType = Scripts.Compile("namespace Fleet.Stats { public enum Rank { Cadet, Captain } }",
            TestScripts.ShipPartA, """
                                   using Prowl.Runtime;
                                   using Prowl.Runtime.Networking;
                                   using Prowl.Runtime.Networking.Attributes;
                                   using Prowl.Runtime.Networking.Collections;
                                   using Prowl.Runtime.Networking.Enums;
                                   using Prowl.Runtime.Networking.Serialization;
                                   using Fleet.Stats;

                                   namespace Fleet;

                                   public partial class Ship
                                   {
                                       [Sync] public Rank Rank { get; set; } = Rank.Captain;

                                       [Rpc.Broadcast] public void Promote(Rank to) => Rank = to;
                                   }
                                   """).GetType("Fleet.Ship")!;

        Scripts.WithComponent(shipType,
            ship => Assert.Equal("Captain", shipType.GetProperty("Rank")!.GetValue(ship)!.ToString()));
    }

    /// <summary>Descriptors on a generic component are found on whichever instantiation is in use.</summary>
    [Fact]
    public void GenericComponentsNetwork()
    {
        Type holderType = Scripts.Compile("""
                                          using Prowl.Runtime;
                                          using Prowl.Runtime.Networking;
                                          using Prowl.Runtime.Networking.Attributes;
                                          using Prowl.Runtime.Networking.Collections;
                                          using Prowl.Runtime.Networking.Enums;
                                          using Prowl.Runtime.Networking.Serialization;

                                          public class Holder<T> : Component
                                          {
                                              [Sync] public int Count { get; set; } = 3;

                                              [Rpc.Broadcast] public void Bump(int by) => Count += by;
                                          }

                                          public class IntHolder : Holder<int> { }
                                          """).GetType("IntHolder")!;

        Scripts.WithComponent(holderType, holder =>
        {
            Assert.Equal(new[] { NetworkCodeGen.NetworkId("Count") }, Scripts.Slots(holder));
            Assert.NotNull(NetworkMembers.Of(holder).Rpc(NetworkCodeGen.NetworkId("Bump(int)")));
        });
    }

    [Fact]
    public void RpcPrologueDoesNotCollideWithTheMethodsOwnNames() =>
        Assert.NotNull(Scripts.Compile("""
                                       public class StatsNames : Prowl.Runtime.Component
                                       {
                                           [Prowl.Runtime.Networking.Attributes.Rpc.Broadcast]
                                           public void WithParameter(int __rpc_stats) { }

                                           [Prowl.Runtime.Networking.Attributes.Rpc.Broadcast]
                                           public void WithLocal() { int __rpc_stats = 1; }
                                       }
                                       """).GetType("StatsNames"));
}
