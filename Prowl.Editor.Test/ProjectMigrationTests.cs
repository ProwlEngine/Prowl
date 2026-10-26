// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO.Compression;
using System.Text.Json.Nodes;

using Prowl.Echo;
using Prowl.Editor.Migration;
using Prowl.Editor.Projects;
using Prowl.Runtime;

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
        Assert.Equal(3f, items[1]["intensity"].FloatValue);
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

    private static void WriteSettings(string asset, Action<EchoObject> edit)
    {
        MetaFileData meta = MetaFile.Read(asset + ".meta");
        meta.Settings ??= EchoObject.NewCompound();
        edit(meta.Settings);
        MetaFile.Write(asset + ".meta", meta);
    }
}
