namespace Prowl.Wicked.Interest;

using Prowl.Wicked.Core;
using Prowl.Wicked.Network;

/// <summary>
/// Higher-level interest management base class with OnRebuildObservers callback.
/// Like Mirror's InterestManagement class.
/// </summary>
public abstract class InterestManagement : InterestManagementBase
{
    // Cached hashset to avoid allocations during rebuild
    private readonly HashSet<NetworkConnection> _newObservers = new();

    /// <summary>
    /// Rebuild observers for the given entity.
    /// Server will automatically spawn/despawn added/removed ones.
    /// </summary>
    /// <param name="entity">The entity to rebuild observers for.</param>
    /// <param name="newObservers">HashSet to fill with new observers.</param>
    public abstract void OnRebuildObservers(Entity entity, HashSet<NetworkConnection> newObservers);

    /// <summary>
    /// Helper function to trigger a full rebuild of all entities.
    /// Most implementations should call this in a certain interval.
    /// </summary>
    protected void RebuildAll()
    {
        if (!NetworkServer.Active || World.Active == null) return;

        foreach (var entity in World.Active.Entities.Values)
        {
            NetworkServer.RebuildObservers(entity, false);
        }
    }

    /// <summary>
    /// Default implementation of OnCheckObserver that returns true.
    /// Override if you need custom initial visibility checks.
    /// </summary>
    public override bool OnCheckObserver(Entity entity, NetworkConnection newObserver)
    {
        return true;
    }

    /// <summary>
    /// Rebuilds observers for the entity using OnRebuildObservers.
    /// </summary>
    public override void Rebuild(Entity entity, bool initialize)
    {
        // Clear the cached hashset
        _newObservers.Clear();

        // Let derived class fill in new observers
        OnRebuildObservers(entity, _newObservers);

        // IMPORTANT: AFTER rebuilding, add owner connection in any case
        // to ensure player always sees their own objects no matter what.
        // Fixes edge cases where owner might be teleported out of visibility range.
        if (entity.Owner != null)
        {
            _newObservers.Add(entity.Owner);
        }

        bool changed = false;

        // Add all newObservers that aren't in .observers yet
        foreach (var conn in _newObservers)
        {
            // Only add ready connections
            if (conn != null && conn.IsReady)
            {
                if (initialize || !entity.Observers.ContainsKey(conn.ConnectionId))
                {
                    // New observer - add to observing (which sends spawn message)
                    conn.AddToObserving(entity);
                    changed = true;
                }
            }
        }

        // Remove all old observers that aren't in newObservers anymore
        foreach (var conn in entity.Observers.Values.ToArray())
        {
            if (!_newObservers.Contains(conn))
            {
                // Removed observer - remove from observing (which sends despawn message)
                conn.RemoveFromObserving(entity, false);
                changed = true;
            }
        }

        // Copy new observers to entity's observers
        if (changed)
        {
            entity.ObserversInternal.Clear();
            foreach (var conn in _newObservers)
            {
                if (conn != null && conn.IsReady)
                {
                    entity.ObserversInternal.Add(conn.ConnectionId, conn);
                }
            }
        }
    }
}
