// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.ComponentModel;

using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>
///     A component that writes its own network state, for state that packs
///     better by hand than as [Sync] values. It goes with a spawn, and again each time
///     <see cref="Networking.SetDirty" /> marks it. State travels reliably and in order, so a later write may hold only
///     what changed since the last. The host writes it unless <see cref="NetworkSerializeAttribute" /> says otherwise.
/// </summary>
public interface INetworkSerialize
{
    /// <summary>
    ///     Writes the state. <paramref name="initial" /> is true whenever all of it is needed: for a spawn, and for an
    ///     owner written or owner only state when ownership changes. Otherwise it follows a
    ///     <see cref="Networking.SetDirty" /> and may write only what changed.
    /// </summary>
    void OnSerialize(NetWriter writer, bool initial);

    /// <summary>Reads what <see cref="OnSerialize" /> wrote, with the <paramref name="initial" /> it was written with.</summary>
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

    // The state says itself whether it is whole, since a whole one also goes in ordinary state records, as to a new
    // owner, and has to be read the way it was written.
    internal override void Write(Component component, NetWriter writer)
    {
        writer.WriteBool(true);
        ((INetworkSerialize)component).OnSerialize(writer, true);
    }

    internal override void WriteChanges(Component component, NetWriter writer)
    {
        writer.WriteBool(false);
        ((INetworkSerialize)component).OnSerialize(writer, false);
    }

    internal override void Read(Component component, ref NetReader reader, bool initial) =>
        ((INetworkSerialize)component).OnDeserialize(ref reader, reader.ReadBool());

    // The component's own format: only it can read it, so the bytes are taken as they are. Its slot is the last
    // one, so what is left of its component's block is all its.
    internal override void Validate(Component component, ref NetReader reader, bool initial = false) =>
        reader.ReadBytes(reader.Remaining);

    internal override string FormatValue(Component component) => "Written by OnSerialize";
}
