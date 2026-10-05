using System;

namespace Prowl.Graphite;

internal static class ValidationHelpers
{
    internal static void RequireNotNull(GraphicsDevice? device, object value, string parameterName, string caller)
    {
        if (device is { ValidationEnabled: false })
            return;

        if (value == null)
        {
            throw new ArgumentNullException(parameterName,
                $"'{parameterName}' passed to {caller} must be non-null.");
        }
    }

    internal static void RequireNotNullRender(GraphicsDevice? device, object value, string typeName, string caller)
    {
        if (device is { ValidationEnabled: false })
            return;

        if (value == null)
        {
            throw new RenderException($"{typeName} passed to {caller} must be non-null.");
        }
    }

    /// <summary>
    /// Array layer count for a texture, x6 for cubemap faces.
    /// </summary>
    internal static uint GetEffectiveArrayLayers(Texture texture)
        => texture.Type == TextureType.TextureCube ? texture.ArrayLayers * 6 : texture.ArrayLayers;

    internal static void CopyTextureCheckNotNull(GraphicsDevice? device, Texture source, Texture destination)
    {
        if (device is { ValidationEnabled: false })
            return;

        RequireNotNull(device, source, nameof(source), "CopyTexture");
        RequireNotNull(device, destination, nameof(destination), "CopyTexture");
    }

    internal static void CopyTextureCheckDimensionsCompatible(GraphicsDevice? device, Texture source, Texture destination)
    {
        if (device is { ValidationEnabled: false })
            return;

        if (source.SampleCount != destination.SampleCount || source.Width != destination.Width
            || source.Height != destination.Height || source.Depth != destination.Depth
            || source.Format != destination.Format)
        {
            throw new RenderException("Source and destination Textures are not compatible to be copied in CopyTexture.");
        }
    }

    internal static void CopyTextureCheckCompatibilityAll(GraphicsDevice? device, Texture source, Texture destination, uint effectiveSrcArrayLayers)
    {
        if (device is { ValidationEnabled: false })
            return;

        uint effectiveDstArrayLayers = GetEffectiveArrayLayers(destination);
        if (effectiveSrcArrayLayers != effectiveDstArrayLayers || source.MipLevels != destination.MipLevels)
        {
            throw new RenderException("Source and destination Textures are not compatible to be copied in CopyTexture.");
        }
        CopyTextureCheckDimensionsCompatible(device, source, destination);
    }

    internal static void CopyTextureCheckCompatibilityForSubresource(GraphicsDevice? device, Texture source, Texture destination, uint mipLevel, uint arrayLayer)
    {
        if (device is { ValidationEnabled: false })
            return;

        uint effectiveSrcArrayLayers = GetEffectiveArrayLayers(source);
        uint effectiveDstArrayLayers = GetEffectiveArrayLayers(destination);
        CopyTextureCheckDimensionsCompatible(device, source, destination);
        if (mipLevel >= source.MipLevels || mipLevel >= destination.MipLevels || arrayLayer >= effectiveSrcArrayLayers || arrayLayer >= effectiveDstArrayLayers)
        {
            throw new RenderException("mipLevel and arrayLayer must be less than the given Textures' mip level count and array layer count.");
        }
    }

    internal static void CopyTextureCheckRegion(GraphicsDevice? device, uint width, uint height, uint depth, uint layerCount)
    {
        if (device is { ValidationEnabled: false })
            return;

        if (width == 0 || height == 0 || depth == 0)
        {
            throw new RenderException("The given copy region is empty.");
        }
        if (layerCount == 0)
        {
            throw new RenderException("layerCount must be greater than 0.");
        }
    }
}
