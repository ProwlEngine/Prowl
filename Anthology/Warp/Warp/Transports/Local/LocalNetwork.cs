// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Prowl.Warp.Transports.Local;

/// <summary>
///     An in-process "network" that <see cref="LocalTransport" />s connect through. Transports only see
///     listeners registered on the same <see cref="LocalNetwork" />, so separate instances (one per test,
///     say) never interfere. Thread safe, so the transports on it can be pumped from different threads.
/// </summary>
public sealed class LocalNetwork
{
    private readonly ConcurrentDictionary<string, LocalTransport> _listeners = new(StringComparer.Ordinal);

    internal bool TryRegister(string address, LocalTransport transport) => _listeners.TryAdd(address, transport);

    internal void Unregister(string address, LocalTransport transport) =>
        _listeners.TryRemove(new KeyValuePair<string, LocalTransport>(address, transport));

    internal LocalTransport? Find(string address) =>
        _listeners.TryGetValue(address, out LocalTransport? transport) ? transport : null;
}
