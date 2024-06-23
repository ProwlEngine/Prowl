namespace Prowl.Runtime;

public struct LayerMask
{
    [SerializeField]
    private uint mask;

    public uint Mask => mask;

    public bool HasLayer(byte index) => (mask & (1 << index)) == (1 << index);
    public void SetLayer(byte index) => mask |= 1u << index;
    public void RemoveLayer(byte index) => mask &= ~(1u << index);
    public static LayerMask operator |(LayerMask mask1, LayerMask mask2) => new() { mask = mask1.mask | mask2.mask };
    public static LayerMask operator &(LayerMask mask1, LayerMask mask2) => new() { mask = mask1.mask & mask2.mask };
    public override bool Equals(object? obj)
    {
        if (obj is null || !(obj is LayerMask other))
            return false;
        return mask == other.mask;
    }
    public override int GetHashCode() => mask.GetHashCode();

    public static string LayerToName(byte index) => TagLayerManager.GetLayer(index);
    public static byte NameToLayer(string name) => TagLayerManager.GetLayerIndex(name);
}