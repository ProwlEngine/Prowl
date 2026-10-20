// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Warp.Transports.Steam;

/// <summary>
/// Placeholder for a transport over Steam Networking Sockets. Not implemented yet: every member throws
/// <see cref="NotImplementedException"/>.
/// </summary>
/// <remarks>
/// <para>The plan, so the stub reflects the intended shape:</para>
/// <list type="bullet">
/// <item>No managed dependency. Call the Steamworks flat C API (<c>SteamAPI_ISteamNetworkingSockets_*</c>)
/// directly through P/Invoke; the game ships <c>steam_api</c> itself, as it already must to use Steam.</item>
/// <item>Addresses are SteamIDs as decimal strings. <see cref="Transport.Listen"/> opens a P2P listen
/// socket, so connections route through Valve's relay network: NAT traversal and encryption come free.</item>
/// <item><see cref="Delivery.Reliable"/> maps to <c>k_nSteamNetworkingSend_Reliable</c> and
/// <see cref="Delivery.Unreliable"/> to <c>k_nSteamNetworkingSend_Unreliable</c>, both with
/// <c>NoNagle</c>, since <see cref="Transport.Flush"/> already batches sends per tick.</item>
/// <item><see cref="Transport.Receive"/> drains <c>ReceiveMessagesOnPollGroup</c> and
/// connection status callbacks; Steam does the reliability, so <see cref="Transport.Flush"/> only
/// calls <c>FlushMessagesOnConnection</c>.</item>
/// </list>
/// </remarks>
public sealed class SteamTransport : Transport
{
    public override bool IsListening => false;

    public override void Listen() => throw NotImplemented();

    public override Connection Connect(string address) => throw NotImplemented();

    public override void Receive() => throw NotImplemented();

    public override void Flush() => throw NotImplemented();

    public override void Shutdown()
    {
        // Nothing was ever opened, and Dispose calls this, so it must not throw.
    }

    private static NotImplementedException NotImplemented() => new("SteamTransport is not implemented yet. Use KcpTransport or LocalTransport.");
}
