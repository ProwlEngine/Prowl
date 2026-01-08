namespace Prowl.Wicked.Interest;

using Prowl.Wicked.Core;
using Prowl.Wicked.Network;

/// <summary>
/// Base class for interest management systems.
/// Low level base class allows for low level spatial hashing etc.
/// Like Mirror's InterestManagementBase.
/// </summary>
public abstract class InterestManagementBase
{
    /// <summary>
    /// Resets the state of the interest manager.
    /// </summary>
    public virtual void ResetState() { }

    /// <summary>
    /// Callback used by the visibility system to determine if an observer
    /// (player) can see the entity.
    /// </summary>
    /// <param name="entity">The entity to check visibility for.</param>
    /// <param name="newObserver">The connection checking visibility.</param>
    /// <returns>True if the connection can see the entity.</returns>
    public abstract bool OnCheckObserver(Entity entity, NetworkConnection newObserver);

    /// <summary>
    /// Called on the server when a new networked entity is spawned.
    /// Useful for 'only rebuild if changed' interest management algorithms.
    /// </summary>
    public virtual void OnSpawned(Entity entity) { }

    /// <summary>
    /// Called on the server when a networked entity is destroyed.
    /// Useful for 'only rebuild if changed' interest management algorithms.
    /// </summary>
    public virtual void OnDestroyed(Entity entity) { }

    /// <summary>
    /// Rebuilds the observers for an entity.
    /// </summary>
    /// <param name="entity">The entity to rebuild observers for.</param>
    /// <param name="initialize">True if this is the initial rebuild (first spawn).</param>
    public abstract void Rebuild(Entity entity, bool initialize);

    /// <summary>
    /// Adds the specified connection to the observers of entity.
    /// Helper method for derived classes.
    /// </summary>
    protected void AddObserver(NetworkConnection connection, Entity entity)
    {
        connection.AddToObserving(entity);
        entity.ObserversInternal.Add(connection.ConnectionId, connection);
    }

    /// <summary>
    /// Removes the specified connection from the observers of entity.
    /// Helper method for derived classes.
    /// </summary>
    protected void RemoveObserver(NetworkConnection connection, Entity entity)
    {
        connection.RemoveFromObserving(entity, false);
        entity.ObserversInternal.Remove(connection.ConnectionId);
    }
}
