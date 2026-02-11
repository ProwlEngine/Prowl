// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime;

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

    public Resolution ShadowResolution = Resolution._1024;
    public CascadeCount Cascades = CascadeCount.Four;

    public float ShadowDistance = 100f;

    private Material? _lightMaterial;

    // Cascade data (max 4 cascades)
    private Float4x4[] _cascadeShadowMatrices = new Float4x4[4];
    private Float4[] _cascadeAtlasParams = new Float4[4]; // xy = atlas pos, z = atlas size, w = split distance
    private int _activeCascades = 0;

    public override void Update()
    {
        GameObject.Scene.PushLight(this);
    }

    public override void DrawGizmos()
    {
        Debug.DrawArrow(Transform.Position, -Transform.Forward, Color.Yellow);
        Debug.DrawWireCircle(Transform.Position, Transform.Forward, 0.5f, Color.Yellow);

        // Create and Draw each Frustum
        foreach (var cascade in _cascadeShadowMatrices)
        {
            Frustum frustum = Frustum.FromMatrix(cascade);
            var corners = frustum.GetCorners();

            // Corner indices from GetCorners():
            // 0: Near-Left-Bottom,  1: Near-Right-Bottom,  2: Near-Left-Top,  3: Near-Right-Top
            // 4: Far-Left-Bottom,   5: Far-Right-Bottom,   6: Far-Left-Top,   7: Far-Right-Top

            Debug.DrawLine(corners[0], corners[1], Color.Cyan);
            Debug.DrawLine(corners[1], corners[3], Color.Cyan);
            Debug.DrawLine(corners[3], corners[2], Color.Cyan);
            Debug.DrawLine(corners[2], corners[0], Color.Cyan);

            Debug.DrawLine(corners[4], corners[5], Color.Cyan);
            Debug.DrawLine(corners[5], corners[7], Color.Cyan);
            Debug.DrawLine(corners[7], corners[6], Color.Cyan);
            Debug.DrawLine(corners[6], corners[4], Color.Cyan);

            Debug.DrawLine(corners[0], corners[4], Color.Cyan);
            Debug.DrawLine(corners[1], corners[5], Color.Cyan);
            Debug.DrawLine(corners[2], corners[6], Color.Cyan);
            Debug.DrawLine(corners[3], corners[7], Color.Cyan);
        }
    }


    public override LightType GetLightType() => LightType.Directional;

    private void GetShadowMatrix(Float3 cameraPosition, int shadowResolution, float cascadeDistance, out Float4x4 view, out Float4x4 projection)
    {
        Float3 forward = -Transform.Forward;
        projection = Float4x4.CreateOrtho(cascadeDistance, cascadeDistance, -cascadeDistance * 0.5f, cascadeDistance * 0.5f);

        // Calculate texel size in world units
        float texelSize = (cascadeDistance * 2.0f) / shadowResolution;

        // Build orthonormal basis for light space
        Float3 lightUp = Float3.Normalize(Transform.Up);
        Float3 lightRight = Float3.Normalize(Float3.Cross(lightUp, forward));
        lightUp = Float3.Normalize(Float3.Cross(forward, lightRight)); // Recompute to ensure orthogonality

        // Project camera position onto light space axes
        float x = Float3.Dot(cameraPosition, lightRight);
        float y = Float3.Dot(cameraPosition, lightUp);
        float z = Float3.Dot(cameraPosition, forward); // KEEP the Z component! god damnit lost so much time to this

        // Snap only X and Y to texel grid in light space
        x = Maths.Round(x / texelSize) * texelSize;
        y = Maths.Round(y / texelSize) * texelSize;

        // Reconstruct the snapped position (X and Y snapped, Z preserved)
        Float3 snappedPosition = (lightRight * x) + (lightUp * y) + (forward * z);

        // Position the shadow map at the snapped position
        view = Float4x4.CreateLookTo(snappedPosition, forward, Transform.Up);
    }

    public override void RenderShadows(RenderPipeline pipeline, Float3 cameraPosition, System.Collections.Generic.IReadOnlyList<IRenderable> renderables)
    {
        if (!DoCastShadows())
        {
            // No shadows
            _activeCascades = 0;
            return;
        }

        // Determine number of cascades
        int numCascades = (int)Cascades;
        _activeCascades = numCascades;

        // Calculate linear split distances
        float cascadeInterval = ShadowDistance / numCascades;


        // Light direction vectors
        Float3 forward = -Transform.Forward;
        Float3 right = Transform.Right;
        Float3 up = Transform.Up;

        // Render each cascade
        for (int cascadeIndex = 0; cascadeIndex < numCascades; cascadeIndex++)
        {
            // Calculate this cascade's distance (linear split)
            float cascadeDistance = cascadeInterval * (cascadeIndex + 1);

            // Get shadow resolution per cascade
            int res = (int)ShadowResolution;
            // Half resolution for each cascade beyond the first
            if (cascadeIndex > 0)
                res /= cascadeIndex + 1;


            // Reserve space in shadow atlas for this cascade
            Int2? slot = ShadowAtlas.ReserveTiles(res, res, GetLightID() + cascadeIndex);

            if (slot != null)
            {
                int atlasX = slot.Value.X;
                int atlasY = slot.Value.Y;

                // Set viewport to this cascade's atlas region
                Graphics.Viewport(atlasX, atlasY, (uint)res, (uint)res);

                // Calculate shadow matrix for this cascade distance
                GetShadowMatrix(cameraPosition, res, cascadeDistance, out Float4x4 view, out Float4x4 proj);

                Frustum frustum = Frustum.FromMatrix(proj * view);

                // Cull and render shadow casters for this cascade
                System.Collections.Generic.HashSet<int> culledRenderableIndices = pipeline.CullRenderables(renderables, frustum, LayerMask.Everything);
                pipeline.AssignCameraMatrices(view, proj);
                pipeline.DrawRenderables(renderables, "LightMode", "ShadowCaster", new ViewerData(GetLightPosition(), forward, right, up), culledRenderableIndices, false);

                // Store cascade data for shader
                _cascadeShadowMatrices[cascadeIndex] = proj * view;
                _cascadeAtlasParams[cascadeIndex] = new Float4(atlasX, atlasY, res, cascadeDistance);
            }
            else
            {
                // Failed to reserve atlas space for this cascade
                _cascadeAtlasParams[cascadeIndex] = new Float4(-1, -1, 0, cascadeDistance);
            }
        }
    }

    public override void OnRenderLight(RenderTexture gBuffer, RenderTexture destination, RenderPipeline.CameraSnapshot css)
    {
        // Create material if needed
        _lightMaterial ??= new Material(Shader.LoadDefault(DefaultShader.DirectionalLight));

        // Set GBuffer textures
        _lightMaterial.SetTexture("_GBufferA", gBuffer.InternalTextures[0]);
        _lightMaterial.SetTexture("_GBufferB", gBuffer.InternalTextures[1]);
        _lightMaterial.SetTexture("_GBufferC", gBuffer.InternalTextures[2]);
        _lightMaterial.SetTexture("_GBufferD", gBuffer.InternalTextures[3]);
        _lightMaterial.SetTexture("_CameraDepthTexture", gBuffer.InternalDepth);

        // Set shadow atlas texture and size
        var shadowAtlas = ShadowAtlas.GetAtlas();
        _lightMaterial.SetTexture("_ShadowAtlas", shadowAtlas.InternalDepth);
        _lightMaterial.SetVector("_ShadowAtlasSize", new Float2(shadowAtlas.Width, shadowAtlas.Height));

        // Set directional light properties
        _lightMaterial.SetVector("_LightDirection", Transform.Forward);
        _lightMaterial.SetColor("_LightColor", Color);
        _lightMaterial.SetFloat("_LightIntensity", (float)Intensity);

        // Set shadow properties
        _lightMaterial.SetFloat("_ShadowBias", (float)ShadowBias);
        _lightMaterial.SetFloat("_ShadowNormalBias", (float)ShadowNormalBias);
        _lightMaterial.SetFloat("_ShadowStrength", (float)ShadowStrength);
        _lightMaterial.SetFloat("_ShadowQuality", (float)ShadowQuality);

        // Set cascade data
        _lightMaterial.SetInt("_CascadeCount", _activeCascades);

        // Set cascade matrices and atlas parameters
        for (int i = 0; i < 4; i++)
        {
            _lightMaterial.SetMatrix($"_CascadeShadowMatrix{i}", _cascadeShadowMatrices[i]);
            _lightMaterial.SetVector($"_CascadeAtlasParams{i}", _cascadeAtlasParams[i]);
        }

        // Draw fullscreen quad with the directional light shader
        RenderPipeline.Blit(gBuffer, destination, _lightMaterial, 0, false, false);
    }
}
