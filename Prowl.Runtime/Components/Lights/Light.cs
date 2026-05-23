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

    /// <summary>
    /// Slot index into the per-light shadow arrays (point: <c>_PointShadowMatrices</c>,
    /// spot: <c>_SpotShadowMatrices</c>). -1 if no shadow data was uploaded for this light
    /// this frame. Directional lights store shadow data in the cascade arrays instead;
    /// for them this remains -1 even when shadows are active.
    /// Owned and populated by <see cref="Rendering.SceneLightSystem"/> during reconcile.
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
    /// <summary>Render this light's shadow map(s) into the shared shadow atlas.
    ///
    /// <para>
    /// Implementations rent and submit their own <see cref="CommandBuffer"/> per face
    /// (point lights), cascade (directional), or single tile (spot). They CANNOT share
    /// a CB across multiple faces because each face calls <see cref="RenderPipeline.AssignCameraMatrices"/>
    /// which uploads view/proj into the single GlobalUniforms UBO sharing a CB would
    /// queue all the face draws to execute against whatever matrices the LAST face
    /// uploaded.
    /// </para>
    ///
    /// <para>
    /// The shadow atlas itself has already been bound + cleared by the caller in a
    /// separate setup CB before this method runs.
    /// </para>
    /// </summary>
    public abstract void RenderShadows(RenderPipeline pipeline, Float3 cameraPosition, System.Collections.Generic.IReadOnlyList<IRenderable> renderables);

    public abstract ForwardLightData GetForwardLightData();
}
