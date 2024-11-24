// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

/// <summary>
/// A struct that represents a layer mask.
/// </summary>
public struct LayerMask
{
    public readonly static LayerMask Everything = new() { mask = uint.MaxValue };
    public readonly static LayerMask Nothing = new() { mask = 0 };

    [SerializeField]
    private uint mask;

    public uint Mask => mask;

    public void Clear() => mask = 0;

    public bool HasLayer(int index) => (mask & (1 << index)) == (1 << index);
    public void SetLayer(int index) => mask |= 1u << index;
    public void RemoveLayer(int index) => mask &= ~(1u << index);
    public static LayerMask operator |(LayerMask mask1, LayerMask mask2) => new() { mask = mask1.mask | mask2.mask };
    public static LayerMask operator &(LayerMask mask1, LayerMask mask2) => new() { mask = mask1.mask & mask2.mask };
    public override bool Equals(object? obj)
    {
        if (obj is null || !(obj is LayerMask other))
            return false;
        return mask == other.mask;
    }
    public override int GetHashCode() => mask.GetHashCode();

    public static string LayerToName(int index) => TagLayerManager.GetLayer(index);
    public static int NameToLayer(string name) => TagLayerManager.GetLayerIndex(name);
}
