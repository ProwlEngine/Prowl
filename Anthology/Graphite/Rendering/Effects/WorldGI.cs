using System;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// World space global illumination as the view's screen lighting: probes over the view trace the world's voxels, lit by
/// their hit light, the radiance cache and the sky, resampled over frames and between probes as ReSTIR reservoirs, and
/// every pixel takes the probe around it that suits it best, smoothed over frames. Past the voxel box rays march the
/// screen further and take the sky where they find nothing. Needs a voxel box, see <see cref="RenderWorld.SetVoxelVolume"/>,
/// and leaves the baked light without one. See Graphite/Internal/WorldGI.glsl.
/// </summary>
public sealed class WorldGI : ScreenLightingFeature
{
    private const int ProbeSize = 8, Cells = 4;

    private static readonly PropertyID s_depth = "_CameraDepthTexture", s_normals = "_Normals", s_motion = "_Motion", s_probes = "_Probes",
        s_atlas = "_Atlas", s_current = "_Current", s_history = "_History", s_historyMeta = "_HistoryMeta", s_historyValid = "_HistoryValid",
        s_probeHistoryValid = "_ProbeHistoryValid", s_maxHistory = "_MaxHistory", s_hasVoxels = "_HasVoxels", s_screenSize = "_ScreenSize", s_probeJitter = "_ProbeJitter", s_frame = "_Frame",
        s_boxMin = "_BoxMin", s_voxels = "Graphite_Voxels", s_cache = "Graphite_VoxelCache", s_lighting = "Graphite_VoxelLighting",
        s_sunShadow = "Graphite_VoxelSunShadow", s_sky = "Graphite_VoxelSky", s_previousColor = "_PreviousColor", s_hasPreviousColor = "_HasPreviousColor",
        s_boxSize = "_BoxSize", s_previousProbes = "_PreviousProbes", s_previousProbeJitter = "_PreviousProbeJitter",
        s_reservoir = "_Reservoir", s_reservoirData = "_ReservoirData", s_reservoirHit = "_ReservoirHit", s_previousReservoir = "_PreviousReservoir",
        s_previousReservoirData = "_PreviousReservoirData", s_previousReservoirHit = "_PreviousReservoirHit", s_previousProbePositions = "_PreviousProbePositions", s_probePositions = "_ProbePositions", s_hasOcclusion = "_HasOcclusion",
        s_atlasHit = "_AtlasHit", s_gather = "_Gather", s_surface = "_Surface", s_surfaceNormals = "_SurfaceNormals", s_pass = "_Pass", s_passes = "_Passes", s_sh = "_SH", s_source = "_Source", s_probeAO = "_ProbeAO",
        s_reference = "_Reference", s_referenceFrames = "_ReferenceFrames", s_occlusion = "_Occlusion",
        s_occlusionRadius = "_OcclusionRadius", s_occlusionThickness = "_OcclusionThickness";

    private static readonly PropertyID s_hiZ = "_HiZ";

    private readonly RendererResources _shared;
    private readonly FullscreenProgram _probes, _trace, _probeAO, _temporalReuse, _spatialReuse, _sh, _occlusion, _gather, _gatherOccluded, _splitRows, _splitColumns, _temporal, _filter, _reference, _output, _visibility;
    private readonly PropertySet _properties = new();
    private int _spatialPasses;
    private bool _showReference, _directionalOcclusion;
    private float _occlusionRadius, _occlusionThickness, _resolution = 1f;
    private float _maxHistory;

    private sealed class ViewState
    {
        public TextureHandle Reference;
        public int ReferenceFrames;
        public Float4x4 ReferenceView;
        public Double3 ReferenceCamera;
        public TextureHandle HiZ, Probes, Atlas, ProbeAO, Reservoirs, SpatialA, SpatialB, SH, Occlusion, Gather, SplitRows, SplitColumns, History, Filtered, Visibility, Irradiance;

        /// <summary>Last frame's probe jitter.</summary>
        public Float2 PreviousJitter;
        public VoxelInputs? Inputs;
        public int Frame;

        /// <summary>Whether a frame went by without the GI, so its histories are stale.</summary>
        public bool Skipped = true;

        /// <summary>The scene colour copy last frame drew into, which this frame's screen trace reads before the copy runs again.</summary>
        public Texture? Copied;
    }

    /// <param name="runsOn">The views it lights. A reflection probe's GI settles over frames, so it should capture
    /// several frames in a row.</param>
    public WorldGI(Renderer renderer, ViewKinds runsOn = ViewKinds.Cameras) : base(runsOn: runsOn, sceneReads: SceneTargets.Normals | SceneTargets.Motion)
    {
        _shared = renderer.Resources;
        EffectResources effects = renderer.Effects;
        _probes = effects.Program(this, "WorldGI", "#define GI_PROBES 1\n", source: false);
        _trace = effects.Program(this, "WorldGI", "#define GI_TRACE 1\n", source: false, lit: true);
        _probeAO = effects.Program(this, "WorldGI", "#define GI_PROBE_AO 1\n", source: false);
        _temporalReuse = effects.Program(this, "WorldGI", "#define GI_RESTIR_TEMPORAL 1\n", source: false);
        _spatialReuse = effects.Program(this, "WorldGI", "#define GI_RESTIR_SPATIAL 1\n", source: false);
        _sh = effects.Program(this, "WorldGI", "#define GI_SH 1\n", source: false);
        _occlusion = effects.Program(this, "WorldGI", "#define GI_OCCLUSION 1\n", source: false);
        _gather = effects.Program(this, "WorldGI", "#define GI_GATHER 1\n", source: false);
        _gatherOccluded = effects.Program(this, "WorldGI", "#define GI_GATHER 1\n#define GI_GATHER_OCCLUSION 1\n", source: false);
        _splitRows = effects.Program(this, "WorldGI", "#define GI_SPLIT 1\n", source: false);
        _splitColumns = effects.Program(this, "WorldGI", "#define GI_SPLIT 1\n#define GI_SPLIT_VERTICAL 1\n", source: false);
        _temporal = effects.Program(this, "WorldGI", "#define GI_TEMPORAL 1\n", source: false);
        _filter = effects.Program(this, "WorldGI", "#define GI_FILTER 1\n", source: false);
        _reference = effects.Program(this, "WorldGI", "#define GI_REFERENCE 1\n", source: false, lit: true);
        _output = effects.Program(this, "WorldGI", "#define GI_OUTPUT 1\n", source: false);
        _visibility = effects.Program(this, "WorldGI", "#define GI_VISIBILITY 1\n", source: false);
    }

    /// <summary>
    /// The resolution the GI is gathered and smoothed at. Probes are a pixel in 8 x 8 of the view whatever it is, and the
    /// result is brought up to the view's resolution by depth and normal, so the screen lighting stays full resolution.
    /// </summary>
    public EffectResolution Resolution { get; set; } = EffectResolution.Half;

    /// <summary>How many passes of eight neighbours each reservoir is resampled with, 0 to 4.</summary>
    public int SpatialPasses { get; set; } = 4;

    /// <summary>How many frames the denoiser's history holds at most.</summary>
    public int MaxHistory { get; set; } = 32;

    /// <summary>
    /// Occludes the GI below the probes' spacing from a horizon search of the depth around each pixel: its light comes
    /// along the pixel's bent normal from the cone left open, probes as closed in as it are favoured, and the sky's
    /// reflection is occluded the same.
    /// </summary>
    public bool DirectionalOcclusion { get; set; } = true;

    /// <summary>How far in metres the occlusion searches for surfaces around a pixel.</summary>
    public float OcclusionRadius { get; set; } = 1;

    /// <summary>How far in metres a surface the depth shows is taken to reach behind it.</summary>
    public float OcclusionThickness { get; set; } = 0.3f;

    /// <summary>
    /// Shows the truth to compare the GI against in its place: every pixel path traced through the voxels alone, sun,
    /// emission, the local lights the radiance cache holds and the sky, averaged while the camera holds still. Slow.
    /// </summary>
    public bool Reference { get; set; }

    public override float Scale => 1f;

    public override bool WritesIrradiance => true;

    internal override bool KeepsSceneColor => true;

    public override bool ReadsHiZ => true;

    public override void Extract()
    {
        _spatialPasses = System.Math.Clamp(SpatialPasses, 0, 4);
        _showReference = Reference;
        _maxHistory = System.Math.Max(MaxHistory, 1);
        _directionalOcclusion = DirectionalOcclusion && OcclusionRadius > 0;
        _occlusionRadius = OcclusionRadius;
        _occlusionThickness = System.Math.Max(OcclusionThickness, 0);
        _resolution = Resolution.Scale();
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        builder.ReadScene();
        ViewState state = builder.GetViewState<ViewState>();
        state.Inputs = builder.ReadVoxels();
        (state.Visibility, TextureHandle? irradiance) = builder.WriteScreenLighting(true);
        state.Irradiance = irradiance!.Value;
        int probesX = ((int)view.Width + ProbeSize - 1) / ProbeSize, probesY = ((int)view.Height + ProbeSize - 1) / ProbeSize;
        GraphTextureDesc atlas = view.Texture(PixelFormat.R16_G16_B16_A16_Float) with { Width = probesX * Cells, Height = probesY * Cells };
        GraphTextureDesc reservoirs = atlas with { ColorFormats = [PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R16_G16_B16_A16_Float] };
        GraphTextureDesc probeTargets = view.Texture(PixelFormat.R32_G32_B32_A32_Float) with { Width = probesX, Height = probesY, ColorFormats = [PixelFormat.R32_G32_B32_A32_Float, PixelFormat.R32_G32_B32_A32_Float] };
        state.Probes = builder.History("Probes", probeTargets, usage: TextureState.Attachment | TextureState.Sampled);
        state.ProbeAO = builder.Temporary("ProbeAO", view.Texture(PixelFormat.R16_G16_B16_A16_Float) with { Width = probesX, Height = probesY });
        state.Atlas = builder.Temporary("Atlas", atlas with { ColorFormats = [PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R16_G16_B16_A16_Float] });
        state.Reservoirs = builder.History("Reservoirs", reservoirs, usage: TextureState.Attachment | TextureState.Sampled);
        state.HiZ = builder.Read(RendererOutputs.HiZ);
        state.SpatialA = builder.Temporary("SpatialA", reservoirs);
        state.SpatialB = builder.Temporary("SpatialB", reservoirs);
        state.SH = builder.Temporary("SH", atlas with { Width = probesX * 3, Height = probesY * 3 });
        state.Gather = builder.Temporary("Gather", view.Texture(PixelFormat.R16_G16_B16_A16_Float, _resolution) with { ColorFormats = [PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R32_G32_B32_A32_Float, PixelFormat.R16_G16_Float] });
        state.SplitRows = builder.Temporary("SplitRows", view.Texture(PixelFormat.R16_G16_B16_A16_Float, _resolution));
        state.SplitColumns = builder.Temporary("SplitColumns", view.Texture(PixelFormat.R16_G16_B16_A16_Float, _resolution));
        if (_directionalOcclusion)
            state.Occlusion = builder.Temporary("Occlusion", view.Texture(PixelFormat.R16_G16_B16_A16_Float, _resolution));
        GraphTextureDesc history = view.Texture(PixelFormat.R16_G16_B16_A16_Float, _resolution) with { ColorFormats = [PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R32_G32_B32_A32_Float] };
        state.History = builder.History("History", history, usage: TextureState.Attachment | TextureState.Sampled);
        state.Filtered = builder.Temporary("Filtered", view.Texture(PixelFormat.R16_G16_B16_A16_Float, _resolution));
        if (_showReference)
            state.Reference = builder.History("Reference", view.Texture(PixelFormat.R32_G32_B32_A32_Float, ScaleSnapshot), usage: TextureState.Attachment | TextureState.Sampled);
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        RenderTexture irradiance = context.Get(state.Irradiance);
        if (state.Inputs is not { Voxels.Data: { } data } inputs)
        {
            state.Skipped = true;
            Present(context, state, irradiance, null);
            return;
        }

        int frame = state.Frame++;
        WorldVoxels voxels = inputs.Voxels;
        Float3 extent = voxels.Extent;
        Double3 min = voxels.Center - new Double3(extent.X, extent.Y, extent.Z) * 0.5 - context.View.Position;
        _properties.SetFloat2(s_screenSize, new Float2(context.View.Width, context.View.Height));
        Float2 jitter = new(Halton.Of(frame % 4 + 1, 2), Halton.Of(frame % 4 + 1, 3));
        _properties.SetFloat2(s_probeJitter, jitter);
        _properties.SetFloat2(s_previousProbeJitter, state.PreviousJitter);
        _properties.SetInt(s_frame, frame);
        _properties.SetFloat4(s_boxMin, new Float4((float)min.X, (float)min.Y, (float)min.Z, voxels.Header.VoxelSize));
        _properties.SetFloat3(s_boxSize, extent);
        _properties.SetTexture(s_depth, context.Depth!, _shared.PointClamp);
        _properties.SetTexture(s_normals, context.Normals!, _shared.PointClamp);
        _properties.SetTexture(s_motion, context.Motion!, _shared.PointClamp);
        _properties.SetBuffer(s_voxels, data);
        _properties.SetBuffer(s_cache, inputs.Cache);
        _properties.SetBuffer(s_lighting, voxels.Lighting.Buffer);
        _properties.SetTexture(s_sunShadow, inputs.SunShadow, inputs.ShadowSampler);
        _properties.SetTexture(s_sky, inputs.Sky, _shared.LinearClamp);
        Texture? copy = context.SceneColorCopy;
        bool hasPrevious = copy != null && copy == state.Copied && context.HasPreviousFrame;
        state.Copied = copy;
        _properties.SetTexture(s_previousColor, copy ?? _shared.NoSceneColor, _shared.LinearClamp);
        _properties.SetFloat(s_hasPreviousColor, hasPrevious ? 1 : 0);

        if (_showReference)
        {
            state.Skipped = true;
            RenderReference(context, state, irradiance);
            return;
        }
        bool resumed = state.Skipped;
        state.Skipped = false;

        RenderTexture probes = context.Get(state.Probes), atlas = context.Get(state.Atlas), reservoirs = context.Get(state.Reservoirs);
        context.Fullscreen(_probes, probes, _properties);
        _properties.SetTexture(s_probes, probes.ColorTextures[0], _shared.PointClamp);
        _properties.SetTexture(s_probePositions, probes.ColorTextures[1], _shared.PointClamp);
        _properties.SetTexture(s_hiZ, context.Get(state.HiZ).ColorTextures[0], _shared.PointClamp);
        context.Fullscreen(_trace, atlas, _properties);
        _properties.SetTexture(s_atlas, atlas.ColorTextures[0], _shared.PointClamp);
        _properties.SetTexture(s_atlasHit, atlas.ColorTextures[1], _shared.PointClamp);
        if (_directionalOcclusion)
        {
            RenderTexture occlusion = context.Get(state.Occlusion);
            _properties.SetFloat(s_occlusionRadius, _occlusionRadius);
            _properties.SetFloat(s_occlusionThickness, _occlusionThickness);
            context.Fullscreen(_occlusion, occlusion, _properties);
            _properties.SetTexture(s_occlusion, occlusion.ColorTextures[0], _shared.PointClamp);
        }
        else
            _properties.SetTexture(s_occlusion, _shared.NoSceneColor, _shared.PointClamp);
        _properties.SetFloat(s_hasOcclusion, _directionalOcclusion ? 1 : 0);
        RenderTexture probeAO = context.Get(state.ProbeAO);
        context.Fullscreen(_probeAO, probeAO, _properties);
        _properties.SetTexture(s_probeAO, probeAO.ColorTextures[0], _shared.PointClamp);

        // Last frame's probes and reservoirs, which hold only their temporal result
        RenderTexture? previousProbes = context.History(state.Probes), previousReservoirs = context.History(state.Reservoirs);
        bool probeHistory = !resumed && previousProbes != null && previousReservoirs != null && context.HasPreviousFrame;
        _properties.SetTexture(s_previousProbes, probeHistory ? previousProbes!.ColorTextures[0] : _shared.NoSceneColor, _shared.PointClamp);
        _properties.SetTexture(s_previousProbePositions, probeHistory ? previousProbes!.ColorTextures[1] : _shared.NoSceneColor, _shared.PointClamp);
        _properties.SetTexture(s_previousReservoir, probeHistory ? previousReservoirs!.ColorTextures[0] : _shared.NoSceneColor, _shared.PointClamp);
        _properties.SetTexture(s_previousReservoirData, probeHistory ? previousReservoirs!.ColorTextures[1] : _shared.NoSceneColor, _shared.PointClamp);
        _properties.SetTexture(s_previousReservoirHit, probeHistory ? previousReservoirs!.ColorTextures[2] : _shared.NoSceneColor, _shared.PointClamp);
        _properties.SetFloat(s_probeHistoryValid, probeHistory ? 1 : 0);
        context.Fullscreen(_temporalReuse, reservoirs, _properties);

        RenderTexture source = reservoirs;
        for (int pass = 0; pass < _spatialPasses; pass++)
        {
            RenderTexture target = context.Get(pass % 2 == 0 ? state.SpatialA : state.SpatialB);
            _properties.SetTexture(s_reservoir, source.ColorTextures[0], _shared.PointClamp);
            _properties.SetTexture(s_reservoirData, source.ColorTextures[1], _shared.PointClamp);
            _properties.SetTexture(s_reservoirHit, source.ColorTextures[2], _shared.PointClamp);
            _properties.SetInt(s_pass, pass);
            _properties.SetInt(s_passes, _spatialPasses);
            context.Fullscreen(_spatialReuse, target, _properties);
            source = target;
        }

        RenderTexture sh = context.Get(state.SH), gather = context.Get(state.Gather);
        _properties.SetTexture(s_reservoir, source.ColorTextures[0], _shared.PointClamp);
        _properties.SetTexture(s_reservoirData, source.ColorTextures[1], _shared.PointClamp);
        context.Fullscreen(_sh, sh, _properties);
        _properties.SetTexture(s_sh, sh.ColorTextures[0], _shared.PointClamp);

        RenderTexture history = context.Get(state.History);
        RenderTexture? previous = context.History(state.History);
        bool valid = !resumed && previous != null && context.HasPreviousFrame;
        _properties.SetTexture(s_history, valid ? previous!.ColorTextures[0] : _shared.NoSceneColor, _shared.LinearClamp);
        _properties.SetFloat(s_historyValid, valid ? 1 : 0);
        context.Fullscreen(_directionalOcclusion ? _gatherOccluded : _gather, gather, _properties);

        // The gather blurred along rows then columns, guided by last frame's history, before the history takes it
        RenderTexture rows = context.Get(state.SplitRows), columns = context.Get(state.SplitColumns);
        _properties.SetTexture(s_current, gather.ColorTextures[0], _shared.PointClamp);
        _properties.SetTexture(s_surface, gather.ColorTextures[1], _shared.PointClamp);
        _properties.SetTexture(s_surfaceNormals, gather.ColorTextures[2], _shared.PointClamp);
        context.Fullscreen(_splitRows, rows, _properties);
        _properties.SetTexture(s_current, rows.ColorTextures[0], _shared.PointClamp);
        _properties.SetTexture(s_gather, gather.ColorTextures[0], _shared.PointClamp);
        context.Fullscreen(_splitColumns, columns, _properties);

        _properties.SetTexture(s_current, columns.ColorTextures[0], _shared.PointClamp);
        _properties.SetTexture(s_historyMeta, valid ? previous!.ColorTextures[1] : _shared.NoSceneColor, _shared.PointClamp);
        _properties.SetFloat(s_historyValid, valid ? 1 : 0);
        _properties.SetFloat(s_maxHistory, _maxHistory);
        context.Fullscreen(_temporal, history, _properties);

        RenderTexture filtered = context.Get(state.Filtered);
        _properties.SetTexture(s_history, history.ColorTextures[0], _shared.PointClamp);
        _properties.SetTexture(s_historyMeta, history.ColorTextures[1], _shared.PointClamp);
        context.Fullscreen(_filter, filtered, _properties);

        Present(context, state, irradiance, filtered, history.ColorTextures[1]);
        state.PreviousJitter = jitter;
    }

    // The history as the view's screen irradiance, and its sky visibility as the view's screen visibility, or no
    // irradiance and full visibility without one
    private void Present(FeatureContext context, ViewState state, RenderTexture irradiance, RenderTexture? history, Texture? meta = null)
    {
        _properties.SetTexture(s_history, history?.ColorTextures[0] ?? _shared.NoSceneColor, _shared.LinearClamp);
        _properties.SetTexture(s_historyMeta, meta ?? _shared.NoSceneColor, _shared.PointClamp);
        _properties.SetTexture(s_depth, context.Depth!, _shared.PointClamp);
        _properties.SetTexture(s_normals, context.Normals!, _shared.PointClamp);
        _properties.SetFloat2(s_screenSize, new Float2(context.View.Width, context.View.Height));
        _properties.SetFloat(s_hasVoxels, history != null ? 1 : 0);
        context.Fullscreen(_output, irradiance, _properties);
        context.Fullscreen(_visibility, context.Get(state.Visibility), _properties);
    }

    // The path traced truth in place of the GI, starting over whenever the camera moves
    private void RenderReference(FeatureContext context, ViewState state, RenderTexture irradiance)
    {
        RenderTexture target = context.Get(state.Reference);
        RenderTexture? previous = context.History(state.Reference);
        Float4x4 view = context.Blocks[0].View;
        bool still = previous != null && context.HasPreviousFrame && view == state.ReferenceView && context.View.Position == state.ReferenceCamera;
        state.ReferenceFrames = still ? state.ReferenceFrames + 1 : 0;
        state.ReferenceView = view;
        state.ReferenceCamera = context.View.Position;
        _properties.SetTexture(s_reference, still ? previous!.ColorTextures[0] : _shared.NoSceneColor, _shared.PointClamp);
        _properties.SetFloat(s_referenceFrames, still ? state.ReferenceFrames : 0);
        context.Fullscreen(_reference, target, _properties);
        Present(context, state, irradiance, target);
    }
}
