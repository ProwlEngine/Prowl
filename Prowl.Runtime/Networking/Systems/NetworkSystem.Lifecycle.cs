// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;


namespace Prowl.Runtime.Networking;

internal sealed partial class NetworkSystem
{
    internal void BeforeObjectDisposed(GameObject go)
    {
        if (Entries.TryGetValue(go.Identifier, out NetworkObject? entry) && entry.GameObject == go)
        {
            entry.NotifyDespawn();
        }
    }

    internal void AssertStructureEditable(GameObject? go)
    {
        if (!go.IsValid() || !IsActive || go.Scene != ReplicatedScene || go.Scene is not { IsDisposed: false })
        {
            return;
        }

        if (FindEntry(go) is { TearingDown: false })
        {
            throw new InvalidOperationException("A spawned network object's components and hierarchy are fixed. " +
                                                "Build the tree before Network.Spawn, or spawn/despawn a whole object.");
        }
    }

    internal void AssertObjectDestroyable(GameObject go)
    {
        if (!IsActive || go.Scene is not { IsDisposed: false })
        {
            return;
        }

        if (FindEntry(go) is not { TearingDown: false } entry)
        {
            // An ordinary object can still hold spawned roots, which only the host may despawn.
            if (!IsHost)
            {
                foreach (GameObject child in go.GetChildrenDeep())
                {
                    AssertStructureEditable(child);
                }
            }

            return;
        }

        if (IsHost && entry.GameObject == go)
        {
            return;
        }

        AssertStructureEditable(go);
    }

    internal void AssertHierarchyEditable(GameObject? go)
    {
        AssertStructureEditable(go);
        if (!go.IsValid() || !IsActive || go.Scene is not { IsDisposed: false })
        {
            return;
        }

        foreach (GameObject child in go.GetChildrenDeep())
        {
            AssertStructureEditable(child);
        }
    }
}
