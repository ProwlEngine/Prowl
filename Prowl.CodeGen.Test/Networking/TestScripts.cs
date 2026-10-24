// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.CodeGen.Test.Networking;

/// <summary>Game scripts the networking tests compile, each using most of what one part of it does.</summary>
internal static class TestScripts
{
    /// <summary>[Sync] and RPCs, laid out over lines in every way the rewrite has to keep.</summary>
    public const string Ship = """
                               using System.Collections.Generic;
                               using Prowl.Runtime;
                               using Prowl.Runtime.Networking;
                               using Prowl.Runtime.Networking.Attributes;
                               using Prowl.Runtime.Networking.Collections;
                               using Prowl.Runtime.Networking.Enums;
                               using Prowl.Runtime.Networking.Serialization;
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

    /// <summary>One part of a partial component; <see cref="ShipPartB" /> is the other.</summary>
    public const string ShipPartA = """
                                    using Prowl.Runtime;
                                    using Prowl.Runtime.Networking;
                                    using Prowl.Runtime.Networking.Attributes;
                                    using Prowl.Runtime.Networking.Collections;
                                    using Prowl.Runtime.Networking.Enums;
                                    using Prowl.Runtime.Networking.Serialization;

                                    namespace Fleet;

                                    public partial class Ship : Component
                                    {
                                        [Sync] public int Health { get; set; } = 100;

                                        [Rpc.Broadcast] public void Fire(int power) => Fired += power;

                                        public int Fired;
                                    }
                                    """;

    public const string ShipPartB = """
                                    using Prowl.Runtime;
                                    using Prowl.Runtime.Networking;
                                    using Prowl.Runtime.Networking.Attributes;
                                    using Prowl.Runtime.Networking.Collections;
                                    using Prowl.Runtime.Networking.Enums;
                                    using Prowl.Runtime.Networking.Serialization;

                                    namespace Fleet
                                    {
                                        public partial class Ship
                                        {
                                            [Sync(SyncFlags.FromHost)] public int Ammo { get; set; } = 30;

                                            [Rpc.Host] public void Reload() => Ammo = 30;
                                        }
                                    }
                                    """;

    /// <summary>Change hooks, collections and the game's own types, polymorphic and cyclic ones included.</summary>
    public const string Fleet = """
                                using System.Collections.Generic;
                                using System.Linq;
                                using Prowl.Echo;
                                using Prowl.Runtime;
                                using Prowl.Runtime.Networking;
                                using Prowl.Runtime.Networking.Attributes;
                                using Prowl.Runtime.Networking.Collections;
                                using Prowl.Runtime.Networking.Enums;
                                using Prowl.Runtime.Networking.Serialization;
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
}
