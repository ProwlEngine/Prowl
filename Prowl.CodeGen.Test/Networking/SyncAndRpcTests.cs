// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.CodeGen.Networking;
using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

using Net = Prowl.Runtime.Networking.Networking;

using Xunit;

namespace Prowl.CodeGen.Test.Networking;

/// <summary>
///     What [Sync(SyncFlags.FromOwner)] properties and RPCs compile to: plain members without a session, and descriptors the runtime
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
            Assert.Equal(SyncFlags.None, members.Sync(NetworkCodeGen.NetworkId("Callsign"))!.Flags);
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
            Assert.Equal(SyncFlags.None, members.Sync(NetworkCodeGen.NetworkId("Ammo"))!.Flags);
            Assert.NotNull(members.Rpc(NetworkCodeGen.NetworkId("Fire(int)")));

            // Offline, both parts' RPCs are plain calls.
            shipType.GetMethod("Fire")!.Invoke(ship, [2]);
            Assert.Equal(2, shipType.GetField("Fired")!.GetValue(ship));
        });
    }

    /// <summary>
    ///     A [Sync(SyncFlags.FromOwner)] type and initializer only the declaring file's usings resolve: the field has to stay where those
    ///     usings apply.
    /// </summary>
    [Fact]
    public void PartsKeepTheirOwnUsings()
    {
        Type shipType = Scripts.Compile("namespace Fleet.Stats { public enum Rank { Cadet, Captain } }",
            TestScripts.ShipPartA, """
                                   using Prowl.Runtime;
                                   using Prowl.Runtime.Networking;
                                   using Fleet.Stats;

                                   namespace Fleet;

                                   public partial class Ship
                                   {
                                       [Sync(SyncFlags.FromOwner)] public Rank Rank { get; set; } = Rank.Captain;

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

                                          public class Holder<T> : Component
                                          {
                                              [Sync(SyncFlags.FromOwner)] public int Count { get; set; } = 3;

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
    public void ATargetRpcSendsAllButItsPeerAndRunsWithTheLocalPeer()
    {
        Type bell = Scripts.Compile("""
            using Prowl.Courier;
            using Prowl.Runtime;
            using Prowl.Runtime.Networking;

            public class Bell : Component
            {
                public string Heard = "";
                [Rpc.Target] public void Whisper(NetPeer target, string text) => Heard = text;
            }
            """).GetType("Bell")!;
        Scripts.WithComponent(bell, component =>
        {
            RpcMember rpc = NetworkMembers.Of(component).Rpc(NetworkCodeGen.NetworkId("Whisper(NetPeer,string)"))!;
            Assert.Equal(RpcMode.Target, rpc.Mode);
            using NetWriter arguments = new();
            NetSerializer<string>.Write(arguments, "psst");
            NetReader reader = new(arguments.WrittenSpan);
            rpc.ValidateArguments(component, ref reader);
            Assert.True(reader.IsAtEnd);
        });
    }

    [Theory]
    [InlineData("[Rpc.Target] public void Whisper(string text) { }")]
    [InlineData("[Rpc.Target] public static void Whisper(Prowl.Courier.NetPeer target) { }")]
    public void ATargetRpcNeedsAnInstanceMethodTakingItsPeerFirst(string method) =>
        Assert.Contains(Scripts.Build("using Prowl.Runtime; using Prowl.Runtime.Networking; public class Bell : Component { " + method + " }").Errors,
            e => e.Id == "PROWLNET011");

    [Fact]
    public void ArraysListsAndBytesOfSendableTypesAreSendable()
    {
        var build = Scripts.Build("""
            using System.Collections.Generic;
            using Prowl.Runtime;
            using Prowl.Runtime.Networking;

            public struct Point { public int X, Y; }

            public class Board : Component
            {
                [Sync] public int[] Scores { get; set; }
                [Sync] public List<Point> Path { get; set; }
                [Rpc.Host(RpcFlags.AnyCaller)] public void Pick(List<string> names, byte[] data, Point[] points) { }
            }
            """);
        Assert.Empty(build.Errors);
    }

    [Fact]
    public void ANetworkCollectionCannotGoToTheOwnerAlone() =>
        Assert.Contains(Scripts.Build("using Prowl.Runtime; using Prowl.Runtime.Networking; public class Bag : Component { [Sync(SyncFlags.ToOwner)] public NetList<int> Items { get; } = new(); }").Errors,
            e => e.Id == "PROWLNET007");

    [Fact]
    public void AnArrayOfAnUnsendableTypeIsReported() =>
        Assert.NotEmpty(Scripts.Build("using Prowl.Runtime; using Prowl.Runtime.Networking; public class Board : Component { [Sync] public System.IO.Stream[] Streams { get; set; } }").Errors);

    [Fact]
    public void RpcsNamedThroughAGlobalUsingStaticAreNetworked()
    {
        Type bell = Scripts.Compile("global using static Prowl.Runtime.Networking.Rpc;",
                "public class Bell : Prowl.Runtime.Component { [Broadcast] public void Ring() { } [Host] public void Ask() { } [Owner] public void Tell() { } }")
            .GetType("Bell")!;
        Scripts.WithComponent(bell, component =>
        {
            NetworkMembers members = NetworkMembers.Of(component);
            Assert.Equal(RpcMode.Broadcast, members.Rpc(NetworkCodeGen.NetworkId("Ring()"))!.Mode);
            Assert.Equal(RpcMode.Host, members.Rpc(NetworkCodeGen.NetworkId("Ask()"))!.Mode);
            Assert.Equal(RpcMode.Owner, members.Rpc(NetworkCodeGen.NetworkId("Tell()"))!.Mode);
        });
    }

    [Fact]
    public void AFileWhoseOnlyNetworkingIsATargetRpcIsNetworked()
    {
        Type bell = Scripts.Compile("global using static Prowl.Runtime.Networking.Rpc;",
                "public class Bell : Prowl.Runtime.Component { [Target] public void Whisper(Prowl.Courier.NetPeer to, int code) { } }")
            .GetType("Bell")!;
        Scripts.WithComponent(bell, component => Assert.True(NetworkMembers.Of(component).Any));
    }

    [Fact]
    public void ATargetRpcMayNameItsPeerAsNullable()
    {
        Type bell = Scripts.Compile("""
                                    #nullable enable
                                    using Prowl.Runtime;
                                    using Prowl.Runtime.Networking;
                                    using Prowl.Courier;

                                    public class Bell : Component { [Rpc.Target] public void Whisper(NetPeer? to, int code) { } }
                                    """).GetType("Bell")!;
        Scripts.WithComponent(bell, component => Assert.True(NetworkMembers.Of(component).Any));
    }

    [Fact]
    public void ATypeTheScriptsRegisterASerializerForCanBeSent()
    {
        Assembly assembly = Scripts.Compile("""
                                            using System;
                                            using Prowl.Runtime;
                                            using Prowl.Runtime.Networking;
                                            using Prowl.Courier;

                                            public static class Wire
                                            {
                                                public static void Register() => NetSerializer<DateTime>.Register(
                                                    static (w, v) => w.WriteInt64(v.Ticks), static (ref NetReader r) => new DateTime(r.ReadInt64()));
                                            }

                                            public class Clock : Component
                                            {
                                                [Sync] public DateTime Started { get; set; }
                                                [Rpc.Broadcast] public void Stamp(DateTime at) { }
                                            }
                                            """);
        Assert.NotNull(assembly.GetType("Clock"));

        // One nobody registers is still refused.
        Assert.Contains(Scripts.Build("using Prowl.Runtime; using Prowl.Runtime.Networking; public class Clock : Component { [Sync] public System.DateTime Started { get; set; } }").Errors,
            e => e.Id == "PROWLNET005");
    }

    [Fact]
    public void TheValueLimitCountsOwnStateOnATypeWithNoSyncOfItsOwn()
    {
        string properties = string.Concat(Enumerable.Range(0, 64).Select(i => $"[Sync] public int V{i} {{ get; set; }} "));
        Assert.Contains(Scripts.Build($$"""
                                        using Prowl.Runtime;
                                        using Prowl.Runtime.Networking;
                                        using Prowl.Courier;

                                        public class Full : Component { {{properties}} }
                                        """, """
                                        using Prowl.Runtime;
                                        using Prowl.Runtime.Networking;
                                        using Prowl.Courier;

                                        public class Over : Full, INetworkSerialize
                                        {
                                            public void OnSerialize(NetWriter writer, bool initial) { }
                                            public void OnDeserialize(ref NetReader reader, bool initial) { }
                                        }
                                        """).Errors, e => e.Id == "PROWLNET012");
    }

    [Fact]
    public void RpcChannelsAreCarriedIntoTheDescriptors()
    {
        Assembly assembly = Scripts.Compile("""
                                            using Prowl.Runtime;
                                            using Prowl.Runtime.Networking;

                                            public class Radio : Component
                                            {
                                                [Rpc.Broadcast(Channel = 2)] public void Chat(string text) { }
                                                [Rpc.Host(RpcFlags.Unreliable, Channel = 1)] public void Ping() { }
                                                [Rpc.Broadcast] public void Plain() { }
                                                [Rpc.Broadcast(Channel = 3)] public static void Announce(int code) { }
                                            }
                                            """);
        Type radio = assembly.GetType("Radio")!;
        Scripts.WithComponent(radio, component =>
        {
            NetworkMembers members = NetworkMembers.Of(component);
            Assert.Equal(2, members.Rpc(NetworkCodeGen.NetworkId("Chat(string)"))!.Channel);
            RpcMember ping = members.Rpc(NetworkCodeGen.NetworkId("Ping()"))!;
            Assert.Equal((RpcFlags.Unreliable, (byte)1), (ping.Flags, ping.Channel));
            Assert.Equal(0, members.Rpc(NetworkCodeGen.NetworkId("Plain()"))!.Channel);
        });

        StaticRpcMember announce = StaticRpcMember.Find(StaticRpcMember.HashOf(radio),
            NetworkCodeGen.NetworkId("Announce(int)"))!;
        Assert.Equal(3, announce.Channel);
    }

    [Fact]
    public void RpcPrologueDoesNotCollideWithTheMethodsOwnNames() =>
        Assert.NotNull(Scripts.Compile("""
                                       public class StatsNames : Prowl.Runtime.Component
                                       {
                                           [Prowl.Runtime.Networking.Rpc.Broadcast]
                                           public void WithParameter(int __rpc_stats) { }

                                           [Prowl.Runtime.Networking.Rpc.Broadcast]
                                           public void WithLocal() { int __rpc_stats = 1; }
                                       }
                                       """).GetType("StatsNames"));

    [Fact]
    public void AnOverrideIsTheSameRpcHoweverItsParametersAreSpelled()
    {
        Assembly assembly = Scripts.Compile("""
                                            using System;
                                            using Prowl.Runtime;
                                            using Prowl.Runtime.Networking;

                                            public class Gun : Component
                                            {
                                                [Rpc.Broadcast] public virtual void Fire(int power) { }
                                            }

                                            public class Cannon : Gun
                                            {
                                                [Rpc.Broadcast] public override void Fire(Int32 power) { base.Fire(power); }
                                            }

                                            public class Mortar : Cannon
                                            {
                                                [Rpc.Broadcast] public override void Fire(System.Int32 power) { base.Fire(power); }
                                            }
                                            """);
        Scripts.WithComponent(assembly.GetType("Mortar")!, component =>
        {
            NetworkMembers members = NetworkMembers.Of(component);
            Assert.NotNull(members.Rpc(NetworkCodeGen.NetworkId("Fire(int)")));
            Assert.Null(members.Rpc(NetworkCodeGen.NetworkId("Fire(Int32)")));
            Assert.Null(members.Rpc(NetworkCodeGen.NetworkId("Fire(System.Int32)")));
        });
    }

    [Fact]
    public void AReceivedCallToAnOverrideIsNotSentAgainByTheBaseCall()
    {
        Assembly assembly = Scripts.Compile("""
                                            using System;
                                            using Prowl.Runtime;
                                            using Prowl.Runtime.Networking;

                                            public class Gun : Component
                                            {
                                                public int Fired;
                                                [Rpc.Host(RpcFlags.AnyCaller)] public virtual void Fire(int power) => Fired++;
                                            }

                                            public class Cannon : Gun
                                            {
                                                [Rpc.Host(RpcFlags.AnyCaller)] public override void Fire(Int32 power) { base.Fire(power); }
                                            }
                                            """);
        using HostedComponent hosted = new(assembly.GetType("Cannon")!);
        using NetWriter call = new(), arguments = new();
        NetSerializer<int>.Write(arguments, 3);
        NetworkMembers members = NetworkMembers.Of(hosted.Component);
        call.WriteVarUInt32(Net.System!.NetIdOf(hosted.Component.GameObject));
        call.WriteByte(0);
        call.WriteVarUInt32((uint)members.IndexOf(members.Rpc(NetworkCodeGen.NetworkId("Fire(int)"))!));
        call.WriteBytesWithLength(arguments.WrittenSpan);
        hosted.Client.SendRaw(hosted.Client.HostPeer!, HostedComponent.RpcToHost, call.WrittenSpan);
        hosted.Frames(50);

        Assert.Equal(1, hosted.Field("Fired"));
        Assert.Equal(0, hosted.CallsAtClient);
    }

    [Theory]
    [InlineData("base.Fire(power);", 1)]
    [InlineData("Extra++;", 0)]
    public void ALocalCallToAnOverrideIsSentOnce(string overrideBody, int baseRuns)
    {
        Assembly assembly = Scripts.Compile($$"""
                                              using Prowl.Runtime;
                                              using Prowl.Runtime.Networking;

                                              public class Gun : Component
                                              {
                                                  public int Fired, Extra;
                                                  [Rpc.Broadcast] public virtual void Fire(int power) => Fired++;
                                              }

                                              public class Cannon : Gun
                                              {
                                                  [Rpc.Broadcast] public override void Fire(int power) { {{overrideBody}} }
                                              }
                                              """);
        using HostedComponent hosted = new(assembly.GetType("Cannon")!);
        hosted.Component.GetType().GetMethod("Fire")!.Invoke(hosted.Component, [3]);
        hosted.Frames(50);

        Assert.Equal(1, hosted.CallsAtClient);
        Assert.Equal(baseRuns, hosted.Field("Fired"));
        Assert.Equal(1 - baseRuns, hosted.Field("Extra"));
    }

    /// <summary>A script component on the host of a running session, with a raw client that gets ready and counts the calls it is sent.</summary>
    private sealed class HostedComponent : IDisposable
    {
        public const ushort Scene = Net.EngineMessageIds, RpcToHost = Scene + 6, RpcFromHost = Scene + 7, Ready = Scene + 8;

        private readonly SceneInstance _scene = SceneInstance.Create("Hosted");

        public HostedComponent(Type type)
        {
            _scene.Activate();
            SceneInstance.Main = _scene;
            LocalNetwork network = new();
            Net.Host(new LocalTransport(network, "host"));

            // Spawned from a factory, which is how an object built in code is networked.
            Net.RegisterFactory("hosted", (in NetworkSpawn _) =>
            {
                GameObject built = new(type.Name);
                built.AddComponent(type);
                return built;
            });
            Component = Net.SpawnFactory("hosted", default, Prowl.Vector.Quaternion.Identity).GetComponent(type)!;
            Client = NetSession.Join(new LocalTransport(network, "client"), "host", "client",
                options: new NetSessionOptions { AppVersion = NetworkSchema.AppVersion(0) });
            // Told which scenes to load, it answers that it has them, and the host spawns for it.
            Client.OnRaw(Scene, (NetPeer _, ref NetReader r, Delivery _, byte _) =>
            {
                r.ReadByte();
                Reply(Ready, r.ReadUInt64());
            });
            Client.OnRaw(RpcFromHost, (NetPeer _, ref NetReader _, Delivery _, byte _) => CallsAtClient++);
            for (int i = 0; i < 1000 && !ClientReady; i++)
            {
                Frames(1);
            }

            Assert.True(ClientReady);
        }

        public Component Component { get; }
        public NetSession Client { get; }
        public int CallsAtClient { get; private set; }

        private bool ClientReady => Net.Session!.Peers.Any(p => !p.IsLocal && Net.IsPeerReady(p));

        public int Field(string name) => (int)Component.GetType().GetField(name)!.GetValue(Component)!;

        public void Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Net.Receive();
                Net.Flush();
                Client.Update();
                Thread.Sleep(1);
            }
        }

        private void Reply(ushort id, ulong generation)
        {
            using NetWriter writer = new();
            writer.WriteUInt64(generation);
            Client.SendRaw(Client.HostPeer!, id, writer.WrittenSpan);
        }

        public void Dispose()
        {
            Net.Shutdown();
            Client.Dispose();
            SceneInstance.Shutdown();
        }
    }
}
