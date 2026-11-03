// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Lightmapping;
using Prowl.Photonic;
using Prowl.Runtime;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

public class LightmapBakeServiceTests
{
    private static T NewLight<T>(float intensity) where T : Light, new()
    {
        T light = new GameObject("Light").AddComponent<T>();
        light.UseTemperature = false;
        light.Color = Color.White;
        light.Intensity = intensity;
        return light;
    }

    [Fact]
    public void BakeColor_IsTheLightsLuxOrCandelaOverPi()
    {
        Assert.Equal(100000f / MathF.PI, LightmapBakeService.BakeColor(NewLight<DirectionalLight>(100000f)).X, 2);
        Assert.Equal(70f / MathF.PI, LightmapBakeService.BakeColor(NewLight<PointLight>(70f)).Y, 4);

        RectLight rect = NewLight<RectLight>(300f);
        rect.Width = 2f;
        rect.Height = 0.5f;
        Assert.Equal(300f / MathF.PI, LightmapBakeService.BakeColor(rect).Z, 3);
    }

    [Fact]
    public void BakedPages_HoldPhysicalIrradianceOverPi_ThroughTheirRgbmScale()
    {
        using var baker = new LightmapBaker();
        baker.Options.Bounces = 0;
        baker.Options.DilatePixels = 0;
        BakeScene bake = baker.BeginScene("Floor");

        // A square metre of floor facing up, a 1000 candela point light two metres over its centre
        Float3[] positions = [new(-0.5f, 0f, -0.5f), new(0.5f, 0f, -0.5f), new(0.5f, 0f, 0.5f), new(-0.5f, 0f, 0.5f)];
        Float2[] uvs = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)];
        bake.CreateMaterial("floor");
        BakeMesh floor = bake.BeginMesh("floor")
            .AddVertices(positions, [Float3.UnitY, Float3.UnitY, Float3.UnitY, Float3.UnitY])
            .AddUVLayer("UV0", uvs)
            .AddUVLayer("UV1", uvs)
            .AddMaterialGroup("floor", [0, 2, 1, 0, 3, 2])
            .End();

        PointLight light = NewLight<PointLight>(1000f);
        light.Range = 100f;
        light.BakeMode = LightBakeMode.Baked;
        light.Transform.Position = new Float3(0f, 2f, 0f);
        LightmapBakeService.AddLight(bake, light);
        bake.End();

        LightmapTarget target = baker.CreateTextureTarget("page", 16, 16);
        target.AddBakeInstance(floor, Float4x4.Identity);
        baker.Start().RunIterations(1);
        baker.Cancel();
        baker.Job!.Wait();

        float peak = 0f;
        int brightest = 0;
        ReadOnlySpan<float> pixels = target.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] > peak) { peak = pixels[i]; brightest = i; }
        }
        float underLight = 1000f / 4f / MathF.PI;
        Assert.InRange(peak, underLight * 0.97f, underLight * 1.001f);

        (byte[][] pages, float scale) = LightmapBakeService.EncodePages([target]);
        int texel = brightest / 3 * 4;
        byte[] page = pages[0];
        float decoded = page[texel + brightest % 3] / 255f * (page[texel + 3] / 255f * 8f) * scale;
        Assert.InRange(decoded, peak * 0.99f, peak * 1.01f);
    }
}
