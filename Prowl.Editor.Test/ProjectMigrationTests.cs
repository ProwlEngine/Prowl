// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO.Compression;
using System.Text.Json.Nodes;

using Prowl.Echo;
using Prowl.Editor.Migration;
using Prowl.Editor.Projects;

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

    private static string LegacyReference(Guid guid)
    {
        var reference = EchoObject.NewCompound();
        reference["AssetID"] = new EchoObject(guid.ToString());
        var root = EchoObject.NewCompound();
        root["Material"] = reference;
        return root.WriteToString();
    }

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
        Guid material = Guid.NewGuid();
        string scene = WriteAsset("Main.scene", LegacyReference(material), "Prowl.Editor.Assets.SceneImporter");
        string shader = WriteAsset("Lit.shader", "#include \"Fragment\"\nvoid main() {}", "ShaderImporter");
        Directory.CreateDirectory(Path.Combine(_root, "Library", "cache"));
        File.WriteAllText(Path.Combine(_root, "Library", "cache", "stale.bin"), "x");

        Project project = Project.Open(_root);
        Assert.True(project.Version.IsUnknown);
        Assert.True(project.NeedsMigration);

        ProjectMigration.Migrate(project);

        EchoObject migrated = EchoObject.ReadFromString(File.ReadAllText(scene));
        Assert.Equal(material.ToString(), migrated["Material"]["$asset"].StringValue);
        Assert.False(migrated["Material"].TryGet("AssetID", out _));
        Assert.Contains("#include \"ProwlCG\"", File.ReadAllText(shader));
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
        Guid material = Guid.NewGuid();
        string scene = WriteAsset("Main.scene", LegacyReference(material), "SceneImporter");
        string shader = WriteAsset("Lit.shader", "#include \"Fragment\"", "ShaderImporter");
        WriteProwlFile("1.0-preview.5", appliedSteps: 1);

        Project project = Project.Open(_root);
        var pending = ProjectMigration.PendingSteps(project);
        Assert.Equal(1, Assert.Single(pending).Index);

        ProjectMigration.Migrate(project);

        Assert.Contains("#include \"ProwlCG\"", File.ReadAllText(shader));
        Assert.True(EchoObject.ReadFromString(File.ReadAllText(scene))["Material"].TryGet("AssetID", out _));
    }

    [Fact]
    public void LegacyPreviewProject_IsCurrent()
    {
        WriteProwlFile("preview-5");
        Project project = Project.Open(_root);
        Assert.Equal(Project.CurrentVersion, project.Version);
        Assert.False(project.NeedsMigration);
        Assert.False(project.IsFromNewerEngine);
    }

    [Theory]
    [InlineData("99.0", null)]
    [InlineData("1.0-preview.5", 99)]
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
}
