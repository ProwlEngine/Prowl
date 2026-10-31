using System;
using System.Collections.Generic;

namespace Prowl.Graphite.Rendering;

internal readonly record struct CameraShape(int Layers, bool Ldr, SceneTargets Targets, int UpscaleScale, int ScreenScale, bool ScreenIrradiance, int HostBuffers, int Cubes)
{
    public bool Upscales => UpscaleScale > 0;

    public bool HasScreenLighting => ScreenScale > 0;

    public float OutputScale => Upscales ? 256f / UpscaleScale : 1f;

    public float ScreenFraction => ScreenScale / 256f;

    public static CameraShape Of(in ViewDesc view, IReadOnlyList<RenderFeature> selected, int hostBuffers, int cubes)
    {
        SceneTargets targets = SceneTargets.None;
        bool upscales = false;
        int screen = 0;
        bool irradiance = false;
        foreach (RenderFeature feature in selected)
        {
            targets |= feature.SceneReads;
            upscales |= feature.Slot == RenderSlot.Upscale;
            if (feature is ScreenLightingFeature producer)
            {
                screen = Math.Max(1, (int)MathF.Round(producer.ScaleSnapshot * 256f));
                irradiance = producer.IrradianceSnapshot;
            }
        }
        int scale = upscales ? Math.Max(1, (int)MathF.Round(view.RenderScale * 256f)) : 0;
        return new CameraShape(Math.Max((int)view.LayerCount, 1), view.Ldr, targets, scale, screen, irradiance, hostBuffers, cubes);
    }
}
