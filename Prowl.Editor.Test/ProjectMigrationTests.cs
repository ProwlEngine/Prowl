// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO.Compression;
using System.Text.Json.Nodes;

using Prowl.Echo;
using Prowl.Editor.Migration;
using Prowl.Editor.Migration.Releases;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.ParticleSystem;
using Prowl.Runtime.ParticleSystem.Modules;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

public class ProjectMigrationTests : IDisposable
{
    private readonly string _root;

    public ProjectMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ProwlMigrationTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Assets"));
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_root)!, true); } catch { }
        GC.SuppressFinalize(this);
    }

    private void WriteProwlFile(string? version, int? appliedSteps = null)
    {
        var data = new JsonObject { ["name"] = "Old", ["engine"] = "Prowl" };
        if (version != null) data["version"] = version;
        if (appliedSteps != null) data["appliedSteps"] = appliedSteps;
        File.WriteAllText(Path.Combine(_root, "Old.prowl"), data.ToJsonString());
    }

    private string WriteAsset(string name, string content, string importer)
    {
        string path = Path.Combine(_root, "Assets", name);
        File.WriteAllText(path, content);
        MetaFile.Write(path + ".meta", MetaFile.CreateNew(importer));
        return path;
    }

    // A component as an older release saved it, its settings under their old names
    private static EchoObject OldComponent(string type, Guid id, params (string Name, EchoObject Value)[] fields)
    {
        var component = EchoObject.NewCompound();
        component["$type"] = new EchoObject($"Prowl.Runtime.{type}, Prowl.Runtime");
        component["_identifier"] = new EchoObject(id.ToString());
        foreach ((string name, EchoObject value) in fields)
            component[name] = value;
        return component;
    }

    private static string Scene(params EchoObject[] items)
    {
        var list = EchoObject.NewList();
        foreach (EchoObject item in items)
            list.ListAdd(item);
        var root = EchoObject.NewCompound();
        root["Items"] = list;
        return root.WriteToString();
    }

    private static EchoObject Item(string path) => EchoObject.ReadFromString(File.ReadAllText(path))["Items"];

    [Theory]
    [InlineData("1.0-preview.4", "1.0-preview.5")]
    [InlineData("1.0-preview.5", "1.0")]
    [InlineData("1.0", "1.0a")]
    [InlineData("1.0a", "1.0b")]
    [InlineData("1.0z", "1.0aa")]
    [InlineData("1.0b", "1.1-preview.1")]
    [InlineData("1.1", "1.1a")]
    [InlineData("1.9", "1.10")]
    [InlineData("1.10a", "2.0")]
    [InlineData("", "0.1")]
    public void EngineVersion_Orders(string older, string newer)
    {
        EngineVersion a = EngineVersion.Parse(older), b = EngineVersion.Parse(newer);
        Assert.True(a < b);
        Assert.True(b > a);
    }

    [Theory]
    [InlineData("1.0-preview.5")]
    [InlineData("1.0")]
    [InlineData("1.1a")]
    [InlineData("2.3bc-preview.12")]
    public void EngineVersion_RoundTrips(string text) => Assert.Equal(text, EngineVersion.Parse(text).ToString());

    [Theory]
    [InlineData("1")]
    [InlineData("1.0.1")]
    [InlineData("1.0-preview.0")]
    [InlineData("1.0-beta.1")]
    [InlineData("v1.0")]
    public void EngineVersion_RejectsUnknownFormats(string text) => Assert.False(EngineVersion.TryParse(text, out _));

    [Fact]
    public void EngineVersion_ReadsLegacyPreviewNames() => Assert.Equal(EngineVersion.Parse("1.0-preview.5"), EngineVersion.Parse("preview-5"));

    [Fact]
    public void Releases_AreUniqueNotNewerThanCurrent_AndHaveSteps()
    {
        IReadOnlyList<MigrationRelease> releases = ProjectMigration.Releases;
        Assert.NotEmpty(releases);
        Assert.Equal(releases.Count, releases.Select(r => r.Version).Distinct().Count());
        foreach (MigrationRelease release in releases)
        {
            Assert.True(release.Version <= Project.CurrentVersion, $"{release.Name} is newer than {Project.CurrentVersion}");
            Assert.NotEmpty(release.Steps);
        }
    }

    [Fact]
    public void UnversionedProject_MigratesToCurrent()
    {
        string scene = WriteAsset("Main.scene", Scene(OldComponent("MeshRenderer", Guid.NewGuid(), ("ShadowCasting", new EchoObject(0L)))), "Prowl.Editor.Assets.SceneImporter");
        Directory.CreateDirectory(Path.Combine(_root, "Library", "cache"));
        File.WriteAllText(Path.Combine(_root, "Library", "cache", "stale.bin"), "x");

        Project project = Project.Open(_root);
        Assert.True(project.Version.IsUnknown);
        Assert.True(project.NeedsMigration);

        ProjectMigration.Migrate(project);

        EchoObject renderer = Item(scene)[0];
        Assert.Equal(0L, renderer["shadowCasting"].LongValue);
        Assert.False(renderer.TryGet("ShadowCasting", out _));
        Assert.False(File.Exists(Path.Combine(_root, "Library", "cache", "stale.bin")));

        using (ZipArchive backup = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(project.BackupsPath, "*.zip"))))
        {
            Assert.NotNull(backup.GetEntry("Assets/Main.scene"));
            Assert.Contains(backup.Entries, e => e.FullName.EndsWith(".prowl"));
        }

        Project reopened = Project.Open(_root);
        Assert.Equal(Project.CurrentVersion, reopened.Version);
        Assert.False(reopened.NeedsMigration);
    }

    [Fact]
    public void PartlyMigratedProject_ResumesAtTheNextStep()
    {
        string scene = WriteAsset("Main.scene", Scene(
            OldComponent("MeshRenderer", Guid.NewGuid(), ("ShadowCasting", new EchoObject(0L))),
            OldComponent("Light", Guid.NewGuid(), ("Intensity", new EchoObject(3f)))), "SceneImporter");
        WriteProwlFile("1.0", appliedSteps: 1);

        Project project = Project.Open(_root);
        List<(MigrationRelease Release, int Index)> pending = ProjectMigration.PendingSteps(project);
        Assert.Equal(1, pending[0].Index);
        Assert.All(pending, p => Assert.True(p.Index >= 1));

        ProjectMigration.Migrate(project);

        EchoObject items = Item(scene);
        Assert.True(items[0].TryGet("ShadowCasting", out _));
        Assert.Equal(3f * LegacyLightUnits.Light, items[1]["intensity"].FloatValue);
    }

    [Fact]
    public void PrefabOverridesOnRenamedSettings_FollowTheRename()
    {
        Guid light = Guid.NewGuid(), renderer = Guid.NewGuid(), unrelated = Guid.NewGuid();
        WriteAsset("Lamp.prefab", Scene(OldComponent("Light", light), OldComponent("MeshRenderer", renderer)), "PrefabImporter");
        EchoObject Override(string path)
        {
            var entry = EchoObject.NewCompound();
            entry["Path"] = new EchoObject(path);
            entry["Value"] = new EchoObject(1f);
            return entry;
        }
        string target = Guid.NewGuid().ToString();
        string scene = WriteAsset("Main.scene", Scene(
            Override($"{target}/{light}/Color.R"),
            Override($"{target}/{renderer}/ReceiveShadows"),
            Override($"{target}/{unrelated}/Color.R")), "SceneImporter");
        WriteProwlFile("1.0-preview.5");

        ProjectMigration.Migrate(Project.Open(_root));

        EchoObject items = Item(scene);
        Assert.Equal($"{target}/{light}/color.R", items[0]["Path"].StringValue);
        Assert.Equal($"{target}/{renderer}/receiveShadows", items[1]["Path"].StringValue);
        Assert.Equal($"{target}/{unrelated}/Color.R", items[2]["Path"].StringValue);
    }

    private static EchoObject Profile(params EchoObject[] components)
    {
        var values = EchoObject.NewList();
        foreach (EchoObject component in components)
            values.ListAdd(component);
        var list = EchoObject.NewCompound();
        list["$values"] = values;
        var profile = EchoObject.NewCompound();
        profile["Components"] = list;
        profile["Name"] = new EchoObject("Profile");
        return profile;
    }

    private static EchoObject VolumeComponent(string type, long? mode = null)
    {
        var component = EchoObject.NewCompound();
        component["$type"] = new EchoObject($"Prowl.Runtime.Rendering.{type}, Prowl.Runtime");
        if (mode is long value)
        {
            var parameter = EchoObject.NewCompound();
            parameter["Value"] = new EchoObject(value);
            parameter["Override"] = new EchoObject(true);
            component["Mode"] = parameter;
        }
        component["Active"] = new EchoObject(true);
        return component;
    }

    private static EchoObject[] ProfileComponents(EchoObject camera) => [.. camera["Profile"]["Components"]["$values"].List];

    private static long TonemappingMode(EchoObject camera)
        => ProfileComponents(camera).Single(c => c["$type"].StringValue.StartsWith("Prowl.Runtime.Rendering.Tonemapping,")).Get("Mode")!["Value"].LongValue;

    [Fact]
    public void CameraTonemapping_MovesIntoTheBaseProfile()
    {
        EchoObject sharedAsset = Profile();
        sharedAsset["$type"] = new EchoObject("Prowl.Runtime.Rendering.VolumeProfile, Prowl.Runtime");
        string profilePath = WriteAsset("Shared.volume", sharedAsset.WriteToString(), "CustomAssetImporter");
        Guid shared = MetaFile.Read(profilePath + ".meta").Guid;
        var sharedRef = EchoObject.NewCompound();
        sharedRef["$asset"] = new EchoObject(shared.ToString());

        string scene = WriteAsset("Main.scene", Scene(
            OldComponent("Camera", Guid.NewGuid(), ("Tonemapping", new EchoObject(0L))),
            OldComponent("Camera", Guid.NewGuid(), ("Tonemapping", new EchoObject(2L)), ("HDR", new EchoObject(false))),
            OldComponent("Camera", Guid.NewGuid(), ("Profile", Profile(VolumeComponent("Bloom")))),
            OldComponent("Camera", Guid.NewGuid(), ("Tonemapping", new EchoObject(3L)), ("Profile", Profile(VolumeComponent("Tonemapping", 5L)))),
            OldComponent("Camera", Guid.NewGuid(), ("Tonemapping", new EchoObject(5L)), ("Profile", sharedRef))), "SceneImporter");
        WriteProwlFile("1.0-preview.5");

        ProjectMigration.Migrate(Project.Open(_root));

        EchoObject[] cameras = [.. Item(scene).List];
        Assert.All(cameras, c => Assert.False(c.TryGet("Tonemapping", out _)));
        Assert.Equal(1L, TonemappingMode(cameras[0]));
        Assert.False(cameras[1].TryGet("Profile", out _));
        Assert.Equal(3L, TonemappingMode(cameras[2]));
        Assert.Equal(2, ProfileComponents(cameras[2]).Length);
        Assert.Equal(5L, TonemappingMode(cameras[3]));
        Assert.Single(ProfileComponents(cameras[3]));
        Assert.Equal(shared.ToString(), cameras[4]["Profile"]["$asset"].StringValue);

        EchoObject sharedProfile = EchoObject.ReadFromString(File.ReadAllText(profilePath));
        Assert.Equal(6L, Assert.Single(sharedProfile["Components"]["$values"].List).Get("Mode")!["Value"].LongValue);

        var profile = Serializer.Deserialize<Prowl.Runtime.Rendering.VolumeProfile>(cameras[0]["Profile"])!;
        var tonemapping = profile.Get<Prowl.Runtime.Rendering.Tonemapping>()!;
        Assert.Equal(Prowl.Runtime.Rendering.TonemappingMode.ACES, tonemapping.Mode.Value);
        Assert.True(tonemapping.Mode.Override);
    }

    [Fact]
    public void CameraTonemappingOverrides_AreDropped_AndTheStepCanRunAgain()
    {
        Guid camera = Guid.NewGuid();
        WriteAsset("Rig.prefab", Scene(OldComponent("Camera", camera, ("Tonemapping", new EchoObject(1L)))), "PrefabImporter");
        EchoObject Override(string path)
        {
            var entry = EchoObject.NewCompound();
            entry["Path"] = new EchoObject(path);
            entry["Value"] = new EchoObject(4L);
            return entry;
        }
        string target = Guid.NewGuid().ToString();
        string scene = WriteAsset("Main.scene", Scene(Override($"{target}/{camera}/Tonemapping"), Override($"{target}/{camera}/HDR")), "SceneImporter");
        WriteProwlFile("1.0-preview.5");

        ProjectMigration.Migrate(Project.Open(_root));
        Migration.Releases.Release_1_0.CameraTonemappingToProfile(new MigrationContext(Project.Open(_root)));

        Assert.Equal($"{target}/{camera}/HDR", Assert.Single(Item(scene).List)["Path"].StringValue);
        EchoObject rig = Item(Path.Combine(_root, "Assets", "Rig.prefab"))[0];
        Assert.Equal(2L, TonemappingMode(rig));
        Assert.Single(ProfileComponents(rig));
    }

    [Fact]
    public void LegacyPreviewProject_NeedsMigration()
    {
        WriteProwlFile("preview-5");
        Project project = Project.Open(_root);
        Assert.Equal(EngineVersion.Parse("1.0-preview.5"), project.Version);
        Assert.True(project.NeedsMigration);
        Assert.False(project.IsFromNewerEngine);
    }

    [Theory]
    [InlineData("99.0", null)]
    [InlineData("1.0", 99)]
    public void NewerProject_IsRefusedAndLeftAlone(string version, int? appliedSteps)
    {
        WriteProwlFile(version, appliedSteps);
        string before = File.ReadAllText(Path.Combine(_root, "Old.prowl"));

        Project project = Project.Open(_root);
        Assert.True(project.IsFromNewerEngine);
        Assert.False(project.NeedsMigration);
        Assert.Throws<InvalidOperationException>(() => ProjectMigration.Migrate(project));
        Assert.Equal(before, File.ReadAllText(Path.Combine(_root, "Old.prowl")));
    }

    [Fact]
    public void UnrecognizedVersion_FailsToOpen()
    {
        WriteProwlFile("banana");
        Assert.Throws<InvalidOperationException>(() => Project.Open(_root));
    }

    [Fact]
    public void TextureMetas_MoveMasksAndSpritesOntoTheNewTypes_AndKeepTheirSize()
    {
        string mask = WriteAsset("Mask.png", "", "TextureImporter");
        string sheet = WriteAsset("Sheet.png", "", "TextureImporter");
        string limited = WriteAsset("Limited.png", "", "TextureImporter");
        WriteSettings(mask, s => s["textureType"] = new EchoObject(2));
        WriteSettings(sheet, s => { EchoObject sprite = EchoObject.NewCompound(); sprite["Mode"] = new EchoObject(2L); s["sprite"] = sprite; });
        WriteSettings(limited, s => { s["textureType"] = new EchoObject(0); s["maxSize"] = new EchoObject(512); });

        ProjectMigration.Migrate(Project.Open(_root));

        EchoObject maskSettings = MetaFile.Read(mask + ".meta").Settings!;
        Assert.Equal(0, maskSettings["textureType"].IntValue);
        Assert.False(maskSettings["srgb"].BoolValue);
        Assert.Equal(16384, maskSettings["maxSize"].IntValue);
        Assert.Equal(3, MetaFile.Read(sheet + ".meta").Settings!["textureType"].IntValue);
        Assert.Equal(512, MetaFile.Read(limited + ".meta").Settings!["maxSize"].IntValue);

        EchoObject again = MetaFile.Read(mask + ".meta").Settings!;
        Assert.Null(Migration.Releases.Release_1_0.TextureImportTypes(mask, again));
    }

    [Fact]
    public void TextureMetas_WithoutSettings_AndLightmapPages_AreMigrated()
    {
        string bare = WriteAsset("Bare.png", "", "TextureImporter");
        Directory.CreateDirectory(Path.Combine(_root, "Assets", "Main_lightmaps"));
        string page = WriteAsset(Path.Combine("Main_lightmaps", "Lightmap-0.png"), "", "TextureImporter");
        MetaFileData meta = MetaFile.Read(bare + ".meta");
        meta.Settings = null;
        MetaFile.Write(bare + ".meta", meta);

        ProjectMigration.Migrate(Project.Open(_root));

        Assert.Equal(16384, MetaFile.Read(bare + ".meta").Settings!["maxSize"].IntValue);
        Assert.Equal(7, MetaFile.Read(page + ".meta").Settings!["textureType"].IntValue);
    }

    private string WriteMaterial(string name, EchoObject material) => WriteAsset(name, material.WriteToString(), "MaterialImporter");

    private static Material ReadMaterial(string path) => Serializer.Deserialize<Material>(EchoObject.ReadFromString(File.ReadAllText(path)))!;

    private static Guid GuidOf(DefaultShader shader) => BuiltInAssets.GuidFor(shader);

    // A material as a release before OpenPBR saved it, its values set under the old names, saved before overrides were tracked
    private static EchoObject OldMaterial(DefaultShader shader, Action<Material> set, SurfaceSettings? surface = null)
    {
        Material material = new(Shader.LoadDefault(shader));
        if (surface != null) material.Surface = surface;
        set(material);
        material._overrides.Clear();
        return Serializer.Serialize(material);
    }

    // Saved by a release before Graphite: keyed dictionaries and no overrides
    private static string PreGraphiteMaterial(string shader, string textures) => $$"""
        {
          "$id": 1,
          "$type": "Prowl.Runtime.Resources.Material, Prowl.Runtime, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
          "_shader": { "$asset": "{{BuiltInAssets.DeterministicGuid($"$Default:Shader/{shader}")}}" },
          "_properties": {
            "$id": 2,
            "_colors": { "_MainColor": { "R": 1F, "G": 0.5F, "B": 0.25F, "A": 0.5F } },
            "_floats": { "_Metallic": 0.8F, "_Anisotropy": 0.3F },
            "_textures": { {{textures}} }
          },
          "Name": "Old"
        }
        """;

    [Fact]
    public void MigrationLiterals_MatchTheBuiltInAssets()
    {
        Assert.Equal(GuidOf(DefaultShader.Lit), Release_1_0.LitShader);
        Assert.Equal(GuidOf(DefaultShader.Unlit), Release_1_0.UnlitShader);
        Assert.Equal(GuidOf(DefaultShader.Grass), Release_1_0.GrassShader);
        Assert.Equal(GuidOf(DefaultShader.Terrain), Release_1_0.TerrainShader);
        Assert.Equal(new[] { GuidOf(DefaultShader.ProceduralSky), GuidOf(DefaultShader.CubemapSky), GuidOf(DefaultShader.PanoramaSky), GuidOf(DefaultShader.SixSidedSky) },
            Release_1_0.ExposedSkyShaders);
        Assert.Equal(new[] { BuiltInAssets.GuidFor(DefaultTexture.White), BuiltInAssets.GuidFor(DefaultTexture.Gray18), BuiltInAssets.GuidFor(DefaultTexture.Normal),
            BuiltInAssets.GuidFor(DefaultTexture.Surface), BuiltInAssets.GuidFor(DefaultTexture.Emission) }, Release_1_0.PlaceholderTextures);
        Assert.Equal(17, Release_1_0.LegacyShaders.Count);
        foreach ((Guid guid, (string name, _)) in Release_1_0.LegacyShaders)
            Assert.Equal(BuiltInAssets.DeterministicGuid($"$Default:Shader/{name}"), guid);
    }

    [Fact]
    public void LitMaterialFromBeforeOpenPbr_MovesItsValuesOntoTheNewNames()
    {
        Texture2D grid = Texture2D.LoadDefault(DefaultTexture.Grid)!;
        string path = WriteMaterial("Old.mat", OldMaterial(DefaultShader.Lit, m =>
        {
            m.SetColor("_MainColor", new Color(0.5f, 0.4f, 0.3f, 0.6f));
            m.SetFloat("_Roughness", 0.2f);
            m.SetFloat("_Metallic", 0.7f);
            m.SetInt("_Emission", 1);
            m.SetFloat("_EmissionIntensity", 3f);
            m.SetTexture("_MainTex", grid);
            m.SetVector("_MainTex_ST", new Float4(2, 3, 0.5f, 0.25f));
            m.SetTexture("_SurfaceTex", grid);
            m.SetInt("_SurfaceTexUV", 1);
            m.SetFloat("_Custom", 4f);
        }, new SurfaceSettings(SurfaceType.Cutout)));

        ProjectMigration.Migrate(Project.Open(_root));

        Material material = ReadMaterial(path);
        Assert.Equal(new Color(0.5f, 0.4f, 0.3f, 0.6f), material.GetColor("base_color"));
        Assert.Equal(0.2f, material.GetFloat("specular_roughness"));
        Assert.Equal(0.7f, material.GetFloat("base_metalness"));
        Assert.Equal(0.6f, material.GetFloat("geometry_opacity"));
        Assert.Equal(3f * LegacyLightUnits.Luminance, material.GetFloat("emission_luminance"));
        Assert.Equal(Color.White, material.GetColor("emission_color"));
        Assert.False(material.IsOverridden("_Roughness"));
        Assert.False(material.IsOverridden("_Emission"));
        Assert.False(material._properties.HasFloat("_EmissionIntensity"));

        Assert.Equal(grid.AssetID, material.GetTexture("base_color_map")!.AssetID);
        Assert.Equal(new Float4(2, 3, 0.5f, 0.25f), material.GetTextureScaleOffset("base_color_map"));
        Assert.Equal(grid.AssetID, material.GetTexture("geometry_opacity_map")!.AssetID);
        Assert.Equal(3, material.GetTextureChannel("geometry_opacity_map"));
        Assert.Equal(new Float4(2, 3, 0.5f, 0.25f), material.GetTextureScaleOffset("geometry_opacity_map"));
        Assert.Equal(1, material.GetTextureChannel("specular_roughness_map"));
        Assert.Equal(2, material.GetTextureChannel("base_metalness_map"));
        Assert.Equal(1, material.GetTextureUVSet("specular_roughness_map"));
        Assert.Equal(1, material.GetTextureUVSet("base_metalness_map"));
        Assert.Null(material._properties.GetTexture("_MainTex"));
        Assert.Null(material._properties.GetTexture("_SurfaceTex"));
        Assert.False(material._properties.HasVector4("_MainTex_ST"));

        // A value saved before overrides were tracked stays set beside the moved ones
        Assert.True(material.IsOverridden("_Custom"));
        Assert.Equal(4f, material.GetFloat("_Custom"));
    }

    [Fact]
    public void UnlitMaterialWithoutALuminance_TakesTheLegacyOne_AndOneWithItKeepsIt()
    {
        string old = WriteMaterial("Old.mat", OldMaterial(DefaultShader.Unlit, m => m.SetColor("_MainColor", new Color(0.1f, 0.2f, 0.3f, 1f))));
        string set = WriteMaterial("Set.mat", OldMaterial(DefaultShader.Unlit, m => m.SetFloat("emission_luminance", 500f)));

        ProjectMigration.Migrate(Project.Open(_root));

        Material oldMaterial = ReadMaterial(old);
        Assert.Equal(LegacyLightUnits.Luminance, oldMaterial.GetFloat("emission_luminance"));
        Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 1f), oldMaterial.GetColor("base_color"));
        Assert.False(oldMaterial._properties.HasFloat("geometry_opacity"));
        Assert.Equal(500f, ReadMaterial(set).GetFloat("emission_luminance"));
    }

    [Theory]
    [InlineData("StandardCutoutDoubleSided", DefaultShader.Lit, SurfaceType.Cutout, SurfaceCull.Off)]
    [InlineData("StandardTransparent", DefaultShader.Lit, SurfaceType.Transparent, SurfaceCull.Back)]
    [InlineData("UnlitDoubleSided", DefaultShader.Unlit, SurfaceType.Opaque, SurfaceCull.Off)]
    public void MaterialOfAShaderFromBeforeGraphite_MovesOntoLitOrUnlit(string oldShader, DefaultShader expected, SurfaceType type, SurfaceCull cull)
    {
        string path = WriteAsset("Old.mat", PreGraphiteMaterial(oldShader, ""), "MaterialImporter");

        ProjectMigration.Migrate(Project.Open(_root));

        Material loaded = ReadMaterial(path);
        Assert.Same(Shader.LoadDefault(expected), loaded.Shader);
        Assert.Equal(new SurfaceSettings(type, SurfaceBlend.Alpha, cull), loaded.Surface);
        Assert.Equal(new Color(1f, 0.5f, 0.25f, 0.5f), loaded.GetColor("base_color"));
    }

    [Fact]
    public void MaterialFromBeforeGraphite_DropsPlaceholders_AndTurnsEmissionOnForAnEmissionMap()
    {
        string white = BuiltInAssets.GuidFor(DefaultTexture.White).ToString();
        string grid = BuiltInAssets.GuidFor(DefaultTexture.Grid).ToString();
        string path = WriteAsset("Old.mat", PreGraphiteMaterial("StandardAnisotropic",
            $"\"_MainTex\": {{ \"$asset\": \"{white}\" }}, \"_EmissionTex\": {{ \"$asset\": \"{grid}\" }}"), "MaterialImporter");
        string plain = WriteAsset("Plain.mat", PreGraphiteMaterial("Standard", ""), "MaterialImporter");

        ProjectMigration.Migrate(Project.Open(_root));

        Material material = ReadMaterial(path);
        Assert.Null(material._properties.GetTexture("base_color_map"));
        Assert.Null(material._properties.GetTexture("_MainTex"));
        Assert.Equal(grid, material.GetTexture("emission_color_map")!.AssetID.ToString());
        Assert.Equal(LegacyLightUnits.Luminance, material.GetFloat("emission_luminance"));
        Assert.False(material._properties.HasFloat("base_metalness"));
        float ratio = 0.7f / 1.3f;
        Assert.Equal(1f - ratio * ratio, material.GetFloat("specular_roughness_anisotropy"), 5);
        Assert.False(material._properties.HasFloat("_Anisotropy"));
        Assert.False(material._properties.HasFloat("tangent_rotation"));

        Material standard = ReadMaterial(plain);
        Assert.False(standard._properties.HasFloat("_Anisotropy"));
        Assert.False(standard._properties.HasFloat("specular_roughness_anisotropy"));
        Assert.False(standard._properties.HasFloat("base_metalness"));
        Assert.False(standard._properties.HasFloat("emission_luminance"));
    }

    [Fact]
    public void LitAnisotropy_KeepsItsStretch_DirectionMapAndBitangentAxis()
    {
        Texture2D grid = Texture2D.LoadDefault(DefaultTexture.Grid)!;
        string path = WriteMaterial("Brushed.mat", OldMaterial(DefaultShader.Lit, m =>
        {
            m.SetFloat("_Anisotropy", -0.5f);
            m.SetTexture("_AnisoDirectionMap", grid);
            m.SetVector("_AnisoDirectionMap_ST", new Float4(2, 2, 0, 0));
        }));

        ProjectMigration.Migrate(Project.Open(_root));

        Material material = ReadMaterial(path);
        Assert.Equal(8f / 9f, material.GetFloat("specular_roughness_anisotropy"), 5);
        Assert.Equal(MathF.PI * 0.5f, material.GetFloat("tangent_rotation"), 5);
        Assert.Equal(grid.AssetID, material.GetTexture("geometry_tangent_map")!.AssetID);
        Assert.Equal(new Float4(2, 2, 0, 0), material.GetTextureScaleOffset("geometry_tangent_map"));
        Assert.False(material._properties.HasFloat("_Anisotropy"));
        Assert.Null(material._properties.GetTexture("_AnisoDirectionMap"));
    }

    [Fact]
    public void GrassSkyAndTerrainMaterials_TakeTheirNewNames()
    {
        string grass = WriteMaterial("Grass.mat", OldMaterial(DefaultShader.Grass, m =>
        {
            m.SetFloat("_AlphaCutoff", 0.3f);
            m.SetFloat("_Translucency", 0.5f);
            m.SetFloat("_WindStrength", 0.2f);
        }));
        string sky = WriteMaterial("Sky.mat", OldMaterial(DefaultShader.PanoramaSky, m => m.SetFloat("_Exposure", 2f)));
        string cubemap = WriteMaterial("Cubemap.mat", OldMaterial(DefaultShader.CubemapSky, m => m.SetFloat("_Rotation", 90f)));
        string procedural = WriteMaterial("Procedural.mat", OldMaterial(DefaultShader.ProceduralSky, m => m.SetFloat("_Exposure", 3f)));
        string terrain = WriteMaterial("Terrain.mat", OldMaterial(DefaultShader.Terrain, m => { m.SetInt("_HasHoles", 1); m.SetFloat("_TerrainSize", 512f); }));

        ProjectMigration.Migrate(Project.Open(_root));

        Material grassMaterial = ReadMaterial(grass);
        Assert.Equal(0.3f, grassMaterial.GetFloat("alpha_cutoff"));
        Assert.False(grassMaterial._properties.HasFloat("_Translucency"));
        Assert.Equal(0.2f, grassMaterial.GetFloat("_WindStrength"));
        Material skyMaterial = ReadMaterial(sky);
        Assert.Equal(2f * LegacyLightUnits.Luminance, skyMaterial.GetFloat("intensity"));
        Assert.False(skyMaterial._properties.HasFloat("_Exposure"));
        Assert.Equal(LegacyLightUnits.Luminance, ReadMaterial(cubemap).GetFloat("intensity"));
        Assert.Equal(3f, ReadMaterial(procedural).GetFloat("intensity"));
        Material terrainMaterial = ReadMaterial(terrain);
        Assert.False(terrainMaterial._properties.HasInt("_HasHoles"));
        Assert.Equal(512f, terrainMaterial.GetFloat("_TerrainSize"));
    }

    private static EchoObject OldScene(SceneEnvironment environment, params EchoObject[] items)
    {
        EchoObject scene = EchoObject.ReadFromString(Scene(items));
        scene["$type"] = new EchoObject("Prowl.Runtime.Resources.Scene, Prowl.Runtime");
        scene["Environment"] = Serializer.Serialize(environment);
        return scene;
    }

    private static SceneEnvironment ReadEnvironment(string scene)
        => Serializer.Deserialize<SceneEnvironment>(EchoObject.ReadFromString(File.ReadAllText(scene))["Environment"])!;

    [Fact]
    public void SceneEnvironment_AndItsInlineSkyMaterial_CarryOntoPhysicalUnits()
    {
        var uniform = new SceneEnvironment();
        uniform.Ambient.Mode = AmbientMode.Uniform;
        uniform.Ambient.Intensity = 2f;
        uniform.SkyMaterial!.SetFloat("_Exposure", 1.5f);
        string uniformScene = WriteAsset("Uniform.scene", OldScene(uniform).WriteToString(), "SceneImporter");

        var sky = new SceneEnvironment();
        sky.Ambient.Intensity = 2f;
        EchoObject withoutFog = OldScene(sky);
        withoutFog["Environment"].Remove("Fog");
        string skyScene = WriteAsset("Sky.scene", withoutFog.WriteToString(), "SceneImporter");

        ProjectMigration.Migrate(Project.Open(_root));

        SceneEnvironment migrated = ReadEnvironment(uniformScene);
        Assert.Equal(2f * LegacyLightUnits.Luminance, migrated.Ambient.Intensity);
        Assert.Equal(LegacyLightUnits.Luminance, migrated.Fog.Intensity);
        Assert.Equal(1.5f, migrated.SkyMaterial!.GetFloat("intensity"));
        Assert.False(migrated.SkyMaterial._properties.HasFloat("_Exposure"));

        SceneEnvironment skyLit = ReadEnvironment(skyScene);
        Assert.Equal(2f, skyLit.Ambient.Intensity);
        Assert.Equal(LegacyLightUnits.Luminance, skyLit.Fog.Intensity);
        Assert.Equal(new SceneEnvironment().Fog.Density, skyLit.Fog.Density);
    }

    private static EchoObject Override(string path, EchoObject value)
    {
        var entry = EchoObject.NewCompound();
        entry["Path"] = new EchoObject(path);
        entry["Value"] = value;
        return entry;
    }

    [Fact]
    public void LightIntensities_AndTheirPrefabOverrides_CarryOntoPhysicalUnits()
    {
        Guid source = Guid.NewGuid(), other = Guid.NewGuid();
        EchoObject instance = OldComponent("Light", Guid.NewGuid(), ("intensity", new EchoObject(2f)));
        instance["_prefabTemplateIdentity"] = new EchoObject(source.ToString());
        string target = Guid.NewGuid().ToString();
        string scene = WriteAsset("Main.scene", Scene(
            instance,
            OldComponent("Light", Guid.NewGuid(), ("intensity", new EchoObject(0.5f)), ("units", new EchoObject(1))),
            Override($"{target}/{source}/intensity", new EchoObject(4f)),
            Override($"{target}/{source}/range", new EchoObject(4f)),
            Override($"{target}/{other}/intensity", new EchoObject(4f))), "SceneImporter");
        WriteProwlFile("1.0", appliedSteps: 4);

        ProjectMigration.Migrate(Project.Open(_root));

        EchoObject items = Item(scene);
        Assert.Equal(2f * LegacyLightUnits.Light, items[0]["intensity"].FloatValue);
        Assert.Equal(0.5f, items[1]["intensity"].FloatValue);
        Assert.Equal(4f * LegacyLightUnits.Light, items[2]["Value"].FloatValue);
        Assert.Equal(4f, items[3]["Value"].FloatValue);
        Assert.Equal(4f, items[4]["Value"].FloatValue);

        var light = (Light)Serializer.Deserialize<Component>(items[0])!;
        Assert.Equal(2f * LegacyLightUnits.Light, light.Intensity);
    }

    [Fact]
    public void ParticleLightModules_AndTheirPrefabOverrides_CarryOntoPhysicalUnits()
    {
        static EchoObject OldModule(EchoObject module, float constant)
        {
            module["Intensity"]["ConstantValue"] = new EchoObject(constant);
            module["Intensity"]["MinValue"] = new EchoObject(0.25f);
            module["Intensity"]["MaxValue"] = new EchoObject(0.5f);
            module["Intensity"]["CurveMultiplier"] = new EchoObject(2f);
            module.Remove("units");
            return module;
        }

        Guid system = Guid.NewGuid();
        EchoObject component = Serializer.Serialize(typeof(Component), new ParticleSystemComponent());
        component["_identifier"] = new EchoObject(system.ToString());
        OldModule(component["Light"], 1f);
        string target = Guid.NewGuid().ToString();
        EchoObject moduleOverride = Override($"{target}/{system}/Light", OldModule(Serializer.Serialize(new LightModule()), 3f));
        string scene = WriteAsset("Main.scene", Scene(component, moduleOverride), "SceneImporter");
        WriteProwlFile("1.0", appliedSteps: 4);

        ProjectMigration.Migrate(Project.Open(_root));

        EchoObject items = Item(scene);
        var particles = (ParticleSystemComponent)Serializer.Deserialize<Component>(items[0])!;
        Assert.Equal(1f * LegacyLightUnits.Light, particles.Light.Intensity.ConstantValue);
        Assert.Equal(0.25f * LegacyLightUnits.Light, particles.Light.Intensity.MinValue);
        Assert.Equal(0.5f * LegacyLightUnits.Light, particles.Light.Intensity.MaxValue);
        Assert.Equal(2f * LegacyLightUnits.Light, particles.Light.Intensity.CurveMultiplier);
        Assert.Equal(3f * LegacyLightUnits.Light, items[1]["Value"]["Intensity"]["ConstantValue"].FloatValue);
    }

    private static void RemoveUnitMarkers(EchoObject tag)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List) RemoveUnitMarkers(item);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;
        tag.Remove("units");
        tag.Remove("_lightUnits");
        foreach (string name in tag.GetNames().ToList()) RemoveUnitMarkers(tag[name]);
    }

    // Point lights saved back as the single Light component they were before the split
    private static void AsOldLights(EchoObject tag)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List) AsOldLights(item);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;
        if (tag.TryGet("$type", out EchoObject? type) && type!.StringValue.StartsWith("Prowl.Runtime.PointLight,", StringComparison.Ordinal))
        {
            tag["$type"] = new EchoObject("Prowl.Runtime.Light, Prowl.Runtime");
            tag["type"] = new EchoObject(1L);
        }
        foreach (string name in tag.GetNames().ToList()) AsOldLights(tag[name]);
    }

    private static (float Light, float Particles, float Ambient) Intensities(string path)
    {
        Scene scene = Serializer.Deserialize<Scene>(EchoObject.ReadFromString(File.ReadAllText(path)))!;
        try
        {
            GameObject lit = scene.RootObjects.First(o => o.Name == "Lit");
            return (lit.GetComponent<Light>()!.Intensity, lit.GetComponent<ParticleSystemComponent>()!.Light.Intensity.ConstantValue, scene.Environment.Ambient.Intensity);
        }
        finally { scene.Dispose(); }
    }

    [Fact]
    public void LightUnitMarkers_SurviveAResave_SoMigratingAgainKeepsTheIntensities()
    {
        var lit = new GameObject("Lit");
        lit.AddComponent<PointLight>().Intensity = 2f;
        lit.AddComponent<ParticleSystemComponent>().Light.Intensity.ConstantValue = 3f;
        var scene = new Scene();
        scene.Environment.Ambient.Mode = AmbientMode.Uniform;
        scene.Environment.Ambient.Intensity = 0.5f;
        scene.Add(lit);
        EchoObject old = Serializer.Serialize(typeof(object), scene)!;
        scene.Dispose();
        RemoveUnitMarkers(old);
        AsOldLights(old);
        string path = WriteAsset("Main.scene", old.WriteToString(), "SceneImporter");
        WriteProwlFile("1.0", appliedSteps: 6);

        ProjectMigration.Migrate(Project.Open(_root));
        (float Light, float Particles, float Ambient) migrated = Intensities(path);
        Assert.Equal(2f * LegacyLightUnits.Light, migrated.Light);
        Assert.Equal(3f * LegacyLightUnits.Light, migrated.Particles);
        Assert.Equal(0.5f * LegacyLightUnits.Luminance, migrated.Ambient);

        Scene loaded = Serializer.Deserialize<Scene>(EchoObject.ReadFromString(File.ReadAllText(path)))!;
        File.WriteAllText(path, Serializer.Serialize(typeof(object), loaded)!.WriteToString());
        loaded.Dispose();

        var again = new MigrationContext(Project.Open(_root));
        ProjectMigration.Releases.Single(r => r.Name == "1.0").Steps[6].Run(again);

        Assert.Empty(again.ChangedFiles);
        Assert.Equal(migrated, Intensities(path));
    }

    private static EchoObject BakedScene(float lightmapScale)
    {
        var scene = new Scene();
        scene.BakedLighting.ProbePositions = [Float3.Zero];
        scene.BakedLighting.ProbeSH = [new SphericalHarmonicsL2 { C0 = new Float3(1f, 2f, 3f), C8 = new Float3(0.5f, 0.5f, 0.5f) }];
        scene.BakedLighting.LightmapScale = lightmapScale;
        EchoObject echo = Serializer.Serialize(typeof(object), scene)!;
        scene.Dispose();
        return echo;
    }

    [Fact]
    public void BakesFromBeforePhysicalLight_KeepTheirLook_AndOnlyUnscaledPhysicalOnesNeedARebake()
    {
        EchoObject old = BakedScene(1f);
        RemoveUnitMarkers(old);
        old["BakedLighting"].Remove("LightmapScale");
        string oldPath = WriteAsset("Old.scene", old.WriteToString(), "SceneImporter");

        EchoObject unscaled = BakedScene(1f);
        unscaled["BakedLighting"].Remove("LightmapScale");
        Assert.True(Release_1_0.NeedsRebake(unscaled));
        WriteAsset("Unscaled.scene", unscaled.WriteToString(), "SceneImporter");
        WriteProwlFile("1.0", appliedSteps: 6);

        ProjectMigration.Migrate(Project.Open(_root));

        EchoObject migrated = EchoObject.ReadFromString(File.ReadAllText(oldPath));
        Assert.False(Release_1_0.NeedsRebake(migrated));
        Scene scene = Serializer.Deserialize<Scene>(migrated)!;
        Assert.Equal(LegacyLightUnits.Luminance, scene.BakedLighting.LightmapScale);
        Assert.Equal(new Float3(1f, 2f, 3f) * LegacyLightUnits.Luminance, scene.BakedLighting.ProbeSH[0].C0);
        Assert.Equal(new Float3(0.5f, 0.5f, 0.5f) * LegacyLightUnits.Luminance, scene.BakedLighting.ProbeSH[0].C8);
        Assert.Equal(Float3.Zero, scene.BakedLighting.ProbePositions[0]);
        scene.Dispose();

        Assert.False(Release_1_0.NeedsRebake(BakedScene(3f)));
        var empty = new Scene();
        Assert.False(Release_1_0.NeedsRebake(Serializer.Serialize(typeof(object), empty)!));
        empty.Dispose();
    }

    [Fact]
    public void MaterialAndLightSteps_RunAgain_ChangeNothing()
    {
        WriteMaterial("Lit.mat", OldMaterial(DefaultShader.Lit, m => { m.SetColor("_MainColor", Color.White); m.SetInt("_Emission", 1); }));
        WriteMaterial("Unlit.mat", OldMaterial(DefaultShader.Unlit, m => m.SetColor("_MainColor", Color.White)));
        WriteAsset("Legacy.mat", PreGraphiteMaterial("StandardCutout", ""), "MaterialImporter");
        var environment = new SceneEnvironment();
        environment.Ambient.Mode = AmbientMode.Hemisphere;
        EchoObject light = OldComponent("Light", Guid.NewGuid(), ("intensity", new EchoObject(2f)));
        WriteAsset("Main.scene", OldScene(environment, light, Override($"{Guid.NewGuid()}/{light["_identifier"].StringValue}/intensity", new EchoObject(1f))).WriteToString(), "SceneImporter");
        WriteProwlFile("1.0-preview.5");

        ProjectMigration.Migrate(Project.Open(_root));
        Dictionary<string, string> migrated = Directory.GetFiles(Path.Combine(_root, "Assets")).ToDictionary(f => f, File.ReadAllText);

        var again = new MigrationContext(Project.Open(_root));
        foreach (MigrationStep step in ProjectMigration.Releases.Single(r => r.Name == "1.0").Steps)
            step.Run(again);

        Assert.Empty(again.ChangedFiles);
        foreach ((string file, string text) in migrated)
            Assert.Equal(text, File.ReadAllText(file));
    }

    [Fact]
    public void LightCones_BecomeAnAngleAndSoftness_RangesGoAutomatic_AndColoursStayOffTemperature()
    {
        string scene = WriteAsset("Main.scene", Scene(
            OldComponent("Light", Guid.NewGuid(), ("type", new EchoObject(2L)), ("innerAngle", new EchoObject(20f)), ("outerAngle", new EchoObject(40f)),
                ("range", new EchoObject(7f)), ("color", Serializer.Serialize(new Color(1f, 0.5f, 0.25f, 1f)))),
            OldComponent("Light", Guid.NewGuid(), ("type", new EchoObject(2L)), ("outerAngle", new EchoObject(60f))),
            OldComponent("Light", Guid.NewGuid(), ("type", new EchoObject(2L)), ("innerAngle", new EchoObject(10f)), ("outerAngle", new EchoObject(0f))),
            OldComponent("Light", Guid.NewGuid(), ("range", new EchoObject(3f)))), "SceneImporter");
        WriteProwlFile("1.0", appliedSteps: 8);

        ProjectMigration.Migrate(Project.Open(_root));

        EchoObject items = Item(scene);
        foreach (EchoObject item in items.List)
        {
            Assert.False(item.TryGet("innerAngle", out _));
            Assert.False(item.TryGet("outerAngle", out _));
            Assert.False(item.TryGet("range", out _));
            Assert.False(item["useTemperature"].BoolValue);
        }
        Assert.Equal(80f, items[0]["spotAngle"].FloatValue);
        Assert.Equal(0.5f, items[0]["spotSoftness"].FloatValue);
        Assert.Equal(120f, items[1]["spotAngle"].FloatValue);
        Assert.Equal(0.5f, items[1]["spotSoftness"].FloatValue);
        Assert.Equal(0f, items[2]["spotAngle"].FloatValue);
        Assert.Equal(0f, items[2]["spotSoftness"].FloatValue);
        Assert.False(items[3].TryGet("spotAngle", out _));

        var spot = Assert.IsType<SpotLight>(Serializer.Deserialize<Component>(items[0]));
        Assert.Equal(80f, spot.SpotAngle);
        Assert.Equal(0.5f, spot.SpotSoftness);
        Assert.False(spot.OverrideRange);
        Assert.False(spot.UseTemperature);
        Assert.Equal(new Color(1f, 0.5f, 0.25f, 1f), spot.FinalColor);

        var untouched = Assert.IsType<PointLight>(Serializer.Deserialize<Component>(items[3]));
        Assert.False(untouched.OverrideRange);
        Assert.Equal(2700f, untouched.Temperature);
    }

    [Fact]
    public void LightConeOverrides_BecomeSpotAngleAndSoftnessOverrides_AndTheStepCanRunAgain()
    {
        Guid light = Guid.NewGuid();
        WriteAsset("Lamp.prefab", Scene(OldComponent("Light", light, ("type", new EchoObject(2L)), ("innerAngle", new EchoObject(20f)), ("outerAngle", new EchoObject(40f)))), "PrefabImporter");
        string both = Guid.NewGuid().ToString(), outer = Guid.NewGuid().ToString(), inner = Guid.NewGuid().ToString();
        string scene = WriteAsset("Main.scene", Scene(
            Override($"{both}/{light}/innerAngle", new EchoObject(15f)),
            Override($"{both}/{light}/outerAngle", new EchoObject(30f)),
            Override($"{outer}/{light}/outerAngle", new EchoObject(60f)),
            Override($"{inner}/{light}/innerAngle", new EchoObject(10f)),
            Override($"{inner}/{light}/range", new EchoObject(5f)),
            Override($"{inner}/{Guid.NewGuid()}/innerAngle", new EchoObject(10f))), "SceneImporter");
        WriteProwlFile("1.0", appliedSteps: 8);

        ProjectMigration.Migrate(Project.Open(_root));

        Dictionary<string, float> overrides = Item(scene).List.ToDictionary(o => o["Path"].StringValue, o => o["Value"].FloatValue);
        Assert.Equal(7, overrides.Count);
        Assert.Equal(0.5f, overrides[$"{both}/{light}/spotSoftness"]);
        Assert.Equal(60f, overrides[$"{both}/{light}/spotAngle"]);
        Assert.Equal(120f, overrides[$"{outer}/{light}/spotAngle"]);
        Assert.Equal(1f - 20f / 60f, overrides[$"{outer}/{light}/spotSoftness"], 5);
        Assert.Equal(0.75f, overrides[$"{inner}/{light}/spotSoftness"]);
        Assert.False(overrides.ContainsKey($"{inner}/{light}/spotAngle"));
        Assert.Equal(5f, overrides[$"{inner}/{light}/range"]);
        Assert.Contains(overrides.Keys, k => k.EndsWith("/innerAngle", StringComparison.Ordinal) && !k.Contains(light.ToString()));

        Dictionary<string, string> migrated = Directory.GetFiles(Path.Combine(_root, "Assets")).ToDictionary(f => f, File.ReadAllText);
        var again = new MigrationContext(Project.Open(_root));
        Release_1_0.LightConesAndTemperature(again);
        Assert.Empty(again.ChangedFiles);
        foreach ((string file, string text) in migrated)
            Assert.Equal(text, File.ReadAllText(file));
    }

    [Fact]
    public void Lights_SplitIntoAComponentForEachKind_KeepingTheirFields_AndTheStepCanRunAgain()
    {
        string scene = WriteAsset("Main.scene", Scene(
            OldComponent("Light", Guid.NewGuid(), ("type", new EchoObject(0L)), ("useTemperature", new EchoObject(true)), ("angularRadius", new EchoObject(0.5f)),
                ("spotAngle", new EchoObject(30f)), ("range", new EchoObject(4f)), ("units", new EchoObject(1))),
            OldComponent("Light", Guid.NewGuid(), ("useTemperature", new EchoObject(true)), ("sourceRadius", new EchoObject(0.1f)), ("width", new EchoObject(3f))),
            OldComponent("Light", Guid.NewGuid(), ("type", new EchoObject(2L)), ("useTemperature", new EchoObject(true)), ("spotAngle", new EchoObject(30f))),
            OldComponent("Light", Guid.NewGuid(), ("type", new EchoObject(3L)), ("useTemperature", new EchoObject(true)), ("height", new EchoObject(2f))),
            OldComponent("Light", Guid.NewGuid(), ("type", new EchoObject(4L)), ("useTemperature", new EchoObject(true)), ("width", new EchoObject(0.4f))),
            OldComponent("Light", Guid.NewGuid(), ("type", new EchoObject(5L)), ("useTemperature", new EchoObject(true)), ("sourceRadius", new EchoObject(0.05f)), ("length", new EchoObject(2f)))),
            "SceneImporter");
        WriteProwlFile("1.0", appliedSteps: 10);

        ProjectMigration.Migrate(Project.Open(_root));

        EchoObject items = Item(scene);
        Component Load(int i) => Serializer.Deserialize<Component>(items[i])!;

        var sun = Assert.IsType<DirectionalLight>(Load(0));
        Assert.Equal(0.5f, sun.AngularRadius);
        Assert.Equal(2700f, sun.Temperature);
        Assert.Equal(800f / (4f * MathF.PI), sun.Intensity, 4);
        foreach (string gone in new[] { "type", "units", "spotAngle", "range" })
            Assert.False(items[0].TryGet(gone, out _), gone);

        var point = Assert.IsType<PointLight>(Load(1));
        Assert.Equal(0.1f, point.SourceRadius);
        Assert.False(items[1].TryGet("width", out _));

        var spot = Assert.IsType<SpotLight>(Load(2));
        Assert.Equal(30f, spot.SpotAngle);
        Assert.Equal(1f / 3f, spot.SpotSoftness, 5);

        var rect = Assert.IsType<RectLight>(Load(3));
        Assert.Equal(1f, rect.Width);
        Assert.Equal(2f, rect.Height);

        var disk = Assert.IsType<DiskLight>(Load(4));
        Assert.Equal(0.4f, disk.Diameter);

        var tube = Assert.IsType<TubeLight>(Load(5));
        Assert.Equal(0.05f, tube.Radius);
        Assert.Equal(2f, tube.Length);

        string migrated = File.ReadAllText(scene);
        var again = new MigrationContext(Project.Open(_root));
        Release_1_0.SplitLights(again);
        Assert.Empty(again.ChangedFiles);
        Assert.Equal(migrated, File.ReadAllText(scene));
    }

    [Fact]
    public void LightOverrides_FollowTheSplit_AndThoseOfFieldsTheKindLacksAreDropped()
    {
        Guid disk = Guid.NewGuid(), point = Guid.NewGuid();
        WriteAsset("Lamp.prefab", Scene(
            OldComponent("Light", disk, ("type", new EchoObject(4L)), ("useTemperature", new EchoObject(true))),
            OldComponent("Light", point, ("useTemperature", new EchoObject(true)))), "PrefabImporter");
        string target = Guid.NewGuid().ToString();
        string scene = WriteAsset("Main.scene", Scene(
            Override($"{target}/{disk}/width", new EchoObject(2f)),
            Override($"{target}/{disk}/range", new EchoObject(6f)),
            Override($"{target}/{point}/spotAngle", new EchoObject(20f)),
            Override($"{target}/{point}/type", new EchoObject(2L)),
            Override($"{target}/{point}/color.R", new EchoObject(0.5f))), "SceneImporter");
        WriteProwlFile("1.0", appliedSteps: 10);

        ProjectMigration.Migrate(Project.Open(_root));

        Dictionary<string, float> overrides = Item(scene).List.ToDictionary(o => o["Path"].StringValue, o => o["Value"].FloatValue);
        Assert.Equal(3, overrides.Count);
        Assert.Equal(2f, overrides[$"{target}/{disk}/diameter"]);
        Assert.Equal(6f, overrides[$"{target}/{disk}/range"]);
        Assert.Equal(0.5f, overrides[$"{target}/{point}/color.R"]);
    }

    [Fact]
    public void ShadersUsingOldLitNames_AreListed_AndLeftAlone()
    {
        const string source = """
            #include "Prowl/Lit.glsl"
            uniform vec4 _MainColor;
            uniform sampler2D _SurfaceTex;
            uniform vec4 _NormalTex_ST;
            uniform float _MyRoughness;
            uniform float _ParallaxSteps;
            """;
        List<string> found = Release_1_0.OldShaderNames(source);
        Assert.Contains("the Prowl/Lit.glsl include", found);
        Assert.Contains("_MainColor is now base_color", found);
        Assert.Contains(found, f => f.StartsWith("_SurfaceTex is now", StringComparison.Ordinal));
        Assert.Contains("_NormalTex is now geometry_normal_map", found);
        Assert.DoesNotContain(found, f => f.StartsWith("_Roughness ", StringComparison.Ordinal));
        Assert.DoesNotContain(found, f => f.StartsWith("_Parallax ", StringComparison.Ordinal));
        Assert.Empty(Release_1_0.OldShaderNames("uniform vec4 base_color;"));

        string shader = WriteAsset("Custom.shader", source, "ShaderImporter");
        var warnings = new List<string>();
        void Collect(string message, DebugStackTrace? trace, LogSeverity severity)
        {
            if (severity == LogSeverity.Warning && message.Contains("Custom.shader", StringComparison.Ordinal))
                lock (warnings) warnings.Add(message);
        }
        Prowl.Runtime.Debug.OnLog += Collect;
        try { ProjectMigration.Migrate(Project.Open(_root)); }
        finally { Prowl.Runtime.Debug.OnLog -= Collect; }

        Assert.Equal(source, File.ReadAllText(shader));
        Assert.Contains("_MainColor is now base_color", Assert.Single(warnings));
    }

    private static void WriteSettings(string asset, Action<EchoObject> edit)
    {
        MetaFileData meta = MetaFile.Read(asset + ".meta");
        meta.Settings ??= EchoObject.NewCompound();
        edit(meta.Settings);
        MetaFile.Write(asset + ".meta", meta);
    }
}
