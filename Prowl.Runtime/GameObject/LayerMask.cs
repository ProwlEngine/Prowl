namespace Prowl.Runtime;

public class LayerMask
{
    private uint mask = 0;

    public bool Intersects(LayerMask other) => HasLayer(other.mask);
    public bool HasLayer(uint index) => (mask & (1 << (int)index)) == (1 << (int)index);
    public void SetLayer(uint index) => mask |= 1u << (int)index;
    public void RemoveLayer(uint index) => mask &= ~(1u << (int)index);
    public static LayerMask operator |(LayerMask mask1, LayerMask mask2) => new() { mask = mask1.mask | mask2.mask };
    public static LayerMask operator &(LayerMask mask1, LayerMask mask2) => new() { mask = mask1.mask & mask2.mask };
    public override bool Equals(object obj)
    {
        if (obj is null || !(obj is LayerMask other))
            return false;
        return mask == other.mask;
    }
    public override int GetHashCode() => mask.GetHashCode();

    public static string LayerToName(int index) => TagLayerManager.GetLayer(index);
    public static int NameToLayer(string name) => TagLayerManager.GetLayerIndex(name);
}