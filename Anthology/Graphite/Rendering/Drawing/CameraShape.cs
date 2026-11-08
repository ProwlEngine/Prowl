using System;
using System.Collections.Generic;

namespace Prowl.Graphite.Rendering;

internal readonly record struct CameraShape(int Layers, bool Ldr, SceneTargets Targets, int UpscaleScale, int ScreenScale, bool ScreenIrradiance, int HostBuffers, int Cubes)
{
    /// <summary>The scene targets features between the prepass and the opaque pass read, which the prepass writes.</summary>
    public SceneTargets PrepassTargets { get; init; }

    /// <summary>The value of <see cref="GlobalAxis.Prepass"/> that writes <see cref="PrepassTargets"/>, null for depth alone.</summary>
    public string? PrepassValue => (PrepassTargets & SceneTargets.Normals) != 0 ? "NORMALS" : (PrepassTargets & SceneTargets.Motion) != 0 ? "MOTION" : null;

    public bool Upscales => UpscaleScale > 0;

    public bool HasScreenLighting => ScreenScale > 0;

    public float OutputScale => Upscales ? 256f / UpscaleScale : 1f;

    public float ScreenFraction => ScreenScale / 256f;

    public static CameraShape Of(in ViewDesc view, IReadOnlyList<RenderFeature> selected, int hostBuffers, int cubes)
    {
        SceneTargets targets = SceneTargets.None;
        SceneTargets prepass = SceneTargets.None;
        bool upscales = false;
        int screen = 0;
        bool irradiance = false;
        foreach (RenderFeature feature in selected)
        {
            targets |= feature.SceneReads;
            if (feature.Slot > RenderSlot.Prepass && feature.Slot < RenderSlot.Opaque)
                prepass |= feature.SceneReads;
            upscales |= feature.Slot == RenderSlot.Upscale;
            if (feature is ScreenLightingFeature producer)
            {
                screen = Math.Max(1, (int)MathF.Round(producer.ScaleSnapshot * 256f));
                irradiance = producer.IrradianceSnapshot;
            }
        }
        int scale = upscales ? Math.Max(1, (int)MathF.Round(view.RenderScale * 256f)) : 0;
        return new CameraShape(Math.Max((int)view.LayerCount, 1), view.Ldr, targets, scale, screen, irradiance, hostBuffers, cubes) { PrepassTargets = prepass };
    }
}
