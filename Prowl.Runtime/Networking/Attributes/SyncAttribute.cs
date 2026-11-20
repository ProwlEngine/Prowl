// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Keeps a property of a <see cref="Component" /> the same on every peer. The host sets it, or the object's
///     owner with <see cref="SyncFlags.FromOwner" />. Everyone else receives the new value at the next network
///     tick, and setting it there is ignored, with a warning the first time. Late joiners get the current value.
/// </summary>
/// <remarks>
///     Write it on an auto-property, as in <c>[Sync] public int Health { get; set; } = 100;</c>. Prowl's
///     script compiler generates the networking around it, so it only works in game scripts. Without a
///     network session the property behaves like any other. During one, it only travels once its object is
///     spawned, and a client can't set it on an object that is not. The value must be a type
///     <see cref="NetSerializer{T}" /> supports, which includes your own structs and classes: they're sent
///     by their public fields and auto-properties. For a collection, use a get-only <see cref="NetList{T}" />,
///     <see cref="NetDictionary{TKey,TValue}" /> or <see cref="NetSet{T}" />, as in
///     <c>[Sync] public NetList&lt;int&gt; Scores { get; } = new();</c>, which sends each change rather than the
///     whole collection. To react to changes, add <see cref="ChangeAttribute" />.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SyncAttribute : Attribute
{
    public SyncAttribute()
    {
    }

    public SyncAttribute(SyncFlags flags) => Flags = flags;

    public SyncFlags Flags { get; set; }
}
