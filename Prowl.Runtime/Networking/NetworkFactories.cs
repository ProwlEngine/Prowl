// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>What a network factory is asked to build: where, and the bytes the host passed along.</summary>
public readonly record struct NetworkSpawn(string Factory, Float3 Position, Quaternion Rotation, byte[] Data);

/// <summary>Builds a networked object in code, the same tree on every peer. Returns a new object in no scene yet.</summary>
public delegate GameObject NetworkFactory(in NetworkSpawn spawn);

/// <summary>Client side handlers that build and take back instances of one prefab, so they can be pooled.</summary>
internal static class NetworkPrefabHandlers
{
    private static readonly Dictionary<Guid, (Func<PrefabAsset, GameObject>? Spawn, Action<GameObject>? Unspawn)> s_handlers = new();

    internal static void Register(Guid prefab, Func<PrefabAsset, GameObject>? spawn, Action<GameObject>? unspawn)
    {
        lock (s_handlers)
        {
            s_handlers[prefab] = (spawn, unspawn);
        }
    }

    internal static bool Unregister(Guid prefab)
    {
        lock (s_handlers)
        {
            return s_handlers.Remove(prefab);
        }
    }

    internal static (Func<PrefabAsset, GameObject>? Spawn, Action<GameObject>? Unspawn) For(Guid prefab)
    {
        lock (s_handlers)
        {
            return s_handlers.GetValueOrDefault(prefab);
        }
    }
}

internal static class NetworkFactories
{
    private static readonly Dictionary<string, (NetworkFactory Build, Action<GameObject>? Destroy)> s_factories = new();

    internal static void Register(string name, NetworkFactory build, Action<GameObject>? destroy)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(build);
        lock (s_factories)
        {
            s_factories[name] = (build, destroy);
        }
    }

    internal static bool Unregister(string name)
    {
        lock (s_factories)
        {
            return s_factories.Remove(name);
        }
    }

    internal static bool TryGet(string name, out NetworkFactory build, out Action<GameObject>? destroy)
    {
        lock (s_factories)
        {
            if (s_factories.TryGetValue(name, out (NetworkFactory Build, Action<GameObject>? Destroy) factory))
            {
                (build, destroy) = factory;
                return true;
            }
        }

        build = null!;
        destroy = null;
        return false;
    }
}
