// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>A light at a place in the scene, which fades with distance and stops at its <see cref="Range"/>.</summary>
public abstract class LocalLight : Light
{
    /// <summary>Whether <see cref="Range"/> is set by hand instead of worked out by <see cref="AutomaticRange"/>.</summary>
    public bool OverrideRange { get => overrideRange; set { overrideRange = value; Wake(); } }
    [SerializeField] private bool overrideRange;

    /// <summary>
    /// Distance from the light at which it reaches zero: the one set by hand while <see cref="OverrideRange"/> is on,
    /// else <see cref="AutomaticRange"/>. Setting it turns <see cref="OverrideRange"/> on.
    /// </summary>
    public float Range
    {
        get => overrideRange ? range : AutomaticRange;
        set { range = value; overrideRange = true; Wake(); }
    }
    [SerializeField, ShowIf(nameof(OverrideRange))] private float range = 10.0f;

    /// <summary>The luminous intensity in candela along the light's brightest direction, a far field estimate for area lights.</summary>
    public abstract float AxisCandela { get; }

    /// <summary>Where the light's brightest channel falls below the quality level's <see cref="QualityLevel.LightCutoff"/>.</summary>
    public float AutomaticRange
    {
        get
        {
            Color color = FinalColor;
            float peak = Maths.Max(color.R, Maths.Max(color.G, color.B));
            return Maths.Sqrt(Maths.Max(AxisCandela * peak, 0f) / Maths.Max(QualitySettings.Active.LightCutoff, 1e-4f));
        }
    }

    protected override void DescribeShape(ref LightDesc desc) => desc.Range = Maths.Max(Range, 0.01f);

    public override void DrawGizmosSelected() => Debug.DrawWireSphere(Transform.Position, Range, Color.Yellow);
}
