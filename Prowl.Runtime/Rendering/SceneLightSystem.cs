// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Per-scene owner of the light trees forward shading walks. The render pipeline calls
/// <see cref="Reconcile"/> once per frame with the lights collected from the scene, and only what
/// changed is rebuilt and uploaded.
///
/// <para>
/// Directional lights never enter either tree. The brightest is uploaded via the cascade-shadow
/// uniform path; up to <see cref="MaxExtraDirectionalLights"/> others light the scene unshadowed.
/// </para>
///
/// <para>
/// Every point and spot light that casts shadows gets them while it is in view, sized and cached by each
/// camera's own <see cref="ShadowRenderer"/>. Its shadow data lives in a table, so there is no cap on how many.
/// </para>
/// </summary>
public sealed class SceneLightSystem : IDisposable
{
    /// <summary>How many directional lights beyond the main one light the scene (unshadowed). Must
    /// match <c>MAX_EXTRA_DIRECTIONAL_LIGHTS</c> in <c>Lighting.glsl</c>.</summary>
    public const int MaxExtraDirectionalLights = 4;

    private readonly ForwardLightTrees _trees = new();
    private readonly HashSet<IRenderableLight> _seenThisFrame = new(ReferenceEqualityComparer.Instance);

    // Per-frame results.
    private IRenderableLight _directional;
    private readonly List<IRenderableLight> _extraDirectionals = new();
    private readonly List<Light> _shadowLights = new();
    private readonly HashSet<Light> _shadowLightSet = new(ReferenceEqualityComparer.Instance);

    // Every shadow casting point and spot light holds a block id for as long as it casts, the same for every camera,
    // so the light's record can point at it while each camera fills the block with its own shadow data
    private readonly Dictionary<Light, int> _shadowIds = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<int> _freeShadowIds = new();
    private readonly List<Light> _releasedShadowIds = new();
    private readonly Func<Light, int> _shadowIdOf;
    private int _nextShadowId;

    /// <summary>The point and spot lights in the static and dynamic trees.</summary>
    internal ForwardLightTrees Trees => _trees;

    /// <summary>The brightest directional light this frame, or null. Render pipeline takes this
    /// for cascade shadows + the directional uniform slot.</summary>
    public IRenderableLight Directional => _directional;

    /// <summary>The other directional lights this frame, lit without shadows.</summary>
    public IReadOnlyList<IRenderableLight> ExtraDirectionals => _extraDirectionals;

    /// <summary>Point and spot lights that cast shadows this frame, in view or not.</summary>
    public IReadOnlyList<Light> ShadowLights => _shadowLights;

    /// <summary>Makes and caches every shadow map of this scene.</summary>
    public SceneLightSystem()
    {
        _shadowIdOf = light => _shadowIds.TryGetValue(light, out int id) ? id : -1;
    }

    /// <summary>
    /// Walk this frame's lights, track them in the static or dynamic tree, and pick the directional
    /// and the point and spot lights that cast shadows. Cheap when nothing changed.
    /// <paramref name="cameraPosition"/> is kept for callers, shadows are sized later.
    ///
    /// <para>
    /// Note on <paramref name="cullingMask"/>: per-camera light filtering by layer is not
    /// applied here. The trees are per scene and shared between cameras, so every light in the
    /// scene affects every camera. The argument is kept for forward compatibility.
    /// </para>
    /// </summary>
    /// <param name="lights">All lights in the scene.</param>
    /// <param name="cameraPosition">The camera position, unused for now.</param>
    /// <param name="cullingMask">The camera layer mask, unused for now.</param>
    /// <param name="view">The view moving lights are culled to, or null to keep them all, as both eyes of a headset need.</param>
    public void Reconcile(IReadOnlyList<IRenderableLight> lights, Float3 cameraPosition, LayerMask cullingMask, Frustum? view = null)
    {
        _ = cullingMask; // see remark above
        _ = cameraPosition;
        _seenThisFrame.Clear();
        _directional = null;
        _extraDirectionals.Clear();
        _shadowLights.Clear();

        for (int i = 0; i < lights.Count; i++)
        {
            var light = lights[i];
            if (light == null) continue;

            // Fully-baked lights live entirely in the lightmap + probes excluded from the realtime
            // set. (Mixed lights stay realtime only their indirect bounce is baked.)
            if (light is Light bakedLight && bakedLight.BakeMode == LightBakeMode.Baked)
                continue;

            if (light.GetLightType() == LightType.Directional)
            {
                _extraDirectionals.Add(light);
                continue;
            }

            _seenThisFrame.Add(light);
            _trees.Track(light, light.GetForwardLightData(), IsStaticLight(light));

            if (light.DoCastShadows() && light is Light shadowLight)
                _shadowLights.Add(shadowLight);
        }

        PickDirectionals();
        _trees.RemoveUnseen(_seenThisFrame);
        AssignShadowIds();
        _trees.BeginFrame(view);
    }

    // _extraDirectionals holds every directional on entry. The brightest becomes the main light, which
    // owns the cascades, and the rest stay as extras up to the shader's limit.
    private void PickDirectionals()
    {
        if (_extraDirectionals.Count == 0) return;

        int main = 0;
        float mainIntensity = _extraDirectionals[0].GetForwardLightData().Intensity;
        for (int i = 1; i < _extraDirectionals.Count; i++)
        {
            float intensity = _extraDirectionals[i].GetForwardLightData().Intensity;
            if (intensity > mainIntensity)
            {
                main = i;
                mainIntensity = intensity;
            }
        }

        _directional = _extraDirectionals[main];
        _extraDirectionals.RemoveAt(main);

        foreach (var extra in _extraDirectionals)
        {
            if (extra.DoCastShadows())
            {
                Debug.LogWarningOnce("ExtraDirectionalShadows",
                    "Only the brightest directional light casts shadows. The other directional lights light the scene without shadows.");
                break;
            }
        }

        if (_extraDirectionals.Count > MaxExtraDirectionalLights)
        {
            Debug.LogWarningOnce("TooManyDirectionalLights",
                $"Only {MaxExtraDirectionalLights + 1} directional lights are supported. The dimmest ones are ignored.");
            _extraDirectionals.Sort((a, b) => b.GetForwardLightData().Intensity.CompareTo(a.GetForwardLightData().Intensity));
            _extraDirectionals.RemoveRange(MaxExtraDirectionalLights, _extraDirectionals.Count - MaxExtraDirectionalLights);
        }
    }

    private static bool IsStaticLight(IRenderableLight light)
    {
        // Honour the GameObject.IsStatic flag when the light is a Component-backed Light.
        // Custom IRenderableLight implementations default to dynamic.
        return light is Light l && l.GameObject != null && l.GameObject.IsStatic;
    }

    /// <summary>
    /// Brings the shadow maps of the directional light and every shadowed point and spot light in view up to
    /// date, drawing only what changed, and points each light at its shadow data.
    /// </summary>
    /// <param name="pipeline">The current render pipeline.</param>
    /// <param name="camera">The camera this render is for.</param>
    /// <param name="renderables">Everything that could cast a shadow this frame.</param>
    /// <param name="shadows">The camera's own shadow maps and atlas.</param>
    internal void RenderShadows(RenderPipeline pipeline, ShadowRenderer shadows, in ShadowCamera camera, IReadOnlyList<IRenderable> renderables)
    {
        shadows.Update(pipeline, camera, _directional as DirectionalLight, _shadowLights, renderables, _shadowIdOf);
    }

    // Lights that started casting get an id and lights that stopped give theirs back
    private void AssignShadowIds()
    {
        _shadowLightSet.Clear();
        foreach (Light light in _shadowLights)
        {
            _shadowLightSet.Add(light);
            if (_shadowIds.ContainsKey(light)) continue;
            int id = _freeShadowIds.Count > 0 ? _freeShadowIds.Pop() : _nextShadowId++;
            _shadowIds[light] = id;
            _trees.SetShadowSlot(light, id);
        }

        if (_shadowIds.Count == _shadowLightSet.Count) return;
        _releasedShadowIds.Clear();
        foreach (Light light in _shadowIds.Keys)
            if (!_shadowLightSet.Contains(light)) _releasedShadowIds.Add(light);
        foreach (Light light in _releasedShadowIds)
        {
            _freeShadowIds.Push(_shadowIds[light]);
            _shadowIds.Remove(light);
            _trees.SetShadowSlot(light, -1);
        }
    }

    /// <summary>
    /// Upload all uniforms touched by <c>Lighting.glsl</c> and <c>LightTree.glsl</c>: the light
    /// trees, the directional light slot, the cascade shadow data, and the local shadow data
    /// texture and atlas. Call after <see cref="Reconcile"/> and <see cref="RenderShadows"/>, before
    /// any forward draws.
    /// </summary>
    /// <param name="view">The view this frame's cascades were fitted to. The shader fades shadows by depth along it.</param>
    /// <param name="shadows">The camera's own shadow maps, which <see cref="RenderShadows"/> brought up to date.</param>
    internal void UploadGlobalUniforms(in ShadowFitView view, ShadowRenderer shadows)
    {
        // Encoded into one buffer and submitted once, rather than a buffer per global
        using var cmd = Graphics.GetCommandBuffer("LightUniforms");
        _trees.Upload(cmd);
        UploadDirectionalLight(cmd, view, shadows);
        UploadLocalShadows(cmd, shadows);
        Graphics.Submit(cmd);
    }

    private void UploadDirectionalLight(CommandBuffer cmd, in ShadowFitView view, ShadowRenderer shadows)
    {
        cmd.SetGlobalVector("_ShadowViewOrigin", view.Origin);
        cmd.SetGlobalVector("_ShadowViewForward", view.Forward);

        cmd.SetGlobalInt("_ExtraDirectionalLightCount", _extraDirectionals.Count);
        for (int i = 0; i < _extraDirectionals.Count; i++)
        {
            var extra = _extraDirectionals[i].GetForwardLightData();
            cmd.SetGlobalVector($"_ExtraDirectionalLightDirection[{i}]", extra.Direction);
            cmd.SetGlobalVector($"_ExtraDirectionalLightColor[{i}]", extra.Color * extra.Intensity);
        }

        if (_directional == null)
        {
            cmd.SetGlobalInt("_DirectionalLightEnabled", 0);
            cmd.SetGlobalVector("_DirectionalLightDirection", Float3.Zero);
            cmd.SetGlobalVector("_DirectionalLightColor", Float3.Zero);
            cmd.SetGlobalFloat("_DirectionalLightIntensity", 0f);
            cmd.SetGlobalInt("_DirectionalLightShadowEnabled", 0);
            cmd.SetGlobalInt("_CascadeCount", 0);
            return;
        }

        var data = _directional.GetForwardLightData();
        // Direct lighting wants the raw direction; intensity applies the same * 8 scaling the
        // legacy ForwardLightManager did so existing scenes look identical at low light counts.
        cmd.SetGlobalInt("_DirectionalLightEnabled", 1);
        cmd.SetGlobalVector("_DirectionalLightDirection", data.Direction);
        cmd.SetGlobalVector("_DirectionalLightColor", data.Color);
        cmd.SetGlobalFloat("_DirectionalLightIntensity", data.Intensity);
        int cascades = data.ShadowEnabled ? shadows.CascadeCount : 0;
        cmd.SetGlobalInt("_DirectionalLightShadowEnabled", cascades > 0 ? 1 : 0);
        cmd.SetGlobalFloat("_DirectionalLightShadowDepthBias", data.ShadowDepthBias);
        cmd.SetGlobalFloat("_DirectionalLightShadowNormalBias", data.ShadowNormalBias);
        cmd.SetGlobalFloat("_DirectionalLightShadowDistance", data.ShadowDistance);
        cmd.SetGlobalFloat("_DirectionalLightShadowStrength", data.ShadowStrength);
        cmd.SetGlobalFloat("_DirectionalLightShadowQuality", data.ShadowQuality);

        cmd.SetGlobalInt("_CascadeCount", cascades);
        for (int c = 0; c < 4; c++)
        {
            bool used = c < cascades;
            cmd.SetGlobalMatrix($"_CascadeShadowMatrix{c}", used ? shadows.CascadeMatrices[c] : Float4x4.Identity);
            cmd.SetGlobalVector($"_CascadeAtlasParams{c}", used ? shadows.CascadeAtlasParams[c] : Float4.Zero);
            cmd.SetGlobalVector($"_CascadeSphere{c}", used ? shadows.CascadeSpheres[c] : Float4.Zero);
        }
    }

    private void UploadLocalShadows(CommandBuffer cmd, ShadowRenderer shadows)
    {
        shadows.BindData(cmd);

        Texture2D? atlas = shadows.AtlasTexture;
        if (atlas.IsValid())
        {
            cmd.SetGlobalTexture("_ShadowAtlas", atlas);
            cmd.SetGlobalVector("_ShadowAtlasSize", new Float2(atlas.Width, atlas.Height));
        }
    }

    public void Dispose()
    {
        _trees.Dispose();
        _seenThisFrame.Clear();
        _shadowLights.Clear();
        _shadowIds.Clear();
        _freeShadowIds.Clear();
        _directional = null;
        _extraDirectionals.Clear();
    }
}
