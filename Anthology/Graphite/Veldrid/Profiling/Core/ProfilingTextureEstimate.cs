#if PROFILE_USAGE
namespace Prowl.Veldrid;

/// <summary>
/// Shared logical-size estimator for backends that do not expose a texture's true allocation size
/// (D3D11, OpenGL). Sums the tightly-packed pixel bytes across every mip level and array layer,
/// matching the convention used for buffers (logical, not alignment-padded).
/// </summary>
internal static class ProfilingTextureEstimate
{
    public static long EstimateBytes(Texture texture)
    {
        long bytes = 0;
        for (uint level = 0; level < texture.MipLevels; level++)
        {
            Util.GetMipDimensions(texture, level, out uint mipWidth, out uint mipHeight, out uint mipDepth);
            uint rowPitch = FormatHelpers.GetRowPitch(mipWidth, texture.Format);
            uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, mipHeight, texture.Format);
            bytes += (long)depthPitch * mipDepth;
        }
        return bytes * texture.ArrayLayers;
    }
}
#endif
