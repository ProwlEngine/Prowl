// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Graphite;

namespace Prowl.Runtime.Rendering;

/// <summary>How a graph resource's pixel dimensions are determined at execution time.</summary>
public enum TextureSizeMode
{
    /// <summary>Sized as a fraction (<see cref="RenderTextureDesc.Scale"/>) of the rendering camera's pixel size.</summary>
    CameraRelative,

    /// <summary>Fixed <see cref="RenderTextureDesc.Width"/> x <see cref="RenderTextureDesc.Height"/> in pixels.</summary>
    Explicit
}

/// <summary>
/// Describes a texture resource a pass wants to read or write. Passes declaring the same
/// <see cref="RenderResourceID"/> share one physical <c>RenderTexture</c>; the first declaration's
/// description wins, so a writer should own the authoritative description.
/// </summary>
public struct RenderTextureDesc
{
    public TextureSizeMode SizeMode;
    public float Scale;
    public int Width;
    public int Height;
    public PixelFormat[] ColorFormats;
    public bool EnableDepth;

    private static PixelFormat[] DefaultFormats(PixelFormat[] formats)
        => formats is { Length: > 0 } ? formats : new[] { PixelFormat.R8_G8_B8_A8_UNorm };

    /// <summary>A resource sized as <paramref name="scale"/> of the camera's pixel dimensions.</summary>
    public static RenderTextureDesc CameraSized(bool depth = true, float scale = 1f, params PixelFormat[] formats) => new()
    {
        SizeMode = TextureSizeMode.CameraRelative,
        Scale = scale,
        ColorFormats = DefaultFormats(formats),
        EnableDepth = depth
    };

    /// <summary>A resource with fixed pixel dimensions, independent of the camera.</summary>
    public static RenderTextureDesc Sized(int width, int height, bool depth = true, params PixelFormat[] formats) => new()
    {
        SizeMode = TextureSizeMode.Explicit,
        Scale = 1f,
        Width = width,
        Height = height,
        ColorFormats = DefaultFormats(formats),
        EnableDepth = depth
    };

    /// <summary>Resolves the concrete pixel dimensions for a camera of the given size.</summary>
    public readonly (int width, int height) Resolve(uint cameraWidth, uint cameraHeight)
    {
        if (SizeMode == TextureSizeMode.Explicit)
            return (Math.Max(1, Width), Math.Max(1, Height));

        float s = Scale <= 0f ? 1f : Scale;
        return (Math.Max(1, (int)(cameraWidth * s)), Math.Max(1, (int)(cameraHeight * s)));
    }
}
