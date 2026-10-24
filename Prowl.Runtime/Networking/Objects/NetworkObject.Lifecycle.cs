// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

internal sealed partial class NetworkObject
{
    internal void NotifySpawn()
    {
        if (SpawnNotified || !System.IsActive || System.FindEntry(GameObject) != this)
        {
            return;
        }

        // Record membership before game code runs: callbacks may destroy this root or end the session.
        SpawnNotified = true;
        NotifiedOwnerId = Owner;
        HasLocalAuthority = System.HasControl(GameObject);
        UpdatePhysics();
        SceneEvents.RunLifecycle<INetworkEvents>(GameObject, e =>
        {
            if (SpawnNotified && SpawnListeners.Add((Component)e))
            {
                e.OnNetworkSpawn();
            }
        });

        if (SpawnNotified && HasLocalAuthority)
        {
            NotifyAuthority(true);
        }
    }

    internal void NotifyAuthority(bool gained) =>
        SceneEvents.RunLifecycle<INetworkEvents>(GameObject, e =>
        {
            Component component = (Component)e;
            if (gained)
            {
                if (SpawnNotified && HasLocalAuthority && SpawnListeners.Contains(component) &&
                    AuthorityListeners.Add(component))
                {
                    e.OnNetworkAuthorityGained();
                }
            }
            else if (AuthorityListeners.Remove(component))
            {
                e.OnNetworkAuthorityLost();
            }
        });

    internal void NotifyOwnership()
    {
        if (!SpawnNotified)
        {
            return;
        }

        bool hadAuthority = HasLocalAuthority;
        int previousOwnerId = NotifiedOwnerId;
        uint generation = Generation;
        bool hasAuthority = System.HasControl(GameObject);
        HasLocalAuthority = hasAuthority;
        NotifiedOwnerId = Owner;
        if (hadAuthority && !hasAuthority)
        {
            NotifyAuthority(false);
        }

        if (!SpawnNotified || Generation != generation)
        {
            return;
        }

        NetPeer? oldOwner = null;
        if (System.Session is { } session)
        {
            session.TryGetPeer(previousOwnerId, out oldOwner);
        }

        NetPeer? newOwner = System.GetOwner(GameObject);
        SceneEvents.RunLifecycle<INetworkEvents>(GameObject, e =>
        {
            if (SpawnNotified && Generation == generation && SpawnListeners.Contains((Component)e))
            {
                e.OnNetworkOwnerChanged(oldOwner, newOwner);
            }
        });

        if (!hadAuthority && hasAuthority && SpawnNotified && Generation == generation)
        {
            NotifyAuthority(true);
        }
    }

    internal void NotifyDespawn()
    {
        if (!SpawnNotified)
        {
            return;
        }

        // End membership before notifying listeners so nested teardown cannot notify twice.
        SpawnNotified = false;
        TearingDown = true;
        HasLocalAuthority = false;
        NotifyAuthority(false);
        SceneEvents.RunLifecycle<INetworkEvents>(GameObject, e =>
        {
            if (SpawnListeners.Remove((Component)e))
            {
                e.OnNetworkDespawn();
            }
        });
        SpawnListeners.Clear();
        AuthorityListeners.Clear();
        RestorePhysics();
    }

    internal void ChangeOwner(int owner)
    {
        if (Owner == owner)
        {
            return;
        }

        Float3 velocity = TransferVelocity ?? OwnershipVelocity();
        SendOwnershipState(velocity);
        ApplyOwner(owner, System.NextGeneration(), velocity);

        using NetWriter message = new(32);
        new OwnerMessage(GameObject.Identifier, owner, Generation).Write(message);
        System.BroadcastEngine(EngineMessage.Owner, message.WrittenSpan);
        NotifyOwnership();
    }

    internal void ApplyOwner(int owner, uint generation, Float3 velocity)
    {
        TransferVelocity = null;
        Owner = owner;
        Generation = generation;
        TransformSync.Reset();
        UpdatePhysics(velocity);
    }
}
