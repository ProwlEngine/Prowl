// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime;

public enum ShadowQuality
{
    Hard = 0,
    Soft = 1
}

/// <summary>
/// How a light participates in lightmap baking.
/// </summary>
public enum LightBakeMode
{
    /// <summary>Not baked. Lit entirely in realtime; never written into a lightmap or probe.</summary>
    Realtime = 0,
    /// <summary>Direct lighting + shadows are realtime; the light's indirect (bounced) GI is baked
    /// into lightmaps/probes. (Baked-Indirect; the light still uploads as a realtime light.)</summary>
    Mixed = 1,
    /// <summary>Fully baked (direct + indirect + shadows) into lightmaps/probes. Excluded from the
    /// realtime light set, so it only affects lightmapped static geometry (and probes).</summary>
    Baked = 2,
}

[ComponentIcon("\uf185")] // Sun
public abstract class Light : MonoBehaviour, IRenderableLight
{

    public Color Color = Color.White;
    public float Intensity = 1.0f;
    public float ShadowStrength = 1.0f;

    /// <summary>How far receivers are pushed toward the light before the shadow test, in shadow map texels.</summary>
    public float DepthBias = 1.0f;

    /// <summary>How far receivers are pushed out along their surface normal before the shadow test, in shadow map texels.</summary>
    public float NormalBias = 1.0f;

    public bool CastShadows = true;
    public ShadowQuality ShadowQuality = ShadowQuality.Soft;

    /// <summary>How this light is baked. <see cref="LightBakeMode.Baked"/> lights are excluded from
    /// the realtime light set by <see cref="Rendering.SceneLightSystem"/> (they live entirely in the
    /// lightmap/probes); Mixed and Realtime lights light in realtime as usual.</summary>
    public LightBakeMode BakeMode = LightBakeMode.Realtime;

    /// <summary>Hardware depth offset applied while drawing casters, scaled by each polygon's depth slope,
    /// so surfaces at a grazing angle to the light do not shadow themselves.</summary>
    internal const float CasterSlopeBias = 1f;

    /// <summary>Constant part of the caster offset, in steps of the atlas depth format. Two 16 bit steps either way,
    /// a 24 bit step is 256 times finer, and one of those is too small to stop lit surfaces shading themselves.</summary>
    internal static float CasterConstantBias => ShadowAtlas.DepthPrecision == ShadowDepthPrecision.Bits16 ? 2f : 512f;

    /// <summary>The largest tile a shadow map of this light may take. Point and spot lights are sized from how big
    /// they are on screen up to this.</summary>
    internal virtual int MaxShadowResolution => 0;


    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        lights.Add(this);
    }

    public virtual int GetLayer() => GameObject.LayerIndex;
    public virtual int GetLightID() => InstanceID;
    public abstract LightType GetLightType();
    public virtual Float3 GetLightPosition() => Transform.Position;
    public virtual Float3 GetLightDirection() => Transform.Forward;
    public virtual bool DoCastShadows() => CastShadows;

    public abstract ForwardLightData GetForwardLightData();
}
