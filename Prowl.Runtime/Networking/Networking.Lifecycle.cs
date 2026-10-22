// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

public static partial class Networking
{
    private static void NotifySpawn(NetworkEntry entry)
    {
        if (entry.SpawnNotified || !IsActive || FindEntry(entry.GameObject) != entry)
            return;

        // Record membership before game code runs: callbacks may destroy this root or end the session.
        entry.SpawnNotified = true;
        entry.NotifiedOwnerId = entry.Owner;
        entry.HasLocalAuthority = HasControl(entry.GameObject);
        ControlChanged(entry.GameObject);
        SceneEvents.RunLifecycle<INetworkEvents>(entry.GameObject, e =>
        {
            if (entry.SpawnNotified && entry.SpawnListeners.Add((Component)e))
                e.OnNetworkSpawn();
        });

        if (entry.SpawnNotified && entry.HasLocalAuthority)
            NotifyAuthority(entry, gained: true);
    }

    private static void NotifyAuthority(NetworkEntry entry, bool gained)
    {
        SceneEvents.RunLifecycle<INetworkEvents>(entry.GameObject, e =>
        {
            Component component = (Component)e;
            if (gained)
            {
                if (entry.SpawnNotified && entry.HasLocalAuthority && entry.SpawnListeners.Contains(component) &&
                    entry.AuthorityListeners.Add(component))
                    e.OnNetworkAuthorityGained();
            }
            else if (entry.AuthorityListeners.Remove(component))
            {
                e.OnNetworkAuthorityLost();
            }
        });
    }

    private static void NotifyOwnership(NetworkEntry entry)
    {
        if (!entry.SpawnNotified)
            return;

        bool hadAuthority = entry.HasLocalAuthority;
        int previousOwnerId = entry.NotifiedOwnerId;
        uint generation = entry.Generation;
        bool hasAuthority = HasControl(entry.GameObject);
        entry.HasLocalAuthority = hasAuthority;
        entry.NotifiedOwnerId = entry.Owner;
        if (hadAuthority && !hasAuthority)
            NotifyAuthority(entry, gained: false);
        if (!entry.SpawnNotified || entry.Generation != generation)
            return;

        NetPeer? oldOwner = null;
        if (Session is { } session)
            session.TryGetPeer(previousOwnerId, out oldOwner);
        NetPeer? newOwner = GetOwner(entry.GameObject);
        SceneEvents.RunLifecycle<INetworkEvents>(entry.GameObject, e =>
        {
            if (entry.SpawnNotified && entry.Generation == generation && entry.SpawnListeners.Contains((Component)e))
                e.OnNetworkOwnerChanged(oldOwner, newOwner);
        });

        if (!hadAuthority && hasAuthority && entry.SpawnNotified && entry.Generation == generation)
            NotifyAuthority(entry, gained: true);
    }

    private static void NotifyDespawn(NetworkEntry entry)
    {
        if (!entry.SpawnNotified)
            return;

        ClearStateRevisions(entry.GameObject);
        // End membership before notifying listeners so nested teardown cannot notify twice.
        entry.SpawnNotified = false;
        entry.TearingDown = true;
        entry.HasLocalAuthority = false;
        NotifyAuthority(entry, gained: false);
        SceneEvents.RunLifecycle<INetworkEvents>(entry.GameObject, e =>
        {
            if (entry.SpawnListeners.Remove((Component)e))
                e.OnNetworkDespawn();
        });
        entry.SpawnListeners.Clear();
        entry.AuthorityListeners.Clear();
    }

    internal static void BeforeObjectDisposed(GameObject go)
    {
        if (s_entries.TryGetValue(go.Identifier, out NetworkEntry? entry) && entry.GameObject == go)
            NotifyDespawn(entry);
    }

    internal static void AssertStructureEditable(GameObject? go)
    {
        if (!go.IsValid() || !IsActive || go.Scene != s_scene || go.Scene is not { IsDisposed: false })
            return;

        if (FindEntry(go) is { TearingDown: false })
            throw new InvalidOperationException("A spawned network object's components and hierarchy are fixed. " +
                "Build the tree before NetworkSpawn, or spawn/despawn a whole object.");
    }

    internal static void AssertObjectDestroyable(GameObject go)
    {
        if (FindEntry(go) is not { TearingDown: false } entry || !IsActive || go.Scene is not { IsDisposed: false })
            return;
        if (IsHost && entry.GameObject == go)
            return;

        AssertStructureEditable(go);
    }

    internal static void AssertHierarchyEditable(GameObject? go)
    {
        AssertStructureEditable(go);
        if (!go.IsValid() || !IsActive || go.Scene is not { IsDisposed: false })
            return;

        foreach (GameObject child in go.GetChildrenDeep())
            AssertStructureEditable(child);
    }
}
