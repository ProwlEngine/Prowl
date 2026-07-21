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
    public float ShadowBias = 0.001f;
    public float ShadowNormalBias = 0.0f;
    public bool CastShadows = true;
    public ShadowQuality ShadowQuality = ShadowQuality.Soft;

    /// <summary>How this light is baked. <see cref="LightBakeMode.Baked"/> lights are excluded from
    /// the realtime light set (they live entirely in the lightmap/probes); Mixed and Realtime
    /// lights light in realtime as usual.</summary>
    public LightBakeMode BakeMode = LightBakeMode.Realtime;

    /// <summary>
    /// Slot index into the per-light shadow arrays (point: <c>_PointShadowMatrices</c>,
    /// spot: <c>_SpotShadowMatrices</c>). -1 if no shadow data was uploaded for this light
    /// this frame. Directional lights store shadow data in the cascade arrays instead;
    /// for them this remains -1 even when shadows are active.
    /// </summary>
    public int ShadowSlot { get; internal set; } = -1;


    public override void OnRenderCollect(SceneCuller culler)
    {
        culler.Add(this);
    }

    public virtual int GetLayer() => GameObject.LayerIndex;
    public virtual int GetLightID() => InstanceID;
    public abstract LightType GetLightType();
    public virtual Float3 GetLightPosition() => Transform.Position;
    public virtual Float3 GetLightDirection() => Transform.Forward;
    public virtual bool DoCastShadows() => CastShadows;

    public abstract ForwardLightData GetForwardLightData();
}
