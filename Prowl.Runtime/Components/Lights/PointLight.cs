// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime;

[AddComponentMenu("Light/Point Light")]
[ComponentIcon("\uf0eb")] // Lightbulb
public class PointLight : Light
{
    public enum Resolution : int
    {
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
    }

    public Resolution ShadowResolution = Resolution._256;
    public float Range = 10.0f;

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        lights.Add(this);
    }

    public override void DrawGizmos()
    {
        var icon = Resources.Texture2D.LoadDefault(Resources.DefaultTexture.IconLight);
        if (icon != null) Debug.DrawIcon(icon, Transform.Position, 0.5f, Color.White);
    }

    public override void DrawGizmosSelected()
    {
        Debug.DrawWireSphere(Transform.Position, Range, Color.Yellow);
    }

    public override LightType GetLightType() => LightType.Point;

    public override ForwardLightData GetForwardLightData()
    {
        return new ForwardLightData
        {
            Type = LightType.Point,
            Position = Transform.Position,
            Direction = Transform.Forward,
            Color = new Float3(this.Color.R, this.Color.G, this.Color.B),
            Intensity = Intensity,
            Range = Range,
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
