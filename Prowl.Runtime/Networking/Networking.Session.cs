// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Tasks;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

public static partial class Networking
{
    private static readonly TimeSpan s_workerShutdownTimeout = TimeSpan.FromSeconds(5);
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
        s_transport = s_configuration.UsesWorker(transport) ? CreateWorker(transport) : transport;
    }

    /// <summary>Runs <paramref name="transport" /> on a networking worker, with the same queue budgets it has itself.</summary>
    internal static ThreadedTransport CreateWorker(Transport transport)
    {
        TransportCapabilities capabilities = transport.Capabilities;
        if (!capabilities.IsAvailable || capabilities.Execution == TransportExecution.CreatingThreadRequired)
        {
            throw new NotSupportedException("This transport cannot run on a networking worker.");
        }

        return new ThreadedTransport(transport)
        {
            MaxQueuedDataEventsPerConnection = transport.MaxQueuedDataEventsPerConnection,
            MaxQueuedDataBytesPerConnection = transport.MaxQueuedDataBytesPerConnection,
            MaxQueuedDataBytes = transport.MaxQueuedDataBytes
        };
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
            Session = null;
            System?.Dispose();
            System = null;
            s_ending = false;
            ReleaseTransport();
        }

        session?.Dispose();
    }

    private static void ReleaseTransport()
    {
        // Ownership is kept until the worker stops, so a stalled one can be shut down again later.
        if (s_transport is ThreadedTransport worker)
        {
            worker.Shutdown(s_workerShutdownTimeout);
        }

        s_transport?.Dispose();
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
        if (s_transport is ThreadedTransport worker && !ReceiveFromWorker(worker, session))
        {
            return;
        }

        session.Receive();
        if (System == system)
        {
            system.Receive();
        }
    }

    /// <summary>Hands the worker's events to the session. False once a failed worker has ended the session.</summary>
    private static bool ReceiveFromWorker(ThreadedTransport worker, NetSession session)
    {
        try
        {
            worker.Receive();
        }
        catch (Exception failure)
        {
            Debug.LogError($"[Network] The networking worker failed: {failure}");
            Shutdown("The networking worker failed.");
            return false;
        }

        while (session.State != SessionState.Ended && worker.Poll(out TransportEvent ev))
        {
            session.ProcessEvent(ev);
        }

        return Session == session;
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
        if (Session == session && s_transport is ThreadedTransport worker)
        {
            worker.Flush();
        }
    }
}
