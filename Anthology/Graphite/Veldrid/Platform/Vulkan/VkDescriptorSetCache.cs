using System;
using System.Collections.Generic;

using Silk.NET.Vulkan;

namespace Prowl.Veldrid.Vk;

/// <summary>
/// Per-shader, cross-frame cache of descriptor sets keyed by the identity of the resources written
/// into them. A cache hit means the set's contents are byte-identical, so a cached set is reused
/// as-is and never rewritten -- which sidesteps the frames-in-flight write hazard entirely.
///
/// Because the identity includes a UBO's backing buffer handle, a set that binds a per-frame
/// transient UBO naturally gets one entry per ring slot, each reused every MaxFramesInFlight frames
/// once steady state is reached.
///
/// Eviction keeps the <see cref="WarmFloorPerSet"/> most-recently-used entries per set index resident
/// and frees any entry beyond that floor once it has gone unused for longer than the retention window
/// (the frames-in-flight count). That same window guarantees a freed set is already GPU-retired.
/// </summary>
internal sealed class VkDescriptorSetCache
{
    // Per set-index minimum kept resident even when idle. The cache grows past this on demand and
    // only frees back down to it (never below) when entries age out.
    private const int WarmFloorPerSet = 32;

    private sealed class Entry
    {
        public DescriptorAllocationToken Token;
        public DescriptorResourceCounts Counts;
        public ulong[] Identity;
        public int SetIdx;
        public ulong LastUsedFrameId;
    }

    private readonly VkGraphicsDevice _gd;
    private readonly VkDescriptorPoolManager _pool;
    private readonly Dictionary<ulong[], Entry> _byIdentity;
    private readonly Dictionary<ulong[], Entry>.AlternateLookup<ReadOnlySpan<ulong>> _lookup;
    private readonly Dictionary<int, List<Entry>> _bySet = [];
    private readonly List<Entry> _evictScratch = [];

    public VkDescriptorSetCache(VkGraphicsDevice gd)
    {
        _gd = gd;
        _pool = new VkDescriptorPoolManager(gd); // persistent pool: supports per-set Free for eviction
        _byIdentity = new Dictionary<ulong[], Entry>(IdentityComparer.Instance);
        _lookup = _byIdentity.GetAlternateLookup<ReadOnlySpan<ulong>>();
        gd.RegisterDescriptorSetCache(this);
    }

    /// <summary>Returns the cached set for the given resource identity, refreshing its last-used time.</summary>
    public bool TryGet(ReadOnlySpan<ulong> identity, ulong frameId, out DescriptorSet set)
    {
        if (_lookup.TryGetValue(identity, out Entry entry))
        {
            entry.LastUsedFrameId = frameId;
            set = entry.Token.Set;
            return true;
        }
        set = default;
        return false;
    }

    /// <summary>
    /// Allocates a new set for a cache miss and records it. The caller is responsible for writing the
    /// descriptors into the returned set.
    /// </summary>
    public DescriptorSet Allocate(
        int setIdx, DescriptorSetLayout layout, in DescriptorResourceCounts counts,
        ReadOnlySpan<ulong> identity, ulong frameId)
    {
        DescriptorAllocationToken token = _pool.Allocate(counts, layout);
        Entry entry = new()
        {
            Token = token,
            Counts = counts,
            Identity = identity.ToArray(),
            SetIdx = setIdx,
            LastUsedFrameId = frameId,
        };

        _byIdentity[entry.Identity] = entry;
        if (!_bySet.TryGetValue(setIdx, out List<Entry> list))
        {
            list = [];
            _bySet[setIdx] = list;
        }
        list.Add(entry);

        return token.Set;
    }

    /// <summary>
    /// Frees entries that have gone unused for longer than <paramref name="retention"/> frames, keeping
    /// the <see cref="WarmFloorPerSet"/> most-recently-used entries per set index resident regardless.
    /// Anything freed here is older than the retention window, so it is guaranteed GPU-retired.
    /// </summary>
    public void Sweep(ulong currentFrameId, uint retention)
    {
        foreach (List<Entry> list in _bySet.Values)
        {
            if (list.Count <= WarmFloorPerSet) continue;

            // Newest first: the leading WarmFloorPerSet entries are the warm floor and always stay.
            list.Sort(static (a, b) => b.LastUsedFrameId.CompareTo(a.LastUsedFrameId));

            for (int i = WarmFloorPerSet; i < list.Count; i++)
            {
                Entry entry = list[i];
                if (currentFrameId - entry.LastUsedFrameId > retention)
                    _evictScratch.Add(entry);
            }
        }

        foreach (Entry entry in _evictScratch)
        {
            _pool.Free(entry.Token, entry.Counts);
            _byIdentity.Remove(entry.Identity);
            _bySet[entry.SetIdx].Remove(entry);
        }
        _evictScratch.Clear();
    }

    /// <summary>Frees every cached set and destroys the backing pool. Called on program disposal.</summary>
    public void Destroy()
    {
        _gd.UnregisterDescriptorSetCache(this);
        foreach (Entry entry in _byIdentity.Values)
            _pool.Free(entry.Token, entry.Counts);
        _byIdentity.Clear();
        _bySet.Clear();
        _pool.DestroyAll();
    }

    // Full-identity equality (no hashing shortcuts): a hash collision just chains in the bucket and is
    // resolved by SequenceEqual, so two distinct binding sets can never alias one descriptor set.
    private sealed class IdentityComparer
        : IEqualityComparer<ulong[]>, IAlternateEqualityComparer<ReadOnlySpan<ulong>, ulong[]>
    {
        public static readonly IdentityComparer Instance = new();

        public bool Equals(ulong[]? a, ulong[]? b) => a.AsSpan().SequenceEqual(b);
        public int GetHashCode(ulong[] a) => Hash(a);

        public bool Equals(ReadOnlySpan<ulong> alternate, ulong[] other) => alternate.SequenceEqual(other);
        public int GetHashCode(ReadOnlySpan<ulong> alternate) => Hash(alternate);
        public ulong[] Create(ReadOnlySpan<ulong> alternate) => alternate.ToArray();

        private static int Hash(ReadOnlySpan<ulong> values)
        {
            ulong hash = 1469598103934665603UL; // FNV-1a offset basis
            foreach (ulong v in values)
                hash = (hash ^ v) * 1099511628211UL; // FNV-1a prime
            return (int)(hash ^ (hash >> 32));
        }
    }
}
