// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Vector;

using GLightType = Prowl.Graphite.Rendering.LightType;

namespace Prowl.Runtime;

/// <summary>A one sided glowing rectangle facing Forward, like a panel, <see cref="Width"/> along Right and <see cref="Height"/> along Up.</summary>
[AddComponentMenu("Rendering/Lights/Rect Light")]
[ComponentIcon("")] // Lightbulb
public sealed class RectLight : AreaLight
{
    public float Width { get => width; set { width = value; Wake(); } }
    [SerializeField] private float width = 1.0f;

    public float Height { get => height; set { height = value; Wake(); } }
    [SerializeField] private float height = 1.0f;

    private static readonly LightPreset[] s_presets =
    [
        new("Monitor", LightUnit.Nit, 250f, 6500f, Width: 0.6f, Height: 0.34f),
        new("Ceiling panel", LightUnit.Lumen, 3600f, 4000f, Width: 0.6f, Height: 0.6f),
        new("Softbox", LightUnit.Nit, 5000f, 5600f, Width: 1f, Height: 1f),
    ];

    public override IReadOnlyList<LightPreset> Presets => s_presets;

    public override LightPreset DefaultPreset => s_presets[1];

    public override float SurfaceArea => Maths.Max(Width, 0f) * Maths.Max(Height, 0f);

    protected override float ProjectedArea => SurfaceArea;

    protected override void ApplyShape(LightPreset preset)
    {
        if (preset.Width is float presetWidth) width = presetWidth;
        if (preset.Height is float presetHeight) height = presetHeight;
    }

    protected override bool HasShape(LightPreset preset)
        => (preset.Width is not float w || Width == w) && (preset.Height is not float h || Height == h);

    protected override void DescribeShape(ref LightDesc desc)
    {
        base.DescribeShape(ref desc);
        desc.Type = GLightType.Rect;
        desc.Size = new Float2(Maths.Max(Width, 0f), Maths.Max(Height, 0f));
    }

    public override void DrawGizmos()
    {
        base.DrawGizmos();
        Float3 position = Transform.Position, x = Transform.Right * (Width * 0.5f), y = Transform.Up * (Height * 0.5f);
        Debug.DrawLine(position - x - y, position + x - y, Color.Yellow);
        Debug.DrawLine(position + x - y, position + x + y, Color.Yellow);
        Debug.DrawLine(position + x + y, position - x + y, Color.Yellow);
        Debug.DrawLine(position - x + y, position - x - y, Color.Yellow);
        Debug.DrawArrow(position, Transform.Forward * 0.5f, Color.Yellow);
    }
}
