// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Recast.Detour.Crowd;

namespace Prowl.Runtime;

/// <summary>
/// One agent type's crowd: the Detour crowd, the navmesh instance it steers against, and the
/// 16 query-filter slots it was constructed over. Slot 0 is the shared default (all areas, no
/// cost overrides); slots 1..15 are refcounted and allocated per distinct (AreaMask,
/// cost-overrides) configuration, so agents with identical filters share a slot. Slot numbers
/// are NOT stable across release/re-acquire — nothing outside this entry may key state on
/// them. Main-thread only, like all crowd state.
/// </summary>
internal sealed class NavMeshCrowdEntry
{
    public readonly DtCrowd Crowd;
    public readonly NavMeshInstance Instance;

    // The filter objects the crowd reads live each update — mutating one changes the steering
    // of every agent on that slot immediately.
    private readonly NavMeshDetourFilter[] _filters;
    private readonly int[] _refCounts = new int[DtCrowdConst.DT_CROWD_MAX_QUERY_FILTER_TYPE];

    // Once per entry: a crowd rebind makes every agent re-acquire, and a persistent overflow
    // population would otherwise warn per agent per rebake — log spam at destructible-world
    // frequency. The entry is recreated on rebind, so each new crowd re-warns exactly once.
    private bool _exhaustionWarned;

    public NavMeshCrowdEntry(DtCrowd crowd, NavMeshInstance instance, NavMeshDetourFilter[] filters)
    {
        Crowd = crowd;
        Instance = instance;
        _filters = filters;
    }

    /// <summary>
    /// Slot whose filter matches the configuration exactly, sharing where possible: the
    /// default config maps to slot 0, a config already in use bumps that slot's refcount, and
    /// a new config takes a free slot. On exhaustion (16 distinct steering configurations for
    /// one agent type) warns and falls back to slot 0.
    /// </summary>
    public int AcquireFilterSlot(NavMeshAreaMask areaMask, float[]? costOverrides, string? agentName = null)
    {
        if (areaMask == NavMeshAreaMask.Everything && OverridesEqual(costOverrides, null))
            return 0;

        // Exact-match scan beats hashing here: at most 15 candidates, and comparing the full
        // config can never merge two different configurations the way a hash collision would.
        for (int slot = 1; slot < _filters.Length; slot++)
        {
            if (_refCounts[slot] > 0 && _filters[slot].AreaMask == areaMask.Mask
                && OverridesEqual(_filters[slot].CostOverrides, costOverrides))
            {
                _refCounts[slot]++;
                return slot;
            }
        }

        for (int slot = 1; slot < _filters.Length; slot++)
        {
            if (_refCounts[slot] == 0)
            {
                // A copy, so the agent editing its own costs later cannot skew a shared slot.
                _filters[slot].AreaMask = areaMask.Mask;
                _filters[slot].CostOverrides = (float[]?)costOverrides?.Clone();
                _refCounts[slot] = 1;
                return slot;
            }
        }

        if (!_exhaustionWarned)
        {
            _exhaustionWarned = true;
            string who = string.IsNullOrEmpty(agentName) ? "an agent" : $"agent '{agentName}'";
            Debug.LogWarning($"[Navigation] All {_filters.Length} crowd filter slots for agent type {Instance.AgentTypeId} are in use ({_filters.Length - 1} distinct AreaMask/cost configurations); {who} steers with the default filter instead. Explicit queries (CalculatePath etc.) are unaffected. Further overflows on this crowd will not be logged.");
        }
        return 0;
    }

    /// <summary>Release a slot returned by <see cref="AcquireFilterSlot"/>. Slot 0 is shared
    /// and never released. A slot's filter resets to defaults when its last user leaves.</summary>
    public void ReleaseFilterSlot(int slot)
    {
        if (slot <= 0 || slot >= _refCounts.Length || _refCounts[slot] == 0) return;
        if (--_refCounts[slot] == 0)
        {
            _filters[slot].AreaMask = uint.MaxValue;
            _filters[slot].CostOverrides = null;
        }
    }

    private static bool OverridesEqual(float[]? a, float[]? b)
    {
        if (ReferenceEquals(a, b)) return true; // both null: the common mask-only case
        // 0 means "no override", so a null array equals an all-zero one.
        for (int i = 0; i < NavMeshAreas.MaxAreas; i++)
        {
            float av = a != null && i < a.Length ? a[i] : 0f;
            float bv = b != null && i < b.Length ? b[i] : 0f;
            if (av != bv) return false;
        }
        return true;
    }
}
