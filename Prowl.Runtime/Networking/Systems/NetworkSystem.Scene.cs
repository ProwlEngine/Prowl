// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

using Jitter2.Dynamics;

using Prowl.Echo;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Resources;
using Prowl.Warp;

// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

// Sharing the host's scene with clients.
internal sealed partial class NetworkSystem
{
    internal void OnHostSceneLoaded()
    {
        if (Session is not { IsHost: true, State: SessionState.Active } session)
        {
            return;
        }

        // Objects that survived the swap (DontDestroyOnLoad) keep their network state; the rest went with the old scene.
        using FrameStatScope stats = FrameStats.Network.Scope();
        ReplicatedScene = Scene.Current;
        SceneGeneration++;
        LocalJoinState = NetworkJoinState.Loading;
        Index.Clear();
        Sync.Dirty.Clear();
        foreach (NetworkObject entry in Entries.Values.ToArray())
        {
            if (!entry.GameObject.IsValid() || entry.GameObject.Scene != ReplicatedScene)
            {
                entry.NotifyDespawn();
            }

            if (Session != session)
            {
                return;
            }
        }

        Entries.RemoveWhere(e => !e.GameObject.IsValid() || e.GameObject.Scene != ReplicatedScene);
        RegisterSceneObjects(ReplicatedScene);

        if (Session != session)
        {
            return;
        }

        DispatchNetworkListener(e => e.OnNetworkLoading());
        if (Session != session)
        {
            return;
        }

        SetLocalReady();
        if (Session != session)
        {
            return;
        }

        SendSnapshot(session.Peers.Where(peer => !peer.IsLocal).ToArray());
    }

    /// <summary>Starts <paramref name="peers" /> loading the scene, from one snapshot written for all of them.</summary>
    internal void SendSnapshot(params NetPeer[] peers)
    {
        if (Session is not { } session || ReplicatedScene == null || peers.Length == 0)
        {
            return;
        }

        foreach (NetPeer peer in peers)
        {
            BeginPeerLoad(peer);
        }

        using NetWriter message = new(64 * 1024);
        using FrameStatScope stats = FrameStats.Network.Scope();
        WriteSnapshot(ReplicatedScene, message);
        // Writing runs components' serialization callbacks, which could end the session.
        if (Session != session)
        {
            return;
        }

        foreach (NetPeer peer in peers)
        {
            SendEngine(peer, EngineMessage.SceneSnapshot, message.WrittenSpan);
        }
    }

    /// <summary>
    ///     Writes <paramref name="scene" /> as clients receive it: the scene itself, minus anything that is
    ///     <see cref="NetworkMode.Never" />, then the networked objects' owners and every [Sync] value.
    /// </summary>
    internal void WriteSnapshot(Scene scene, NetWriter output)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        EchoObject echo = null!;
        NetworkSerialization.WritingSnapshot = true;
        try
        {
            AsSetUp(() => echo = Serializer.Serialize(scene, SnapshotContext()));
        }
        finally
        {
            NetworkSerialization.WritingSnapshot = false;
        }

        new SceneSnapshotMessage(SceneSnapshotMessage.CurrentFormat, Math.Max(SceneGeneration, 1),
            scene.Name, Compress(echo)).Write(output);

        new ObjectStateBlock((uint)Entries.Count).Write(output);
        foreach (NetworkObject entry in Entries.Values)
        {
            new SnapshotObjectMessage(entry.GameObject.Identifier, entry.Owner, entry.Orphaned,
                entry.Generation, entry.NetId).Write(output);
        }

        Sync.WriteSyncState(output, scene.AllObjects);
    }

    /// <summary>Builds the scene a snapshot carries, leaving the reader on the network state after it.</summary>
    /// <exception cref="InvalidDataException">The snapshot can't be read.</exception>
    internal Scene ReadSnapshot(ref NetReader reader) => ReadSnapshot(ref reader, out _);

    internal Scene ReadSnapshot(ref NetReader reader, out ulong generation)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        SceneSnapshotMessage message = SceneSnapshotMessage.Read(ref reader);
        generation = message.Generation;
        Scene? scene = Serializer.Deserialize<Scene>(Decompress(message.Snapshot), SnapshotContext());
        if (scene.IsNotValid())
        {
            throw new InvalidDataException("The host's scene snapshot was empty.");
        }

        scene!.Name = message.Name;
        return scene;
    }

    internal void OnSceneSnapshot(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        if (delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("Scene snapshots must be reliable.");
        }

        NetSession? session = Session;
        Scene scene = ReadSnapshot(ref reader, out ulong sceneGeneration);
        bool queuedForActivation = false;
        try
        {
            if (Session != session || sceneGeneration <= SceneGeneration)
            {
                return;
            }

            SceneGeneration = sceneGeneration;
            LocalJoinState = NetworkJoinState.Loading;
            CatchUpApplied = false;
            ReadyDeadline = Now + Configuration.SceneLoadTimeoutSeconds;

            foreach (NetworkObject entry in Entries.Values.ToArray())
            {
                entry.NotifyDespawn();
                if (Session != session)
                {
                    return;
                }
            }

            // Incoming messages can resolve this scene before it becomes current at the frame boundary.
            ReplicatedScene = scene;
            Index.Clear();
            IndexFrame = -1;
            Entries.Clear();
            Sync.Dirty.Clear();

            uint count = ObjectStateBlock.Read(ref reader).Count;
            for (uint i = 0; i < count; i++)
            {
                SnapshotObjectMessage state = SnapshotObjectMessage.Read(ref reader);
                Guid id = state.Identifier;
                int owner = state.OwnerId;
                NetworkOrphaned orphaned = state.Orphaned;
                uint generation = state.Generation, netId = state.NetId;
                if (FindByIdentifier(id) is GameObject go)
                {
                    go.Network.ApplyOrphaned(orphaned);
                    NetworkObject entry = new(this, go) { Owner = owner, Generation = generation, NetId = netId };
                    Entries.Add(entry);
                    entry.UpdatePhysics();
                }
            }

            Sync.ApplySyncState(sender, ref reader, null, true);
            foreach (NetworkObject entry in Entries.Values.ToArray())
            {
                entry.NotifySpawn();
                if (Session != session)
                {
                    return;
                }
            }

            if (!reader.IsAtEnd)
            {
                throw new InvalidDataException("Scene snapshot contains trailing bytes.");
            }

            DispatchNetworkListener(e => e.OnNetworkLoading());
            if (Session != session)
            {
                return;
            }

            Scene.Load(scene);
            queuedForActivation = true;
        }
        finally
        {
            // Scene.Load takes ownership only on success. Cancellation and malformed metadata must
            // release the deserialized scene, including its physics and rendering resources.
            if (!queuedForActivation)
            {
                scene.Dispose();
            }
        }
    }

    /// <summary>Registers the scene's own networked objects, which belong to the host until it spawns or hands them out.</summary>
    internal void RegisterSceneObjects(Scene scene)
    {
        NetSession? session = Session;
        foreach (GameObject go in scene.AllObjects.ToArray())
        {
            if (Session != session || !IsActive || ReplicatedScene != scene)
            {
                break;
            }

            if (NetworkAccessor.ModeOf(go) == NetworkMode.Object && go.Network.Root == go &&
                !NetworkAccessor.IsExcludedFromNetwork(go) &&
                !Entries.Contains(go.Identifier))
            {
                NetworkObject entry = new(this, go) { Generation = NextGeneration(), NetId = NextNetId() };
                Entries.Add(entry);
                entry.NotifySpawn();
            }
        }
    }

    // Writing, a reference to a NetworkMode.Never object becomes an empty link; reading, that link becomes null.
    internal SerializationContext SnapshotContext() =>
        new() { ExternalReferences = new NetworkSerialization.SnapshotReferences() };

    internal byte[] Compress(EchoObject echo)
    {
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Fastest, true))
        using (BinaryWriter writer = new(zlib))
        {
            // SnapshotDecoder validates this fixed-width wire layout before Echo reads it.
            echo.WriteToBinary(writer,
                new BinarySerializationOptions { EncodingMode = BinaryEncodingMode.Performance });
        }

        return compressed.ToArray();
    }

    internal EchoObject Decompress(ReadOnlySpan<byte> data)
    {
        try
        {
            return SnapshotDecoder.Decode(data, Configuration);
        }
        catch (Exception e) when (e is not InvalidDataException)
        {
            // Reported to the session as malformed data, which ends it, rather than escaping into the game loop.
            throw new InvalidDataException($"Network data could not be read: {e.Message}", e);
        }
    }

    internal void AsSetUp(Action write)
    {
        List<Rigidbody3D> switched = new();
        foreach (NetworkObject entry in Entries.Values)
        foreach (KeyValuePair<Rigidbody3D, MotionType> body in entry.ProxyBodies)
        {
            if (body.Key.IsValid())
            {
                body.Key.MotionType = body.Value;
                switched.Add(body.Key);
            }
        }

        try
        {
            write();
        }
        finally
        {
            foreach (Rigidbody3D body in switched)
            {
                body.MotionType = MotionType.Kinematic;
            }
        }
    }
}
