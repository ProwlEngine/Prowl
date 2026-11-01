// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GLightType = Prowl.Graphite.Rendering.LightType;

namespace Prowl.Runtime;

/// <summary>Parallel light from infinitely far away, like the sun. Only its rotation matters. Its intensity is in lux.</summary>
[AddComponentMenu("Rendering/Lights/Directional Light")]
[ComponentIcon("")] // Sun
public sealed class DirectionalLight : Light
{
    /// <summary>The angular radius of the light's disk in degrees, the sun's about 0.27, which softens contact hardening shadows.</summary>
    public float AngularRadius { get => angularRadius; set { angularRadius = value; Wake(); } }
    [SerializeField] private float angularRadius = 0.27f;

    /// <summary>The fraction of each cascade blended into the next, 0 for a hard switch. The cascades and how far they reach are the quality level's.</summary>
    public float CascadeBlend { get => cascadeBlend; set { cascadeBlend = value; Wake(); } }
    [SerializeField, Range(0f, 1f)] private float cascadeBlend = 0.1f;

    [System.NonSerialized, NotHeld] private Scene? _sunScene;

    private static readonly LightPreset[] s_presets =
    [
        new("Midday sun", LightUnit.Lux, 100000f, 5600f),
        new("Low sun", LightUnit.Lux, 20000f, 3500f),
        new("Sunset", LightUnit.Lux, 2000f, 2500f),
        new("Full moon", LightUnit.Lux, 0.25f, 4100f),
    ];
    private static readonly LightUnit[] s_units = [LightUnit.Lux, LightUnit.Ev100];

    public override IReadOnlyList<LightPreset> Presets => s_presets;

    public override IReadOnlyList<LightUnit> Units => s_units;

    public override bool CanCastShadows => true;

    protected override void DescribeShape(ref LightDesc desc)
    {
        desc.Type = GLightType.Directional;
        desc.AngularRadius = AngularRadius;
        desc.Shadow.Cascades = (byte)Maths.Clamp((int)QualitySettings.Active.ShadowCascades, 1, 4);
        desc.Shadow.Distance = Maths.Max(QualitySettings.Active.ShadowDistance, 0.01f);
        desc.Shadow.CascadeBlend = CascadeBlend;
    }

    private protected override void OnSynced()
    {
        if (_sunScene.IsNotValid() && GameObject.Scene.IsValid())
        {
            _sunScene = GameObject.Scene;
            _sunScene.Rendering.AddDirectional(this);
        }
    }

    private protected override void OnRemoved()
    {
        if (_sunScene.IsValid() && !_sunScene.IsDisposed) _sunScene.Rendering.RemoveDirectional(this);
        _sunScene = null;
    }

    public override void DrawGizmos()
    {
        base.DrawGizmos();
        Debug.DrawArrow(Transform.Position, Transform.Forward, Color.Yellow);
        Debug.DrawWireCircle(Transform.Position, Transform.Forward, 0.5f, Color.Yellow);
    }
}
