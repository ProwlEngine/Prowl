// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Prowl.Warp.Transports.Kcp;

/// <summary>
///     Reads a socket on one background thread and writes it on another, handing datagrams to and from the
///     thread that pumps the transport through queues.
/// </summary>
internal sealed class KcpIoThreads : IDisposable
{
    private const int MaxPooledAddresses = 4096;

    // What one queued datagram costs against the budget at least: its buffer, address and queue slot.
    private const int MinDatagramCost = 256;

    private readonly ConcurrentQueue<SocketAddress> _addresses = new();
    private readonly long _maxQueuedBytes;
    private readonly int _mtu;
    private readonly ConcurrentQueue<Datagram> _outgoing = new();
    private readonly ConcurrentQueue<Datagram> _received = new();
    private readonly Thread _receiver;
    private readonly Thread _sender;
    private readonly AutoResetEvent _sendSignal = new(false);
    private readonly Socket _socket;
    private long _queuedBytes;
    private long _outgoingBytes;
    private int _sendPending;
    private volatile bool _stopping;

    /// <param name="socket">A bound socket. It is switched to blocking mode for the receive thread.</param>
    /// <param name="mtu">Larger datagrams are dropped, as the inline path does.</param>
    /// <param name="maxQueuedBytes">
    ///     Budget for each direction's queue. A datagram costs its length, and at least 256 bytes. Beyond it
    ///     datagrams are dropped, like a full OS buffer.
    /// </param>
    public KcpIoThreads(Socket socket, int mtu, long maxQueuedBytes)
    {
        _socket = socket;
        _mtu = mtu;
        _maxQueuedBytes = maxQueuedBytes;
        socket.Blocking = true;
        _receiver = new Thread(ReceiveLoop) { IsBackground = true, Name = "Warp KCP receive" };
        _sender = new Thread(SendLoop) { IsBackground = true, Name = "Warp KCP send" };
        _receiver.Start();
        _sender.Start();
    }

    /// <summary>Datagrams received and not yet taken.</summary>
    public int Count => _received.Count;

    /// <summary>Joins the receive thread once the socket is closed, and frees whatever is still queued.</summary>
    public void Dispose()
    {
        StopSending();
        _receiver.Join();
        while (_received.TryDequeue(out Datagram datagram))
        {
            ArrayPool<byte>.Shared.Return(datagram.Buffer);
        }

        _sendSignal.Dispose();
    }

    /// <summary>Sends everything queued, then ends the send thread. Call before closing the socket.</summary>
    public void StopSending()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        _sendSignal.Set();
        _sender.Join();
    }

    /// <summary>
    ///     Takes the next received datagram, copying it into <paramref name="destination" />. <paramref name="from" />
    ///     is where it came from, valid until the next call.
    /// </summary>
    public bool TryTake(Span<byte> destination, ref SocketAddress? from, out int length)
    {
        length = 0;
        if (!_received.TryDequeue(out Datagram datagram))
        {
            return false;
        }

        Interlocked.Add(ref _queuedBytes, -Cost(datagram.Length));
        datagram.Buffer.AsSpan(0, datagram.Length).CopyTo(destination);
        ArrayPool<byte>.Shared.Return(datagram.Buffer);
        if (from != null)
        {
            ReturnAddress(from);
        }

        from = datagram.Address;
        length = datagram.Length;
        return true;
    }

    /// <summary>Queues a datagram for the send thread, copying the data and address. False if the queue is full.</summary>
    public bool Send(SocketAddress to, ReadOnlySpan<byte> datagram)
    {
        if (Interlocked.Read(ref _outgoingBytes) + Cost(datagram.Length) > _maxQueuedBytes)
        {
            return false;
        }

        Interlocked.Add(ref _outgoingBytes, Cost(datagram.Length));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(datagram.Length);
        datagram.CopyTo(buffer);
        _outgoing.Enqueue(new Datagram(buffer, datagram.Length, CopyAddress(to)));
        if (Interlocked.Exchange(ref _sendPending, 1) == 0)
        {
            _sendSignal.Set();
        }

        return true;
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[KcpPacket.MaxDatagramSize];
        SocketAddress address = new(_socket.AddressFamily, SocketAddress.GetMaximumAddressSize(_socket.AddressFamily));
        while (!_stopping)
        {
            int length;
            try
            {
                length = _socket.ReceiveFrom(buffer, SocketFlags.None, address);
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                if (_stopping || e is ObjectDisposedException)
                {
                    return;
                }

                // A reset or an oversized datagram loses just that packet. Anything else could repeat at once,
                // so back off rather than spin.
                if (e is SocketException { SocketErrorCode: not (SocketError.ConnectionReset or SocketError.MessageSize) })
                {
                    Thread.Sleep(1);
                }

                continue;
            }

            if (length == 0 || length > _mtu || Interlocked.Read(ref _queuedBytes) + Cost(length) > _maxQueuedBytes)
            {
                continue;
            }

            byte[] copy = ArrayPool<byte>.Shared.Rent(length);
            buffer.AsSpan(0, length).CopyTo(copy);
            Interlocked.Add(ref _queuedBytes, Cost(length));
            _received.Enqueue(new Datagram(copy, length, CopyAddress(address)));
        }
    }

    private void SendLoop()
    {
        while (true)
        {
            _sendSignal.WaitOne();
            Volatile.Write(ref _sendPending, 0);
            bool stopping = _stopping;
            while (_outgoing.TryDequeue(out Datagram datagram))
            {
                try
                {
                    _socket.SendTo(datagram.Buffer.AsSpan(0, datagram.Length), SocketFlags.None, datagram.Address);
                }
                catch (Exception e) when (e is SocketException or ObjectDisposedException)
                {
                    // UDP makes no promises; a failed send is just a lost packet.
                }

                Interlocked.Add(ref _outgoingBytes, -Cost(datagram.Length));
                ArrayPool<byte>.Shared.Return(datagram.Buffer);
                ReturnAddress(datagram.Address);
            }

            if (stopping)
            {
                return;
            }
        }
    }

    private static int Cost(int length) => Math.Max(length, MinDatagramCost);

    private SocketAddress CopyAddress(SocketAddress source)
    {
        if (!_addresses.TryDequeue(out SocketAddress? copy))
        {
            copy = new SocketAddress(source.Family, SocketAddress.GetMaximumAddressSize(source.Family));
        }

        source.Buffer.Span[..source.Size].CopyTo(copy.Buffer.Span);
        copy.Size = source.Size;
        return copy;
    }

    private void ReturnAddress(SocketAddress address)
    {
        if (_addresses.Count < MaxPooledAddresses)
        {
            _addresses.Enqueue(address);
        }
    }

    private readonly record struct Datagram(byte[] Buffer, int Length, SocketAddress Address);
}
