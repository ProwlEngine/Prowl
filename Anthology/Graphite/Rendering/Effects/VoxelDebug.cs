using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>What <see cref="VoxelDebug"/> shows of the voxels each pixel's ray hits.</summary>
public enum VoxelDebugMode
{
    /// <summary>Albedo shaded by the face hit, emissive voxels as stored.</summary>
    Albedo,

    /// <summary>The axis aligned normal of the face hit.</summary>
    Normals,

    /// <summary>How many cells the trace stepped through, red to yellow up to 64.</summary>
    Steps,

    /// <summary>The light the hit sends back, direct and bounced from the radiance cache, the sky where the ray leaves.</summary>
    Lighting,

    /// <summary>The bounced light alone, the hit's albedo times the radiance cache at its face.</summary>
    Indirect,
}

/// <summary>
/// Shows the world's voxel volume in place of the view's image, traced from the camera through its occupancy pyramid.
/// Pixels whose ray misses show the image darkened. Draws nothing while the world has no box, see
/// <see cref="RenderWorld.SetVoxelVolume"/>.
/// </summary>
public sealed class VoxelDebug : PostEffect
{
    private static readonly PropertyID s_voxels = "Graphite_Voxels", s_cache = "Graphite_VoxelCache", s_lighting = "Graphite_VoxelLighting", s_sunShadow = "Graphite_VoxelSunShadow",
        s_sky = "Graphite_VoxelSky", s_boxMin = "_BoxMin", s_mode = "_Mode";

    private sealed class State
    {
        public VoxelInputs? Inputs;
    }

    private readonly FullscreenProgram _program, _passThrough;
    private readonly PropertySet _properties = new();
    private VoxelDebugMode _mode;

    public VoxelDebug(Renderer renderer) : base(renderer, RenderSlot.DisplayPost)
    {
        _program = Program("VoxelDebug");
        _passThrough = renderer.CreateFullscreenProgram(InternalShaders.CopyBack, "VoxelDebug pass through");
        Order = 1000;
    }

    public VoxelDebugMode Mode { get; set; }

    public override void Extract() => _mode = Mode;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.GetViewState<State>().Inputs = builder.ReadVoxels();
        builder.WriteColor();
    }

    public override void Render(FeatureContext context)
    {
        if (context.GetViewState<State>().Inputs is not { Voxels.Data: { } data } inputs)
        {
            context.Fullscreen(_passThrough);
            return;
        }
        WorldVoxels voxels = inputs.Voxels;
        Float3 extent = voxels.Extent;
        Double3 min = voxels.Center - new Double3(extent.X, extent.Y, extent.Z) * 0.5 - context.View.Position;
        _properties.SetBuffer(s_voxels, data);
        _properties.SetBuffer(s_cache, inputs.Cache);
        _properties.SetBuffer(s_lighting, voxels.Lighting.Buffer);
        _properties.SetTexture(s_sunShadow, inputs.SunShadow, inputs.ShadowSampler);
        _properties.SetTexture(s_sky, inputs.Sky, LinearClamp);
        _properties.SetFloat4(s_boxMin, new Float4((float)min.X, (float)min.Y, (float)min.Z, voxels.Header.VoxelSize));
        _properties.SetInt(s_mode, (int)_mode);
        context.Fullscreen(_program, _properties);
    }
}
