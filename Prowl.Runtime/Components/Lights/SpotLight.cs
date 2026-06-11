// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Events;
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

    private Float4x4 _shadowMatrix;
    private Float4 _shadowAtlasParams; // xy = atlas pos, z = atlas size, w = 1.0

    public override void OnRenderCollect(SceneEvents.OnRenderCollectArgs args)
    {
        args.lights.Add(this);
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

    private void GetShadowMatrix(out Float4x4 view, out Float4x4 projection)
    {
        Float3 forward = Transform.Forward;
        Float3 position = Transform.Position;

        // Use perspective projection for spot light
        float fov = SpotAngle * 2.0f; // Full cone angle
        projection = Float4x4.CreatePerspectiveFov(fov * Maths.Deg2Rad, 1.0f, 0.1f, Range);

        view = Float4x4.CreateLookTo(position, forward, Transform.Up);
    }

    public override void RenderShadows(RenderPipeline pipeline, Float3 cameraPosition, System.Collections.Generic.IReadOnlyList<IRenderable> renderables)
    {
        if (!DoCastShadows())
        {
            _shadowAtlasParams = new Float4(-1, -1, 0, 0);
            return;
        }

        int res = (int)ShadowResolution;
        Int2? slot = ShadowAtlas.ReserveTiles(res, res, GetLightID());

        if (slot != null)
        {
            int atlasX = slot.Value.X;
            int atlasY = slot.Value.Y;

            GetShadowMatrix(out Float4x4 view, out Float4x4 proj);

            Frustum frustum = Frustum.FromMatrix(proj * view);

            Float3 forward = Transform.Forward;
            Float3 right = Transform.Right;
            Float3 up = Transform.Up;

            bool[] culledRenderableIndices = pipeline.CullRenderables(renderables, frustum, LayerMask.Everything);
            pipeline.AssignCameraMatrices(view, proj);

            using var cmd = Graphics.GetCommandBuffer("SpotLightShadow");
            cmd.SetRenderTarget(ShadowAtlas.GetAtlas().frameBuffer);
            cmd.SetViewport(atlasX, atlasY, (uint)res, (uint)res);
            pipeline.DrawRenderables(cmd, renderables, "LightMode", "ShadowCaster", new ViewerData(GetLightPosition(), forward, right, up), culledRenderableIndices, false);
            Graphics.Submit(cmd);

            // Store shadow data for shader
            _shadowMatrix = proj * view;
            _shadowAtlasParams = new Float4(atlasX, atlasY, res, 1.0f);
        }
        else
        {
            // Failed to reserve atlas space
            _shadowAtlasParams = new Float4(-1, -1, 0, 0);
        }
    }

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

            ShadowEnabled = CastShadows && _shadowAtlasParams.Z > 0,
            ShadowBias = ShadowBias,
            ShadowNormalBias = ShadowNormalBias,
            ShadowStrength = ShadowStrength,
            ShadowQuality = (float)ShadowQuality,

            SpotShadowMatrix = _shadowMatrix,
            SpotShadowAtlasParams = _shadowAtlasParams,
        };
    }
}
