namespace Prowl.Wicked.Interest;

using Prowl.Wicked.Core;
using Prowl.Wicked.Network;

/// <summary>
/// Default interest manager that makes all entities visible to all connections.
/// Like Mirror's default behavior when no interest management is configured.
/// </summary>
public class DefaultInterestManager : InterestManagement
{
    /// <summary>
    /// Singleton instance of the default interest manager.
    /// </summary>
    public static DefaultInterestManager Instance { get; } = new();

    /// <summary>
    /// Rebuilds observers by adding all ready connections.
    /// Everyone sees everything.
    /// </summary>
    public override void OnRebuildObservers(Entity entity, HashSet<NetworkConnection> newObservers)
    {
        // Add all ready connections - everyone sees everything
        foreach (var conn in NetworkServer.Connections.Values)
        {
            if (conn.IsReady)
            {
                newObservers.Add(conn);
            }
        }
    }

    /// <summary>
    /// Always returns true - all entities are visible to all connections.
    /// </summary>
    public override bool OnCheckObserver(Entity entity, NetworkConnection newObserver)
    {
        return true;
    }
}
