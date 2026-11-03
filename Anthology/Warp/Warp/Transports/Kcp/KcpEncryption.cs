// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Security.Cryptography;

namespace Prowl.Warp.Transports.Kcp;

/// <summary>
///     Turns on encryption for a <see cref="KcpTransport" />. Each connection agrees on fresh keys with an
///     ephemeral P-256 key exchange, then seals every session packet with AES-256-GCM and drops replays. Each
///     direction moves to fresh keys every 2^24 packets and wipes the old ones.
/// </summary>
/// <remarks>
///     Without <see cref="TrustedHostKey" /> traffic is private from anyone who only listens, but a man in the
///     middle can still pose as the host. Give the host a <see cref="SigningKey" /> and clients its public key to
///     rule that out. Each end follows the other to a new address, such as a phone moving from Wi-Fi to mobile data,
///     once that address answers a challenge. Encryption makes that challenge impossible to forge.
/// </remarks>
public sealed class KcpEncryption
{
    /// <summary>Host: a P-256 key that signs each handshake. Clients that trust its public key can then tell it is genuine.</summary>
    public ECDsa? SigningKey { get; set; }

    /// <summary>
    ///     Client: the host's P-256 public key, as exported by <see cref="AsymmetricAlgorithm.ExportSubjectPublicKeyInfo" />.
    ///     When set, a host that cannot prove it holds the matching <see cref="SigningKey" /> is refused with
    ///     <see cref="DisconnectReason.AuthenticationFailed" /> once the connect timeout passes.
    /// </summary>
    public byte[]? TrustedHostKey { get; set; }

    /// <summary>
    ///     Host: a secret of at least 16 bytes that you keep across restarts. With it, a restarted host can tell clients
    ///     of its previous run that their session is gone, so they don't wait for a timeout. Without it, encrypted
    ///     clients only learn of a restart by timing out.
    /// </summary>
    public byte[]? ResetKey { get; set; }

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

        if (ResetKey != null && ResetKey.Length < 16)
        {
            throw new ArgumentException("The reset key must be at least 16 bytes.", nameof(ResetKey));
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

    internal KcpEncryption Copy() => new()
    {
        SigningKey = SigningKey,
        TrustedHostKey = (byte[]?)TrustedHostKey?.Clone(),
        ResetKey = (byte[]?)ResetKey?.Clone()
    };
}
