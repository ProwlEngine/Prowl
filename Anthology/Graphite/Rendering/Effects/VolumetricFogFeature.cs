using System;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The world's volumetric fog for a view, built in and on while the world's <see cref="EnvironmentDesc.VolumetricFog"/>
/// is: after the shadow cascades and before the opaques, a compute pass lights every cell of a grid fitted to the view, a
/// cell per 8 by 8 pixels and the fog's depth slices, at a point jittered through the cell each frame, a second blends it
/// over last frame's where the cell was then, and a third accumulates it toward the camera. Every shader's Graphite_ApplyFog then reads the grid at its
/// point, opaques, transparents and water alike.
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

    private readonly RendererResources _resources;
    private readonly PropertySet _properties = new();

    internal VolumetricFogFeature(RendererResources resources) : base(RenderSlot.AfterPrepass, 0, FeatureGroup.VolumetricFog, ViewKinds.Cameras)
        => _resources = resources;

    // Under the sea the ocean's underwater pass fogs the view instead
    public override bool IsActive(in FeatureView view)
        => view.World is { } world && world.Lighting.Environment.VolumetricFog.Enabled && world.Underwater?.Below(view.Position) != true;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        State state = builder.GetViewState<State>();
        state.Cells = (view.Width + 7) / 8;
        state.Rows = (view.Height + 7) / 8;
        state.Slices = (uint)Math.Clamp(view.World!.Lighting.Environment.VolumetricFog.Slices, 16, 256);
        (state.Light, state.History, state.Volume, state.Fresh) = builder.WriteVolumetricFog(state.Cells, state.Rows, state.Slices * (uint)Math.Max(view.LayerCount, 1));
    }

    public override void Render(FeatureContext context)
    {
        State state = context.GetViewState<State>();
        VolumetricFogDesc fog = context.View.World!.Lighting.Environment.VolumetricFog;
        Texture light = context.Get(state.Light).ColorTextures[0], history = context.Get(state.History).ColorTextures[0], volume = context.Get(state.Volume).ColorTextures[0];
        CommandBuffer cmd = context.Command;
        int frame = state.Frame++;
        Float4 jitter = new(Halton(frame, 2), Halton(frame, 3), Halton(frame, 5), 0f);
        float keep = state.Fresh ? 0f : Math.Clamp(fog.TemporalBlend, 0f, 0.98f);
        uint groupsX = (state.Cells + 3) / 4, groupsY = (state.Rows + 3) / 4, groupsZ = (state.Slices + 3) / 4;

        for (int layer = 0; layer < context.View.LayerCount; layer++)
        {
            float first = layer * state.Slices;
            cmd.SetComputeShader(_resources.FogLightProgram.Program);
            context.BindLighting(layer);
            _properties.Clear();
            _properties.SetTexture("Fog_Raw", volume);
            _properties.SetFloat4("Fog_Grid", new Float4(state.Cells, state.Rows, first, 0f));
            _properties.SetFloat4("Fog_Jitter", jitter);
            cmd.SetProperties(_properties);
            cmd.Dispatch(groupsX, groupsY, groupsZ);
            cmd.ComputeBarrier();

            cmd.SetComputeShader(_resources.FogBlendProgram.Program);
            context.BindLighting(layer);
            _properties.Clear();
            _properties.SetTexture("Fog_Raw", volume);
            _properties.SetTexture("Fog_Light", light);
            _properties.SetTexture("Fog_History", history, _resources.LinearClamp);
            _properties.SetFloat4("Fog_Grid", new Float4(state.Cells, state.Rows, first, keep));
            cmd.SetProperties(_properties);
            cmd.Dispatch(groupsX, groupsY, groupsZ);
            cmd.ComputeBarrier();

            cmd.SetComputeShader(_resources.FogIntegrateProgram.Program);
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
/// The volumetric fog over the sky, which no shader fogs as it draws: after the atmosphere, so in front of the clouds, every
/// pixel with nothing drawn takes the fog grid's whole column.
/// </summary>
public sealed class VolumetricFogSkyFeature : RenderFeature
{
    private readonly RendererResources _resources;
    private readonly PropertySet _properties = new();

    internal VolumetricFogSkyFeature(RendererResources resources) : base(RenderSlot.Atmosphere, 1000, FeatureGroup.VolumetricFogSky, ViewKinds.Cameras)
        => _resources = resources;

    // Under the sea the ocean's underwater pass fogs the view instead
    public override bool IsActive(in FeatureView view)
        => view.World is { } world && world.Lighting.Environment.VolumetricFog.Enabled && world.Underwater?.Below(view.Position) != true;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        builder.ReadScene();
        builder.WriteColor();
    }

    public override void Render(FeatureContext context)
    {
        _properties.SetTexture("_FogSceneDepth", context.Depth!, _resources.PointClamp);
        context.Fullscreen(_resources.FogSkyProgram, _properties);
    }
}
