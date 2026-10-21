// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Warp;

/// <summary>The single-threaded driver used by the Host/Join convenience APIs.</summary>
internal sealed class SessionPump(Transport transport)
{
    public void Listen() => transport.Listen();
    public Connection Connect(string address) => transport.Connect(address);

    public void Receive(NetSession session)
    {
        transport.Receive();
        while (session.State != SessionState.Ended && transport.Poll(out TransportEvent ev)) session.ProcessEvent(ev);
    }

    public void Flush() => transport.Flush();

    public void Shutdown()
    {
        transport.Shutdown();
        while (transport.Poll(out _)) { }
    }
}
