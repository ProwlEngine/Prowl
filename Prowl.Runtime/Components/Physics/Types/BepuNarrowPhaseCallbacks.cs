// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;

using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;

using Prowl.Runtime.Contacts;

namespace Prowl.Runtime;

public unsafe struct BepuNarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    internal CollidableProperty<PhysicsMaterial> CollidableMaterials { get; set; }

    internal ContactEventsManager ContactEvents { get; set; }

    public void Initialize(Simulation simulation)
    {
        //Often, the callbacks type is created before the simulation instance is fully constructed, so the simulation will call this function when it's ready.
        //Any logic which depends on the simulation existing can be put here.
        Physics.Characters.Initialize(simulation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
    {
        return a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
    {
        var matA = CollidableMaterials[pair.A];
        var matB = CollidableMaterials[pair.B];

        return PhysicsMaterial.AllowContactGeneration(matA, matB);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
    {
        //For the purposes of this demo, we'll use multiplicative blending for the friction and choose spring properties according to which collidable has a higher maximum recovery velocity.
        var a = CollidableMaterials[pair.A];
        var b = CollidableMaterials[pair.B];
        pairMaterial.FrictionCoefficient = a.FrictionCoefficient * b.FrictionCoefficient;
        pairMaterial.MaximumRecoveryVelocity = (float)MathD.Max(a.MaximumRecoveryVelocity, b.MaximumRecoveryVelocity);
        pairMaterial.SpringSettings = pairMaterial.MaximumRecoveryVelocity == a.MaximumRecoveryVelocity ? a.SpringSettings : b.SpringSettings;
        //For the purposes of the demo, contact constraints are always generated.
        ContactEvents.HandleManifold(workerIndex, pair, ref manifold);
        Physics.Characters.TryReportContacts(pair, ref manifold, workerIndex, ref pairMaterial);

        if (a.IsTrigger || b.IsTrigger)
        {
            return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
    {
        return true;
    }

    public void Dispose()
    {
    }
}
