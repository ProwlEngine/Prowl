// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

/// <summary>A point or spot light, which shines from a small sphere and can shine through a cookie or a photometric profile.</summary>
public abstract class PunctualLight : LocalLight
{
    /// <summary>The radius of the light's sphere, which softens highlights and contact hardening shadows.</summary>
    public float SourceRadius { get => sourceRadius; set { sourceRadius = value; Wake(); } }
    [SerializeField] private float sourceRadius = 0f;

    /// <summary>A texture the light shines through: a 2D texture across a spot's cone, a cubemap around a point.</summary>
    public Texture? Cookie { get => cookie; set { cookie = value; Wake(); } }
    [SerializeField] private Texture? cookie;

    /// <summary>A photometric profile as a 2D texture: the angle from the axis across U, around it across V, intensity in R.</summary>
    public Texture2D? IesProfile { get => iesProfile; set { iesProfile = value; Wake(); } }
    [SerializeField] private Texture2D? iesProfile;

    public override IReadOnlyList<LightUnit> Units => s_units;
    private static readonly LightUnit[] s_units = [LightUnit.Lumen, LightUnit.Candela];

    public override bool CanCastShadows => true;

    public override float AxisCandela => Intensity;

    private protected override bool IsReady => Ready(Cookie) && Ready(IesProfile);

    // A texture the light was given is in the world, none or loaded
    private static bool Ready(Texture? texture) => texture.IsNotValid() || texture.IsLoaded;

    protected override void DescribeShape(ref LightDesc desc)
    {
        base.DescribeShape(ref desc);
        desc.SourceRadius = SourceRadius;

        Texture? cookie = Cookie;
        if (cookie.IsValid() && cookie.IsLoaded)
        {
            desc.Cookie = cookie.GpuIfLoaded;
            desc.CookieSampler = cookie.SamplerUnchecked;
        }
        Texture2D? ies = IesProfile;
        if (ies.IsValid() && ies.IsLoaded)
        {
            desc.IesProfile = ies.GpuIfLoaded;
            if (desc.CookieSampler == null) desc.CookieSampler = ies.SamplerUnchecked;
        }
    }
}
