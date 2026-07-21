// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime;

[AddComponentMenu("Light/Spot Light")]
[ComponentIcon("\uf0e7")] // Bolt
public class SpotLight : Light
{
    public enum Resolution : int
    {
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
    }

    public Resolution ShadowResolution = Resolution._512;
    public float Range = 8.0f;
    public float SpotAngle = 45.0f; // Outer cone angle in degrees
    public float InnerSpotAngle = 30.0f; // Inner cone angle in degrees for smooth falloff

    public override void OnRenderCollect(SceneCuller culler)
    {
        culler.Add(this);
    }

    public override void DrawGizmos()
    {
        var icon = Resources.Texture2D.LoadDefault(Resources.DefaultTexture.IconLight);
        if (icon != null) Debug.DrawIcon(icon, Transform.Position, 0.5f, Color.White);
    }

    public override void DrawGizmosSelected()
    {
        Debug.DrawArrow(Transform.Position, Transform.Forward, Color.Yellow);

        // Draw cone to visualize spot light
        float outerAngleRad = SpotAngle * Maths.Deg2Rad;
        float radius = Maths.Tan(outerAngleRad) * Range;
        Float3 endPosition = Transform.Position + Transform.Forward * Range;

        // Draw cone outline
        int segments = 4;
        Float3 prevPoint = Float3.Zero;
        for (int i = 0; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Maths.PI * 2.0f;
            Float3 offset = (Transform.Right * Maths.Cos(angle) + Transform.Up * Maths.Sin(angle)) * radius;
            Float3 point = endPosition + offset;

            // Draw line from light position to cone edge
            Debug.DrawLine(Transform.Position, point, Color.Yellow);

            prevPoint = point;
        }

        // Draw circle at the end of the cone
        Debug.DrawWireCircle(endPosition, Transform.Forward, radius, Color.Yellow);
    }

    public override LightType GetLightType() => LightType.Spot;

    public override ForwardLightData GetForwardLightData()
    {
        return new ForwardLightData
        {
            Type = LightType.Spot,
            Position = Transform.Position,
            Direction = Transform.Forward,
            Color = new Float3(this.Color.R, this.Color.G, this.Color.B),
            Intensity = Intensity,
            Range = Range,
            SpotAngle = SpotAngle,
            InnerSpotAngle = InnerSpotAngle,

            ShadowEnabled = false,
            ShadowBias = ShadowBias,
            ShadowNormalBias = ShadowNormalBias,
            ShadowStrength = ShadowStrength,
            ShadowQuality = (float)ShadowQuality,
        };
    }
}
