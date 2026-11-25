// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Keeps a networked object's transform in sync: whoever controls the object sends
///     its position, rotation and scale, and everyone else follows, smoothed. A networked object without one keeps its
///     transform to itself. Put a second one on the object with a <see cref="Target" /> for a child that moves on its own.
///     It has to be part of the object as its prefab, factory or scene builds it: one added later, by game code once
///     the object runs, is not networked. A static object, or one with every channel off, sends nothing.
/// </summary>
[AddComponentMenu("Networking/Network Transform")]
public sealed class NetworkTransform : NetworkBehaviour
{
    /// <summary>Send the position.</summary>
    public bool SyncPosition = true;

    /// <summary>Send the rotation.</summary>
    public bool SyncRotation = true;

    /// <summary>Send the scale.</summary>
    public bool SyncScale = true;

    /// <summary>Blend between received poses. Off, the object jumps to each one as it arrives.</summary>
    public bool Interpolate = true;

    /// <summary>
    ///     The object whose transform this sends: its own when not set, or a child of the networked object. It
    ///     is part of how the object is built and is never sent, so every peer has to set the same one.
    /// </summary>
    public GameObject? Target;

    private TransformSync? _sync;

    /// <summary>What it last sent, and what it received and shows of it.</summary>
    internal TransformSync Sync => _sync ??= new TransformSync(this);

    /// <summary>The networked object it belongs to while spawned, and its place among that object's transforms.</summary>
    internal NetworkObject? Entry;

    internal byte Index;
    internal double LastCaptureTime;

    // The tick it last had a turn to send on, once it has had one.
    internal uint LastSendTick;
    internal bool HasSendTick;

    // Its place in what the transform system keeps for each peer, and a stamp that outdates what that place held before.
    internal int SendSlot = -1;
    internal uint SendStamp;

    // Its place among the moving transforms waiting to be passed on at the next tick, or -1.
    internal int RelayIndex = -1;

    // True once a rest pose of it may be owed to some peer.
    internal bool PoseOwed;

    // Whether it synced when last checked, so a body it drives is handed back once it stops.
    internal bool WasSyncing;

    /// <summary>The object it moves.</summary>
    internal GameObject Moved => Target.IsValid() ? Target! : GameObject;

    internal TransformChannels Channels =>
        (SyncPosition ? TransformChannels.Position : 0) |
        (SyncRotation ? TransformChannels.Rotation : 0) |
        (SyncScale ? TransformChannels.Scale : 0);

    /// <summary>False for a static object, or with every channel off: nothing to send.</summary>
    internal bool Syncs => Channels != 0 && Moved.IsValid() && !Moved.IsStatic;

    /// <summary>Ticks apart the pose goes out while it moves: <see cref="NetworkBehaviour.SyncInterval" /> in ticks, rounded, at least 1.</summary>
    internal int Interval =>
        float.IsFinite(SyncInterval) && SyncInterval > 0
            ? (int)Math.Clamp(Math.Round(SyncInterval / NetworkSystem.TickInterval), 1, byte.MaxValue)
            : 1;

    /// <summary>Sets the channels it syncs on the object it moves, leaving the others as they are.</summary>
    internal void Apply(Float3 position, Quaternion rotation, Float3 scale)
    {
        Transform transform = Moved.Transform;
        if (SyncPosition)
        {
            transform.LocalPosition = position;
        }

        if (SyncRotation)
        {
            transform.LocalRotation = rotation;
        }

        if (SyncScale)
        {
            transform.LocalScale = scale;
        }
    }

    /// <summary>
    ///     Moves the object to a world pose at once, so peers jump there instead of blending across. Only the peer
    ///     that controls it may, while it is networked.
    /// </summary>
    public void Teleport(Float3 position, Quaternion rotation)
    {
        if (Networking.System is { IsActive: true } system && system.FindEntry(GameObject) is { } entry)
        {
            if (!system.HasControl(entry.GameObject))
            {
                throw new InvalidOperationException("Only the peer that controls a networked object can teleport it.");
            }

            Sync.Teleport();
        }

        Moved.Transform.Position = position;
        Moved.Transform.Rotation = rotation;
    }
}
