// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Warp;
using Prowl.Warp.Transports.Kcp;
using Prowl.Warp.Transports.Local;

namespace Prowl.Runtime.Networking;

public static partial class Networking
{
    private static NetworkConfiguration s_configuration = new();
    private static readonly LocalNetwork s_localNetwork = new();

    /// <summary>A copy of the configured project defaults.</summary>
    public static NetworkConfiguration Configuration => s_configuration.ValidatedCopy();

    /// <summary>Applies replication timing now; transport and session options apply to subsequent sessions.</summary>
    public static void Configure(NetworkConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        s_configuration = configuration.ValidatedCopy();
        System?.Configure(s_configuration);
        TickRate = s_configuration.TickRate;
        InterpolationDelay = TimeSpan.FromMilliseconds(s_configuration.InterpolationMilliseconds);
    }

    /// <summary>Creates a transport using project defaults. Local transports share a network within this process only.</summary>
    internal static Transport CreateTransport(bool hosting)
    {
        if (s_configuration.Transport == NetworkTransport.Local)
        {
            return new LocalTransport(s_localNetwork,
                hosting ? s_configuration.LocalAddress : Guid.NewGuid().ToString());
        }

        return new KcpTransport(s_configuration.CreateKcpOptions());
    }

    /// <summary>Hosts using the project's transport and session defaults.</summary>
    public static NetSession Host(string name = "Host")
    {
        BeginSession();
        NetworkSystem.ValidateSceneMessageLimit(null);
        return Host(CreateTransport(true), name);
    }

    /// <summary>Joins using the project's transport and session defaults. KCP addresses may include a port.</summary>
    public static NetSession Join(string address, string name, ReadOnlySpan<byte> token = default)
    {
        BeginSession();
        NetworkSystem.ValidateSceneMessageLimit(null);
        return Join(CreateTransport(false), address, name, token);
    }
}
