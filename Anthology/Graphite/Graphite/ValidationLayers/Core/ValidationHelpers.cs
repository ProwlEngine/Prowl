using System;

namespace Prowl.Graphite;

internal static class ValidationHelpers
{
    internal static void RequireNotNull(object value, string parameterName, string caller)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (value == null)
        {
            throw new ArgumentNullException(parameterName,
                $"'{parameterName}' passed to {caller} must be non-null.");
        }
    }

    internal static void RequireNotNullRender(object value, string typeName, string caller)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (value == null)
        {
            throw new RenderException($"{typeName} passed to {caller} must be non-null.");
        }
    }

    /// <summary>
    /// Returns the number of array layers a <see cref="Texture"/> occupies, accounting for the six faces
    /// contributed by each layer of a <see cref="TextureUsage.Cubemap"/>.
    /// </summary>
    internal static uint GetEffectiveArrayLayers(Texture texture)
        => (texture.Usage & TextureUsage.Cubemap) != 0 ? texture.ArrayLayers * 6 : texture.ArrayLayers;
}
