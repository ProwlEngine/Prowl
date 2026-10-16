// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Migration.Releases;
using Prowl.Runtime;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

public class LightMigrationTests
{
    private static EchoObject Component(EchoObject gameObject, string typePrefix)
        => gameObject["Components"].List.First(c => c["$type"].StringValue.StartsWith(typePrefix + ",", StringComparison.Ordinal));

    private static void Rename(EchoObject tag, string from, string to)
    {
        EchoObject value = tag[from];
        tag.Remove(from);
        tag[to] = value;
    }

    // A GameObject saved with one of the old light components, built from a Light saved today
    internal static EchoObject LegacyLightObject(string oldType, Action<Light> setup)
    {
        var go = new GameObject("Lamp");
        Light light = go.AddComponent<Light>();
        light.Type = oldType switch
        {
            "Prowl.Runtime.DirectionalLight" => LightType.Directional,
            "Prowl.Runtime.SpotLight" => LightType.Spot,
            _ => LightType.Point,
        };
        setup(light);
        EchoObject echo = Serializer.Serialize(go);
        MakeLegacy(echo);
        return echo;
    }

    private static readonly string[] s_oldTypes = ["Prowl.Runtime.DirectionalLight", "Prowl.Runtime.PointLight", "Prowl.Runtime.SpotLight"];

    /// <summary>Gives every Light in the tree the shape the old Directional, Point and Spot light types saved.</summary>
    internal static void MakeLegacy(EchoObject tag)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List) MakeLegacy(item);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;
        if (tag.TryGet("$type", out EchoObject? type) && type!.StringValue.StartsWith("Prowl.Runtime.Light,", StringComparison.Ordinal))
        {
            string oldType = s_oldTypes[tag["Type"].LongValue];
            tag["$type"] = new EchoObject(type.StringValue.Replace("Prowl.Runtime.Light,", oldType + ","));
            tag.Remove("Type");
            Rename(tag, "OuterAngle", "SpotAngle");
            Rename(tag, "InnerAngle", "InnerSpotAngle");
            Rename(tag, "ShadowFilter", "ShadowQuality");
        }
        foreach (string name in tag.GetNames().ToList()) MakeLegacy(tag[name]);
    }

    private static Light Migrate(EchoObject echo)
    {
        Dictionary<string, string> components = [];
        Release_1_0_Preview5.CollectLegacyComponents(echo, components);
        Assert.NotNull(Release_1_0_Preview5.ConvertLightsAndShadows(echo, components));
        return Serializer.Deserialize<GameObject>(echo)!.GetComponent<Light>()!;
    }

    [Fact]
    public void OldSpotLight_BecomesASpotLight_WithItsAngles()
    {
        EchoObject echo = LegacyLightObject("Prowl.Runtime.SpotLight", l =>
        {
            l.Range = 7f;
            l.OuterAngle = 40f;
            l.InnerAngle = 12f;
            l.Intensity = 3f;
            l.ShadowFilter = ShadowFilter.Hard;
            l.ShadowResolution = ShadowResolution._2048;
        });

        Light light = Migrate(echo);

        Assert.Equal(LightType.Spot, light.Type);
        Assert.Equal(7f, light.Range);
        Assert.Equal(40f, light.OuterAngle);
        Assert.Equal(12f, light.InnerAngle);
        Assert.Equal(3f, light.Intensity);
        Assert.Equal(ShadowFilter.Hard, light.ShadowFilter);
        Assert.Equal(ShadowResolution._2048, light.ShadowResolution);
    }

    [Theory]
    [InlineData("Prowl.Runtime.DirectionalLight", LightType.Directional)]
    [InlineData("Prowl.Runtime.PointLight", LightType.Point)]
    public void OldLights_KeepTheirKind(string oldType, LightType expected)
    {
        EchoObject echo = LegacyLightObject(oldType, l => { l.Cascades = ShadowCascades.Four; l.Color = Color.Red; });

        Light light = Migrate(echo);

        Assert.Equal(expected, light.Type);
        Assert.Equal(ShadowCascades.Four, light.Cascades);
        Assert.Equal(Color.Red, light.Color);
    }

    [Theory]
    [InlineData(false, ShadowCasting.Off)]
    [InlineData(true, ShadowCasting.On)]
    public void MeshRendererCastShadows_BecomesShadowCasting(bool cast, ShadowCasting expected)
    {
        var go = new GameObject("Box");
        go.AddComponent<MeshRenderer>();
        EchoObject echo = Serializer.Serialize(go);
        EchoObject renderer = Component(echo, "Prowl.Runtime.MeshRenderer");
        renderer.Remove("ShadowCasting");
        renderer["CastShadows"] = new EchoObject(cast);

        Assert.NotNull(Release_1_0_Preview5.ConvertLightsAndShadows(echo, new Dictionary<string, string>()));

        Assert.Equal(expected, Serializer.Deserialize<GameObject>(echo)!.GetComponent<MeshRenderer>()!.ShadowCasting);
    }

    [Fact]
    public void PrefabOverrides_OnOldFields_FollowTheRename()
    {
        EchoObject prefab = LegacyLightObject("Prowl.Runtime.SpotLight", _ => { });
        string lightId = Component(prefab, "Prowl.Runtime.SpotLight")["_identifier"].StringValue;
        var box = new GameObject("Box");
        box.AddComponent<MeshRenderer>();
        EchoObject boxEcho = Serializer.Serialize(box);
        string rendererId = Component(boxEcho, "Prowl.Runtime.MeshRenderer")["_identifier"].StringValue;
        string objectId = Guid.NewGuid().ToString();

        EchoObject Override(string path, EchoObject value)
        {
            EchoObject o = EchoObject.NewCompound();
            o["Path"] = new EchoObject(path);
            o["Value"] = value;
            return o;
        }
        EchoObject scene = EchoObject.NewCompound();
        EchoObject overrides = EchoObject.NewList();
        overrides.ListAdd(Override($"{objectId}/{lightId}/SpotAngle", new EchoObject(20f)));
        overrides.ListAdd(Override($"{objectId}/{lightId}/Color.R", new EchoObject(0.5f)));
        overrides.ListAdd(Override($"{objectId}/{rendererId}/CastShadows", new EchoObject(false)));
        scene["Overrides"] = overrides;

        Dictionary<string, string> components = [];
        Release_1_0_Preview5.CollectLegacyComponents(prefab, components);
        Release_1_0_Preview5.CollectLegacyComponents(boxEcho, components);
        Assert.NotNull(Release_1_0_Preview5.ConvertLightsAndShadows(scene, components));

        List<EchoObject> result = scene["Overrides"].List;
        Assert.Equal($"{objectId}/{lightId}/OuterAngle", result[0]["Path"].StringValue);
        Assert.Equal(20f, result[0]["Value"].FloatValue);
        Assert.Equal($"{objectId}/{lightId}/Color.R", result[1]["Path"].StringValue);
        Assert.Equal($"{objectId}/{rendererId}/ShadowCasting", result[2]["Path"].StringValue);
        Assert.Equal(0L, result[2]["Value"].LongValue);
    }

    [Fact]
    public void RunningTwice_ChangesNothingTheSecondTime()
    {
        EchoObject echo = LegacyLightObject("Prowl.Runtime.PointLight", _ => { });
        Dictionary<string, string> components = [];
        Release_1_0_Preview5.CollectLegacyComponents(echo, components);
        Assert.NotNull(Release_1_0_Preview5.ConvertLightsAndShadows(echo, components));

        Dictionary<string, string> again = [];
        Release_1_0_Preview5.CollectLegacyComponents(echo, again);
        Assert.Empty(again.Where(c => c.Value != "Prowl.Runtime.MeshRenderer"));
        Assert.Null(Release_1_0_Preview5.ConvertLightsAndShadows(echo, again));
    }
}
