// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime;

[AddComponentMenu("Light/Directional Light")]
public class DirectionalLight : Light
{
    public enum Resolution : int
    {
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096,
    }

    public enum CascadeCount : int
    {
        One = 1,
        Two = 2,
        Four = 4,
    }

    public Resolution ShadowResolution = Resolution._2048;
    public CascadeCount Cascades = CascadeCount.Two;

    public float ShadowDistance = 70f;

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        lights.Add(this);
    }

    public override void DrawGizmos()
    {
        var icon = Resources.Texture2D.LoadDefault(Resources.DefaultTexture.IconLight);
        if (icon != null) Debug.DrawIcon(icon, Transform.Position, 0.5f, Color.White);

        Debug.DrawArrow(Transform.Position, -Transform.Forward, Color.Yellow);
        Debug.DrawWireCircle(Transform.Position, Transform.Forward, 0.5f, Color.Yellow);

        //// Create and Draw each Frustum
        //foreach (var cascade in _cascadeShadowMatrices)
        //{
        //    Frustum frustum = Frustum.FromMatrix(cascade);
        //    var corners = frustum.GetCorners();
        //
        //    // Corner indices from GetCorners():
        //    // 0: Near-Left-Bottom,  1: Near-Right-Bottom,  2: Near-Left-Top,  3: Near-Right-Top
        //    // 4: Far-Left-Bottom,   5: Far-Right-Bottom,   6: Far-Left-Top,   7: Far-Right-Top
        //
        //    Debug.DrawLine(corners[0], corners[1], Color.Cyan);
        //    Debug.DrawLine(corners[1], corners[3], Color.Cyan);
        //    Debug.DrawLine(corners[3], corners[2], Color.Cyan);
        //    Debug.DrawLine(corners[2], corners[0], Color.Cyan);
        //
        //    Debug.DrawLine(corners[4], corners[5], Color.Cyan);
        //    Debug.DrawLine(corners[5], corners[7], Color.Cyan);
        //    Debug.DrawLine(corners[7], corners[6], Color.Cyan);
        //    Debug.DrawLine(corners[6], corners[4], Color.Cyan);
        //
        //    Debug.DrawLine(corners[0], corners[4], Color.Cyan);
        //    Debug.DrawLine(corners[1], corners[5], Color.Cyan);
        //    Debug.DrawLine(corners[2], corners[6], Color.Cyan);
        //    Debug.DrawLine(corners[3], corners[7], Color.Cyan);
        //}
    }


    public override LightType GetLightType() => LightType.Directional;

    public override ForwardLightData GetForwardLightData()
    {
        return new ForwardLightData
        {
            Type = LightType.Directional,
            Position = Transform.Position,
            Direction = Transform.Forward,
            Color = new Float3(this.Color.R, this.Color.G, this.Color.B),
            Intensity = Intensity,
            Range = 0,
            SpotAngle = 0,
            InnerSpotAngle = 0,

            ShadowEnabled = false,
            ShadowBias = ShadowBias,
            ShadowNormalBias = ShadowNormalBias,
            ShadowStrength = ShadowStrength,
            ShadowQuality = (float)ShadowQuality,
        };
    }
}
