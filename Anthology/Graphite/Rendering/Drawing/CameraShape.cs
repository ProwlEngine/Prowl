using System;
using System.Collections.Generic;

namespace Prowl.Graphite.Rendering;

internal readonly record struct CameraShape(int Layers, bool Ldr, SceneTargets Targets, int UpscaleScale)
{
    public bool Upscales => UpscaleScale > 0;

    public float OutputScale => Upscales ? 256f / UpscaleScale : 1f;

    public static CameraShape Of(in ViewDesc view, IReadOnlyList<RenderFeature> selected)
    {
        SceneTargets targets = SceneTargets.None;
        bool upscales = false;
        foreach (RenderFeature feature in selected)
        {
            targets |= feature.SceneReadsSnapshot;
            upscales |= feature.SlotSnapshot == RenderSlot.Upscale;
        }
        int scale = upscales ? Math.Max(1, (int)MathF.Round(view.RenderScale * 256f)) : 0;
        return new CameraShape(Math.Max(view.LayerCount, 1), view.Ldr, targets, scale);
    }

    public override string ToString()
        => $"L{Layers} {(Ldr ? "Ldr" : "Hdr")} T{(int)Targets}{(Upscales ? $" U{UpscaleScale}" : "")}";
}
