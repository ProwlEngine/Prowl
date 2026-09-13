// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// A transient, additive cost overlay for one agent type's navmesh, on top of whatever
/// <see cref="NavMeshAreas"/> already charges each polygon's area. Owned by <see cref="NavMeshSystem"/>
/// (see <see cref="NavMeshSystem.GetOrCreateCostLayer"/>/<see cref="NavMeshSystem.AddCost"/>) rather than
/// by any one <see cref="NavMeshQuery"/>, so a cost survives a query rebuild (a rebake, a link change) -
/// gunfire suppressing a courtyard, or a fire spreading across a floor, has nothing to do with whether
/// the mesh underneath it happened to get rebuilt a moment later.
/// <para/>
/// Every entry decays back to nothing on its own schedule (<see cref="Decay"/>, pumped once per fixed
/// tick by <see cref="NavMeshSystem.TickCrowds"/>) rather than needing an explicit removal call - the
/// common case for this kind of cost is "temporarily less desirable," not "permanently blocked" (that's
/// what <see cref="NavMeshModifierVolume"/> or a real <see cref="NavMeshObstacle"/> carve are for). A
/// zero decay rate opts a specific entry out of fading, for a cost meant to persist until
/// <see cref="Clear"/> or another <see cref="NavMeshSystem.AddCost"/> call over it.
/// <para/>
/// Backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>, not a lock shared with
/// <see cref="NavMeshQuery"/>'s own tile-structural one: a cost layer's entries are read by whichever
/// filter a path search happens to be using (see <see cref="NavMeshCostAwareFilter"/>), possibly from a
/// worker thread mid-<see cref="NavMeshQuery.FindPath"/>, while <see cref="NavMeshSystem.AddCost"/> or
/// this class's own <see cref="Decay"/> write to it from the main thread - unrelated to whether a tile is
/// being rebuilt at the same moment, so it earns its own, independent thread-safety.
/// </summary>
public sealed class NavMeshCostLayer
{
    private readonly record struct CostEntry(float Cost, float DecayPerSecond);

    private readonly ConcurrentDictionary<long, CostEntry> _costs = new();

    /// <summary>Sets (or replaces) the additive cost for one polygon, identified by its opaque Detour
    /// reference - never exposed outside this file and <see cref="NavMeshQuery"/>, which is the only
    /// caller that ever resolves a world position to one.</summary>
    internal void Set(long polyRef, float cost, float decayPerSecond) => _costs[polyRef] = new CostEntry(cost, decayPerSecond);

    /// <summary>Removes every cost entry - for a caller that wants a hard reset (a round ending, a scene
    /// transition) rather than waiting out each entry's own decay.</summary>
    public void Clear() => _costs.Clear();

    /// <summary>How many polygons currently carry a cost entry - for a debug overlay, or for a caller
    /// checking whether anything is active at all before doing more expensive work.</summary>
    public int Count => _costs.Count;

    /// <summary>The current additive cost for one polygon, or 0 if it has none. Internal - see <see cref="Set"/>.</summary>
    internal float GetAdditiveCost(long polyRef) => _costs.TryGetValue(polyRef, out CostEntry entry) ? entry.Cost : 0f;

    /// <summary>Advances every entry's decay by <paramref name="deltaTime"/> seconds, dropping any that
    /// have decayed to zero or below. A no-op for an entry whose decay rate is zero or negative - see
    /// this class's own doc comment for why that is a deliberate "never fades on its own" opt-out rather
    /// than a clamped-to-instant removal.</summary>
    internal void Decay(float deltaTime)
    {
        if (_costs.IsEmpty) return;

        foreach (System.Collections.Generic.KeyValuePair<long, CostEntry> entry in _costs)
        {
            if (entry.Value.DecayPerSecond <= 0f) continue;

            float remaining = entry.Value.Cost - entry.Value.DecayPerSecond * deltaTime;
            if (remaining <= 0f)
                _costs.TryRemove(entry.Key, out _);
            else
                _costs.TryUpdate(entry.Key, entry.Value with { Cost = remaining }, entry.Value);
        }
    }
}
