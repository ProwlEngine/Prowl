// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.ComponentModel;

using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>The state a <see cref="NetworkBehaviour" /> writes in its <see cref="NetworkBehaviour.OnSerialize" />, as one more [Sync] slot of its type.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class NetworkSerializeMember() : SyncMember(CustomSlot, "OnSerialize")
{
    /// <summary>The slot no generated [Sync] property takes.</summary>
    public const uint CustomSlot = uint.MaxValue;

    // The state says itself whether it is whole, since a whole one also goes in ordinary state records, as to a new
    // owner, and has to be read the way it was written.
    internal override void Write(Component component, NetWriter writer)
    {
        writer.WriteBool(true);
        ((NetworkBehaviour)component).OnSerialize(writer, true);
    }

    internal override void WriteChanges(Component component, NetWriter writer)
    {
        writer.WriteBool(false);
        ((NetworkBehaviour)component).OnSerialize(writer, false);
    }

    internal override void Read(Component component, ref NetReader reader, bool initial) =>
        ((NetworkBehaviour)component).OnDeserialize(ref reader, reader.ReadBool());

    // Past the whole flag is the component's own format: only it can read it, so the bytes are taken as they are.
    // Its slot is the last one, so what is left of its component's block is all its.
    internal override void Validate(Component component, ref NetReader reader, bool initial = false)
    {
        reader.ReadBool();
        reader.ReadBytes(reader.Remaining);
    }

    internal override string FormatValue(Component component) => "Written by OnSerialize";
}
