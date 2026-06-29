// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;

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
/// One directional light is treated specially: it never enters either BVH and is uploaded via
/// the existing cascade-shadow uniform path on the render pipeline.
/// </para>
///
/// <para>
/// A bounded number of point + spot lights win shadow atlas slots each frame, picked by camera
/// distance. Lights that miss the cut still light surfaces; they just sample as unshadowed.
/// </para>
/// </summary>
public sealed class SceneLightSystem : IDisposable
{
    /// <summary>How many local lights (point + spot combined) can have shadow atlas slots in a
    /// single frame. The atlas budget makes this small in practice; bump alongside the matching
    /// uniform-array sizes in <c>Lighting.glsl</c> if you raise it.</summary>
    public const int MaxShadowCasters = 4;

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
    private readonly List<IRenderableLight> _shadowCasters = new();

    public LightBVH StaticBVH => _staticBVH;
    public LightBVH DynamicBVH => _dynamicBVH;
    public LightBVHTextures StaticTextures => _staticTex;
    public LightBVHTextures DynamicTextures => _dynamicTex;

    /// <summary>The directional light selected this frame, or null. Render pipeline takes this
    /// for cascade shadows + the directional uniform slot.</summary>
    public IRenderableLight Directional => _directional;

    /// <summary>Lights that won shadow atlas slots this frame, in order. The pipeline calls
    /// <c>RenderShadows</c> on each.</summary>
    public IReadOnlyList<IRenderableLight> ShadowCasters => _shadowCasters;

    private enum Membership { Static, Dynamic }

    /// <summary>
    /// Walk this frame's lights, register / unregister with the appropriate BVH, refit dynamics,
    /// pick the directional + closest-N shadow casters, and upload only the dirty rows of each
    /// texture. Cheap when nothing changed.
    ///
    /// <para>
    /// Note on <paramref name="cullingMask"/>: per-camera light filtering by layer is not
    /// applied here. The BVH is a per-scene structure, shared between cameras; per-camera
    /// layer filtering would require either a separate BVH per camera (expensive) or per-leaf
    /// layer bits checked in the shader (not yet implemented). For now every light in the scene
    /// affects every camera. The argument is kept for forward compatibility.
    /// </para>
    /// </summary>
    public void Reconcile(IReadOnlyList<IRenderableLight> lights, Float3 cameraPos, LayerMask cullingMask)
    {
        _ = cullingMask; // see remark above
        _seenThisFrame.Clear();
        _directional = null;
        _shadowCasters.Clear();

        IRenderableLight bestDirectional = null;

        var localCandidates = new List<(IRenderableLight light, float distSq, bool wantsShadow)>();

        for (int i = 0; i < lights.Count; i++)
        {
            var light = lights[i];
            if (light == null) continue;

            // Fully-baked lights live entirely in the lightmap + probes excluded from the realtime
            // set. (Mixed lights stay realtime — only their indirect bounce is baked.)
            if (light is Light bakedLight && bakedLight.BakeMode == LightBakeMode.Baked)
                continue;

            if (light.GetLightType() == LightType.Directional)
            {
                bestDirectional ??= light;
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
                else if (current == Membership.Dynamic)
                {
                    // Refit / topology check happens inside Update; unchanged data is a no-op.
                    _dynamicBVH.Update(light, in data);
                }
                // Static + still static: don't update. The BVH should only change on add/remove.
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
                float dSq = (float)Float3.DistanceSquared(cameraPos, light.GetLightPosition());
                localCandidates.Add((light, dSq, true));
            }
        }

        _directional = bestDirectional;

        // Second pass: drop registrations for lights that didn't show up.
        // Iterate over a snapshot since we mutate _membership inside the loop.
        if (_membership.Count > _seenThisFrame.Count)
        {
            var toRemove = new List<IRenderableLight>();
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
        localCandidates.Sort((a, b) => a.distSq.CompareTo(b.distSq));

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
        // Clear stale slots on lights that were shadow casters last frame but aren't now.
        for (int i = casterCount; i < localCandidates.Count; i++)
        {
            var l = localCandidates[i].light;
            if (_membership.TryGetValue(l, out var m))
                (m == Membership.Static ? _staticBVH : _dynamicBVH).SetShadowSlot(l, -1);
        }

        // Build / refit and upload. Static rebuild happens only when add/remove/transition fired
        // above; dynamic rebuilds when refit invariant breaks.
        _staticBVH.Sync();
        _dynamicBVH.Sync();
        _staticTex.Sync(_staticBVH);
        _dynamicTex.Sync(_dynamicBVH);
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
    public void RenderShadows(RenderPipeline pipeline, Float3 cameraPosition, IReadOnlyList<IRenderable> renderables)
    {
        // Each light manages its own CommandBuffer(s) internally point lights submit
        // one per face, directional submits one per cascade, spot submits a single CB
        // so per-face matrix uploads via AssignCameraMatrices are ordered correctly
        // against that face's draws.
        if (_directional is Light dl)
            dl.RenderShadows(pipeline, cameraPosition, renderables);

        for (int i = 0; i < _shadowCasters.Count; i++)
        {
            if (_shadowCasters[i] is Light sc)
                sc.RenderShadows(pipeline, cameraPosition, renderables);
        }
    }

    /// <summary>
    /// Upload all uniforms touched by <c>Lighting.glsl</c> and <c>LightBVH.glsl</c>: the four
    /// BVH textures, the directional light slot, the cascade shadow data, and the shadow atlas
    /// arrays for the selected closest-N point + spot lights. Call after <see cref="Reconcile"/>
    /// and <see cref="RenderShadows"/>, before any forward draws.
    /// </summary>
    public void UploadGlobalUniforms()
    {
        // All of these are global-uniform writes. Routing each through its own one-op
        // CommandBuffer (the PropertyState.SetGlobalX helpers) meant ~80-100 rent/submit
        // cycles per camera per frame. Encode them all into a single buffer and submit once.
        var cmd = Graphics.GetCommandBuffer("LightUniforms");
        UploadBVHTextures(cmd);
        UploadDirectionalLight(cmd);
        UploadLocalShadowSlots(cmd);
        Graphics.Submit(cmd);
    }

    private void UploadBVHTextures(CommandBuffer cmd)
    {
        PropertySet props = new();

        // Textures may be null when neither tree has been populated yet. Pass them through as-is;
        // the shader only samples them when its corresponding _*LightRoot >= 0 (which we set to
        // -1 here for empty trees). Sizes default to 0 and the root index to -1, both of which
        // make the traversal a no-op.
        if (_staticTex.LightDataTexture != null)
            props.SetTexture("_StaticLightData", _staticTex.LightDataTexture);
        if (_staticTex.NodeDataTexture != null)
            props.SetTexture("_StaticLightNodes", _staticTex.NodeDataTexture);
        if (_dynamicTex.LightDataTexture != null)
            props.SetTexture("_DynamicLightData", _dynamicTex.LightDataTexture);
        if (_dynamicTex.NodeDataTexture != null)
            props.SetTexture("_DynamicLightNodes", _dynamicTex.NodeDataTexture);

        props.SetInt("_StaticLightTexSize", _staticTex.LightTextureSize);
        props.SetInt("_StaticLightTexShift", Log2(_staticTex.LightTextureSize));
        props.SetInt("_StaticNodeTexSize", _staticTex.NodeTextureSize);
        props.SetInt("_StaticNodeTexShift", Log2(_staticTex.NodeTextureSize));
        props.SetInt("_DynamicLightTexSize", _dynamicTex.LightTextureSize);
        props.SetInt("_DynamicLightTexShift", Log2(_dynamicTex.LightTextureSize));
        props.SetInt("_DynamicNodeTexSize", _dynamicTex.NodeTextureSize);
        props.SetInt("_DynamicNodeTexShift", Log2(_dynamicTex.NodeTextureSize));

        props.SetInt("_StaticLightRoot", _staticTex.RootNode);
        props.SetInt("_DynamicLightRoot", _dynamicTex.RootNode);

        cmd.SetProperties(props);
    }

    /// <summary>log2 of a power of 2; returns 0 for size &lt;= 1.</summary>
    private static int Log2(int size)
    {
        int n = 0;
        while ((1 << n) < size) n++;
        return n;
    }

    private void UploadDirectionalLight(CommandBuffer cmd)
    {
        PropertySet props = new();

        if (_directional == null)
        {
            props.SetInt("_DirectionalLightEnabled", 0);
            props.SetVector("_DirectionalLightDirection", Float3.Zero);
            props.SetVector("_DirectionalLightColor", Float3.Zero);
            props.SetFloat("_DirectionalLightIntensity", 0f);
            props.SetInt("_DirectionalLightShadowEnabled", 0);
            props.SetInt("_CascadeCount", 0);
            return;
        }

        var data = _directional.GetForwardLightData();
        // Direct lighting wants the raw direction; intensity applies the same * 8 scaling the
        // legacy ForwardLightManager did so existing scenes look identical at low light counts.
        props.SetInt("_DirectionalLightEnabled", 1);
        props.SetVector("_DirectionalLightDirection", data.Direction);
        props.SetVector("_DirectionalLightColor", data.Color);
        props.SetFloat("_DirectionalLightIntensity", data.Intensity);
        props.SetInt("_DirectionalLightShadowEnabled", data.ShadowEnabled ? 1 : 0);
        props.SetFloat("_DirectionalLightShadowBias", data.ShadowBias);
        props.SetFloat("_DirectionalLightShadowNormalBias", data.ShadowNormalBias);
        props.SetFloat("_DirectionalLightShadowStrength", data.ShadowStrength);
        props.SetFloat("_DirectionalLightShadowQuality", data.ShadowQuality);

        props.SetInt("_CascadeCount", data.ShadowEnabled ? data.CascadeCount : 0);
        if (data.CascadeShadowMatrices != null && data.CascadeAtlasParams != null)
        {
            for (int c = 0; c < 4; c++)
            {
                props.SetMatrix($"_CascadeShadowMatrix{c}",
                    c < data.CascadeCount ? data.CascadeShadowMatrices[c] : Float4x4.Identity);
                props.SetVector($"_CascadeAtlasParams{c}",
                    c < data.CascadeCount ? data.CascadeAtlasParams[c] : Float4.Zero);
            }
        }

        cmd.SetProperties(props);
    }

    // What occupied each shadow slot last frame (0 = empty, 1 = point, 2 = spot). Lets us clear
    // only the slots that actually held data instead of resetting all MaxShadowCasters*7 uniforms
    // every frame.
    private readonly int[] _slotKind = new int[MaxShadowCasters];

    private void UploadLocalShadowSlots(CommandBuffer cmd)
    {
        PropertySet props = new();

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
                    props.SetMatrix($"_PointShadowMatrices[{idx}]", Float4x4.Identity);
                    props.SetVector($"_PointShadowFaceParams[{idx}]", Float4.Zero);
                }
            }
            else if (_slotKind[i] == 2)
            {
                props.SetMatrix($"_SpotShadowMatrices[{i}]", Float4x4.Identity);
                props.SetVector($"_SpotShadowAtlasParams[{i}]", Float4.Zero);
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
                        props.SetMatrix($"_PointShadowMatrices[{idx}]", data.PointShadowMatrices[f]);
                        props.SetVector($"_PointShadowFaceParams[{idx}]", data.PointShadowFaceParams[f]);
                    }
                    _slotKind[slot] = 1;
                }
            }
            else if (data.Type == LightType.Spot)
            {
                props.SetMatrix($"_SpotShadowMatrices[{slot}]", data.SpotShadowMatrix);
                props.SetVector($"_SpotShadowAtlasParams[{slot}]", data.SpotShadowAtlasParams);
                _slotKind[slot] = 2;
            }
        }

        // Shadow atlas texture itself.
        var shadowAtlas = ShadowAtlas.GetAtlas();
        if (shadowAtlas != null)
        {
            props.SetTexture("_ShadowAtlas", shadowAtlas.InternalDepth);
            props.SetVector("_ShadowAtlasSize", new Float2(shadowAtlas.Width, shadowAtlas.Height));
        }

        cmd.SetProperties(props);
    }

    public void Dispose()
    {
        _staticTex.Dispose();
        _dynamicTex.Dispose();
        _membership.Clear();
        _seenThisFrame.Clear();
        _shadowCasters.Clear();
        _directional = null;
    }
}
