using System.Collections.Generic;

namespace Prowl.Veldrid;

[System.Flags]
internal enum ChangeMask : byte
{
    None = 0,
    Uniforms = 1 << 0,
    Resources = 1 << 1,
    Both = Uniforms | Resources,
}

/// <summary>
/// Per-command-buffer merged view of all <see cref="PropertySet"/> entries pushed via
/// <see cref="CommandBuffer.SetProperties"/>. One dictionary keyed by the raw int value of the entry's
/// <see cref="PropertyID"/> or <see cref="PropertyID"/>. Last-writer-by-name wins.
/// </summary>
internal sealed class MergedPropertyTable
{
    public readonly Dictionary<PropertyID, PropertyEntry> Entries = new();

    /// <summary>Incremented when a merge dirtied at least one uniform entry.</summary>
    public uint UniformVersion;
    /// <summary>Incremented when a merge dirtied at least one resource entry.</summary>
    public uint ResourceVersion;

    public void Clear()
    {
        Entries.Clear();
        unchecked { UniformVersion++; ResourceVersion++; }
    }

    /// <summary>
    /// Merges <paramref name="src"/> entries into this table subject to <paramref name="mask"/>.
    /// Increments <see cref="UniformVersion"/> or <see cref="ResourceVersion"/> at most once per call
    /// if the respective kind was touched.
    /// </summary>
    public void IngestFrom(PropertySet src, ChangeMask mask)
    {
        if (mask == ChangeMask.None) return;

        bool dirtyUniforms = false;
        bool dirtyResources = false;

        foreach (KeyValuePair<PropertyID, PropertyEntry> kv in src.RawEntries)
        {
            PropertyEntry entry = kv.Value;
            bool isUniform = entry.Kind == PropertyEntryKind.Uniform;

            if (isUniform && (mask & ChangeMask.Uniforms) == 0) continue;
            if (!isUniform && (mask & ChangeMask.Resources) == 0) continue;

            Entries[kv.Key] = entry;

            if (isUniform) dirtyUniforms = true;
            else dirtyResources = true;
        }

        if (dirtyUniforms) unchecked { UniformVersion++; }
        if (dirtyResources) unchecked { ResourceVersion++; }
    }
}
