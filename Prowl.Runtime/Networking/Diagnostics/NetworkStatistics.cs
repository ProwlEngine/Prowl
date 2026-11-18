// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>How many messages and bytes went one way.</summary>
public readonly record struct NetworkTraffic(long Messages, long Bytes);

/// <summary>One engine message as it was sent or received: its kind, the peer at the other end and its size.</summary>
public readonly record struct NetworkMessageInfo(string Kind, NetPeer Peer, int Bytes);

/// <summary>
///     Engine message traffic since the last <see cref="Reset" />, in total and by kind, as Mirror's network statistics
///     give. Counted on the main thread as messages are handled and sent; payload bytes only, without transport headers.
/// </summary>
public static class NetworkStatistics
{
    private static readonly string[] s_names = BuildNames();
    private static readonly long[] s_receivedMessages = new long[s_names.Length], s_receivedBytes = new long[s_names.Length];
    private static readonly long[] s_sentMessages = new long[s_names.Length], s_sentBytes = new long[s_names.Length];

    /// <summary>Raised for each engine message handled from a peer.</summary>
    public static event Action<NetworkMessageInfo>? MessageReceived;

    /// <summary>Raised for each engine message sent to a peer.</summary>
    public static event Action<NetworkMessageInfo>? MessageSent;

    public static NetworkTraffic Received => Sum(s_receivedMessages, s_receivedBytes);

    public static NetworkTraffic Sent => Sum(s_sentMessages, s_sentBytes);

    /// <summary>Traffic of each kind of engine message that has any, received and sent.</summary>
    public static IEnumerable<(string Kind, NetworkTraffic Received, NetworkTraffic Sent)> ByKind()
    {
        for (int i = 0; i < s_names.Length; i++)
        {
            if (s_receivedMessages[i] != 0 || s_sentMessages[i] != 0)
            {
                yield return (s_names[i], new NetworkTraffic(s_receivedMessages[i], s_receivedBytes[i]),
                    new NetworkTraffic(s_sentMessages[i], s_sentBytes[i]));
            }
        }
    }

    public static void Reset()
    {
        Array.Clear(s_receivedMessages);
        Array.Clear(s_receivedBytes);
        Array.Clear(s_sentMessages);
        Array.Clear(s_sentBytes);
    }

    internal static void CountReceived(EngineMessage kind, NetPeer peer, int bytes)
    {
        int index = kind - EngineMessage.SceneSnapshot;
        if ((uint)index >= (uint)s_names.Length)
        {
            return;
        }

        s_receivedMessages[index]++;
        s_receivedBytes[index] += bytes;
        MessageReceived?.Invoke(new NetworkMessageInfo(s_names[index], peer, bytes));
    }

    internal static void CountSent(EngineMessage kind, NetPeer peer, int bytes)
    {
        int index = kind - EngineMessage.SceneSnapshot;
        if ((uint)index >= (uint)s_names.Length)
        {
            return;
        }

        s_sentMessages[index]++;
        s_sentBytes[index] += bytes;
        MessageSent?.Invoke(new NetworkMessageInfo(s_names[index], peer, bytes));
    }

    private static NetworkTraffic Sum(long[] messages, long[] bytes)
    {
        long count = 0, size = 0;
        for (int i = 0; i < messages.Length; i++)
        {
            count += messages[i];
            size += bytes[i];
        }

        return new NetworkTraffic(count, size);
    }

    private static string[] BuildNames()
    {
        int last = 0;
        foreach (EngineMessage kind in Enum.GetValues<EngineMessage>())
        {
            last = Math.Max(last, kind - EngineMessage.SceneSnapshot);
        }

        string[] names = new string[last + 1];
        for (int i = 0; i < names.Length; i++)
        {
            EngineMessage kind = (EngineMessage)(Networking.EngineMessageIds + i);
            names[i] = Enum.IsDefined(kind) ? kind.ToString() : $"Engine {i}";
        }

        return names;
    }
}
