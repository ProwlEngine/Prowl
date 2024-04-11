namespace Prowl.Runtime;

public class LayerMask
{
    private int mask;

    public LayerMask()
    {
        mask = 0;
    }

    public void AddLayer(string layer)
    {
        int layerIndex = TagLayerManager.GetLayerIndex(layer);
        mask |= 1 << layerIndex;
    }

    public void RemoveLayer(string layer)
    {
        int layerIndex = TagLayerManager.GetLayerIndex(layer);
        if (layerIndex != -1)
            mask &= ~(1 << layerIndex);
    }

    public bool Contains(string layer)
    {
        int layerIndex = TagLayerManager.GetLayerIndex(layer);
        return layerIndex != -1 && ((mask >> layerIndex) & 1) != 0;
    }

    public bool Intersects(LayerMask other)
    {
        return (mask & other.mask) != 0;
    }

    public static implicit operator int(LayerMask mask)
    {
        return mask.mask;
    }

    public static LayerMask operator |(LayerMask mask1, LayerMask mask2)
    {
        LayerMask result = new LayerMask();
        result.mask = mask1.mask | mask2.mask;
        return result;
    }

    public static LayerMask operator &(LayerMask mask1, LayerMask mask2)
    {
        LayerMask result = new LayerMask();
        result.mask = mask1.mask & mask2.mask;
        return result;
    }
}