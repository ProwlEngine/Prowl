// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// glTF cameras and KHR_lights_punctual. Both are node-attached with no geometry, so what has to
/// survive is the lens or emitter data plus the node that positions it.
/// </summary>
public sealed class GltfCameraLightTests
{
    private static Model Load(string body, ModelImporterSettings? settings = null)
    {
        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          {{body}}
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ModelImporter.Load(stream, "gltf", settings ?? ModelImporterSettings.Raw);
    }

    // ---------------------------------------------------------------- cameras

    [Fact]
    public void PerspectiveCamera_CarriesItsLens()
    {
        var model = Load("""
        "nodes": [ { "name": "Cam", "camera": 0 } ],
        "cameras": [ {
          "name": "Main", "type": "perspective",
          "perspective": { "yfov": 0.7853981634, "aspectRatio": 1.7777, "znear": 0.1, "zfar": 250.0 }
        } ]
        """);

        var camera = Assert.Single(model.Cameras);
        Assert.Equal("Main", camera.Name);
        Assert.Equal(CameraProjection.Perspective, camera.Projection);
        Assert.Equal(0.7853981634f, camera.VerticalFovRadians, 5);
        Assert.Equal(1.7777f, camera.AspectRatio!.Value, 4);
        Assert.Equal(0.1f, camera.NearPlane, 5);
        Assert.Equal(250f, camera.FarPlane!.Value, 3);

        var node = Assert.Single(model.Nodes, n => n.CameraIndex >= 0);
        Assert.Equal("Cam", node.Name);
        Assert.Equal(0, node.CameraIndex);
    }

    // An omitted zfar is an infinite projection, which has to stay distinguishable from a large one.
    [Fact]
    public void PerspectiveCamera_WithoutFarPlane_ReportsInfinite()
    {
        var model = Load("""
        "nodes": [ { "camera": 0 } ],
        "cameras": [ { "type": "perspective", "perspective": { "yfov": 1.0, "znear": 0.1 } } ]
        """);

        Assert.Null(Assert.Single(model.Cameras).FarPlane);
    }

    [Fact]
    public void OrthographicCamera_CarriesItsMagnification()
    {
        var model = Load("""
        "nodes": [ { "camera": 0 } ],
        "cameras": [ {
          "type": "orthographic",
          "orthographic": { "xmag": 3.0, "ymag": 2.0, "znear": 0.5, "zfar": 90.0 }
        } ]
        """);

        var camera = Assert.Single(model.Cameras);
        Assert.Equal(CameraProjection.Orthographic, camera.Projection);
        Assert.Equal(3f, camera.OrthographicHalfWidth, 4);
        Assert.Equal(2f, camera.OrthographicHalfHeight, 4);
        Assert.Equal(0.5f, camera.NearPlane, 4);
        Assert.Equal(90f, camera.FarPlane!.Value, 3);
    }

    // A near plane of zero makes the projection singular. Exporters write it anyway.
    [Fact]
    public void ZeroNearPlane_IsClamped()
    {
        var model = Load("""
        "nodes": [ { "camera": 0 } ],
        "cameras": [ { "type": "perspective", "perspective": { "yfov": 1.0, "znear": 0.0 } } ]
        """);

        Assert.True(Assert.Single(model.Cameras).NearPlane > 0f);
    }

    // ---------------------------------------------------------------- lights

    private const string LightExt = "\"extensionsUsed\": [ \"KHR_lights_punctual\" ],";

    [Fact]
    public void DirectionalLight_CarriesColourAndIntensity()
    {
        var model = Load($$"""
        {{LightExt}}
        "nodes": [ { "name": "Sun", "extensions": { "KHR_lights_punctual": { "light": 0 } } } ],
        "extensions": { "KHR_lights_punctual": { "lights": [
          { "name": "Sun", "type": "directional", "color": [ 1.0, 0.95, 0.8 ], "intensity": 3.0 }
        ] } }
        """);

        var light = Assert.Single(model.Lights);
        Assert.Equal("Sun", light.Name);
        Assert.Equal(LightType.Directional, light.Type);
        Assert.Equal(1.0f, light.Color.R, 3);
        Assert.Equal(0.95f, light.Color.G, 3);
        Assert.Equal(0.8f, light.Color.B, 3);
        Assert.Equal(3.0f, light.Intensity, 3);
        // A directional light is infinitely distant, so a range would be meaningless.
        Assert.Null(light.Range);

        var node = Assert.Single(model.Nodes, n => n.LightIndex >= 0);
        Assert.Equal("Sun", node.Name);
    }

    [Fact]
    public void PointLight_CarriesRange()
    {
        var model = Load($$"""
        {{LightExt}}
        "nodes": [ { "extensions": { "KHR_lights_punctual": { "light": 0 } } } ],
        "extensions": { "KHR_lights_punctual": { "lights": [
          { "type": "point", "intensity": 40.0, "range": 12.5 }
        ] } }
        """);

        var light = Assert.Single(model.Lights);
        Assert.Equal(LightType.Point, light.Type);
        Assert.Equal(12.5f, light.Range!.Value, 3);
    }

    [Fact]
    public void PointLight_WithoutRange_ReportsUnlimited()
    {
        var model = Load($$"""
        {{LightExt}}
        "nodes": [ { "extensions": { "KHR_lights_punctual": { "light": 0 } } } ],
        "extensions": { "KHR_lights_punctual": { "lights": [ { "type": "point" } ] } }
        """);

        Assert.Null(Assert.Single(model.Lights).Range);
    }

    [Fact]
    public void SpotLight_CarriesConeAngles()
    {
        var model = Load($$"""
        {{LightExt}}
        "nodes": [ { "extensions": { "KHR_lights_punctual": { "light": 0 } } } ],
        "extensions": { "KHR_lights_punctual": { "lights": [
          { "type": "spot", "spot": { "innerConeAngle": 0.2, "outerConeAngle": 0.6 } }
        ] } }
        """);

        var light = Assert.Single(model.Lights);
        Assert.Equal(LightType.Spot, light.Type);
        Assert.Equal(0.2f, light.InnerConeAngleRadians, 4);
        Assert.Equal(0.6f, light.OuterConeAngleRadians, 4);
    }

    // An outer cone no wider than the inner leaves an empty or inverted falloff band.
    [Fact]
    public void SpotLight_WithInvertedCone_IsCorrected()
    {
        var model = Load($$"""
        {{LightExt}}
        "nodes": [ { "extensions": { "KHR_lights_punctual": { "light": 0 } } } ],
        "extensions": { "KHR_lights_punctual": { "lights": [
          { "type": "spot", "spot": { "innerConeAngle": 0.9, "outerConeAngle": 0.5 } }
        ] } }
        """);

        var light = Assert.Single(model.Lights);
        Assert.True(light.InnerConeAngleRadians < light.OuterConeAngleRadians);
    }

    [Fact]
    public void SpotLight_WithoutAngles_UsesSpecDefaults()
    {
        var model = Load($$"""
        {{LightExt}}
        "nodes": [ { "extensions": { "KHR_lights_punctual": { "light": 0 } } } ],
        "extensions": { "KHR_lights_punctual": { "lights": [ { "type": "spot" } ] } }
        """);

        var light = Assert.Single(model.Lights);
        Assert.Equal(0f, light.InnerConeAngleRadians, 4);
        Assert.Equal(MathF.PI / 4f, light.OuterConeAngleRadians, 4);
    }

    [Fact]
    public void OutOfRangeReferences_AreDroppedNotThrown()
    {
        var model = Load($$"""
        {{LightExt}}
        "nodes": [ { "camera": 7, "extensions": { "KHR_lights_punctual": { "light": 7 } } } ],
        "cameras": [ { "type": "perspective", "perspective": { "yfov": 1.0, "znear": 0.1 } } ],
        "extensions": { "KHR_lights_punctual": { "lights": [ { "type": "point" } ] } }
        """);

        Assert.All(model.Nodes, n => Assert.Equal(-1, n.CameraIndex));
        Assert.All(model.Nodes, n => Assert.Equal(-1, n.LightIndex));
        Assert.Equal(2, model.Log.Entries.Count(e => e.Severity == ImportLogSeverity.Warning));
    }

    /// <summary>
    /// A camera or light node carries no geometry, so the graph optimiser reads it as a
    /// pass-through unless told otherwise, and folding it away takes the transform that positioned
    /// it with it.
    /// </summary>
    [Fact]
    public void OptimizeGraph_KeepsCameraAndLightNodes()
    {
        var model = Load($$"""
        {{LightExt}}
        "nodes": [
          { "name": "Rig", "children": [ 1, 2 ] },
          { "name": "Cam", "camera": 0, "translation": [ 0, 2, -5 ] },
          { "name": "Key", "extensions": { "KHR_lights_punctual": { "light": 0 } } }
        ],
        "cameras": [ { "type": "perspective", "perspective": { "yfov": 1.0, "znear": 0.1 } } ],
        "extensions": { "KHR_lights_punctual": { "lights": [ { "type": "point" } ] } }
        """, ModelImporterSettings.EditorMaxQuality);

        Assert.Contains(model.Nodes, n => n.Name == "Cam" && n.CameraIndex == 0);
        Assert.Contains(model.Nodes, n => n.Name == "Key" && n.LightIndex == 0);
    }

    [Fact]
    public void ModelWithoutCamerasOrLights_HasEmptyLists()
    {
        var model = Load("\"nodes\": [ { \"name\": \"Empty\" } ]");

        Assert.Empty(model.Cameras);
        Assert.Empty(model.Lights);
        Assert.All(model.Nodes, n =>
        {
            Assert.Equal(-1, n.CameraIndex);
            Assert.Equal(-1, n.LightIndex);
        });
    }
}
