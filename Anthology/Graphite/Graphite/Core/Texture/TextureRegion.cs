namespace Prowl.Graphite;

/// <summary>
/// Box of texels in one mip level and array layer of a texture.
/// Unset depth defaults to 1, unset offsets to 0.
/// </summary>
public readonly struct TextureRegion
{
    /// <summary>
    /// Min X, in texels.
    /// </summary>
    public readonly uint X;
    /// <summary>
    /// Min Y, in texels.
    /// </summary>
    public readonly uint Y;
    /// <summary>
    /// Min Z, in texels.
    /// </summary>
    public readonly uint Z;
    /// <summary>
    /// Width in texels.
    /// </summary>
    public readonly uint Width;
    /// <summary>
    /// Height in texels.
    /// </summary>
    public readonly uint Height;
    /// <summary>
    /// Depth in texels.
    /// </summary>
    public readonly uint Depth;
    /// <summary>
    /// Mip level.
    /// </summary>
    public readonly uint MipLevel;
    /// <summary>
    /// Array layer.
    /// </summary>
    public readonly uint ArrayLayer;

    /// <summary>
    /// Full box with explicit offsets, size, mip level and layer.
    /// </summary>
    public TextureRegion(uint x, uint y, uint z, uint width, uint height, uint depth, uint mipLevel = 0, uint arrayLayer = 0)
    {
        X = x;
        Y = y;
        Z = z;
        Width = width;
        Height = height;
        Depth = depth;
        MipLevel = mipLevel;
        ArrayLayer = arrayLayer;
    }

    /// <summary>
    /// 1D span at the origin.
    /// </summary>
    public TextureRegion(uint width) : this(0, 0, 0, width, 1, 1)
    {
    }

    /// <summary>
    /// 2D box at the origin of mip 0, layer 0.
    /// </summary>
    public TextureRegion(uint width, uint height) : this(0, 0, 0, width, height, 1)
    {
    }

    /// <summary>
    /// 3D box at the origin of mip 0.
    /// </summary>
    public TextureRegion(uint width, uint height, uint depth) : this(0, 0, 0, width, height, depth)
    {
    }

    /// <summary>
    /// 2D box at an offset in mip 0, layer 0.
    /// </summary>
    public TextureRegion(uint x, uint y, uint width, uint height) : this(x, y, 0, width, height, 1)
    {
    }

    /// <summary>
    /// Whole mip 0 of layer 0.
    /// </summary>
    public static TextureRegion Whole(Texture texture) => Whole(texture, 0, 0);

    /// <summary>
    /// Whole given mip level of the given layer.
    /// </summary>
    public static TextureRegion Whole(Texture texture, uint mipLevel, uint arrayLayer = 0)
    {
        Util.GetMipDimensions(texture, mipLevel, out uint width, out uint height, out uint depth);
        return new TextureRegion(0, 0, 0, width, height, depth, mipLevel, arrayLayer);
    }

    /// <summary>
    /// Copy of this region on another mip level.
    /// </summary>
    public TextureRegion WithMip(uint mipLevel) => new(X, Y, Z, Width, Height, Depth, mipLevel, ArrayLayer);

    /// <summary>
    /// Copy of this region on another array layer.
    /// </summary>
    public TextureRegion WithLayer(uint arrayLayer) => new(X, Y, Z, Width, Height, Depth, MipLevel, arrayLayer);
}
