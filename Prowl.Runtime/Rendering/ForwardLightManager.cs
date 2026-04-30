// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Shadow data exposed by a light for the forward pipeline.
/// </summary>
public struct ForwardLightData
{
    public LightType Type;
    public Float3 Position;
    public Float3 Direction;
    public Float3 Color;
    public float Intensity;
    public float Range;
    public float SpotAngle;       // degrees
    public float InnerSpotAngle;  // degrees

    // Shadow
    public bool ShadowEnabled;
    public float ShadowBias;
    public float ShadowNormalBias;
    public float ShadowStrength;
    public float ShadowQuality;   // 0 = Hard, 1 = Soft

    // Directional cascade data (only for LightType.Directional)
    public int CascadeCount;
    public Float4x4[] CascadeShadowMatrices; // [4]
    public Float4[] CascadeAtlasParams;      // [4]

    // Point shadow data (6 faces)
    public Float4x4[] PointShadowMatrices; // [6]
    public Float4[] PointShadowFaceParams; // [6]

    // Spot shadow data (1 matrix)
    public Float4x4 SpotShadowMatrix;
    public Float4 SpotShadowAtlasParams;
}

/// <summary>
/// Selects the most relevant lights for the frame and uploads their data
/// as global uniforms for forward rendering shaders.
/// </summary>
public static class ForwardLightManager
{
    private const int MaxLights = 8;
    private const int MaxShadowSlots = 2; // Max additional shadow-casting point/spot lights


    /// <summary>
    /// Select up to 8 lights and upload their uniform data for forward shaders.
    /// Directional light always goes to index 0. Point/spot sorted by distance to camera.
    /// </summary>
    public static void SelectAndUploadLights(
        Float3 cameraPosition,
        IReadOnlyList<IRenderableLight> lights,
        LayerMask cullingMask)
    {
        // Reset slot indices on every Light component up-front. The selected ones
        // get assigned below; everything else stays at -1 so consumers know they
        // weren't uploaded this frame.
        for (int i = 0; i < lights.Count; i++)
        {
            if (lights[i] is Light l)
            {
                l.ForwardSlot = -1;
                l.ShadowSlot = -1;
            }
        }

        // Separate directional from point/spot
        IRenderableLight? directional = null;
        var pointSpotLights = new List<(IRenderableLight light, float distSq)>();

        for (int i = 0; i < lights.Count; i++)
        {
            var light = lights[i];
            if (!cullingMask.HasLayer(light.GetLayer())) continue;

            if (light.GetLightType() == LightType.Directional)
            {
                directional ??= light; // Take first directional
            }
            else
            {
                float distSq = Float3.DistanceSquared(cameraPosition, light.GetLightPosition());
                pointSpotLights.Add((light, distSq));
            }
        }

        // Sort point/spot by distance (closest first)
        pointSpotLights.Sort((a, b) => a.distSq.CompareTo(b.distSq));

        // Build the light list: directional at 0, then closest point/spot
        var selected = new List<IRenderableLight>(MaxLights);
        if (directional != null)
            selected.Add(directional);

        int remaining = MaxLights - selected.Count;
        for (int i = 0; i < pointSpotLights.Count && i < remaining; i++)
            selected.Add(pointSpotLights[i].light);

        // Upload (also assigns ForwardSlot / ShadowSlot on each selected Light)
        UploadLightUniforms(selected);
    }

    private static void UploadLightUniforms(List<IRenderableLight> selected)
    {
        int count = selected.Count;
        PropertyState.SetGlobalInt("_LightCount", count);

        int pointShadowSlot = 0;
        int spotShadowSlot = 0;

        for (int i = 0; i < count; i++)
        {
            var light = selected[i];
            var data = light.GetForwardLightData();

            // Stamp the slot index back onto the Light component so other systems
            // (e.g. volumetric fog) can query "which uniform slot am I in" without
            // needing access to the render pipeline's selection list.
            if (light is Light lightComp) lightComp.ForwardSlot = i;

            int typeInt = data.Type switch
            {
                LightType.Directional => 0,
                LightType.Point => 1,
                LightType.Spot => 2,
                _ => 0
            };

            PropertyState.SetGlobalInt($"_LightType[{i}]", typeInt);
            PropertyState.SetGlobalVector($"_LightPositions[{i}]", data.Position);
            PropertyState.SetGlobalVector($"_LightDirections[{i}]", data.Direction);
            PropertyState.SetGlobalVector($"_LightColors[{i}]", data.Color);
            PropertyState.SetGlobalFloat($"_LightIntensities[{i}]", data.Intensity * 8f);
            PropertyState.SetGlobalFloat($"_LightRanges[{i}]", data.Range);
            PropertyState.SetGlobalFloat($"_LightSpotAngles[{i}]", data.SpotAngle);
            PropertyState.SetGlobalFloat($"_LightInnerSpotAngles[{i}]", data.InnerSpotAngle);

            // Shadow base data
            PropertyState.SetGlobalInt($"_LightShadowEnabled[{i}]", data.ShadowEnabled ? 1 : 0);
            PropertyState.SetGlobalFloat($"_LightShadowBias[{i}]", data.ShadowBias);
            PropertyState.SetGlobalFloat($"_LightShadowNormalBias[{i}]", data.ShadowNormalBias);
            PropertyState.SetGlobalFloat($"_LightShadowStrength[{i}]", data.ShadowStrength);
            PropertyState.SetGlobalFloat($"_LightShadowQuality[{i}]", data.ShadowQuality);
            PropertyState.SetGlobalInt($"_LightShadowSlot[{i}]", -1); // default no slot

            // Upload shadow-specific data
            if (data.Type == LightType.Directional && data.ShadowEnabled)
            {
                PropertyState.SetGlobalInt("_CascadeCount", data.CascadeCount);
                if (data.CascadeShadowMatrices != null && data.CascadeAtlasParams != null)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        PropertyState.SetGlobalMatrix($"_CascadeShadowMatrix{c}",
                            c < data.CascadeCount ? data.CascadeShadowMatrices[c] : Float4x4.Identity);
                        PropertyState.SetGlobalVector($"_CascadeAtlasParams{c}",
                            c < data.CascadeCount ? data.CascadeAtlasParams[c] : Float4.Zero);
                    }
                }
            }
            else if (data.Type == LightType.Point && data.ShadowEnabled && pointShadowSlot < MaxShadowSlots)
            {
                int slot = pointShadowSlot++;
                PropertyState.SetGlobalInt($"_LightShadowSlot[{i}]", slot);
                if (light is Light lc) lc.ShadowSlot = slot;

                if (data.PointShadowMatrices != null && data.PointShadowFaceParams != null)
                {
                    for (int f = 0; f < 6; f++)
                    {
                        int idx = slot * 6 + f;
                        PropertyState.SetGlobalMatrix($"_PointShadowMatrices[{idx}]", data.PointShadowMatrices[f]);
                        PropertyState.SetGlobalVector($"_PointShadowFaceParams[{idx}]", data.PointShadowFaceParams[f]);
                    }
                }
            }
            else if (data.Type == LightType.Spot && data.ShadowEnabled && spotShadowSlot < MaxShadowSlots)
            {
                int slot = spotShadowSlot++;
                PropertyState.SetGlobalInt($"_LightShadowSlot[{i}]", slot);
                if (light is Light lc) lc.ShadowSlot = slot;
                PropertyState.SetGlobalMatrix($"_SpotShadowMatrices[{slot}]", data.SpotShadowMatrix);
                PropertyState.SetGlobalVector($"_SpotShadowAtlasParams[{slot}]", data.SpotShadowAtlasParams);
            }
        }

        // Upload shadow atlas texture
        var shadowAtlas = ShadowAtlas.GetAtlas();
        if (shadowAtlas != null)
        {
            PropertyState.SetGlobalTexture("_ShadowAtlas", shadowAtlas.InternalDepth);
            PropertyState.SetGlobalVector("_ShadowAtlasSize", new Float2(shadowAtlas.Width, shadowAtlas.Height));
        }

        // Zero out unused light slots
        if (count == 0)
            PropertyState.SetGlobalInt("_CascadeCount", 0);
    }
}
