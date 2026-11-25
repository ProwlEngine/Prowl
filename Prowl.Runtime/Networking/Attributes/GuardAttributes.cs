// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Prowl.Runtime.Networking;

/// <summary>
///     The method only runs where <see cref="NetworkBehaviour.IsServer" /> holds. Anywhere else it returns at once,
///     with default values, and warns the first time unless <see cref="Quiet" />. On a method of a NetworkBehaviour.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ServerAttribute : Attribute
{
    /// <summary>Return without the warning, for a callback that runs on every peer.</summary>
    public bool Quiet { get; set; }
}

/// <summary>
///     The method only runs where <see cref="NetworkBehaviour.IsClient" /> holds. Anywhere else it returns at once,
///     with default values, and warns the first time unless <see cref="Quiet" />. On a method of a NetworkBehaviour.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ClientAttribute : Attribute
{
    /// <summary>Return without the warning, for a callback that runs on every peer.</summary>
    public bool Quiet { get; set; }
}

/// <summary>What the script compiler puts at the top of a <see cref="ServerAttribute" /> or <see cref="ClientAttribute" /> method.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class NetworkGuard
{
    private static readonly HashSet<(Type, string)> s_warned = new();

    /// <summary>True when the method may run here. False warns once for the method, unless quiet.</summary>
    public static bool Allows(NetworkBehaviour behaviour, string method, bool server, bool quiet)
    {
        if (server ? behaviour.IsServer : behaviour.IsClient)
        {
            return true;
        }

        if (!quiet && s_warned.Add((behaviour.GetType(), method)))
        {
            Debug.LogWarning($"[Network] {behaviour.GetType().Name}.{method} only runs on the {(server ? "host" : "client")}, " +
                             "so the call here was skipped.");
        }

        return false;
    }
}
