// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Prowl.Warp.Tests;

/// <summary>An event with its data copied out, so it outlives the next Poll.</summary>
public readonly record struct Received(
    TransportEventType Type,
    Connection Connection,
    byte[] Data,
    Delivery Delivery,
    DisconnectReason Reason,
    byte Channel = 0);

/// <summary>Pumps a set of transports like a game loop would, recording every event.</summary>
public sealed class Pump
{
    private readonly Dictionary<Transport, List<Received>> _events = new();
    private readonly Transport[] _transports;

    public Pump(params Transport[] transports)
    {
        _transports = transports;
        foreach (Transport transport in transports)
        {
            _events[transport] = new List<Received>();
        }
    }

    public List<Received> EventsOf(Transport transport) => _events[transport];

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
        {
            _events[transport].Add(new Received(ev.Type, ev.Connection, ev.Data.ToArray(), ev.Delivery, ev.Reason,
                ev.Channel));
        }
    }

    /// <summary>Steps until <paramref name="condition" /> holds, failing after <paramref name="timeoutMs" />.</summary>
    public void Until(Func<bool> condition, int timeoutMs = 5000, string? what = null)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException($"Timed out after {timeoutMs} ms waiting for {what ?? "condition"}.");
            }

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

public static class TestData
{
    public static byte[] Pattern(int length, int seed)
    {
        byte[] data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }
}
