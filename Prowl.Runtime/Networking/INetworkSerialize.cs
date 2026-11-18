// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.ComponentModel;

using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>
///     A component that writes its own network state, as Mirror's OnSerialize and OnDeserialize, for state that packs
///     better by hand than as [Sync] values. It goes with spawns and snapshots like a [Sync] value, and again each time
///     <see cref="Networking.SetDirty" /> marks it. Snapshots can be lost and sent again, so write the whole state every
///     time, never a change from the last. The host writes it unless <see cref="NetworkSerializeAttribute" /> says otherwise.
/// </summary>
public interface INetworkSerialize
{
    void OnSerialize(NetWriter writer);

    /// <summary>Reads what <see cref="OnSerialize" /> wrote; <paramref name="initial" /> when it came with the object.</summary>
    void OnDeserialize(ref NetReader reader, bool initial);
}

/// <summary>How an <see cref="INetworkSerialize" /> component's state travels: who writes it, and who gets it.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class NetworkSerializeAttribute(SyncFlags flags) : Attribute
{
    public SyncFlags Flags { get; } = flags;
}

/// <summary>The state an <see cref="INetworkSerialize" /> component writes, as one more [Sync] slot of its type.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class NetworkSerializeMember(SyncFlags flags) : SyncMember(CustomSlot, "OnSerialize", flags)
{
    /// <summary>The slot no generated [Sync] property takes.</summary>
    public const uint CustomSlot = uint.MaxValue;

    internal override void Write(Component component, NetWriter writer) =>
        ((INetworkSerialize)component).OnSerialize(writer);

    internal override void Read(Component component, ref NetReader reader, bool initial) =>
        ((INetworkSerialize)component).OnDeserialize(ref reader, initial);

    // The component's own format: only it can read it, so the bytes are taken as they are.
    internal override void Validate(Component component, ref NetReader reader, bool initial = false) =>
        reader.ReadBytes(reader.Remaining);

    internal override string FormatValue(Component component) => "Written by OnSerialize";
}
