// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Prowl.Runtime.Networking.Objects;

internal sealed class NetworkObjectTable
{
    private readonly Dictionary<Guid, NetworkObject> _byId = new();
    private readonly Dictionary<uint, NetworkObject> _byNetId = new();

    public int Count => _byId.Count;

    public IEnumerable<NetworkObject> Values => _byId.Values;

    public void Add(NetworkObject entry)
    {
        Remove(entry.GameObject.Identifier);
        _byId[entry.GameObject.Identifier] = entry;
        if (entry.NetId != 0)
        {
            _byNetId[entry.NetId] = entry;
        }
    }

    public bool Contains(Guid id) => _byId.ContainsKey(id);

    public bool TryGetValue(Guid id, [NotNullWhen(true)] out NetworkObject? entry) => _byId.TryGetValue(id, out entry);

    public bool TryGetNet(uint netId, [NotNullWhen(true)] out NetworkObject? entry) =>
        _byNetId.TryGetValue(netId, out entry);

    public bool Remove(Guid id) => Remove(id, out _);

    public bool Remove(Guid id, [NotNullWhen(true)] out NetworkObject? entry)
    {
        if (!_byId.Remove(id, out entry))
        {
            return false;
        }

        if (_byNetId.TryGetValue(entry.NetId, out NetworkObject? indexed) && indexed == entry)
        {
            _byNetId.Remove(entry.NetId);
        }

        return true;
    }

    public void RemoveWhere(Func<NetworkObject, bool> predicate)
    {
        foreach (NetworkObject entry in _byId.Values.Where(predicate).ToArray())
        {
            Remove(entry.GameObject.Identifier);
        }
    }

    public void Clear()
    {
        _byId.Clear();
        _byNetId.Clear();
    }
}
