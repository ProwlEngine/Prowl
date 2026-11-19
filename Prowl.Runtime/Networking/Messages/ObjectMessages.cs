// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Courier;
using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Spawn header followed by a SyncBlock of initial component values. A root goes into section SectionSlot minus one,
///     or the active one for 0. Snapshot holds the compressed tree, or a <see cref="TemplateSpawn" /> when Template is set.
/// </summary>
internal readonly ref struct SpawnMessage(
    Guid identifier,
    Guid parentId,
    uint sectionSlot,
    int ownerId,
    NetworkOrphaned orphaned,
    uint generation,
    uint netId,
    ReadOnlySpan<byte> snapshot,
    bool replace = false,
    uint tick = 0,
    bool template = false)
{
    // Set in the orphaned byte when the object replaces any copy the client still has. The host tick then follows NetId.
    private const byte ReplaceBit = 0x80;
    private const byte TemplateBit = 0x40;

    internal readonly Guid Identifier = identifier;
    internal readonly Guid ParentId = parentId;
    internal readonly uint SectionSlot = sectionSlot;
    internal readonly int OwnerId = ownerId;
    internal readonly NetworkOrphaned Orphaned = orphaned;
    internal readonly uint Generation = generation;
    internal readonly uint NetId = netId;
    internal readonly ReadOnlySpan<byte> Snapshot = snapshot;
    internal readonly bool Replace = replace;
    internal readonly uint Tick = tick;
    internal readonly bool Template = template;

    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteGuid(ParentId);
        writer.WriteVarUInt32(SectionSlot);
        writer.WriteVarUInt32((uint)(OwnerId + 1));
        writer.WriteByte((byte)((byte)Orphaned | (Replace ? ReplaceBit : 0) | (Template ? TemplateBit : 0)));
        writer.WriteUInt32(Generation);
        writer.WriteVarUInt32(NetId);
        if (Replace)
        {
            writer.WriteVarUInt32(Tick);
        }

        writer.WriteBytesWithLength(Snapshot);
    }

    internal static SpawnMessage Read(ref NetReader reader)
    {
        Guid identifier = reader.ReadGuid(), parentId = reader.ReadGuid();
        uint sectionSlot = reader.ReadVarUInt32();
        int ownerId = (int)reader.ReadVarUInt32() - 1;
        byte policy = reader.ReadByte();
        uint generation = reader.ReadUInt32();
        uint netId = NetworkValidation.NetId(reader.ReadVarUInt32());
        bool replace = (policy & ReplaceBit) != 0;
        uint tick = replace ? reader.ReadVarUInt32() : 0;
        return new SpawnMessage(identifier, parentId, sectionSlot, ownerId,
            NetworkValidation.Orphaned((NetworkOrphaned)(policy & ~(ReplaceBit | TemplateBit))), generation, netId,
            reader.ReadBytesWithLength(), replace, tick, (policy & TemplateBit) != 0);
    }
}

/// <summary>A despawn, with its reason only when it is not <see cref="NetworkDespawnReason.Destroyed" />.</summary>
internal readonly record struct DespawnMessage(Guid Identifier, NetworkDespawnReason Reason = NetworkDespawnReason.Destroyed)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        if (Reason != NetworkDespawnReason.Destroyed)
        {
            writer.WriteByte((byte)Reason);
        }
    }

    internal static DespawnMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.IsAtEnd ? NetworkDespawnReason.Destroyed : NetworkValidation.DespawnReason((NetworkDespawnReason)reader.ReadByte()));
}

internal readonly record struct OwnerMessage(Guid Identifier, int OwnerId, uint Generation)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteVarUInt32((uint)(OwnerId + 1));
        writer.WriteUInt32(Generation);
    }

    internal static OwnerMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        (int)reader.ReadVarUInt32() - 1,
        reader.ReadUInt32());
}

internal readonly record struct OrphanedMessage(Guid Identifier, uint Generation, NetworkOrphaned Orphaned)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteUInt32(Generation);
        writer.WriteByte((byte)Orphaned);
    }

    internal static OrphanedMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadUInt32(),
        NetworkValidation.Orphaned((NetworkOrphaned)reader.ReadByte()));
}

internal readonly record struct NetworkFlagsMessage(Guid Identifier, uint Generation, NetworkFlags Flags)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteUInt32(Generation);
        writer.WriteByte((byte)Flags);
    }

    internal static NetworkFlagsMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadUInt32(),
        (NetworkFlags)reader.ReadByte());
}

internal readonly record struct SendIntervalMessage(Guid Identifier, uint Generation, byte Interval)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteUInt32(Generation);
        writer.WriteByte(Interval);
    }

    internal static SendIntervalMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadUInt32(),
        reader.ReadByte());
}

/// <summary>
///     A spawn by prefab, factory or scene: what builds it, the identifiers its tree takes in build order, and the
///     root's state. A factory spawn names the factory and carries the bytes the host passed it, a prefab spawn names
///     the asset, and a scene spawn names nothing more: the object is the one the peer's own copy of the scene holds.
/// </summary>
internal readonly record struct TemplateSpawn(
    Guid Prefab,
    string? Factory,
    byte[] Data,
    Guid[] Identifiers,
    Float3 Position,
    Quaternion Rotation,
    Float3 Scale,
    bool Enabled,
    NetworkFlags Flags,
    byte SendInterval,
    byte ComponentCount,
    uint ComponentHash,
    bool Scene = false)
{
    private const byte PrefabKind = 0, FactoryKind = 1, SceneKind = 2;

    internal void Write(NetWriter writer)
    {
        writer.WriteByte(Scene ? SceneKind : Factory != null ? FactoryKind : PrefabKind);
        if (Scene)
        {
        }
        else if (Factory != null)
        {
            writer.WriteString(Factory);
            writer.WriteBytesWithLength(Data);
        }
        else
        {
            writer.WriteGuid(Prefab);
        }

        writer.WriteVarUInt32((uint)Identifiers.Length);
        foreach (Guid id in Identifiers)
        {
            writer.WriteGuid(id);
        }

        NetSerializers.WriteFloat3(writer, Position);
        NetSerializers.WriteQuaternion(writer, Rotation);
        NetSerializers.WriteFloat3(writer, Scale);
        writer.WriteBool(Enabled);
        writer.WriteByte((byte)Flags);
        writer.WriteByte(SendInterval);
        writer.WriteByte(ComponentCount);
        writer.WriteUInt32(ComponentHash);
    }

    internal static TemplateSpawn Read(ref NetReader reader)
    {
        Guid prefab = Guid.Empty;
        string? factory = null;
        byte[] data = [];
        byte kind = reader.ReadByte();
        if (kind > SceneKind)
        {
            throw new InvalidDataException("A template spawn has an unknown kind.");
        }

        if (kind == SceneKind)
        {
        }
        else if (kind == FactoryKind)
        {
            factory = reader.ReadString();
            data = reader.ReadBytesWithLength().ToArray();
            if (string.IsNullOrEmpty(factory))
            {
                throw new InvalidDataException("A factory spawn has no factory name.");
            }
        }
        else
        {
            prefab = reader.ReadGuid();
        }

        uint count = reader.ReadVarUInt32();
        if (count == 0 || count > reader.Remaining / 16)
        {
            throw new InvalidDataException("A template spawn has an invalid identifier count.");
        }

        Guid[] identifiers = new Guid[count];
        for (int i = 0; i < identifiers.Length; i++)
        {
            identifiers[i] = reader.ReadGuid();
        }

        Float3 position = NetSerializers.ReadFloat3(ref reader);
        Quaternion rotation = NetSerializers.ReadQuaternion(ref reader);
        Float3 scale = NetSerializers.ReadFloat3(ref reader);
        bool enabled = reader.ReadBool();
        NetworkFlags flags = (NetworkFlags)reader.ReadByte();
        byte interval = reader.ReadByte();
        byte componentCount = reader.ReadByte();
        uint componentHash = reader.ReadUInt32();
        NetworkValidation.Transform(0, position, rotation, scale);
        if (!NetworkAccessor.ValidFlags(flags) || interval == 0 ||
            componentCount > NetworkComponents.MaxPerObject)
        {
            throw new InvalidDataException("A template spawn has invalid network settings.");
        }

        return new TemplateSpawn(prefab, factory, data, identifiers, position, rotation, scale, enabled, flags, interval,
            componentCount, componentHash, kind == SceneKind);
    }
}
