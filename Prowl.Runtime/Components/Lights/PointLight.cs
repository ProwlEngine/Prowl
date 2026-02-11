// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime;

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

    private Material? _lightMaterial;

    // Shadow cubemap data - 6 faces stored in a 3x2 grid in the shadow atlas
    private Float4[] _shadowFaceParams = new Float4[6]; // xy = atlas pos, z = face size, w = far plane
    private Float4x4[] _shadowMatrices = new Float4x4[6]; // View-projection for each face
    private bool _shadowsValid = false;

    public override void Update()
    {
        GameObject.Scene.PushLight(this);
    }

    public override void DrawGizmos()
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

            // Set viewport for this face
            Graphics.Viewport(viewportX, viewportY, (uint)res, (uint)res);

            // Create view matrix for this face
            (Float3 forward, Float3 up) = faceOrientations[faceIndex];
            Float4x4 view = Float4x4.CreateLookTo(lightPos, forward, up);

            Frustum frustum = Frustum.FromMatrix(projection * view);

            // Calculate viewer data for this face
            Float3 right = Float3.Normalize(Float3.Cross(up, forward));
            ViewerData viewerData = new ViewerData(lightPos, forward, right, up);

            // Cull and render shadow casters for this face
            System.Collections.Generic.HashSet<int> culledRenderableIndices = pipeline.CullRenderables(renderables, frustum, LayerMask.Everything);
            pipeline.AssignCameraMatrices(view, projection);
            pipeline.DrawRenderables(renderables, "LightMode", "ShadowCaster", viewerData, culledRenderableIndices, false);

            // Store face data for shader
            _shadowMatrices[faceIndex] = projection * view;
            _shadowFaceParams[faceIndex] = new Float4(viewportX, viewportY, res, Range);
        }

        _shadowsValid = true;
    }

    private static Mesh? _mesh;
    public override void OnRenderLight(RenderTexture gBuffer, RenderTexture destination, RenderPipeline.CameraSnapshot css)
    {
        // Create sphere mesh if needed (shared by all point lights)
        if (_mesh == null || !_mesh.IsValid())
        {
            _mesh = Mesh.CreateSphere(1.0f, 8, 8); // Unit sphere, scaled by range
        }

        // Create material if needed
        _lightMaterial ??= new Material(Shader.LoadDefault(DefaultShader.PointLight));

        // Set GBuffer textures
        _lightMaterial.SetTexture("_GBufferA", gBuffer.InternalTextures[0]);
        _lightMaterial.SetTexture("_GBufferB", gBuffer.InternalTextures[1]);
        _lightMaterial.SetTexture("_GBufferC", gBuffer.InternalTextures[2]);
        _lightMaterial.SetTexture("_GBufferD", gBuffer.InternalTextures[3]);
        _lightMaterial.SetTexture("_CameraDepthTexture", gBuffer.InternalDepth);

        // Set point light properties
        _lightMaterial.SetVector("_LightPosition", Transform.Position);
        _lightMaterial.SetColor("_LightColor", Color);
        _lightMaterial.SetFloat("_LightIntensity", (float)Intensity);
        _lightMaterial.SetFloat("_LightRange", (float)Range);

        // Set shadow properties
        var shadowAtlas = ShadowAtlas.GetAtlas();
        _lightMaterial.SetTexture("_ShadowAtlas", shadowAtlas.InternalDepth);
        _lightMaterial.SetFloat("_ShadowsEnabled", _shadowsValid ? 1.0f : 0.0f);
        _lightMaterial.SetFloat("_ShadowBias", (float)ShadowBias);
        _lightMaterial.SetFloat("_ShadowNormalBias", (float)ShadowNormalBias);
        _lightMaterial.SetFloat("_ShadowStrength", (float)ShadowStrength);
        _lightMaterial.SetFloat("_ShadowQuality", (float)ShadowQuality);

        // Set shadow matrices and face parameters for all 6 faces
        for (int i = 0; i < 6; i++)
        {
            _lightMaterial.SetMatrix($"_ShadowMatrix{i}", _shadowMatrices[i]);
            _lightMaterial.SetVector($"_ShadowFaceParams{i}", _shadowFaceParams[i]);
        }

        // Create model matrix - scale sphere by range and position at light location
        Float4x4 model = this.Transform.LocalToWorldMatrix;
        Float4x4 scale = Float4x4.CreateScale(new Float3(Range, Range, Range));
        model = model * scale;

        // Set transform matrices
        _lightMaterial.SetMatrix("prowl_ObjectToWorld", model);
        _lightMaterial.SetMatrix("prowl_WorldToObject", model.Invert());

        // Bind destination framebuffer
        Graphics.BindFramebuffer(destination.frameBuffer);

        // Draw sphere mesh
        RenderPipeline.DrawMeshNow(_mesh, _lightMaterial, 0);
    }
}
