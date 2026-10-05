using System;
using System.Security.Cryptography;
using System.Text;

namespace Prowl.Graphite.Vk;

/// <summary>Identity of a shader: SHA-256 over stages, entry points and SPIR-V, plus resource layouts.</summary>
internal sealed class VkShaderKey : IEquatable<VkShaderKey>
{
    private readonly byte[] _digest;
    private readonly ResourceLayoutDescription[] _layouts;
    private readonly int _hash;

    public VkShaderKey(ShaderStageDescription[] stages, ResourceLayoutDescription[] layouts)
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

        _digest = sha.GetHashAndReset();
        _layouts = layouts;

        HashCode hash = new();
        hash.AddBytes(_digest);
        foreach (ResourceLayoutDescription layout in layouts)
        {
            hash.Add(layout.Set);
            hash.Add(layout.Elements?.ArrayHash() ?? 0);
        }

        _hash = hash.ToHashCode();
    }

    public bool Equals(VkShaderKey? other)
    {
        if (other is null || _hash != other._hash || !_digest.AsSpan().SequenceEqual(other._digest))
            return false;

        if (_layouts.Length != other._layouts.Length)
            return false;

        for (int i = 0; i < _layouts.Length; i++)
        {
            if (_layouts[i].Set != other._layouts[i].Set
                || !Util.ArrayEqualsEquatable(_layouts[i].Elements, other._layouts[i].Elements))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as VkShaderKey);

    public override int GetHashCode() => _hash;
}
