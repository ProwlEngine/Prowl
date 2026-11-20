// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Numerics;

namespace Prowl.Runtime.Networking;

/// <summary>
///     A set of groups numbered 0 to 63, such as teams. A networked object in any group reaches only the clients that
///     share one of its groups. An object in none reaches everyone.
/// </summary>
public readonly struct NetworkGroups : IEquatable<NetworkGroups>
{
    public const int Count = 64;

    internal readonly ulong Mask;

    internal NetworkGroups(ulong mask) => Mask = mask;

    public static NetworkGroups None => default;

    public static NetworkGroups Of(int group) => new(Bit(group));

    public bool IsEmpty => Mask == 0;

    public NetworkGroups With(int group) => new(Mask | Bit(group));

    public NetworkGroups Without(int group) => new(Mask & ~Bit(group));

    public bool Contains(int group) => (Mask & Bit(group)) != 0;

    public bool Overlaps(NetworkGroups other) => (Mask & other.Mask) != 0;

    private static ulong Bit(int group)
    {
        if ((uint)group >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(group), group, "Groups are numbered 0 to 63.");
        }

        return 1UL << group;
    }

    public bool Equals(NetworkGroups other) => Mask == other.Mask;

    public override bool Equals(object? obj) => obj is NetworkGroups other && Equals(other);

    public override int GetHashCode() => Mask.GetHashCode();

    public static bool operator ==(NetworkGroups left, NetworkGroups right) => left.Mask == right.Mask;

    public static bool operator !=(NetworkGroups left, NetworkGroups right) => left.Mask != right.Mask;

    public override string ToString()
    {
        if (Mask == 0)
        {
            return "None";
        }

        System.Text.StringBuilder text = new();
        for (ulong rest = Mask; rest != 0; rest &= rest - 1)
        {
            text.Append(text.Length == 0 ? "" : ", ").Append(BitOperations.TrailingZeroCount(rest));
        }

        return text.ToString();
    }
}
