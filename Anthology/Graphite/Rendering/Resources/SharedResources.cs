using Prowl.Graphite;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// What every feature of a renderer may share rather than make its own: the samplers, the noise, the texture a view
/// without a colour copy reads and the scene's formats. See <see cref="Renderer.Shared"/>.
/// </summary>
public sealed class SharedResources
{
    private readonly RendererResources _resources;

    internal SharedResources(RendererResources resources) => _resources = resources;

    public Sampler PointClamp => _resources.PointClamp;
    public Sampler LinearClamp => _resources.LinearClamp;
    public Sampler LinearRepeat => _resources.LinearRepeat;

    /// <summary>Blue noise in red, white noise in green, voronoi in blue, 256 square, mipmapped.</summary>
    public Texture Noise => _resources.Noise;

    /// <summary>Black in six layers, for a feature to bind where a view has no colour to read.</summary>
    public Texture NoSceneColor => _resources.NoSceneColor;

    /// <summary>The format of every view's scene depth.</summary>
    public PixelFormat SceneDepthFormat => _resources.SceneDepthFormat;

    /// <summary>HDR colour without alpha, half the size of 16 bit float where the device draws to it.</summary>
    public PixelFormat OpaqueHdrFormat => _resources.OpaqueHdrFormat;
}
