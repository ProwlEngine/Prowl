// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

using BepuPhysics;
using BepuPhysics.Collidables;

namespace Prowl.Runtime.Raycast;

internal struct OverlapCollectionHandler : ISweepHitHandler
{
    public LayerMask? LayerMask { get; set; }
    private readonly ICollection<PhysicsBody> _collection;

    public OverlapCollectionHandler(ICollection<PhysicsBody> collection, LayerMask? layerMask)
    {
        LayerMask = layerMask;
        _collection = collection;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowTest(CollidableReference collidable)
    {
        var result = Physics.GetContainer(collidable);
        return LayerMask?.HasLayer(result.GameObject.layerIndex) ?? true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowTest(CollidableReference collidable, int childIndex)
    {
        return true;
    }

    public void OnHit(ref float maximumT, float t, System.Numerics.Vector3 hitLocation, System.Numerics.Vector3 hitNormal, CollidableReference collidable) { }

    public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
    {
        _collection.Add(Physics.GetContainer(collidable));
    }
}

internal struct OverlapArrayHandler : ISweepHitHandler
{
    public LayerMask? LayerMask { get; set; }
    private readonly PhysicsBody[] _collection;

    public int Count { get; set; }

    public OverlapArrayHandler(PhysicsBody[] collection, LayerMask? layerMask)
    {
        LayerMask = layerMask;
        _collection = collection;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowTest(CollidableReference collidable)
    {
        var result = Physics.GetContainer(collidable);
        return LayerMask?.HasLayer(result.GameObject.layerIndex) ?? true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowTest(CollidableReference collidable, int childIndex)
    {
        return true;
    }

    public void OnHit(ref float maximumT, float t, System.Numerics.Vector3 hitLocation, System.Numerics.Vector3 hitNormal, CollidableReference collidable) { }

    public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
    {
        if (Count >= _collection.Length)
            return;

        _collection[Count++] = Physics.GetContainer(collidable);

        if (Count == _collection.Length)
            maximumT = -1f; // We want to notify bepu that we don't care about any subsequent collision, not sure that works in the process breaking out early but whatever
    }
}

internal struct OverlapAnyHandler : ISweepHitHandler
{
    public LayerMask? LayerMask { get; set; }
    public bool Any { get; set; }

    public OverlapAnyHandler(LayerMask? layerMask)
    {
        LayerMask = layerMask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowTest(CollidableReference collidable)
    {
        var result = Physics.GetContainer(collidable);
        return LayerMask?.HasLayer(result.GameObject.layerIndex) ?? true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowTest(CollidableReference collidable, int childIndex)
    {
        return true;
    }

    public void OnHit(ref float maximumT, float t, System.Numerics.Vector3 hitLocation, System.Numerics.Vector3 hitNormal, CollidableReference collidable) { }

    public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
    {
        Any = true;
        maximumT = -1f; // Not sure that even works to let bepu know that it should not compute for more at all
    }
}
