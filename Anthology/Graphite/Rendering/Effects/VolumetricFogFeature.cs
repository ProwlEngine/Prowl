using System;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The world's volumetric fog for a view, built in and on while the world's <see cref="EnvironmentDesc.VolumetricFog"/>
/// is: after the shadow cascades and before the opaques, a compute pass lights every cell of a grid fitted to the view, a
/// cell per 8 by 8 pixels and the fog's depth slices, at a point jittered through the cell each frame, a second blends it
/// over last frame's where the cell was then, and a third accumulates it toward the camera. <see cref="VolumetricFogSkyFeature"/>
/// then lays it over the drawn scene, so no surface's shader reads the grid.
/// </summary>
public sealed class VolumetricFogFeature : RenderFeature
{
    private sealed class State
    {
        public TextureHandle Light, History, Volume;
        public bool Fresh;
        public uint Cells, Rows, Slices;
        public int Frame;
    }

    private readonly LazyCompute _light, _blend, _integrate;
    private readonly Sampler _linearClamp;
    private readonly PropertySet _properties = new();

    internal VolumetricFogFeature(Renderer renderer) : base(RenderSlot.AfterPrepass, 0, FeatureGroup.VolumetricFog, ViewKinds.Cameras)
    {
        _light = renderer.SharedPrograms.Compute("Internal/VolumetricFogLight.glsl", lit: true);
        _blend = renderer.SharedPrograms.Compute("Internal/VolumetricFogBlend.glsl", lit: true);
        _integrate = renderer.SharedPrograms.Compute("Internal/VolumetricFogIntegrate.glsl", lit: true);
        _linearClamp = renderer.Shared.LinearClamp;
    }

    public override bool IsActive(in FeatureView view) => view.World != null && view.Environment.VolumetricFog.Enabled;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        State state = builder.GetViewState<State>();
        state.Cells = (view.Width + 7) / 8;
        state.Rows = (view.Height + 7) / 8;
        state.Slices = (uint)Math.Clamp(view.Environment.VolumetricFog.Slices, 16, 256);
        (state.Light, state.History, state.Volume, state.Fresh) = builder.WriteVolumetricFog(state.Cells, state.Rows, state.Slices * (uint)Math.Max(view.LayerCount, 1));
    }

    public override void Render(FeatureContext context)
    {
        State state = context.GetViewState<State>();
        VolumetricFogDesc fog = context.View.Environment.VolumetricFog;
        Texture light = context.Get(state.Light).ColorTextures[0], history = context.Get(state.History).ColorTextures[0], volume = context.Get(state.Volume).ColorTextures[0];
        CommandBuffer cmd = context.Command;
        int frame = state.Frame++;
        Float4 jitter = new(Halton(frame, 2), Halton(frame, 3), Halton(frame, 5), 0f);
        float keep = state.Fresh ? 0f : Math.Clamp(fog.TemporalBlend, 0f, 0.98f);
        uint groupsX = (state.Cells + 3) / 4, groupsY = (state.Rows + 3) / 4, groupsZ = (state.Slices + 3) / 4;

        for (int layer = 0; layer < context.View.LayerCount; layer++)
        {
            float first = layer * state.Slices;
            cmd.SetComputeShader(_light.Program);
            context.BindLighting(layer);
            _properties.Clear();
            _properties.SetTexture("Fog_Raw", volume);
            _properties.SetFloat4("Fog_Grid", new Float4(state.Cells, state.Rows, first, 0f));
            _properties.SetFloat4("Fog_Jitter", jitter);
            cmd.SetProperties(_properties);
            cmd.Dispatch(groupsX, groupsY, groupsZ);
            cmd.ComputeBarrier();

            cmd.SetComputeShader(_blend.Program);
            context.BindLighting(layer);
            _properties.Clear();
            _properties.SetTexture("Fog_Raw", volume);
            _properties.SetTexture("Fog_Light", light);
            _properties.SetTexture("Fog_History", history, _linearClamp);
            _properties.SetFloat4("Fog_Grid", new Float4(state.Cells, state.Rows, first, keep));
            cmd.SetProperties(_properties);
            cmd.Dispatch(groupsX, groupsY, groupsZ);
            cmd.ComputeBarrier();

            cmd.SetComputeShader(_integrate.Program);
            context.BindLighting(layer);
            _properties.Clear();
            _properties.SetTexture("Fog_Light", light);
            _properties.SetTexture("Fog_Volume", volume);
            _properties.SetFloat4("Fog_Grid", new Float4(state.Cells, state.Rows, first, 0f));
            cmd.SetProperties(_properties);
            cmd.Dispatch((state.Cells + 7) / 8, (state.Rows + 7) / 8, 1);
            cmd.ComputeBarrier();
        }
    }

    // The Halton sequence's value at an index, its base a prime, skipping the first which is 0
    private static float Halton(int index, int radix)
    {
        float result = 0f, fraction = 1f;
        for (int i = index % 64 + 1; i > 0; i /= radix)
        {
            fraction /= radix;
            result += fraction * (i % radix);
        }
        return result;
    }
}

/// <summary>
/// The volumetric fog over the drawn scene, which no shader fogs as it draws: after the transparent surfaces, every
/// pixel takes the fog between the camera and the opaque surface it shows, one with none the fog grid's whole column.
/// A transparent surface is fogged as what lies behind it.
/// </summary>
public sealed class VolumetricFogSkyFeature : RenderFeature
{
    private readonly FullscreenProgram _program;
    private readonly Sampler _pointClamp;
    private readonly PropertySet _properties = new();

    internal VolumetricFogSkyFeature(Renderer renderer) : base(RenderSlot.AfterTransparents, -1000, FeatureGroup.VolumetricFogSky, ViewKinds.Cameras)
    {
        _program = renderer.SharedPrograms.Fullscreen(this, "Graphite/Internal/VolumetricFogSky.glsl", lit: true);
        _pointClamp = renderer.Shared.PointClamp;
    }

    public override bool IsActive(in FeatureView view) => view.World != null && view.Environment.VolumetricFog.Enabled;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        builder.ReadScene();
        builder.WriteColor();
    }

    public override void Render(FeatureContext context)
    {
        _properties.SetTexture("_FogSceneDepth", context.Depth!, _pointClamp);
        context.Fullscreen(_program, _properties);
    }
}
