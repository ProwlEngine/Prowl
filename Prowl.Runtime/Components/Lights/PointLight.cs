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

    // Shadow cubemap data - 6 faces stored in a 3x2 grid in the shadow atlas
    private Float4[] _shadowFaceParams = new Float4[6]; // xy = atlas pos, z = face size, w = far plane
    private Float4x4[] _shadowMatrices = new Float4x4[6]; // View-projection for each face
    private bool _shadowsValid = false;

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

    public override void RenderShadows(RenderPipeline pipeline, Float3 cameraPosition, System.Collections.Generic.IReadOnlyList<IRenderable> renderables)
    {
        if (!DoCastShadows())
        {
            _shadowsValid = false;
            return;
        }

        int res = (int)ShadowResolution;
        Float3 lightPos = Transform.Position;

        // Reserve 3x2 grid in shadow atlas for 6 cubemap faces
        // Layout: [+X][-X][+Y]
        //         [-Y][+Z][-Z]
        int requestedWidth = res * 3;
        int requestedHeight = res * 2;
        Int2? slot = ShadowAtlas.ReserveTiles(requestedWidth, requestedHeight, GetLightID());

        if (slot == null)
        {
            _shadowsValid = false;
            return;
        }

        int atlasX = slot.Value.X;
        int atlasY = slot.Value.Y;

        // Define the 6 cube faces with their orientations
        // Each face needs: target direction and up vector
        (Float3 forward, Float3 up)[] faceOrientations = new[]
        {
            (Float3.UnitX,  -Float3.UnitY), // +X (right)
            (-Float3.UnitX, -Float3.UnitY), // -X (left)
            (Float3.UnitY,   Float3.UnitZ), // +Y (up)
            (-Float3.UnitY, -Float3.UnitZ), // -Y (down)
            (Float3.UnitZ,  -Float3.UnitY), // +Z (forward)
            (-Float3.UnitZ, -Float3.UnitY), // -Z (back)
        };

        // Create perspective projection for all faces (90 degree FOV for cubemap)
        Float4x4 projection = Float4x4.CreatePerspectiveFov(Maths.PI / 2.0f, 1.0f, 0.1f, Range);

        // Render each face
        for (int faceIndex = 0; faceIndex < 6; faceIndex++)
        {
            // Calculate viewport position in 3x2 grid
            int gridX = faceIndex % 3;
            int gridY = faceIndex / 3;
            int viewportX = atlasX + (gridX * res);
            int viewportY = atlasY + (gridY * res);

            (Float3 forward, Float3 up) = faceOrientations[faceIndex];
            Float4x4 view = Float4x4.CreateLookTo(lightPos, forward, up);

            Frustum frustum = Frustum.FromMatrix(projection * view);

            Float3 right = Float3.Normalize(Float3.Cross(up, forward));
            ViewerData viewerData = new ViewerData(lightPos, forward, right, up);

            System.Collections.Generic.HashSet<int> culledRenderableIndices = pipeline.CullRenderables(renderables, frustum, LayerMask.Everything);

            // Push this face's view/proj into the global UBO BEFORE the face CB
            // encodes draws otherwise all six faces would batch into one CB and
            // execute against whatever matrices the last face uploaded.
            pipeline.AssignCameraMatrices(view, projection);

            using var cmd = Graphics.GetCommandBuffer($"PointLightFace{faceIndex}");
            cmd.SetRenderTarget(ShadowAtlas.GetAtlas().frameBuffer);
            cmd.SetViewport(viewportX, viewportY, (uint)res, (uint)res);
            pipeline.DrawRenderables(cmd, renderables, "LightMode", "ShadowCaster", viewerData, culledRenderableIndices, false);
            Graphics.Submit(cmd);

            // Store face data for shader
            _shadowMatrices[faceIndex] = projection * view;
            _shadowFaceParams[faceIndex] = new Float4(viewportX, viewportY, res, Range);
        }

        _shadowsValid = true;
    }

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

            ShadowEnabled = CastShadows && _shadowsValid,
            ShadowBias = ShadowBias,
            ShadowNormalBias = ShadowNormalBias,
            ShadowStrength = ShadowStrength,
            ShadowQuality = (float)ShadowQuality,

            PointShadowMatrices = _shadowMatrices,
            PointShadowFaceParams = _shadowFaceParams,
        };
    }
}
