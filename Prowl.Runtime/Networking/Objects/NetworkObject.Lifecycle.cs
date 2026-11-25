// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

internal sealed partial class NetworkObject
{
    // True when this peer owns the object, which is what authority callbacks follow.
    private bool OwnedHere() => System.Session?.LocalPeer is { } local && Owner == local.Id;

    // Runs action on each of the object's behaviours, disabled ones too, while the spawn lasts.
    private void Run(System.Action<NetworkBehaviour> action) =>
        SceneEvents.RunLifecycle<NetworkBehaviour>(GameObject, behaviour =>
        {
            if (SpawnNotified)
            {
                action(behaviour);
            }
        });

    internal void NotifySpawn()
    {
        if (SpawnNotified || !System.IsActive || System.FindEntry(GameObject) != this)
        {
            return;
        }

        // Record membership before game code runs: callbacks may destroy this root or end the session.
        SpawnNotified = true;
        NotifiedOwnerId = Owner;
        HasLocalAuthority = OwnedHere();
        UpdatePhysics();
        if (System.IsHost)
        {
            Run(behaviour =>
            {
                if (SpawnListeners.Add(behaviour))
                {
                    behaviour.OnStartServer();
                }
            });
        }

        Run(behaviour =>
        {
            if (ClientListeners.Add(behaviour))
            {
                SpawnListeners.Add(behaviour);
                behaviour.OnStartClient();
            }
        });

        if (SpawnNotified && HasLocalAuthority)
        {
            NotifyAuthority(true);
        }
    }

    internal void NotifyAuthority(bool gained) =>
        SceneEvents.RunLifecycle<NetworkBehaviour>(GameObject, behaviour =>
        {
            if (gained)
            {
                if (SpawnNotified && HasLocalAuthority && ClientListeners.Contains(behaviour) &&
                    AuthorityListeners.Add(behaviour))
                {
                    behaviour.OnStartAuthority();
                }
            }
            else if (AuthorityListeners.Remove(behaviour))
            {
                behaviour.OnStopAuthority();
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
        bool hasAuthority = OwnedHere();
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
        Run(behaviour =>
        {
            if (Generation == generation && SpawnListeners.Contains(behaviour))
            {
                behaviour.OnOwnerChanged(oldOwner, newOwner);
            }
        });

        if (!hadAuthority && hasAuthority && SpawnNotified && Generation == generation)
        {
            NotifyAuthority(true);
        }
    }

    internal void NotifyDespawn(NetworkDespawnReason reason = NetworkDespawnReason.Destroyed)
    {
        System.Sync.ForgetReferencesOf(this);
        if (!SpawnNotified)
        {
            return;
        }

        // End membership before notifying listeners so nested teardown cannot notify twice.
        SpawnNotified = false;
        System.ForgetPlayer(this);
        GameObject.Network.DespawnReason = reason;
        TearingDown = true;
        HasLocalAuthority = false;
        SceneEvents.RunLifecycle<NetworkBehaviour>(GameObject, behaviour =>
        {
            if (ClientListeners.Remove(behaviour))
            {
                behaviour.OnStopClient();
            }
        });
        NotifyAuthority(false);
        SceneEvents.RunLifecycle<NetworkBehaviour>(GameObject, behaviour =>
        {
            if (SpawnListeners.Remove(behaviour) && System.IsHost)
            {
                behaviour.OnStopServer();
            }
        });
        SpawnListeners.Clear();
        ClientListeners.Clear();
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
        int previous = Owner;
        ApplyOwner(owner, System.NextGeneration(), velocity);

        using NetWriter message = new(32);
        new OwnerMessage(GameObject.Identifier, owner, Generation).Write(message);
        System.SendToObservers(this, EngineMessage.Owner, message.WrittenSpan);
        System.SendOwnerState(this, previous);
        NotifyOwnership();
        if (System.FindEntry(GameObject) == this && !TearingDown)
        {
            System.RebuildObservers(this);
        }
    }

    internal void ApplyOwner(int owner, uint generation, Float3 velocity)
    {
        TransferVelocity = null;
        Owner = owner;
        Generation = generation;
        System.Transforms.OwnerChanged(this);
        ResetTransforms();
        UpdatePhysics(velocity);
    }
}
