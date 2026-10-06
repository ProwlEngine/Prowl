using System;
using System.Security.Cryptography;
using System.Text;

namespace Prowl.Graphite;

/// <summary>
/// SHA-256 over stage, entry point and SPIR-V of each stage, in order. Stable across Graphite versions.
/// </summary>
public readonly struct ProgramKey : IEquatable<ProgramKey>
{
    private const int DigestSize = 32;

    private readonly ulong _a;
    private readonly ulong _b;
    private readonly ulong _c;
    private readonly ulong _d;

    private ProgramKey(ReadOnlySpan<byte> digest)
    {
        _a = BitConverter.ToUInt64(digest);
        _b = BitConverter.ToUInt64(digest[8..]);
        _c = BitConverter.ToUInt64(digest[16..]);
        _d = BitConverter.ToUInt64(digest[24..]);
    }

    /// <summary>
    /// Computes the key of the given stages. Integers are little endian, entry points UTF-8.
    /// </summary>
    public static ProgramKey Compute(ReadOnlySpan<ShaderStageDescription> stages)
    {
        using IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> header = stackalloc byte[12];
        foreach (ShaderStageDescription stage in stages)
        {
            byte[] entry = Encoding.UTF8.GetBytes(stage.EntryPoint);
            BitConverter.TryWriteBytes(header, (int)stage.Stage);
            BitConverter.TryWriteBytes(header[4..], entry.Length);
            BitConverter.TryWriteBytes(header[8..], stage.ShaderBytes.Length);
            sha.AppendData(header);
            sha.AppendData(entry);
            sha.AppendData(stage.ShaderBytes);
        }

        Span<byte> digest = stackalloc byte[DigestSize];
        sha.GetHashAndReset(digest);
        return new ProgramKey(digest);
    }

    /// <summary>
    /// Writes the 32 digest bytes.
    /// </summary>
    public void CopyTo(Span<byte> destination)
    {
        BitConverter.TryWriteBytes(destination, _a);
        BitConverter.TryWriteBytes(destination[8..], _b);
        BitConverter.TryWriteBytes(destination[16..], _c);
        BitConverter.TryWriteBytes(destination[24..], _d);
    }

    /// <summary>
    /// Digest as a byte array.
    /// </summary>
    public byte[] ToArray()
    {
        byte[] bytes = new byte[DigestSize];
        CopyTo(bytes);
        return bytes;
    }

    /// <inheritdoc/>
    public bool Equals(ProgramKey other) => _a == other._a && _b == other._b && _c == other._c && _d == other._d;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ProgramKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);

    /// <inheritdoc/>
    public override string ToString() => Convert.ToHexString(ToArray());

    /// <summary>
    /// Equality.
    /// </summary>
    public static bool operator ==(ProgramKey left, ProgramKey right) => left.Equals(right);

    /// <summary>
    /// Inequality.
    /// </summary>
    public static bool operator !=(ProgramKey left, ProgramKey right) => !left.Equals(right);
}
