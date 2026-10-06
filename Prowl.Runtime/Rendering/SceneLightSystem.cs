// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Per-scene owner of the static and dynamic light BVHs and their GPU-side mirrors. The render
/// pipeline calls <see cref="Reconcile"/> once per frame with the lights collected from the
/// scene; this class adds / removes / refits as needed and uploads only the dirty rows of each
/// texture.
///
/// <para>
/// Directional lights never enter either BVH. The brightest is uploaded via the cascade-shadow
/// uniform path; up to <see cref="MaxExtraDirectionalLights"/> others light the scene unshadowed.
/// </para>
///
/// <para>
/// A bounded number of point + spot lights win shadow atlas slots each frame, picked by distance
/// to the camera. Lights that miss the cut still light surfaces; they just sample as unshadowed.
/// </para>
/// </summary>
public sealed class SceneLightSystem : IDisposable
{
    /// <summary>How many local lights (point + spot combined) can have shadow atlas slots in a
    /// single frame. The atlas budget makes this small in practice; bump alongside the matching
    /// uniform-array sizes in <c>Lighting.glsl</c> if you raise it.</summary>
    public const int MaxShadowCasters = 4;

    /// <summary>How many directional lights beyond the main one light the scene (unshadowed). Must
    /// match <c>MAX_EXTRA_DIRECTIONAL_LIGHTS</c> in <c>Lighting.glsl</c>.</summary>
    public const int MaxExtraDirectionalLights = 4;

    private readonly LightBVH _staticBVH = new();
    private readonly LightBVH _dynamicBVH = new();
    private readonly LightBVHTextures _staticTex = new();
    private readonly LightBVHTextures _dynamicTex = new();

    // Tracking which BVH each registered light currently lives in so we can detect static<->dynamic
    // transitions and removals.
    private readonly Dictionary<IRenderableLight, Membership> _membership = new();
    private readonly HashSet<IRenderableLight> _seenThisFrame = new();

    // Per-frame results.
    private IRenderableLight _directional;
    private readonly List<IRenderableLight> _extraDirectionals = new();
    private readonly List<IRenderableLight> _shadowCasters = new();
    private readonly List<IRenderableLight> _previousCasters = new();
    private readonly List<(IRenderableLight light, float distSq)> _localCandidates = new();
    private readonly List<IRenderableLight> _toRemove = new();
    private static readonly Comparison<(IRenderableLight light, float distSq)> s_byDistance = (a, b) => a.distSq.CompareTo(b.distSq);

    public LightBVH StaticBVH => _staticBVH;
    public LightBVH DynamicBVH => _dynamicBVH;
    public LightBVHTextures StaticTextures => _staticTex;
    public LightBVHTextures DynamicTextures => _dynamicTex;

    /// <summary>The brightest directional light this frame, or null. Render pipeline takes this
    /// for cascade shadows + the directional uniform slot.</summary>
    public IRenderableLight Directional => _directional;

    /// <summary>The other directional lights this frame, lit without shadows.</summary>
    public IReadOnlyList<IRenderableLight> ExtraDirectionals => _extraDirectionals;

    /// <summary>Lights that won shadow atlas slots this frame, in order. The pipeline calls
    /// <c>RenderShadows</c> on each.</summary>
    public IReadOnlyList<IRenderableLight> ShadowCasters => _shadowCasters;

    private enum Membership { Static, Dynamic }

    /// <summary>
    /// Walk this frame's lights, register / unregister with the appropriate BVH, refit dynamics,
    /// pick the directional + closest-N shadow casters, and upload only the dirty rows of each
    /// texture. Cheap when nothing changed. Shadow casters are picked by distance to
    /// <paramref name="cameraPosition"/>.
    ///
    /// <para>
    /// Note on <paramref name="cullingMask"/>: per-camera light filtering by layer is not
    /// applied here. The BVH is a per-scene structure, shared between cameras; per-camera
    /// layer filtering would require either a separate BVH per camera (expensive) or per-leaf
    /// layer bits checked in the shader (not yet implemented). For now every light in the scene
    /// affects every camera. The argument is kept for forward compatibility.
    /// </para>
    /// </summary>
    public void Reconcile(IReadOnlyList<IRenderableLight> lights, Float3 cameraPosition, LayerMask cullingMask)
    {
        _ = cullingMask; // see remark above
        _seenThisFrame.Clear();
        _directional = null;
        _extraDirectionals.Clear();
        _shadowCasters.Clear();

        List<(IRenderableLight light, float distSq)> localCandidates = _localCandidates;
        localCandidates.Clear();

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
            var data = light.GetForwardLightData();

            bool isStatic = IsStaticLight(light);
            if (_membership.TryGetValue(light, out Membership current))
            {
                // Handle static<->dynamic transition.
                if (current == Membership.Static && !isStatic)
                {
                    _staticBVH.Remove(light);
                    _dynamicBVH.Add(light, in data);
                    _membership[light] = Membership.Dynamic;
                }
                else if (current == Membership.Dynamic && isStatic)
                {
                    _dynamicBVH.Remove(light);
                    _staticBVH.Add(light, in data);
                    _membership[light] = Membership.Static;
                }
                else
                {
                    // Refit / topology check happens inside Update; unchanged data is a no-op.
                    (current == Membership.Static ? _staticBVH : _dynamicBVH).Update(light, in data);
                }
            }
            else
            {
                if (isStatic)
                {
                    _staticBVH.Add(light, in data);
                    _membership[light] = Membership.Static;
                }
                else
                {
                    _dynamicBVH.Add(light, in data);
                    _membership[light] = Membership.Dynamic;
                }
            }

            // Track for shadow-caster selection.
            if (light.DoCastShadows())
            {
                float dSq = (float)Float3.DistanceSquared(cameraPosition, light.GetLightPosition());
                localCandidates.Add((light, dSq));
            }
        }

        PickDirectionals();

        // Second pass: drop registrations for lights that didn't show up.
        // Iterate over a snapshot since we mutate _membership inside the loop.
        if (_membership.Count > _seenThisFrame.Count)
        {
            List<IRenderableLight> toRemove = _toRemove;
            toRemove.Clear();
            foreach (var kv in _membership)
                if (!_seenThisFrame.Contains(kv.Key))
                    toRemove.Add(kv.Key);
            foreach (var light in toRemove)
            {
                if (_membership[light] == Membership.Static) _staticBVH.Remove(light);
                else _dynamicBVH.Remove(light);
                _membership.Remove(light);
            }
        }

        // Pick closest-N shadow casters. Reset every registered light's slot to -1 first so
        // anything that lost its slot this frame samples as unshadowed. We only need to touch
        // slots whose current value disagrees with the new assignment.
        localCandidates.Sort(s_byDistance);

        int casterCount = Math.Min(MaxShadowCasters, localCandidates.Count);
        for (int i = 0; i < casterCount; i++)
        {
            var l = localCandidates[i].light;
            _shadowCasters.Add(l);
            int slot = i;
            if (_membership.TryGetValue(l, out var m))
            {
                var bvh = m == Membership.Static ? _staticBVH : _dynamicBVH;
                bvh.SetShadowSlot(l, slot);
            }
        }
        // Clear stale slots on lights that were shadow casters last frame but aren't now,
        // including ones that stopped casting shadows entirely.
        foreach (var l in _previousCasters)
        {
            if (!_shadowCasters.Contains(l) && _membership.TryGetValue(l, out var m))
                (m == Membership.Static ? _staticBVH : _dynamicBVH).SetShadowSlot(l, -1);
        }
        _previousCasters.Clear();
        _previousCasters.AddRange(_shadowCasters);

        // Build / refit and upload. Either tree rebuilds on add/remove/transition or when a light
        // escapes its loose bounds.
        _staticBVH.Sync();
        _dynamicBVH.Sync();
        _staticTex.Sync(_staticBVH);
        _dynamicTex.Sync(_dynamicBVH);
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
        // Honour the GameObject.IsStatic flag when the light is a MonoBehaviour-backed Light.
        // Custom IRenderableLight implementations default to dynamic.
        return light is Light l && l.GameObject != null && l.GameObject.IsStatic;
    }

    /// <summary>
    /// Render shadow maps for the directional light and the selected closest-N point / spot
    /// shadow casters into the shared shadow atlas. The pipeline calls this after binding the
    /// shadow framebuffer.
    /// </summary>
    /// <param name="pipeline">The current render pipeline.</param>
    /// <param name="view">The view the directional light fits its cascades to.</param>
    /// <param name="renderables">Everything that could cast a shadow this frame.</param>
    public void RenderShadows(RenderPipeline pipeline, in ShadowFitView view, IReadOnlyList<IRenderable> renderables)
    {
        // Each light manages its own CommandBuffer(s) internally point lights submit
        // one per face, directional submits one per cascade, spot submits a single CB
        // so per-face matrix uploads via AssignCameraMatrices are ordered correctly
        // against that face's draws.
        if (_directional is Light dl)
            dl.RenderShadows(pipeline, view, renderables);

        for (int i = 0; i < _shadowCasters.Count; i++)
        {
            if (_shadowCasters[i] is Light sc)
                sc.RenderShadows(pipeline, view, renderables);
        }
    }

    /// <summary>
    /// Upload all uniforms touched by <c>Lighting.glsl</c> and <c>LightBVH.glsl</c>: the four
    /// BVH textures, the directional light slot, the cascade shadow data, and the shadow atlas
    /// arrays for the selected closest-N point + spot lights. Call after <see cref="Reconcile"/>
    /// and <see cref="RenderShadows"/>, before any forward draws.
    /// </summary>
    /// <param name="view">The view this frame's cascades were fitted to. The shader fades shadows by depth along it.</param>
    public void UploadGlobalUniforms(in ShadowFitView view)
    {
        // All of these are global-uniform writes. Routing each through its own one-op
        // CommandBuffer (the PropertyState.SetGlobalX helpers) meant ~80-100 rent/submit
        // cycles per camera per frame. Encode them all into a single buffer and submit once.
        using var cmd = Graphics.GetCommandBuffer("LightUniforms");
        UploadBVHTextures(cmd);
        UploadDirectionalLight(cmd, view);
        UploadLocalShadowSlots(cmd);
        Graphics.Submit(cmd);
    }

    private void UploadBVHTextures(CommandBuffer cmd)
    {
        // Textures may be null when neither tree has been populated yet. Pass them through as-is;
        // the shader only samples them when its corresponding _*LightRoot >= 0 (which we set to
        // -1 here for empty trees). Sizes default to 0 and the root index to -1, both of which
        // make the traversal a no-op.
        if (_staticTex.LightDataTexture != null)
            cmd.SetGlobalTexture("_StaticLightData", _staticTex.LightDataTexture);
        if (_staticTex.NodeDataTexture != null)
            cmd.SetGlobalTexture("_StaticLightNodes", _staticTex.NodeDataTexture);
        if (_dynamicTex.LightDataTexture != null)
            cmd.SetGlobalTexture("_DynamicLightData", _dynamicTex.LightDataTexture);
        if (_dynamicTex.NodeDataTexture != null)
            cmd.SetGlobalTexture("_DynamicLightNodes", _dynamicTex.NodeDataTexture);

        cmd.SetGlobalInt("_StaticLightTexSize", _staticTex.LightTextureSize);
        cmd.SetGlobalInt("_StaticLightTexShift", Log2(_staticTex.LightTextureSize));
        cmd.SetGlobalInt("_StaticNodeTexSize", _staticTex.NodeTextureSize);
        cmd.SetGlobalInt("_StaticNodeTexShift", Log2(_staticTex.NodeTextureSize));
        cmd.SetGlobalInt("_DynamicLightTexSize", _dynamicTex.LightTextureSize);
        cmd.SetGlobalInt("_DynamicLightTexShift", Log2(_dynamicTex.LightTextureSize));
        cmd.SetGlobalInt("_DynamicNodeTexSize", _dynamicTex.NodeTextureSize);
        cmd.SetGlobalInt("_DynamicNodeTexShift", Log2(_dynamicTex.NodeTextureSize));

        cmd.SetGlobalInt("_StaticLightRoot", _staticTex.RootNode);
        cmd.SetGlobalInt("_DynamicLightRoot", _dynamicTex.RootNode);
    }

    /// <summary>log2 of a power of 2; returns 0 for size &lt;= 1.</summary>
    private static int Log2(int size)
    {
        int n = 0;
        while ((1 << n) < size) n++;
        return n;
    }

    private void UploadDirectionalLight(CommandBuffer cmd, in ShadowFitView view)
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
        cmd.SetGlobalInt("_DirectionalLightShadowEnabled", data.ShadowEnabled ? 1 : 0);
        cmd.SetGlobalFloat("_DirectionalLightShadowDepthBias", data.ShadowDepthBias);
        cmd.SetGlobalFloat("_DirectionalLightShadowNormalBias", data.ShadowNormalBias);
        cmd.SetGlobalFloat("_DirectionalLightShadowDistance", data.ShadowDistance);
        cmd.SetGlobalFloat("_DirectionalLightShadowStrength", data.ShadowStrength);
        cmd.SetGlobalFloat("_DirectionalLightShadowQuality", data.ShadowQuality);

        cmd.SetGlobalInt("_CascadeCount", data.ShadowEnabled ? data.CascadeCount : 0);
        if (data.CascadeShadowMatrices != null && data.CascadeAtlasParams != null)
        {
            for (int c = 0; c < 4; c++)
            {
                cmd.SetGlobalMatrix($"_CascadeShadowMatrix{c}",
                    c < data.CascadeCount ? data.CascadeShadowMatrices[c] : Float4x4.Identity);
                cmd.SetGlobalVector($"_CascadeAtlasParams{c}",
                    c < data.CascadeCount ? data.CascadeAtlasParams[c] : Float4.Zero);
                cmd.SetGlobalVector($"_CascadeSphere{c}",
                    c < data.CascadeCount && data.CascadeSpheres != null ? data.CascadeSpheres[c] : Float4.Zero);
            }
        }
    }

    // What occupied each shadow slot last frame (0 = empty, 1 = point, 2 = spot). Lets us clear
    // only the slots that actually held data instead of resetting all MaxShadowCasters*7 uniforms
    // every frame.
    private readonly int[] _slotKind = new int[MaxShadowCasters];

    private void UploadLocalShadowSlots(CommandBuffer cmd)
    {
        // Clear only the slots that held data last frame so any stale matrices a shader could
        // index fall through (spot atlas params .z <= 0). Slots reused this frame are overwritten
        // below; slots that were never written keep their default-zero GPU state.
        for (int i = 0; i < MaxShadowCasters; i++)
        {
            if (_slotKind[i] == 1)
            {
                for (int f = 0; f < 6; f++)
                {
                    int idx = i * 6 + f;
                    cmd.SetGlobalMatrix($"_PointShadowMatrices[{idx}]", Float4x4.Identity);
                    cmd.SetGlobalVector($"_PointShadowFaceParams[{idx}]", Float4.Zero);
                }
            }
            else if (_slotKind[i] == 2)
            {
                cmd.SetGlobalMatrix($"_SpotShadowMatrices[{i}]", Float4x4.Identity);
                cmd.SetGlobalVector($"_SpotShadowAtlasParams[{i}]", Float4.Zero);
            }
            _slotKind[i] = 0;
        }

        for (int i = 0; i < _shadowCasters.Count && i < MaxShadowCasters; i++)
        {
            var data = _shadowCasters[i].GetForwardLightData();
            if (!data.ShadowEnabled) continue;

            int slot = i;
            if (data.Type == LightType.Point)
            {
                if (data.PointShadowMatrices != null && data.PointShadowFaceParams != null)
                {
                    for (int f = 0; f < 6; f++)
                    {
                        int idx = slot * 6 + f;
                        cmd.SetGlobalMatrix($"_PointShadowMatrices[{idx}]", data.PointShadowMatrices[f]);
                        cmd.SetGlobalVector($"_PointShadowFaceParams[{idx}]", data.PointShadowFaceParams[f]);
                    }
                    _slotKind[slot] = 1;
                }
            }
            else if (data.Type == LightType.Spot)
            {
                cmd.SetGlobalMatrix($"_SpotShadowMatrices[{slot}]", data.SpotShadowMatrix);
                cmd.SetGlobalVector($"_SpotShadowAtlasParams[{slot}]", data.SpotShadowAtlasParams);
                _slotKind[slot] = 2;
            }
        }

        // Shadow atlas texture itself.
        var shadowAtlas = ShadowAtlas.GetAtlas();
        if (shadowAtlas != null)
        {
            cmd.SetGlobalTexture("_ShadowAtlas", shadowAtlas.InternalDepth);
            cmd.SetGlobalVector("_ShadowAtlasSize", new Float2(shadowAtlas.Width, shadowAtlas.Height));
        }
    }

    public void Dispose()
    {
        _staticTex.Dispose();
        _dynamicTex.Dispose();
        _membership.Clear();
        _seenThisFrame.Clear();
        _shadowCasters.Clear();
        _previousCasters.Clear();
        _directional = null;
        _extraDirectionals.Clear();
    }
}
