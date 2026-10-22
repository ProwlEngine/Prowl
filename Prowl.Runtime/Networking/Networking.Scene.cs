// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Resources;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// Sharing the host's scene with clients.
public static partial class Networking
{
    // Bumped whenever the snapshot layout changes, so a mismatched client fails clearly.
    private const byte SnapshotFormat = 7;

    private static void OnHostSceneLoaded()
    {
        if (Session is not { IsHost: true, State: SessionState.Active } session)
        {
            return;
        }

        // Objects that survived the swap (DontDestroyOnLoad) keep their network state; the rest went with the old scene.
        using FrameStatScope stats = FrameStats.Network.Scope();
        s_scene = Scene.Current;
        s_sceneGeneration++;
        s_joinState = NetworkJoinState.Loading;
        s_index.Clear();
        s_dirty.Clear();
        foreach (NetworkEntry entry in s_entries.Values.ToArray())
        {
            if (!entry.GameObject.IsValid() || entry.GameObject.Scene != s_scene) NotifyDespawn(entry);
            if (Session != session) return;
        }
        s_entries.RemoveWhere(e => !e.GameObject.IsValid() || e.GameObject.Scene != s_scene);
        RegisterSceneObjects(s_scene);

        if (Session != session) return;
        DispatchNetworkListener(e => e.OnNetworkLoading());
        if (Session != session) return;
        SetLocalReady();
        if (Session != session) return;

        foreach (NetPeer peer in session.Peers.ToArray())
        {
            if (!peer.IsLocal)
            {
                SendSnapshot(peer);
            }
        }
    }

    private static void SendSnapshot(NetPeer peer)
    {
        if (Session is not { } session || s_scene == null)
        {
            return;
        }

        BeginPeerLoad(peer);
        if (Session != session || s_scene == null) return;

        using NetWriter message = new(64 * 1024);
        using FrameStatScope stats = FrameStats.Network.Scope();
        WriteSnapshot(s_scene, message);
        SendEngine(peer, EngineMessage.SceneSnapshot, message.WrittenSpan);
    }

    /// <summary>
    ///     Writes <paramref name="scene" /> as clients receive it: the scene itself, minus anything that is
    ///     <see cref="NetworkMode.Never" />, then the networked objects' owners and every [Sync] value.
    /// </summary>
    internal static void WriteSnapshot(Scene scene, NetWriter output)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        EchoObject echo = null!;
        GameObject.WritingNetworkSnapshot = true;
        try
        {
            AsSetUp(() => echo = Serializer.Serialize(scene));
        }
        finally
        {
            GameObject.WritingNetworkSnapshot = false;
        }

        output.WriteByte(SnapshotFormat);
        output.WriteUInt64(Math.Max(s_sceneGeneration, 1));
        output.WriteString(scene.Name);
        output.WriteBytesWithLength(Compress(echo));

        output.WriteVarUInt32((uint)s_entries.Count);
        foreach (NetworkEntry entry in s_entries.Values)
        {
            output.WriteGuid(entry.GameObject.Identifier);
            output.WriteVarUInt32((uint)(entry.Owner + 1));
            output.WriteByte((byte)entry.Orphaned);
            output.WriteUInt32(entry.Generation);
        }

        WriteSyncState(output, scene.AllObjects);
    }

    /// <summary>Builds the scene a snapshot carries, leaving the reader on the network state after it.</summary>
    /// <exception cref="InvalidDataException">The snapshot can't be read.</exception>
    internal static Scene ReadSnapshot(ref NetReader reader) => ReadSnapshot(ref reader, out _);

    private static Scene ReadSnapshot(ref NetReader reader, out ulong generation)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        byte format = reader.ReadByte();
        if (format != SnapshotFormat)
        {
            throw new InvalidDataException(
                $"The host sent scene snapshot format {format}, this build reads {SnapshotFormat}.");
        }

        generation = reader.ReadUInt64();
        if (generation == 0) throw new InvalidDataException("Scene generation cannot be zero.");
        string name = reader.ReadString();
        Scene? scene = Serializer.Deserialize<Scene>(Decompress(reader.ReadBytesWithLength()));
        if (scene.IsNotValid())
        {
            throw new InvalidDataException("The host's scene snapshot was empty.");
        }

        scene!.Name = name;
        return scene;
    }

    private static void OnSceneSnapshot(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        if (delivery != Delivery.Reliable) throw new InvalidDataException("Scene snapshots must be reliable.");
        NetSession? session = Session;
        Scene scene = ReadSnapshot(ref reader, out ulong sceneGeneration);
        bool queuedForActivation = false;
        try
        {
            if (Session != session || sceneGeneration <= s_sceneGeneration)
                return;

            s_sceneGeneration = sceneGeneration;
            s_joinState = NetworkJoinState.Loading;
            s_catchUpApplied = false;
            s_readyDeadline = Now + s_configuration.SceneLoadTimeoutSeconds;

            foreach (NetworkEntry entry in s_entries.Values.ToArray())
            {
                NotifyDespawn(entry);
                if (Session != session)
                    return;
            }

            // Incoming messages can resolve this scene before it becomes current at the frame boundary.
            s_scene = scene;
            s_index.Clear();
            s_indexFrame = -1;
            s_entries.Clear();
            s_stateRevisions.Clear();
            s_dirty.Clear();

            uint count = reader.ReadVarUInt32();
            for (uint i = 0; i < count; i++)
            {
                Guid id = reader.ReadGuid();
                int owner = (int)reader.ReadVarUInt32() - 1;
                NetworkOrphaned orphaned = ReadOrphaned(ref reader);
                uint generation = reader.ReadUInt32();
                if (FindByIdentifier(id) is GameObject go)
                {
                    go.ApplyNetworkOrphaned(orphaned);
                    s_entries.Add(new NetworkEntry(go) { Owner = owner, Orphaned = orphaned, Generation = generation });
                    ControlChanged(go);
                }
            }

            ApplySyncState(sender, ref reader, null, initial: true);
            foreach (NetworkEntry entry in s_entries.Values.ToArray())
            {
                NotifySpawn(entry);
                if (Session != session)
                    return;
            }

            if (!reader.IsAtEnd) throw new InvalidDataException("Scene snapshot contains trailing bytes.");
            DispatchNetworkListener(e => e.OnNetworkLoading());
            if (Session != session) return;
            Scene.Load(scene);
            queuedForActivation = true;
        }
        finally
        {
            // Scene.Load takes ownership only on success. Cancellation and malformed metadata must
            // release the deserialized scene, including its physics and rendering resources.
            if (!queuedForActivation)
                scene.Dispose();
        }
    }

    /// <summary>Registers the scene's own networked objects, which belong to the host until it spawns or hands them out.</summary>
    private static void RegisterSceneObjects(Scene scene)
    {
        NetSession? session = Session;
        foreach (GameObject go in scene.AllObjects.ToArray())
        {
            if (Session != session || !IsActive || s_scene != scene)
                break;
            if (go.NetworkMode == NetworkMode.Object && go.NetworkRoot == go && !go.IsNetworkExcluded &&
                !s_entries.Contains(go.Identifier))
            {
                NetworkEntry entry = new(go) { Orphaned = go.NetworkOrphaned, Generation = NextGeneration() };
                s_entries.Add(entry);
                NotifySpawn(entry);
            }
        }
    }

    internal static byte[] Compress(EchoObject echo)
    {
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Fastest, true))
        using (BinaryWriter writer = new(zlib))
        {
            echo.WriteToBinary(writer);
        }

        return compressed.ToArray();
    }

    internal static EchoObject Decompress(ReadOnlySpan<byte> data)
    {
        try
        {
            using MemoryStream stream = new(data.ToArray());
            using ZLibStream zlib = new(stream, CompressionMode.Decompress);
            using BinaryReader binary = new(zlib);
            return EchoObject.ReadFromBinary(binary);
        }
        catch (Exception e) when (e is not InvalidDataException)
        {
            // Reported to the session as malformed data, which ends it, rather than escaping into the game loop.
            throw new InvalidDataException($"Network data could not be read: {e.Message}", e);
        }
    }
}
