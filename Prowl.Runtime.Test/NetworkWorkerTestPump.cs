// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using Prowl.Warp;

namespace Prowl.Runtime.Test;

/// <summary>An event with its data copied out, so it outlives the next Poll.</summary>
internal readonly record struct WorkerReceived(TransportEventType Type, Connection Connection, byte[] Data, Delivery Delivery, DisconnectReason Reason);

/// <summary>Pumps a set of transports like a game loop would, recording every event.</summary>
internal sealed class WorkerPump
{
    private readonly Transport[] _transports;
    private readonly Dictionary<Transport, List<WorkerReceived>> _events = new();

    public WorkerPump(params Transport[] transports)
    {
        _transports = transports;
        foreach (Transport transport in transports)
            _events[transport] = new List<WorkerReceived>();
    }

    public List<WorkerReceived> EventsOf(Transport transport) => _events[transport];

    public void Step()
    {
        foreach (Transport transport in _transports)
        {
            transport.Receive();
            Drain(transport);
            // Flush raises events too (timeouts), so drain again to see them this step.
            transport.Flush();
            Drain(transport);
        }
    }

    private void Drain(Transport transport)
    {
        while (transport.Poll(out TransportEvent ev))
            _events[transport].Add(new WorkerReceived(ev.Type, ev.Connection, ev.Data.ToArray(), ev.Delivery, ev.Reason));
    }

    /// <summary>Steps until <paramref name="condition"/> holds, failing after <paramref name="timeoutMs"/>.</summary>
    public void Until(Func<bool> condition, int timeoutMs = 5000, string? what = null)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException($"Timed out after {timeoutMs} ms waiting for {what ?? "condition"}.");
            Step();
            Thread.Sleep(1);
        }
    }

    /// <summary>Steps for a fixed time, for asserting that something does not happen.</summary>
    public void For(int milliseconds)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            Step();
            Thread.Sleep(1);
        }
    }
}

internal sealed class WorkerSessionProbe(NetSession session)
{
    public NetSession Session { get; } = session;
    public static void PumpUntil(Func<bool> condition, params WorkerSessionProbe[] probes)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > 5000) throw new TimeoutException("Sessions did not reach the expected state.");
            foreach (WorkerSessionProbe probe in probes) probe.Session.Update();
            Thread.Sleep(1);
        }
    }
}
