// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

public enum ShadowQuality
{
    Hard = 0,
    Soft = 1
}

public abstract class Light : MonoBehaviour, IRenderableLight
{

    public Color Color = Color.White;
    public float Intensity = 8.0f;
    public float ShadowStrength = 1.0f;
    public float ShadowBias = 0.001f;
    public float ShadowNormalBias = 0.0f;
    public bool CastShadows = true;
    public ShadowQuality ShadowQuality = ShadowQuality.Hard;


    public override void OnRenderCollect()
    {
        GameObject.Scene.PushLight(this);
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

    public abstract void OnRenderLight(RenderTexture gBuffer, RenderTexture destination, RenderPipeline.CameraSnapshot css);
}
