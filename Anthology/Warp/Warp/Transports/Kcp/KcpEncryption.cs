// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Security.Cryptography;

namespace Prowl.Warp.Transports.Kcp;

/// <summary>
///     Turns on encryption for a <see cref="KcpTransport" />. Each connection agrees on fresh keys with an
///     ephemeral P-256 key exchange, then seals every session packet with AES-256-GCM and drops replays.
/// </summary>
/// <remarks>
///     Without <see cref="TrustedHostKey" /> traffic is private from anyone who only listens, but a man in the
///     middle can still pose as the host. Give the host a <see cref="SigningKey" /> and clients its public key to
///     rule that out. Because every packet is authenticated, each end also follows the other to a new address, such
///     as a phone moving from Wi-Fi to mobile data, once that address answers an encrypted challenge.
/// </remarks>
public sealed class KcpEncryption
{
    /// <summary>Host: a P-256 key that signs each handshake. Clients that trust its public key can then tell it is genuine.</summary>
    public ECDsa? SigningKey { get; set; }

    /// <summary>
    ///     Client: the host's P-256 public key, as exported by <see cref="AsymmetricAlgorithm.ExportSubjectPublicKeyInfo" />.
    ///     When set, a host that cannot prove it holds the matching <see cref="SigningKey" /> is refused with
    ///     <see cref="DisconnectReason.AuthenticationFailed" />.
    /// </summary>
    public byte[]? TrustedHostKey { get; set; }

    internal void Validate()
    {
        if (!AesGcm.IsSupported)
        {
            throw new PlatformNotSupportedException("AES-GCM is not available on this platform.");
        }

        if (SigningKey != null && SigningKey.KeySize != 256)
        {
            throw new ArgumentException("The signing key must be a P-256 key.", nameof(SigningKey));
        }

        if (TrustedHostKey != null)
        {
            using ECDsa trusted = ImportTrustedHostKey();
        }
    }

    /// <summary>Reads <see cref="TrustedHostKey" />, which must be a P-256 public key.</summary>
    internal ECDsa ImportTrustedHostKey()
    {
        ECDsa key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(TrustedHostKey, out _);
        }
        catch (CryptographicException e)
        {
            key.Dispose();
            throw new ArgumentException("The trusted host key is not a valid public key.", nameof(TrustedHostKey), e);
        }

        if (key.KeySize != 256)
        {
            key.Dispose();
            throw new ArgumentException("The trusted host key must be a P-256 key.", nameof(TrustedHostKey));
        }

        return key;
    }

    internal KcpEncryption Copy() => new() { SigningKey = SigningKey, TrustedHostKey = (byte[]?)TrustedHostKey?.Clone() };
}
