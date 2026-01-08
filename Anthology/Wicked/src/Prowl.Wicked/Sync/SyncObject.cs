namespace Prowl.Wicked.Sync;

/// <summary>
/// Base class for synchronized objects (SyncList, SyncDictionary, etc.).
/// SyncObjects automatically track changes and synchronize state between server and clients.
/// Uses Prowl.Echo for serialization of contained values.
/// </summary>
public abstract class SyncObject
{
    /// <summary>
    /// Called when the SyncObject becomes dirty (has changes to sync).
    /// Set by EntityBehaviour during initialization.
    /// </summary>
    public Action? OnDirty { get; set; }

    /// <summary>
    /// Returns true if the SyncObject should record changes.
    /// This prevents ever-growing change lists when there are no observers.
    /// Set by EntityBehaviour during initialization.
    /// </summary>
    public Func<bool> IsRecording { get; set; } = () => true;

    /// <summary>
    /// Returns true if the SyncObject can be modified.
    /// SyncObjects should only be modified on the server (or by owner with client authority).
    /// Set by EntityBehaviour during initialization.
    /// </summary>
    public Func<bool> IsWritable { get; set; } = () => true;

    /// <summary>
    /// Clears all pending changes. Called after changes have been synchronized.
    /// </summary>
    public abstract void ClearChanges();

    /// <summary>
    /// Writes a complete copy of the object state (for initial sync).
    /// </summary>
    public abstract void OnSerializeAll(BinaryWriter writer);

    /// <summary>
    /// Writes only the changes since last sync (delta sync).
    /// </summary>
    public abstract void OnSerializeDelta(BinaryWriter writer);

    /// <summary>
    /// Reads a complete copy of the object state (for initial sync).
    /// </summary>
    public abstract void OnDeserializeAll(BinaryReader reader);

    /// <summary>
    /// Reads and applies changes since last sync (delta sync).
    /// </summary>
    public abstract void OnDeserializeDelta(BinaryReader reader);

    /// <summary>
    /// Resets the SyncObject to its initial state.
    /// </summary>
    public abstract void Reset();
}
