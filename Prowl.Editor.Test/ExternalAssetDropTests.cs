// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Editor.Test;

public class ExternalAssetDropTests : IDisposable
{
    private readonly string _root;
    private readonly string _assets;
    private readonly string _external;

    public ExternalAssetDropTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ProwlDropTest", Guid.NewGuid().ToString("N"));
        _assets = Path.Combine(_root, "Assets");
        _external = Path.Combine(_root, "External");
        Directory.CreateDirectory(_assets);
        Directory.CreateDirectory(_external);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string MakeExternalFile(string relative)
    {
        string path = Path.Combine(_external, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void SingleFile_CopiesIntoDestinationFolder()
    {
        string src = MakeExternalFile("Grass.png");
        Directory.CreateDirectory(Path.Combine(_assets, "Textures"));

        var plan = ExternalAssetDrop.PlanCopy([src], _assets, "Textures");

        var file = Assert.Single(plan.Files);
        Assert.Equal(src, file.SourceAbs);
        Assert.Equal(Path.Combine(_assets, "Textures", "Grass.png"), file.DestAbs);
        Assert.Equal("Textures/Grass.png", file.DestRel);
        Assert.Empty(plan.Directories);
    }

    [Fact]
    public void JunkAndMetaFiles_AreSkipped()
    {
        var sources = new[]
        {
            MakeExternalFile("Model.fbx.meta"),
            MakeExternalFile("Thumbs.db"),
            MakeExternalFile("desktop.ini"),
            MakeExternalFile(".hidden"),
        };

        var plan = ExternalAssetDrop.PlanCopy(sources, _assets, "");

        Assert.Empty(plan.Files);
    }

    [Fact]
    public void ExistingName_GetsUniqueSuffix()
    {
        File.WriteAllText(Path.Combine(_assets, "Grass.png"), "x");
        string src = MakeExternalFile("Grass.png");

        var plan = ExternalAssetDrop.PlanCopy([src], _assets, "");

        Assert.Equal("Grass (1).png", Path.GetFileName(Assert.Single(plan.Files).DestAbs));
    }

    [Fact]
    public void DuplicateNamesInBatch_AreUniquified()
    {
        string a = MakeExternalFile(Path.Combine("A", "Grass.png"));
        string b = MakeExternalFile(Path.Combine("B", "Grass.png"));

        var plan = ExternalAssetDrop.PlanCopy([a, b], _assets, "");

        Assert.Equal(2, plan.Files.Count);
        Assert.Equal("Grass.png", plan.Files[0].DestRel);
        Assert.Equal("Grass (1).png", plan.Files[1].DestRel);
    }

    [Fact]
    public void FolderTree_PreservesStructureAndSkipsJunk()
    {
        MakeExternalFile(Path.Combine("Pack", "Robot.fbx"));
        MakeExternalFile(Path.Combine("Pack", "Robot.fbx.meta"));
        MakeExternalFile(Path.Combine("Pack", "Textures", "Skin.png"));
        Directory.CreateDirectory(Path.Combine(_external, "Pack", ".git"));

        var plan = ExternalAssetDrop.PlanCopy([Path.Combine(_external, "Pack")], _assets, "");

        Assert.Equal(
            [Path.Combine(_assets, "Pack"), Path.Combine(_assets, "Pack", "Textures")],
            plan.Directories);
        Assert.Equal(2, plan.Files.Count);
        Assert.Contains(plan.Files, f => f.DestRel == "Pack/Robot.fbx");
        Assert.Contains(plan.Files, f => f.DestRel == "Pack/Textures/Skin.png");
    }

    [Fact]
    public void ExistingFolderName_UniquifiesTheRootOnly()
    {
        Directory.CreateDirectory(Path.Combine(_assets, "Pack"));
        MakeExternalFile(Path.Combine("Pack", "Robot.fbx"));

        var plan = ExternalAssetDrop.PlanCopy([Path.Combine(_external, "Pack")], _assets, "");

        Assert.Equal(Path.Combine(_assets, "Pack (1)"), Assert.Single(plan.Directories));
        Assert.Equal("Pack (1)/Robot.fbx", Assert.Single(plan.Files).DestRel);
    }

    [Fact]
    public void SourceAlreadyInAssets_IsNotCopied()
    {
        string existing = Path.Combine(_assets, "Old.png");
        File.WriteAllText(existing, "x");

        var plan = ExternalAssetDrop.PlanCopy([existing], _assets, "Sub");

        Assert.Empty(plan.Files);
        Assert.Equal("Old.png", Assert.Single(plan.AlreadyInProject));
    }

    [Fact]
    public void FolderContainingDestination_IsSkipped()
    {
        var plan = ExternalAssetDrop.PlanCopy([_root], _assets, "");

        Assert.Empty(plan.Files);
        Assert.Empty(plan.Directories);
    }
}
