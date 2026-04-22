// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Add to a GameObject with a Light to make it contribute to volumetric fog.
/// Without this component, the light renders normally but does not produce visible scattering
/// in the volumetric fog effect.
/// </summary>
[AddComponentMenu("Rendering/Fog Light")]
[RequireComponent(typeof(Light))]
[ComponentIcon("\uf6c3")] // CloudMoon
public sealed class FogLight : MonoBehaviour
{
    /// <summary>How much this light contributes to volumetric scattering.</summary>
    [SerializeField] public float IntensityMultiplier = 1.0f;

    /// <summary>If true, the light's shadow map is sampled in the volumetric march so geometry
    /// occludes scattering correctly. Costs extra texture lookups per ray-march step.</summary>
    [SerializeField] public bool CastFogShadows = true;

    /// <summary>If true, <see cref="OverrideColor"/> replaces the light's color for fog scattering only
    /// (the light's normal direct lighting is unaffected).</summary>
    [SerializeField] public bool UseOverrideColor = false;

    /// <summary>Color override for fog scattering. Only used when <see cref="UseOverrideColor"/> is true.</summary>
    [SerializeField] public Color OverrideColor = new(1f, 1f, 1f, 1f);

    /// <summary>
    /// Per-light Henyey-Greenstein anisotropy override added to the effect's global Scattering value.
    /// 0 = use global, &gt;0 = more forward-scatter (sharper god rays toward this light),
    /// &lt;0 = more back-scatter.
    /// </summary>
    [SerializeField] public float ScatteringBias = 0.0f;
}
