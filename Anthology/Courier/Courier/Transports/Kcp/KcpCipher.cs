// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Prowl.Courier.Transports.Kcp;

/// <summary>
///     Seals and opens one connection's session packets with AES-256-GCM. Each direction has its own key, and
///     every packet carries a counter that forms the nonce and lets the receiver drop replays.
/// </summary>
internal sealed class KcpCipher : IDisposable
{
    public const int CounterSize = 8;
    public const int TagSize = 16;

    /// <summary>Bytes a sealed packet adds to the plain one.</summary>
    public const int Overhead = CounterSize + TagSize;

    /// <summary>An uncompressed P-256 point: 0x04, then X and Y.</summary>
    public const int PublicKeySize = 65;

    /// <summary>An ECDSA P-256 signature in IEEE P1363 form.</summary>
    public const int SignatureSize = 64;

    /// <summary>Each direction moves to fresh keys every 2^24 packets.</summary>
    public const int DefaultEpochBits = 24;

    /// <summary>Proof in the Accept that the host derived the same keys.</summary>
    public const int ConfirmationSize = 16;

    private static readonly byte[] ConfirmationLabel = Encoding.ASCII.GetBytes("Prowl.Courier.Kcp.5 key confirmation");

    // How far behind the newest packet a late one may still arrive. Kept as a ring of bits.
    private const int ReplayWindow = 1024;

    // The P-256 field prime and curve constant b, big-endian. The curve is y^2 = x^3 - 3x + b.
    private static readonly BigInteger FieldPrime = Unsigned(Convert.FromHexString(
        "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF"));

    private static readonly BigInteger CurveB = Unsigned(Convert.FromHexString(
        "5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B"));
    private static readonly byte[] Label = Encoding.ASCII.GetBytes("Prowl.Courier.Kcp.5");
    private static readonly byte[] SignatureLabel = Encoding.ASCII.GetBytes("Prowl.Courier.Kcp.5 host signature");

    private static readonly byte[] KeyLabel = Encoding.ASCII.GetBytes("key");
    private static readonly byte[] NextLabel = Encoding.ASCII.GetBytes("next");

    // Each direction's keys come from a chain secret that moves forward one step per epoch of packets. Old
    // secrets are wiped, so the keys of earlier epochs can't be recovered from later state.
    private readonly int _epochBits;
    private readonly byte[] _receiveChain = new byte[32];
    private ulong _receiveEpoch;
    private AesGcm _receive;
    private AesGcm? _receivePrevious;
    private readonly uint _receiveSalt;
    private readonly byte[] _sendChain = new byte[32];
    private ulong _sendEpoch;
    private AesGcm _send;
    private readonly uint _sendSalt;
    private ulong _highest;
    private bool _received;
    private ulong _nextCounter;
    private readonly ulong[] _window = new ulong[ReplayWindow / 64];

    private KcpCipher(ReadOnlySpan<byte> keys, bool isClient, int epochBits)
    {
        ReadOnlySpan<byte> clientChain = keys[..32], hostChain = keys[32..64];
        uint clientSalt = BinaryPrimitives.ReadUInt32LittleEndian(keys[64..]);
        uint hostSalt = BinaryPrimitives.ReadUInt32LittleEndian(keys[68..]);
        (isClient ? clientChain : hostChain).CopyTo(_sendChain);
        (isClient ? hostChain : clientChain).CopyTo(_receiveChain);
        _send = KeyFor(_sendChain);
        _receive = KeyFor(_receiveChain);
        _sendSalt = isClient ? clientSalt : hostSalt;
        _receiveSalt = isClient ? hostSalt : clientSalt;
        _epochBits = epochBits;
    }

    public void Dispose()
    {
        _send.Dispose();
        _receive.Dispose();
        _receivePrevious?.Dispose();
        CryptographicOperations.ZeroMemory(_sendChain);
        CryptographicOperations.ZeroMemory(_receiveChain);
    }

    private static AesGcm KeyFor(ReadOnlySpan<byte> chain)
    {
        Span<byte> key = stackalloc byte[32];
        HKDF.Expand(HashAlgorithmName.SHA256, chain, key, KeyLabel);
        AesGcm aes = new(key, TagSize);
        CryptographicOperations.ZeroMemory(key);
        return aes;
    }

    /// <summary>Moves a chain secret one epoch forward in place, wiping the old one.</summary>
    private static void Advance(Span<byte> chain)
    {
        Span<byte> next = stackalloc byte[32];
        HKDF.Expand(HashAlgorithmName.SHA256, chain, next, NextLabel);
        next.CopyTo(chain);
        CryptographicOperations.ZeroMemory(next);
    }

    /// <summary>A fresh P-256 key pair for one handshake, and its public half in uncompressed form.</summary>
    public static ECDiffieHellman CreateKeyExchange(out byte[] publicKey)
    {
        ECDiffieHellman exchange = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        ECParameters parameters = exchange.ExportParameters(false);
        publicKey = new byte[PublicKeySize];
        publicKey[0] = 4;
        parameters.Q.X!.CopyTo(publicKey, 1);
        parameters.Q.Y!.CopyTo(publicKey, 33);
        return exchange;
    }

    /// <summary>What the host signs: a label, the protocol id, both public keys and the ids of this handshake.</summary>
    public static byte[] Transcript(ReadOnlySpan<byte> clientPublic, ReadOnlySpan<byte> hostPublic, uint nonce,
        uint sessionId, uint protocolId)
    {
        byte[] transcript = new byte[SignatureLabel.Length + 4 + PublicKeySize * 2 + 8];
        Span<byte> rest = transcript;
        SignatureLabel.CopyTo(rest);
        rest = rest[SignatureLabel.Length..];
        BinaryPrimitives.WriteUInt32LittleEndian(rest, protocolId);
        clientPublic.CopyTo(rest[4..]);
        hostPublic.CopyTo(rest[(4 + PublicKeySize)..]);
        BinaryPrimitives.WriteUInt32LittleEndian(rest[(4 + PublicKeySize * 2)..], nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(rest[(8 + PublicKeySize * 2)..], sessionId);
        return transcript;
    }

    /// <summary>
    ///     Agrees on keys with the remote public key. Null if that key is not a valid P-256 point.
    ///     <paramref name="confirmation" /> is a tag only an end holding the same keys can produce.
    /// </summary>
    public static KcpCipher? Create(ECDiffieHellman local, ReadOnlySpan<byte> clientPublic,
        ReadOnlySpan<byte> hostPublic, uint nonce, uint sessionId, bool isClient, Span<byte> confirmation,
        int epochBits = DefaultEpochBits)
    {
        ReadOnlySpan<byte> remote = isClient ? hostPublic : clientPublic;
        if (!IsValidPublicKey(remote))
        {
            return null;
        }

        byte[] secret;
        try
        {
            using ECDiffieHellman peer = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = remote[1..33].ToArray(), Y = remote[33..].ToArray() }
            });
            secret = local.DeriveKeyFromHash(peer.PublicKey, HashAlgorithmName.SHA256);
        }
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException or ArgumentException)
        {
            return null;
        }

        Span<byte> salt = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(salt, nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(salt[4..], sessionId);
        byte[] info = new byte[Label.Length + PublicKeySize * 2];
        Label.CopyTo(info, 0);
        clientPublic.CopyTo(info.AsSpan(Label.Length));
        hostPublic.CopyTo(info.AsSpan(Label.Length + PublicKeySize));

        Span<byte> keys = stackalloc byte[104];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, keys, salt, info);
        CryptographicOperations.ZeroMemory(secret);
        Span<byte> tag = stackalloc byte[32];
        HMACSHA256.HashData(keys[72..], ConfirmationLabel, tag);
        tag[..ConfirmationSize].CopyTo(confirmation);
        KcpCipher cipher = new(keys, isClient, epochBits);
        CryptographicOperations.ZeroMemory(keys);
        return cipher;
    }

    /// <summary>Whether <paramref name="key" /> is an uncompressed point on P-256. Checked before any key work.</summary>
    public static bool IsValidPublicKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != PublicKeySize || key[0] != 4)
        {
            return false;
        }

        BigInteger x = Unsigned(key[1..33]), y = Unsigned(key[33..]);
        if (x >= FieldPrime || y >= FieldPrime)
        {
            return false;
        }

        BigInteger right = (BigInteger.ModPow(x, 3, FieldPrime) - 3 * x + CurveB) % FieldPrime;
        if (right < 0)
        {
            right += FieldPrime;
        }

        return BigInteger.ModPow(y, 2, FieldPrime) == right;
    }

    private static BigInteger Unsigned(ReadOnlySpan<byte> bigEndian) => new(bigEndian, true, true);

    /// <summary>Writes <c>[header][counter][ciphertext][tag]</c> and returns its length. The header and counter are authenticated too.</summary>
    public int Seal(ReadOnlySpan<byte> header, ReadOnlySpan<byte> plain, Span<byte> destination)
    {
        while (_nextCounter >> _epochBits > _sendEpoch)
        {
            Advance(_sendChain);
            _send.Dispose();
            _send = KeyFor(_sendChain);
            _sendEpoch++;
        }

        header.CopyTo(destination);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[header.Length..], _nextCounter);
        Span<byte> nonce = stackalloc byte[12];
        WriteNonce(nonce, _sendSalt, _nextCounter++);

        int aad = header.Length + CounterSize;
        _send.Encrypt(nonce, plain, destination.Slice(aad, plain.Length),
            destination.Slice(aad + plain.Length, TagSize), destination[..aad]);
        return aad + plain.Length + TagSize;
    }

    /// <summary>
    ///     Checks and decrypts a sealed packet whose header is <paramref name="headerSize" /> bytes. False for
    ///     anything forged, damaged or already seen, and with <c>newestOnly</c> for anything older than the newest seen.
    /// </summary>
    public bool Open(ReadOnlySpan<byte> packet, int headerSize, Span<byte> plain, out int length,
        bool newestOnly = false)
    {
        length = packet.Length - headerSize - Overhead;
        int aad = headerSize + CounterSize;
        if (length < 0)
        {
            return false;
        }

        ulong counter = BinaryPrimitives.ReadUInt64LittleEndian(packet[headerSize..]);
        if (!IsFresh(counter) || (newestOnly && _received && counter <= _highest))
        {
            return false;
        }

        // Packets may come from this epoch, the one before it, or the next, which moves the keys forward.
        ulong epoch = counter >> _epochBits;
        AesGcm? key = epoch == _receiveEpoch ? _receive : epoch + 1 == _receiveEpoch ? _receivePrevious : null;
        byte[]? nextChain = null;
        if (epoch == _receiveEpoch + 1)
        {
            nextChain = (byte[])_receiveChain.Clone();
            Advance(nextChain);
            key = KeyFor(nextChain);
        }

        if (key == null)
        {
            return false;
        }

        Span<byte> nonce = stackalloc byte[12];
        WriteNonce(nonce, _receiveSalt, counter);
        bool opened = true;
        try
        {
            key.Decrypt(nonce, packet.Slice(aad, length), packet.Slice(aad + length, TagSize),
                plain[..length], packet[..aad]);
        }
        catch (CryptographicException)
        {
            opened = false;
        }

        if (nextChain != null)
        {
            if (opened)
            {
                _receivePrevious?.Dispose();
                _receivePrevious = _receive;
                _receive = key;
                nextChain.CopyTo(_receiveChain);
                _receiveEpoch++;
            }
            else
            {
                key.Dispose();
            }

            CryptographicOperations.ZeroMemory(nextChain);
        }

        if (!opened)
        {
            return false;
        }

        MarkSeen(counter);
        return true;
    }

    private static void WriteNonce(Span<byte> nonce, uint salt, ulong counter)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(nonce, salt);
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], counter);
    }

    private bool IsFresh(ulong counter)
    {
        if (!_received || counter > _highest)
        {
            return true;
        }

        return _highest - counter < ReplayWindow && !IsMarked(counter);
    }

    private void MarkSeen(ulong counter)
    {
        if (!_received || counter - _highest >= ReplayWindow && counter > _highest)
        {
            Array.Clear(_window);
        }
        else
        {
            for (ulong skipped = _highest + 1; skipped < counter; skipped++)
            {
                _window[skipped / 64 % (ReplayWindow / 64)] &= ~(1UL << (int)(skipped % 64));
            }
        }

        if (!_received || counter > _highest)
        {
            _received = true;
            _highest = counter;
        }

        _window[counter / 64 % (ReplayWindow / 64)] |= 1UL << (int)(counter % 64);
    }

    private bool IsMarked(ulong counter) =>
        (_window[counter / 64 % (ReplayWindow / 64)] & (1UL << (int)(counter % 64))) != 0;
}
