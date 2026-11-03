// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Warp;

/// <summary>The single-threaded driver used by the Host/Join convenience APIs.</summary>
/// <remarks>
///     A handler runs while its event's payload is borrowed from the transport, and polling again would release
///     it. So a Receive from inside a handler does nothing, and a Shutdown waits until the handler returns.
/// </remarks>
internal sealed class SessionPump(Transport transport)
{
    private bool _dispatching;
    private bool _shutdownPending;

    public void Listen() => transport.Listen();
    public Connection Connect(string address) => transport.Connect(address);

    public void Receive(NetSession session)
    {
        if (_dispatching)
        {
            return;
        }

        transport.Receive();
        _dispatching = true;
        try
        {
            while (session.State != SessionState.Ended && transport.Poll(out TransportEvent ev))
            {
                session.ProcessEvent(ev);
            }
        }
        finally
        {
            _dispatching = false;
            if (_shutdownPending)
            {
                _shutdownPending = false;
                Shutdown();
            }
        }
    }

    public void Flush() => transport.Flush();

    public void Shutdown()
    {
        if (_dispatching)
        {
            _shutdownPending = true;
            return;
        }

        transport.Shutdown();
        while (transport.Poll(out _))
        {
        }
    }
}
