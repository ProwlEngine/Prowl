// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Ember;
using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// A tree's state as one frame: its controller's [Sync] values and transforms. Ownership handoff carries one so the
// new controller starts from the old one's final state, ahead of the ownership change itself.
public static partial class Networking
{
    private static void WriteHandoffState(GameObject target, NetWriter output, bool includeAll = false)
    {
        NetworkEntry? entry = FindEntry(target);
        GameObject root = entry != null ? entry.GameObject : target;
        output.WriteInt32(Session!.LocalPeer!.Id);
        output.WriteUInt64(checked(++s_outgoingStateRevision));
        output.WriteGuid(root.Identifier);
        output.WriteUInt32(entry?.Generation ?? 0);
        using NetWriter values = new(), transforms = new();
        int count = 0;
        uint tick = ++s_tick;
        foreach (GameObject go in root.GetChildrenDeep().Prepend(root))
        {
            if (NetworkAccessor.IsExcludedFromNetwork(go) || FindEntry(go) != entry)
            {
                continue;
            }

            foreach (Component part in go.GetComponents())
            {
                foreach (SyncMember sync in NetworkMembers.Of(part).Syncs)
                {
                    if (!includeAll && !HasControl(part, sync.Flags))
                    {
                        continue;
                    }

                    if (WriteValue(values, part, sync.Slot, false, true))
                    {
                        count++;
                    }
                }
            }

            if (!go.Network.HasTransformSync || (!includeAll && !HasControl(go)))
            {
                continue;
            }

            transforms.WriteGuid(go.Identifier);
            transforms.WriteUInt32(tick);
            NetSerializers.WriteFloat3(transforms, go.Transform.LocalPosition);
            NetSerializers.WriteQuaternion(transforms, go.Transform.LocalRotation);
            NetSerializers.WriteFloat3(transforms, go.Transform.LocalScale);
        }

        using NetWriter syncs = new();
        syncs.WriteVarUInt32((uint)count);
        syncs.WriteBytes(values.WrittenSpan);
        output.WriteBytesWithLength(syncs.WrittenSpan);
        output.WriteBytesWithLength(transforms.WrittenSpan);
    }

    // Validation is a separate pass so a malformed final record cannot leave earlier records applied.
    private static bool ProcessHandoffStates(GameObject target, ReadOnlySpan<byte> states, NetPeer sender, bool apply,
        bool forceTransforms = false)
    {
        NetSession? session = Session;
        if (session == null)
        {
            return false;
        }

        NetReader reader = new(states);
        uint count = reader.ReadVarUInt32();
        if (count > 1)
        {
            throw new InvalidDataException("Invalid handoff state count.");
        }

        for (uint i = 0; i < count; i++)
        {
            HandoffFrame frame = new(reader.ReadBytesWithLength());
            bool allowedOrigin = frame.OriginId == sender.Id || (!IsHost && frame.OriginId == NetPeer.HostId);
            if (!allowedOrigin || frame.Revision == 0)
            {
                throw new InvalidDataException("Invalid handoff state origin or revision.");
            }

            if (!session.TryGetPeer(frame.OriginId, out NetPeer? origin))
            {
                return false;
            }

            NetworkEntry? entry = FindEntry(target);
            GameObject root = entry != null ? entry.GameObject : target;
            if (root.Identifier != frame.RootId || (entry?.Generation ?? 0) != frame.Generation)
            {
                return false;
            }

            if (!apply)
            {
                ValidateHandoffSyncs(frame, origin, root);
            }

            ProcessHandoffTransforms(frame, origin, root, entry, apply, forceTransforms);

            if (apply)
            {
                NetReader syncs = new(frame.Syncs);
                ApplySyncState(origin, ref syncs, null, false, frame.Revision);
                if (Session != session || !target.IsValid() || target.IsDestroyQueued ||
                    GenerationOf(target) != frame.Generation)
                {
                    return false;
                }
            }
        }

        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("Handoff states contain trailing bytes.");
        }

        return true;
    }

    private static void ValidateHandoffSyncs(HandoffFrame frame, NetPeer origin, GameObject root)
    {
        NetReader reader = new(frame.Syncs);
        uint count = reader.ReadVarUInt32();
        for (uint i = 0; i < count; i++)
        {
            Guid id = reader.ReadGuid();
            uint slot = reader.ReadVarUInt32();
            uint generation = reader.ReadUInt32();
            NetReader value = new(reader.ReadBytesWithLength());
            if (FindByIdentifier(id) is not Component component || NetworkAccessor.IsExcludedFromNetwork(component.GameObject) ||
                !InHandoffTree(component.GameObject, root) || generation != frame.Generation ||
                NetworkMembers.Of(component).Sync(slot) is not { } sync)
            {
                throw new InvalidDataException("Handoff state contains an unknown Sync value.");
            }

            bool authorized = origin.IsHost || ((sync.Flags & SyncFlags.FromHost) == 0 &&
                                                OwnerOf(component.GameObject) == origin.Id);
            if (!authorized)
            {
                throw new InvalidDataException("Handoff state contains an unauthorized Sync value.");
            }

            try
            {
                sync.Validate(component, ref value);
            }
            catch (Exception e) when (e is ArgumentException or OverflowException or FormatException
                                          or IndexOutOfRangeException)
            {
                throw new InvalidDataException("Handoff contains malformed Sync state.", e);
            }

            if (!value.IsAtEnd)
            {
                throw new InvalidDataException("Handoff Sync value has trailing bytes.");
            }
        }

        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("Handoff state has trailing Sync bytes.");
        }
    }

    private static void ProcessHandoffTransforms(HandoffFrame frame, NetPeer origin, GameObject root,
        NetworkEntry? entry, bool apply, bool forceTransforms)
    {
        NetReader reader = new(frame.Transforms);
        while (!reader.IsAtEnd)
        {
            HandoffTransform pose = new(reader.ReadGuid(), reader.ReadUInt32(), NetSerializers.ReadFloat3(ref reader),
                NetSerializers.ReadQuaternion(ref reader), NetSerializers.ReadFloat3(ref reader));
            if (FindByIdentifier(pose.ObjectId) is not GameObject go || !InHandoffTree(go, root) || NetworkAccessor.IsExcludedFromNetwork(go) ||
                (!origin.IsHost && OwnerOf(go) != origin.Id))
            {
                throw new InvalidDataException("Handoff state contains an unknown or unauthorized transform.");
            }

            float rotationLength = Quaternion.LengthSquared(pose.Rotation);
            if (!Finite(pose.Position) || !Finite(pose.Scale) || !float.IsFinite(rotationLength) ||
                rotationLength < 1e-12f)
            {
                throw new InvalidDataException("Handoff state contains an invalid transform.");
            }

            if (apply && go.Network.HasTransformSync && (forceTransforms || !HasControl(go)))
            {
                ApplyHandoffTransform(go, root, entry, frame, pose, forceTransforms);
            }
        }
    }

    private static void ApplyHandoffTransform(GameObject go, GameObject root, NetworkEntry? entry,
        HandoffFrame frame, HandoffTransform pose, bool forceTransforms)
    {
        (Guid Identifier, uint MaxValue, int OriginId, uint Generation) key = (go.Identifier, uint.MaxValue,
            frame.OriginId, frame.Generation);
        if (s_stateRevisions.GetValueOrDefault(key) >= frame.Revision)
        {
            return;
        }

        if (!forceTransforms && entry != null && go == root && entry.HasReceived &&
            (int)(pose.Tick - entry.LastReceivedTick) < 0)
        {
            return;
        }

        s_stateRevisions[key] = frame.Revision;
        if (entry != null && go == root)
        {
            entry.HasReceived = true;
            entry.LastReceivedTick = pose.Tick;
            entry.Samples.Clear();
            entry.Timeline.Clear();
        }

        ApplyNetworkTransform(go, pose.Position, Quaternion.Normalize(pose.Rotation), pose.Scale);
    }

    private static bool InHandoffTree(GameObject go, GameObject root)
    {
        if (FindEntry(go) != FindEntry(root))
        {
            return false;
        }

        for (GameObject? ancestor = go; ancestor.IsValid(); ancestor = ancestor.Parent)
        {
            if (ancestor == root)
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct HandoffTransform(
        Guid ObjectId,
        uint Tick,
        Float3 Position,
        Quaternion Rotation,
        Float3 Scale);

    private readonly ref struct HandoffFrame
    {
        public readonly int OriginId;
        public readonly ulong Revision;
        public readonly Guid RootId;
        public readonly uint Generation;
        [ReloadIgnore] public readonly ReadOnlySpan<byte> Syncs;
        [ReloadIgnore] public readonly ReadOnlySpan<byte> Transforms;

        public HandoffFrame(ReadOnlySpan<byte> payload)
        {
            NetReader reader = new(payload);
            OriginId = reader.ReadInt32();
            Revision = reader.ReadUInt64();
            RootId = reader.ReadGuid();
            Generation = reader.ReadUInt32();
            Syncs = reader.ReadBytesWithLength();
            Transforms = reader.ReadBytesWithLength();
            if (!reader.IsAtEnd)
            {
                throw new InvalidDataException("Handoff state contains trailing bytes.");
            }
        }
    }
}
