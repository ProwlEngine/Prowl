// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;

namespace Prowl.Runtime.Raycast;

internal struct RayClosestHitHandler : IRayHitHandler, ISweepHitHandler
{
    public LayerMask? LayerMask { get; set; }
    public HitInfo? HitInformation { get; set; }

    public RayClosestHitHandler(LayerMask? layerMask)
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

    public void OnRayHit(in RayData ray, ref float maximumT, float t, System.Numerics.Vector3 normal, CollidableReference collidable, int childIndex)
    {
        HitInformation = new(ray.Origin + ray.Direction * t, normal, t, Physics.GetContainer(collidable));
        maximumT = t;
    }

    public void OnHit(ref float maximumT, float t, System.Numerics.Vector3 hitLocation, System.Numerics.Vector3 hitNormal, CollidableReference collidable)
    {
        HitInformation = new(hitLocation, hitNormal, t, Physics.GetContainer(collidable));
        maximumT = t;
    }

    public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
    {
        // Right now just ignore the hit;
        // We can't just set info to invalid data, it'll be confusing for users,
        // but we might need to find a way to notify that the shape at its resting pose is already intersecting.
    }
}

internal struct RayHitsCollectionHandler : IRayHitHandler, ISweepHitHandler
{
    public LayerMask? LayerMask { get; set; }
    private readonly ICollection<HitInfo> _collection;

    public RayHitsCollectionHandler(ICollection<HitInfo> collection, LayerMask? layerMask)
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

    public void OnRayHit(in RayData ray, ref float maximumT, float t, System.Numerics.Vector3 normal, CollidableReference collidable, int childIndex)
    {
        _collection.Add(new(ray.Origin + ray.Direction * t, normal, t, Physics.GetContainer(collidable)));
    }

    public void OnHit(ref float maximumT, float t, System.Numerics.Vector3 hitLocation, System.Numerics.Vector3 hitNormal, CollidableReference collidable)
    {
        _collection.Add(new(hitLocation, hitNormal, t, Physics.GetContainer(collidable)));
    }

    public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
    {
        // Right now just ignore the hit;
        // We can't just set info to invalid data, it'll be confusing for users,
        // but we might need to find a way to notify that the shape at its resting pose is already intersecting.
    }
}

internal struct RayHitsArrayHandler : IRayHitHandler, ISweepHitHandler
{
    private readonly HitInfo[] _array;

    public LayerMask? LayerMask { get; set; }
    public int Count { get; set; }
    public float StoredMax { get; set; }
    public int IndexOfMax { get; set; }

    public RayHitsArrayHandler(HitInfo[] array, LayerMask? layerMask)
    {
        LayerMask = layerMask;
        _array = array;
        StoredMax = float.NegativeInfinity;
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


    public void OnRayHit(in RayData ray, ref float maximumT, float t, System.Numerics.Vector3 normal, CollidableReference collidable, int childIndex)
    {
        if (Count < _array.Length)
        {
            if (t > StoredMax)
            {
                StoredMax = t;
                IndexOfMax = Count;
            }

            _array[Count++] = new(ray.Origin + ray.Direction * t, normal, t, Physics.GetContainer(collidable));

            if (Count == _array.Length) // Once the array is filled up, ignore all hits that occur further away than the furthest hit in the array
                maximumT = StoredMax;
        }
        else
        {
            Debug.Assert(t > StoredMax, "maximumT should have prevented this hit from being returned, if this is hit it means that we need to change the above into an 'else if (distance < StoredMax)'");

            _array[IndexOfMax] = new(ray.Origin + ray.Direction * t, normal, t, Physics.GetContainer(collidable));

            // Re-scan to find the new max now that the last one was replaced
            StoredMax = float.NegativeInfinity;
            for (int i = 0; i < _array.Length; i++)
            {
                if (_array[i].Distance > StoredMax)
                {
                    StoredMax = _array[i].Distance;
                    IndexOfMax = i;
                }
            }

            maximumT = StoredMax;
        }
    }

    public void OnHit(ref float maximumT, float t, System.Numerics.Vector3 hitLocation, System.Numerics.Vector3 normal, CollidableReference collidable)
    {
        if (Count < _array.Length)
        {
            if (t > StoredMax)
            {
                StoredMax = t;
                IndexOfMax = Count;
            }

            _array[Count++] = new(hitLocation, normal, t, Physics.GetContainer(collidable));

            if (Count == _array.Length) // Once the array is filled up, ignore all hits that occur further away than the furthest hit in the array
                maximumT = StoredMax;
        }
        else
        {
            Debug.Assert(t > StoredMax, "maximumT should have prevented this hit from being returned, if this is hit it means that we need to change the above into an 'else if (distance < StoredMax)'");

            _array[IndexOfMax] = new(hitLocation, normal, t, Physics.GetContainer(collidable));

            // Re-scan to find the new max now that the last one was replaced
            StoredMax = float.NegativeInfinity;
            for (int i = 0; i < _array.Length; i++)
            {
                if (_array[i].Distance > StoredMax)
                {
                    StoredMax = _array[i].Distance;
                    IndexOfMax = i;
                }
            }

            maximumT = StoredMax;
        }
    }

    public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
    {
        // Right now just ignore the hit;
        // We can't just set info to invalid data, it'll be confusing for users,
        // but we might need to find a way to notify that the shape at its resting pose is already intersecting.
    }
}
