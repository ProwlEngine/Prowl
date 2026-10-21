// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Prowl.Warp.Transports.Encryption;

/// <summary>
/// Optional AES-256-GCM message encryption over another transport, authenticated with a 256-bit
/// pre-shared key. Provision keys out of band; a key embedded in a public client is not an identity.
/// Nonces establish fresh directional session keys. This mode does not provide forward secrecy.
/// </summary>
public sealed class EncryptedTransport : Transport
{
    private readonly byte[] _key;
    private readonly TimeSpan _timeout;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<Connection, EncryptedConnection> _peers = new();
    private bool _disposed;
    public Transport Inner { get; }
    public override bool IsListening => Inner.IsListening;
    public override int LocalPort => Inner.LocalPort;
    public override bool IsEncrypted => true;

    public EncryptedTransport(Transport inner, byte[] key, TimeSpan? handshakeTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32) throw new ArgumentException("A 32-byte pre-shared key is required.", nameof(key));
        _timeout = handshakeTimeout ?? TimeSpan.FromSeconds(5);
        NetSessionOptions.ValidateDuration(_timeout, nameof(handshakeTimeout));
        Inner = inner;
        _key = (byte[])key.Clone();
    }

    public override void Listen() { ObjectDisposedException.ThrowIf(_disposed, this); Inner.Listen(); }
    public override Connection Connect(string address)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Connection inner = Inner.Connect(address);
        EncryptedConnection peer = new(this, NextConnectionId(), inner, _clock.Elapsed);
        _peers.Add(inner, peer);
        AddConnection(peer);
        return peer;
    }

    public override void Receive()
    {
        Inner.Receive();
        while (Inner.Poll(out TransportEvent ev))
        {
            if (ev.Type == TransportEventType.Connected && !_peers.ContainsKey(ev.Connection))
            {
                EncryptedConnection added = new(this, NextConnectionId(), ev.Connection, _clock.Elapsed);
                _peers.Add(ev.Connection, added);
                AddConnection(added);
            }
            if (!_peers.TryGetValue(ev.Connection, out EncryptedConnection? peer)) continue;
            switch (ev.Type)
            {
                case TransportEventType.Connected: peer.Start(); break;
                case TransportEventType.Data: peer.Receive(ev.Data.Span, ev.Delivery); break;
                case TransportEventType.Disconnected: Close(peer, ev.Reason); break;
            }
        }
    }

    public override void Flush()
    {
        foreach (EncryptedConnection peer in new List<EncryptedConnection>(_peers.Values))
            if (peer.State == ConnectionState.Connecting && _clock.Elapsed - peer.Created >= _timeout)
                Close(peer, DisconnectReason.Timeout);
        Inner.Flush();
    }

    public override void Shutdown()
    {
        foreach (EncryptedConnection peer in new List<EncryptedConnection>(_peers.Values))
            Close(peer, DisconnectReason.Requested);
        Inner.Shutdown();
        while (Inner.Poll(out _)) { }
    }

    protected override void DisposeCore()
    {
        _disposed = true;
        CryptographicOperations.ZeroMemory(_key);
        Inner.Dispose();
    }

    protected override void OnReceiveQueueOverflow(Connection connection) => Close((EncryptedConnection)connection, DisconnectReason.ResourceLimit);
    private void Close(EncryptedConnection peer, DisconnectReason reason)
    {
        if (!_peers.Remove(peer.InnerConnection)) return;
        peer.InnerConnection.Disconnect();
        peer.Clear();
        RaiseDisconnected(peer, reason);
    }

    private sealed class EncryptedConnection : Connection
    {
        private const int Overhead = 1 + 8 + 16;
        private readonly EncryptedTransport _owner;
        private readonly byte[] _clientNonce = new byte[32];
        private readonly byte[] _serverNonce = new byte[32];
        private readonly ulong[] _sent = new ulong[2], _received = new ulong[2];
        private ulong _unreliableWindow;
        private AesGcm? _encrypt, _decrypt;
        private int _stage;
        public Connection InnerConnection { get; }
        public TimeSpan Created { get; }
        public override TimeSpan RoundTripTime => InnerConnection.RoundTripTime;

        public EncryptedConnection(EncryptedTransport owner, int id, Connection inner, TimeSpan created)
            : base(owner, id, inner.Address, inner.IsOutgoing)
        { _owner = owner; InnerConnection = inner; Created = created; }

        public override int GetMaxMessageSize(Delivery delivery) => Math.Max(0, InnerConnection.GetMaxMessageSize(delivery) - Overhead);
        public override void Disconnect() => _owner.Close(this, DisconnectReason.Requested);
        public void Start()
        {
            if (!IsOutgoing) return;
            RandomNumberGenerator.Fill(_clientNonce);
            _stage = 1;
            SendHandshake(1);
        }

        private byte[] Mac(byte opcode)
        {
            // Domain separation also binds the protocol version, role, and both session nonces.
            byte[] transcript = new byte[80];
            Encoding.ASCII.GetBytes("WarpPSK-v1", transcript);
            transcript[15] = opcode;
            _clientNonce.CopyTo(transcript, 16);
            _serverNonce.CopyTo(transcript, 48);
            return HMACSHA256.HashData(_owner._key, transcript);
        }

        private void SendHandshake(byte opcode)
        {
            byte[] frame = new byte[opcode <= 2 ? 65 : 33];
            frame[0] = opcode;
            if (opcode <= 2) (opcode == 1 ? _clientNonce : _serverNonce).CopyTo(frame, 1);
            Mac(opcode).CopyTo(frame, frame.Length - 32);
            InnerConnection.Send(frame, Delivery.Reliable);
        }

        private void EstablishKeys()
        {
            byte[] salt = new byte[64];
            _clientNonce.CopyTo(salt, 0); _serverNonce.CopyTo(salt, 32);
            byte[] material = HKDF.DeriveKey(HashAlgorithmName.SHA256, _owner._key, 64, salt, "WarpPSK-v1 directional keys"u8.ToArray());
            _encrypt = new AesGcm(material.AsSpan(IsOutgoing ? 0 : 32, 32), 16);
            _decrypt = new AesGcm(material.AsSpan(IsOutgoing ? 32 : 0, 32), 16);
            CryptographicOperations.ZeroMemory(material);
        }

        public void Receive(ReadOnlySpan<byte> frame, Delivery delivery)
        {
            RecordPacketReceived(frame.Length);
            if (frame.IsEmpty) return;
            if (frame[0] == 16)
            {
                if (IsConnected) Decrypt(frame, delivery);
                return;
            }
            if (IsConnected) return;
            byte opcode = frame[0];
            bool expected = !IsOutgoing && (_stage == 0 && opcode == 1 || _stage == 2 && opcode == 3) ||
                            IsOutgoing && (_stage == 1 && opcode == 2 || _stage == 3 && opcode == 4);
            if (!expected || delivery != Delivery.Reliable || frame.Length != (opcode <= 2 ? 65 : 33))
            { _owner.Close(this, DisconnectReason.AuthenticationFailed); return; }
            if (opcode == 1) frame.Slice(1, 32).CopyTo(_clientNonce);
            if (opcode == 2) frame.Slice(1, 32).CopyTo(_serverNonce);
            if (!CryptographicOperations.FixedTimeEquals(Mac(opcode), frame[^32..]))
            { _owner.Close(this, DisconnectReason.AuthenticationFailed); return; }
            switch (opcode)
            {
                case 1:
                    RandomNumberGenerator.Fill(_serverNonce);
                    _stage = 2; SendHandshake(2); break;
                case 2:
                    EstablishKeys(); _stage = 3; SendHandshake(3); break;
                case 3:
                    EstablishKeys(); SendHandshake(4); _owner.RaiseConnected(this); break;
                case 4: _owner.RaiseConnected(this); break;
            }
        }

        public override void Send(ReadOnlySpan<byte> data, Delivery delivery)
        {
            ValidateSend(data, delivery);
            int channel = (int)delivery;
            if (_sent[channel] == ulong.MaxValue) { _owner.Close(this, DisconnectReason.ResourceLimit); return; }
            ulong sequence = ++_sent[channel];
            byte[] frame = ArrayPool<byte>.Shared.Rent(data.Length + Overhead);
            try
            {
                frame[0] = 16;
                BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(1), sequence);
                Span<byte> nonce = stackalloc byte[12];
                BinaryPrimitives.WriteUInt32LittleEndian(nonce, (uint)channel);
                BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], sequence);
                _encrypt!.Encrypt(nonce, data, frame.AsSpan(9, data.Length), frame.AsSpan(9 + data.Length, 16), nonce);
                InnerConnection.Send(frame.AsSpan(0, data.Length + Overhead), delivery);
                RecordPacketSent(data.Length + Overhead);
            }
            finally { ArrayPool<byte>.Shared.Return(frame); }
        }

        private void Decrypt(ReadOnlySpan<byte> frame, Delivery delivery)
        {
            if (frame.Length < Overhead || !Enum.IsDefined(delivery)) return;
            int channel = (int)delivery;
            ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(frame[1..]);
            if (sequence == 0) return;
            ulong last = _received[channel];
            if (sequence <= last && (delivery != Delivery.Unreliable || last - sequence >= 64 ||
                (_unreliableWindow & (1UL << (int)(last - sequence))) != 0)) return;
            int length = frame.Length - Overhead;
            byte[] plain = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                Span<byte> nonce = stackalloc byte[12];
                BinaryPrimitives.WriteUInt32LittleEndian(nonce, (uint)channel);
                BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], sequence);
                try { _decrypt!.Decrypt(nonce, frame.Slice(9, length), frame[^16..], plain.AsSpan(0, length), nonce); }
                catch (CryptographicException)
                {
                    if (delivery != Delivery.Unreliable) _owner.Close(this, DisconnectReason.AuthenticationFailed);
                    return;
                }
                if (delivery != Delivery.Unreliable && sequence != last + 1)
                { _owner.Close(this, DisconnectReason.AuthenticationFailed); return; }
                if (delivery == Delivery.Unreliable)
                {
                    if (sequence > last) _unreliableWindow = sequence - last >= 64 ? 1 : (_unreliableWindow << (int)(sequence - last)) | 1;
                    else _unreliableWindow |= 1UL << (int)(last - sequence);
                }
                _received[channel] = Math.Max(last, sequence);
                _owner.RaiseData(this, plain.AsSpan(0, length), delivery);
            }
            finally { ArrayPool<byte>.Shared.Return(plain, clearArray: true); }
        }

        public void Clear() { _encrypt?.Dispose(); _decrypt?.Dispose(); _encrypt = _decrypt = null; }
    }
}
