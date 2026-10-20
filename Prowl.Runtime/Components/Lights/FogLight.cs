// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Tunes how a Light scatters in volumetric fog. Lights without one scatter at their own colour and intensity.
/// </summary>
[AddComponentMenu("Rendering/Fog Light")]
[RequireComponent(typeof(Light))]
[ComponentIcon("\uf6c3")] // CloudMoon
public sealed class FogLight : Component
{
    // The light on the same object reads these settings, so it syncs again
    private void Wake()
    {
        if (GameObject.IsValid()) GameObject.WakeRendering();
    }

    /// <summary>How much this light contributes to volumetric scattering.</summary>
    public float IntensityMultiplier { get => intensityMultiplier; set { intensityMultiplier = value; Wake(); } }
    [SerializeField] private float intensityMultiplier = 1.0f;

    /// <summary>If true, the light's shadow map is sampled in the volumetric march so geometry
    /// occludes scattering correctly. Costs extra texture lookups per ray-march step.</summary>
    public bool CastFogShadows { get => castFogShadows; set { castFogShadows = value; Wake(); } }
    [SerializeField] private bool castFogShadows = true;

    /// <summary>If true, <see cref="OverrideColor"/> replaces the light's color for fog scattering only
    /// (the light's normal direct lighting is unaffected).</summary>
    public bool UseOverrideColor { get => useOverrideColor; set { useOverrideColor = value; Wake(); } }
    [SerializeField] private bool useOverrideColor = false;

    /// <summary>Color override for fog scattering. Only used when <see cref="UseOverrideColor"/> is true.</summary>
    public Color OverrideColor { get => overrideColor; set { overrideColor = value; Wake(); } }
    [SerializeField] private Color overrideColor = new(1f, 1f, 1f, 1f);

    /// <summary>
    /// Per-light Henyey-Greenstein anisotropy override added to the effect's global Scattering value.
    /// 0 = use global, &gt;0 = more forward-scatter (sharper god rays toward this light),
    /// &lt;0 = more back-scatter.
    /// </summary>
    public float ScatteringBias { get => scatteringBias; set { scatteringBias = value; Wake(); } }
    [SerializeField] private float scatteringBias = 0.0f;

    public override void OnAddedToScene()
    {
        Light? light = GetComponent<Light>();
        if (light.IsValid()) light.Fog = this;
    }
}
