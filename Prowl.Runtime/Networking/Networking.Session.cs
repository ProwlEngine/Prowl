// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Networking.Diagnostics;
using Prowl.Runtime.Networking.Systems;
using Prowl.Runtime.Tasks;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

public static partial class Networking
{
    private static NetworkingThread? s_thread;
    private static Transport? s_transport;
    private static bool s_ending;

    private static void BeginSession()
    {
        MainThreadContext.AssertMainThread();
        if (s_ending || Session != null || System != null || s_transport != null)
        {
            throw new InvalidOperationException(
                "A network session is running or still shutting down. Call Networking.Shutdown first.");
        }
    }

    private static void PrepareTransport(Transport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        bool worker = s_configuration.UsesWorker(transport);
        s_thread = worker ? new NetworkingThread(transport) : null;
        s_transport = transport;
    }

    private static NetSession AttachSession(NetSession session)
    {
        Session = session;
        System = new NetworkSystem(s_configuration, session);
        session.Ended += (reason, message) =>
        {
            s_ending = true;
            Session = null;
            NetworkDiagnostics.Record("Session", "Local", session.LocalPeer, Guid.Empty, $"Ended: {reason}: {message}");
            try
            {
                ReleaseTransport();
            }
            finally
            {
                try
                {
                    System?.Dispose();
                    System = null;
                }
                finally
                {
                    session.Dispose();
                }
            }

            s_ending = false;
            Debug.Log(message == null
                ? $"[Network] Session ended: {reason}."
                : $"[Network] Session ended: {reason}: {message}");
        };
        System.Attach(session);
        return session;
    }

    /// <summary>Leaves the session, or ends it for everyone when hosting. Safe to call repeatedly.</summary>
    public static void Shutdown(string? message = null)
    {
        if (s_ending && s_transport == null)
        {
            return;
        }

        if (Session == null && s_transport == null)
        {
            return;
        }

        MainThreadContext.AssertMainThread();
        using FrameStatScope stats = FrameStats.Network.Scope();
        NetSession? session = Session;
        session?.Shutdown(message);
        if (Session == session)
        {
            ReleaseTransport();
            Session = null;
            System?.Dispose();
            System = null;
            s_ending = false;
        }

        session?.Dispose();
    }

    private static void ReleaseTransport()
    {
        // Clear ownership only after disposal succeeds. A stalled worker can be retried.
        if (s_thread != null)
        {
            s_thread.Dispose();
        }
        else
        {
            s_transport?.Dispose();
        }

        s_thread = null;
        s_transport = null;
    }

    internal static void Receive()
    {
        NetSession? session = Session;
        NetworkSystem? system = System;
        if (session == null || system == null)
        {
            return;
        }

        MainThreadContext.AssertMainThread();
        using FrameStatScope stats = FrameStats.Network.Scope();
        NetworkingThread? thread = s_thread;
        if (thread != null)
        {
            while (session.State != SessionState.Ended && thread.TryReadEvent(out TransportEvent ev))
            {
                session.ProcessEvent(ev);
            }
        }

        session.Receive();
        if (System == system)
        {
            system.Receive();
        }
    }

    internal static void Flush()
    {
        NetSession? session = Session;
        NetworkSystem? system = System;
        if (session == null || system == null)
        {
            return;
        }

        MainThreadContext.AssertMainThread();
        using FrameStatScope stats = FrameStats.Network.Scope();
        system.Flush();
        if (Session != session)
        {
            return;
        }

        session.Flush();
        if (Session == session)
        {
            s_thread?.RequestFlush();
        }
    }
}
