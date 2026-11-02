// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Prowl.Warp.Transports.Kcp;

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

    private const int ReplayWindow = 64;

    // The P-256 field prime and curve constant b, big-endian. The curve is y^2 = x^3 - 3x + b.
    private static readonly BigInteger FieldPrime = Unsigned(Convert.FromHexString(
        "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF"));

    private static readonly BigInteger CurveB = Unsigned(Convert.FromHexString(
        "5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B"));
    private static readonly byte[] Label = Encoding.ASCII.GetBytes("Prowl.Warp.Kcp.5");

    private readonly AesGcm _receive;
    private readonly uint _receiveSalt;
    private readonly AesGcm _send;
    private readonly uint _sendSalt;
    private ulong _highest;
    private bool _received;
    private ulong _nextCounter;
    private ulong _window;

    private KcpCipher(ReadOnlySpan<byte> keys, bool isClient)
    {
        ReadOnlySpan<byte> clientKey = keys[..32], hostKey = keys[32..64];
        uint clientSalt = BinaryPrimitives.ReadUInt32LittleEndian(keys[64..]);
        uint hostSalt = BinaryPrimitives.ReadUInt32LittleEndian(keys[68..]);
        _send = new AesGcm(isClient ? clientKey : hostKey, TagSize);
        _receive = new AesGcm(isClient ? hostKey : clientKey, TagSize);
        _sendSalt = isClient ? clientSalt : hostSalt;
        _receiveSalt = isClient ? hostSalt : clientSalt;
    }

    public void Dispose()
    {
        _send.Dispose();
        _receive.Dispose();
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

    /// <summary>What the host signs: both public keys and the ids of this handshake.</summary>
    public static byte[] Transcript(ReadOnlySpan<byte> clientPublic, ReadOnlySpan<byte> hostPublic, uint nonce,
        uint sessionId)
    {
        byte[] transcript = new byte[PublicKeySize * 2 + 8];
        clientPublic.CopyTo(transcript);
        hostPublic.CopyTo(transcript.AsSpan(PublicKeySize));
        BinaryPrimitives.WriteUInt32LittleEndian(transcript.AsSpan(PublicKeySize * 2), nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(transcript.AsSpan(PublicKeySize * 2 + 4), sessionId);
        return transcript;
    }

    /// <summary>Agrees on keys with the remote public key. Null if that key is not a valid P-256 point.</summary>
    public static KcpCipher? Create(ECDiffieHellman local, ReadOnlySpan<byte> clientPublic,
        ReadOnlySpan<byte> hostPublic, uint nonce, uint sessionId, bool isClient)
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

        Span<byte> keys = stackalloc byte[72];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, keys, salt, info);
        CryptographicOperations.ZeroMemory(secret);
        KcpCipher cipher = new(keys, isClient);
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

        Span<byte> nonce = stackalloc byte[12];
        WriteNonce(nonce, _receiveSalt, counter);
        try
        {
            _receive.Decrypt(nonce, packet.Slice(aad, length), packet.Slice(aad + length, TagSize),
                plain[..length], packet[..aad]);
        }
        catch (CryptographicException)
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

        ulong back = _highest - counter;
        return back < ReplayWindow && (_window & (1UL << (int)back)) == 0;
    }

    private void MarkSeen(ulong counter)
    {
        if (!_received)
        {
            _received = true;
            _highest = counter;
            _window = 1;
        }
        else if (counter > _highest)
        {
            ulong shift = counter - _highest;
            _window = shift >= ReplayWindow ? 1 : (_window << (int)shift) | 1;
            _highest = counter;
        }
        else
        {
            _window |= 1UL << (int)(_highest - counter);
        }
    }
}
