// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text.Json;

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// Maps glTF <c>cameras</c> and the <c>KHR_lights_punctual</c> light list into the intermediate
/// scene. Both are node-attached lenses with no geometry of their own: the node supplies the
/// transform, these supply everything else.
/// </summary>
internal static class GltfCameraLightMapper
{
    /// <summary>Extension key carrying both the document's light list and a node's reference into it.</summary>
    public const string LightsExtension = "KHR_lights_punctual";

    public static void MapCameras(GltfDom dom, IntermediateScene scene, ImportContext ctx)
    {
        if (dom.Cameras is null) return;

        foreach (var src in dom.Cameras)
        {
            var camera = new IntermediateCamera { Name = src.Name };

            if (src.Type == "orthographic" && src.Orthographic is { } ortho)
            {
                camera.Projection = CameraProjection.Orthographic;
                camera.OrthographicHalfWidth = ortho.XMag;
                camera.OrthographicHalfHeight = ortho.YMag;
                camera.NearPlane = ortho.ZNear;
                camera.FarPlane = ortho.ZFar;
            }
            else if (src.Perspective is { } persp)
            {
                camera.Projection = CameraProjection.Perspective;
                camera.VerticalFovRadians = persp.YFov;
                camera.AspectRatio = persp.AspectRatio;
                camera.NearPlane = persp.ZNear;
                camera.FarPlane = persp.ZFar;
            }
            else
            {
                ctx.Log.Warning(
                    $"Camera '{src.Name ?? "(unnamed)"}' declares type '{src.Type}' with no matching " +
                    "parameter block; substituting a default perspective lens.",
                    "GltfCameraLightMapper");
                camera.Projection = CameraProjection.Perspective;
                camera.VerticalFovRadians = MathF.PI / 3f;
            }

            if (!(camera.NearPlane > 0f))
            {
                // A near plane of zero makes the projection singular, and the spec requires it be
                // greater than zero, but exporters do write it.
                ctx.Log.Warning(
                    $"Camera '{src.Name ?? "(unnamed)"}' has a near plane of {camera.NearPlane}; " +
                    "clamped to 0.01 so the projection stays finite.",
                    "GltfCameraLightMapper");
                camera.NearPlane = 0.01f;
            }

            scene.Cameras.Add(camera);
        }
    }

    /// <summary>
    /// Reads the document-level light list. Returns the number of lights found so the node mapper
    /// can range check its references.
    /// </summary>
    public static void MapLights(GltfDom dom, IntermediateScene scene, ImportContext ctx)
    {
        if (dom.Extensions is null) return;
        if (!dom.Extensions.TryGetValue(LightsExtension, out JsonElement ext)) return;

        GltfPunctualLights? parsed;
        try
        {
            parsed = ext.Deserialize<GltfPunctualLights>(JsonOptions);
        }
        catch (JsonException e)
        {
            ctx.Log.Warning($"Could not read the {LightsExtension} light list: {e.Message}", "GltfCameraLightMapper");
            return;
        }

        if (parsed?.Lights is null) return;

        foreach (var src in parsed.Lights)
        {
            var light = new IntermediateLight
            {
                Name = src.Name,
                Intensity = src.Intensity ?? 1f,
                Range = src.Range,
            };

            if (src.Color is { Length: >= 3 } c)
                light.Color = new Color(c[0], c[1], c[2], 1f);

            switch (src.Type)
            {
                case "directional":
                    light.Type = LightType.Directional;
                    // A directional light is infinitely distant, so a range would be meaningless.
                    light.Range = null;
                    break;
                case "spot":
                    light.Type = LightType.Spot;
                    light.InnerConeAngleRadians = src.Spot?.InnerConeAngle ?? 0f;
                    light.OuterConeAngleRadians = src.Spot?.OuterConeAngle ?? (MathF.PI / 4f);
                    if (light.OuterConeAngleRadians <= light.InnerConeAngleRadians)
                    {
                        ctx.Log.Warning(
                            $"Spot light '{src.Name ?? "(unnamed)"}' has an outer cone angle that is not " +
                            "wider than its inner; the falloff band would be empty or inverted, so the inner " +
                            "angle was pulled in.",
                            "GltfCameraLightMapper");
                        light.InnerConeAngleRadians = light.OuterConeAngleRadians * 0.8f;
                    }
                    break;
                case "point":
                    light.Type = LightType.Point;
                    break;
                default:
                    ctx.Log.Warning(
                        $"Light '{src.Name ?? "(unnamed)"}' has unknown type '{src.Type}'; treated as a point light.",
                        "GltfCameraLightMapper");
                    light.Type = LightType.Point;
                    break;
            }

            scene.Lights.Add(light);
        }
    }

    /// <summary>Reads a node's <c>KHR_lights_punctual</c> reference, or -1 when it has none.</summary>
    public static int ReadNodeLight(GltfNode node, int lightCount, ImportContext ctx)
    {
        if (node.Extensions is null) return -1;
        if (!node.Extensions.TryGetValue(LightsExtension, out JsonElement ext)) return -1;

        int index;
        try
        {
            index = ext.Deserialize<GltfNodeLightRef>(JsonOptions)?.Light ?? -1;
        }
        catch (JsonException)
        {
            return -1;
        }

        if (index < 0) return -1;

        if (index >= lightCount)
        {
            ctx.Log.Warning(
                $"Node '{node.Name ?? "(unnamed)"}' references light {index}, but the file declares {lightCount}.",
                "GltfCameraLightMapper");
            return -1;
        }

        return index;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
}
