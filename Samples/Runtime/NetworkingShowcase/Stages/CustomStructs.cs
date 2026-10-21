// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Vector;

namespace NetworkingShowcase;

/// <summary>
/// A struct of the game's own. Nothing marks it for the network: the script compiler sees a [Sync] value and
/// an RPC use it, and writes its serializer from its public fields and auto-properties.
/// </summary>
public struct Loadout
{
    public string Weapon;
    public int Ammo;
    public int Magazine;
    public Color Tint;
    public bool Silenced { get; set; }
}

/// <summary>
/// Stage 6. A [Sync] struct goes as one value: change any field and the whole loadout arrives together. The
/// same struct is an RPC argument too, when someone shows theirs off.
/// </summary>
public class Armory : Component
{
    public static readonly Loadout[] Weapons =
    [
        new() { Weapon = "Pistol", Ammo = 12, Magazine = 12, Tint = new Color(0.8f, 0.8f, 0.85f, 1f) },
        new() { Weapon = "Rifle", Ammo = 30, Magazine = 30, Tint = new Color(0.3f, 0.9f, 0.4f, 1f) },
        new() { Weapon = "Launcher", Ammo = 4, Magazine = 4, Tint = new Color(1f, 0.4f, 0.1f, 1f) },
    ];

    [Sync] public Loadout Current { get; set; } = Weapons[0];

    private readonly Tint _tint = new();

    public string LastShown { get; private set; } = "Nobody has shown off yet";

    public void NextWeapon()
    {
        int next = (Array.FindIndex(Weapons, w => w.Weapon == Current.Weapon) + 1) % Weapons.Length;
        Current = Weapons[next] with { Silenced = Current.Silenced };
    }

    public void Fire() => Current = Current with { Ammo = Math.Max(0, Current.Ammo - 1) };

    public void Reload() => Current = Current with { Ammo = Current.Magazine };

    public void ToggleSilencer() => Current = Current with { Silenced = !Current.Silenced };

    [Rpc.Broadcast]
    public void ShowOff(Loadout loadout) =>
        LastShown = $"{Rpc.Caller?.Name ?? "You"} showed a {(loadout.Silenced ? "silenced " : "")}{loadout.Weapon}, {loadout.Ammo} of {loadout.Magazine}";

    public override void Update()
    {
        Loadout current = Current;
        _tint.Apply(GameObject, current.Tint * (current.Silenced ? 0.4f : 1f), current.Silenced ? 0.5f : 2f);
        float full = current.Magazine == 0 ? 0f : (float)current.Ammo / current.Magazine;
        Transform.LocalScale = new Float3(1f, 0.2f + full * 1.8f, 1f);
    }
}
