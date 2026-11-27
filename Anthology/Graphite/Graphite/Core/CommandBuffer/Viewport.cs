using System;

namespace Prowl.Graphite;

/// <summary>
/// A 3D region.
/// </summary>
public record struct Viewport
{
    /// <summary>
    /// Min X.
    /// </summary>
    public float X;
    /// <summary>
    /// Min Y.
    /// </summary>
    public float Y;
    /// <summary>
    /// Width.
    /// </summary>
    public float Width;
    /// <summary>
    /// Height.
    /// </summary>
    public float Height;
    /// <summary>
    /// Min depth.
    /// </summary>
    public float MinDepth;
    /// <summary>
    /// Max depth.
    /// </summary>
    public float MaxDepth;

    /// <summary>
    /// Makes a Viewport.
    /// </summary>
    /// <param name="x">Min X.</param>
    /// <param name="y">Min Y.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="minDepth">Min depth.</param>
    /// <param name="maxDepth">Max depth.</param>
    public Viewport(float x, float y, float width, float height, float minDepth, float maxDepth)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        MinDepth = minDepth;
        MaxDepth = maxDepth;
    }
}
