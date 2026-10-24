// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.CodeGen.Networking;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Networking.Utilities;
using Prowl.Warp;

using Xunit;

namespace Prowl.CodeGen.Test.Networking;

/// <summary>Change hooks, network collections and the serializers of the game's own types, run for real.</summary>
public class SerializerTests
{
    [Fact]
    public void CollectionsHooksAndTheGamesOwnTypesNetwork()
    {
        Type fleetType = Scripts.Compile(TestScripts.Fleet).GetType("Fleet")!;
        Scripts.WithComponent(fleetType, fleet => Scripts.WithComponent(fleetType, copy =>
        {
            List<string> log = (List<string>)fleetType.GetField("Log")!.GetValue(fleet)!;

            // Hooks run on a change, with or without the values, and not for the same value.
            fleetType.GetProperty("Health")!.SetValue(fleet, 5);
            fleetType.GetProperty("Health")!.SetValue(fleet, 5);
            fleetType.GetProperty("Shield")!.SetValue(fleet, 1);
            Assert.Equal(new[] { "100->5", "ping" }, log);

            NetworkMembers members = NetworkMembers.Of(fleet);
            Assert.Equal(typeof(SyncCollectionMember<,>),
                members.Sync(NetworkCodeGen.NetworkId("Crew"))!.GetType().GetGenericTypeDefinition());
            Assert.Equal(SyncFlags.FromHost, members.Sync(NetworkCodeGen.NetworkId("Kits"))!.Flags);

            // Everything goes across as it was: generated serializers and a hand-written one, a Bow sent as a
            // Weapon, a Ring as an ITrinket, two crew who mentor each other.
            fleetType.GetMethod("Fill")!.Invoke(fleet, []);
            foreach (SyncMember sync in members.Syncs)
            {
                using NetWriter writer = new();
                sync.Write(fleet, writer);
                NetReader reader = new(writer.WrittenSpan);
                sync.Read(copy, ref reader, true);
                Assert.True(reader.IsAtEnd, sync.Name);
            }

            Assert.Equal("3 rifle 9 | Bo:rifle:1<Ada<loop,null | main=3 | 7 | Ace 4 | Bow:Longbow:30 | Ring2",
                fleetType.GetMethod("Describe")!.Invoke(fleet, []));
            Assert.Equal("3 rifle 0 | Bo:rifle:1<Ada<loop,null | main=3 | 7 | Ace 4 | Bow:Longbow:30 | Ring2",
                fleetType.GetMethod("Describe")!.Invoke(copy, []));
        }));
    }

    [Fact]
    public void UnsendableTypesSayWhy()
    {
        IReadOnlyList<string> errors = Scripts.Build("""
                                                     public struct Outer { public Inner I; }
                                                     public struct Inner { public System.Action A; }
                                                     public struct Hidden { private int _a; }
                                                     public abstract class Spell { }
                                                     public class Curse : Spell { public System.Action Effect; }
                                                     public class A : Prowl.Runtime.Component
                                                     {
                                                         [Prowl.Runtime.Networking.Attributes.Sync] public Outer X { get; set; }
                                                         [Prowl.Runtime.Networking.Attributes.Sync] public Spell Cast { get; set; }
                                                         [Prowl.Runtime.Networking.Attributes.Rpc.Broadcast] public void F(Hidden h) { }
                                                     }
                                                     """).Errors.Select(e => e.GetMessage()).ToList();

        Assert.Contains(
            "'X' can't be sent over the network: X.I.A is a System.Action, which isn't a type the network can send (see NetSerializer<T>)",
            errors);
        Assert.Contains(errors,
            e => e.StartsWith("'h' can't be sent over the network: h is a Hidden, which has nothing to send"));
        Assert.Contains(
            "'Cast' can't be sent over the network: Cast can be a Curse, and Curse.Effect is a System.Action, which isn't a type the network can send (see NetSerializer<T>)",
            errors);
    }
}
