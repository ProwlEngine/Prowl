// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

public enum ShadowQuality
{
    Hard = 0,
    Soft = 1
}

[ComponentIcon("\uf185")] // Sun
public abstract class Light : MonoBehaviour, IRenderableLight
{

    public Color Color = Color.White;
    public float Intensity = 8.0f;
    public float ShadowStrength = 1.0f;
    public float ShadowBias = 0.001f;
    public float ShadowNormalBias = 0.0f;
    public bool CastShadows = true;
    public ShadowQuality ShadowQuality = ShadowQuality.Soft;

    /// <summary>
    /// Index this light occupies in the forward shader light arrays this frame
    /// (e.g. <c>_LightPositions[ForwardSlot]</c>). -1 if the light wasn't selected
    /// for upload (too far / too many lights). Set by <see cref="Rendering.ForwardLightManager"/>.
    /// </summary>
    public int ForwardSlot { get; internal set; } = -1;

    /// <summary>
    /// Slot index into the per-light shadow arrays (point: <c>_PointShadowMatrices</c>,
    /// spot: <c>_SpotShadowMatrices</c>). -1 if no shadow data was uploaded for this light
    /// this frame. Directional lights store shadow data in the cascade arrays instead;
    /// for them this remains -1 even when shadows are active.
    /// </summary>
    public int ShadowSlot { get; internal set; } = -1;


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

    /// <summary>
    /// Renders this light's shadow map into the shadow atlas.
    /// Called by the render pipeline during shadow pass.
    /// </summary>
    /// <param name="pipeline">The current render pipeline</param>
    /// <param name="cameraPosition">Position of the camera in world space</param>
    /// <param name="renderables">List of all renderables that could cast shadows</param>
    public abstract void RenderShadows(RenderPipeline pipeline, Float3 cameraPosition, System.Collections.Generic.IReadOnlyList<IRenderable> renderables);

    public abstract ForwardLightData GetForwardLightData();
}
