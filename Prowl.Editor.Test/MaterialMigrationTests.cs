// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Migration.Releases;
using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

public class MaterialMigrationTests
{
    private static string Guid(string path) => BuiltInAssets.DeterministicGuid(path).ToString();

    private static EchoObject Asset(string guid)
    {
        EchoObject reference = EchoObject.NewCompound();
        reference["$asset"] = new EchoObject(guid);
        return reference;
    }

    // A material as the GL era saved it: no overrides list, every slot filled, built in stand ins where nothing was set
    private static EchoObject OldMaterial(string shader, string? emission = null, string? surface = null)
    {
        EchoObject textures = EchoObject.NewCompound();
        textures["_MainTex"] = Asset(Guid("$Default:Texture/White"));
        textures["_NormalTex"] = Asset(Guid("$Default:Texture/Normal"));
        textures["_SurfaceTex"] = Asset(surface ?? Guid("$Default:Texture/Surface"));
        textures["_EmissionTex"] = Asset(emission ?? Guid("$Default:Texture/Emission"));

        EchoObject floats = EchoObject.NewCompound();
        floats["_Metallic"] = new EchoObject(1f);
        floats["_Roughness"] = new EchoObject(0.4f);

        EchoObject properties = EchoObject.NewCompound();
        properties["$id"] = new EchoObject(EchoType.Int, 2);
        properties["_textures"] = textures;
        properties["_floats"] = floats;

        EchoObject material = EchoObject.NewCompound();
        material["_shader"] = Asset(Guid($"$Default:Shader/{shader}"));
        material["_properties"] = properties;
        return material;
    }

    private static List<string> Overrides(EchoObject material) => [.. material["_overrides"].List.Select(o => o.StringValue)];

    [Fact]
    public void AStandardVariant_BecomesLitWithItsSurface_AndDropsStandIns()
    {
        EchoObject material = OldMaterial("StandardCutoutDoubleSided");

        Assert.NotNull(Release_1_0_Preview5.ConvertLegacyMaterials(material));

        Assert.Equal(Guid("$Default:Shader/Lit"), material["_shader"]["$asset"].StringValue);
        Assert.Equal(1L, material["_surface"]["Type"].LongValue);
        Assert.Equal(2L, material["_surface"]["Cull"].LongValue);
        EchoObject properties = material["_properties"];
        Assert.Empty(properties["_textures"].GetNames());
        Assert.False(properties["_floats"].Contains("_Metallic"));
        Assert.Equal(0.4f, properties["_floats"]["_Roughness"].FloatValue);
        Assert.Equal(["_Roughness"], Overrides(material));
    }

    [Fact]
    public void RealMaps_KeepMetallicAndTurnEmissionOn()
    {
        string map = System.Guid.NewGuid().ToString();
        EchoObject material = OldMaterial("Standard", emission: map, surface: map);

        Release_1_0_Preview5.ConvertLegacyMaterials(material);

        EchoObject properties = material["_properties"];
        Assert.Equal(1f, properties["_floats"]["_Metallic"].FloatValue);
        Assert.Equal(1, properties["_ints"]["_Emission"].IntValue);
        Assert.Equal(["_EmissionTex", "_SurfaceTex"], properties["_textures"].GetNames().Order());
        Assert.Contains("_Emission", Overrides(material));
        Assert.Equal(0L, material["_surface"]["Type"].LongValue);
    }

    [Fact]
    public void UnlitTransparent_BecomesUnlit_AndAnisotropicKeepsItsModel()
    {
        EchoObject unlit = OldMaterial("UnlitTransparent");
        EchoObject aniso = OldMaterial("StandardAnisotropic");

        Release_1_0_Preview5.ConvertLegacyMaterials(unlit);
        Release_1_0_Preview5.ConvertLegacyMaterials(aniso);

        Assert.Equal(Guid("$Default:Shader/Unlit"), unlit["_shader"]["$asset"].StringValue);
        Assert.Equal(2L, unlit["_surface"]["Type"].LongValue);
        Assert.Equal(0.5f, aniso["_properties"]["_floats"]["_Anisotropy"].FloatValue);
    }

    [Fact]
    public void MaterialsInsideScenes_AreFound_AndASecondRunChangesNothing()
    {
        EchoObject scene = EchoObject.NewCompound();
        EchoObject list = EchoObject.NewList();
        list.ListAdd(OldMaterial("StandardTransparent"));
        scene["Embedded"] = list;

        Assert.NotNull(Release_1_0_Preview5.ConvertLegacyMaterials(scene));
        Assert.Equal(Guid("$Default:Shader/Lit"), scene["Embedded"].List[0]["_shader"]["$asset"].StringValue);
        Assert.Null(Release_1_0_Preview5.ConvertLegacyMaterials(scene));
    }
}
