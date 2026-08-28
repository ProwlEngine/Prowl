// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Linq;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The asset database's shader catalog: the <c>Shader "Some/Path"</c> declaration each shader
/// carries, which is what the material inspector's picker builds its menu from. Nothing else in the
/// database records it, so it is read from the sources and maintained across import and delete.
/// </summary>
public class ShaderCatalogTests : EditorTestHarness
{
    private const string Header = "// a comment above the declaration\n";

    private Guid WriteShader(string relativePath, string menuPath, bool withHeader = false)
    {
        string absolute = AssetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, $"{(withHeader ? Header : "")}Shader \"{menuPath}\"\n\nProperties\n{{\n}}\n");
        return Assets.ImportFile(relativePath);
    }

    // ---------------------------------------------------------------- built-ins

    [Fact]
    public void Catalog_ContainsTheStandardFamilyUnderItsDeclaredPaths()
    {
        var paths = Assets.GetShaderCatalog().Select(e => e.MenuPath).ToList();

        Assert.Contains("Default/Standard", paths);
        Assert.Contains("Default/Standard Double Sided", paths);
        Assert.Contains("Default/Cutout/Standard", paths);
        Assert.Contains("Default/Transparent/Standard", paths);
        Assert.Contains("Default/Anisotropic/Standard", paths);
        Assert.Contains("Default/Anisotropic/Cutout/Standard Double Sided", paths);
        Assert.Contains("Default/Unlit", paths);
        Assert.Contains("Default/Cutout/Unlit Double Sided", paths);
    }

    [Fact]
    public void Catalog_ExcludesHiddenShadersUnlessAsked()
    {
        Assert.DoesNotContain(Assets.GetShaderCatalog(),
            e => e.MenuPath.StartsWith(EditorAssetBackend.HiddenShaderPrefix, StringComparison.Ordinal));
        Assert.Contains(Assets.GetShaderCatalog(includeHidden: true),
            e => e.MenuPath.StartsWith(EditorAssetBackend.HiddenShaderPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void Catalog_IsSortedByMenuPath()
    {
        var entries = Assets.GetShaderCatalog(includeHidden: true);
        var sorted = entries.Select(e => e.MenuPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal(sorted, entries.Select(e => e.MenuPath).ToList());
    }

    [Fact]
    public void Catalog_HandsBackTheGuidThatResolvesToThatShader()
    {
        var standard = Assets.GetShaderCatalog().Single(e => e.MenuPath == "Default/Standard");

        Assert.True(standard.IsBuiltIn);
        Assert.Equal(BuiltInAssets.GuidFor(DefaultShader.Standard), standard.Guid);
    }

    // The picker's trigger label calls this every frame, including before the popup has ever been
    // opened, so a built-in has to resolve without the catalog having been listed first.
    [Fact]
    public void MenuPath_ResolvesBuiltInsWithoutListingTheCatalogFirst()
    {
        Assert.Equal("Default/Transparent/Standard",
            Assets.GetShaderMenuPath(BuiltInAssets.GuidFor(DefaultShader.StandardTransparent), "None"));
    }

    [Fact]
    public void MenuPath_FallsBackForEmptyAndUnknownGuids()
    {
        Assert.Equal("None", Assets.GetShaderMenuPath(Guid.Empty, "None"));
        Assert.Equal("None", Assets.GetShaderMenuPath(Guid.NewGuid(), "None"));
    }

    // ---------------------------------------------------------------- project shaders

    [Fact]
    public void ImportingAShader_AddsItToTheCatalog()
    {
        Guid guid = WriteShader("Custom.shader", "Custom/My Shader");

        var entry = Assets.GetShaderCatalog().Single(e => e.Guid == guid);
        Assert.Equal("Custom/My Shader", entry.MenuPath);
        Assert.False(entry.IsBuiltIn);
        Assert.Equal("Custom/My Shader", Assets.GetShaderMenuPath(guid, "None"));
    }

    [Fact]
    public void ImportingAShader_ReadsThroughALeadingComment()
    {
        Guid guid = WriteShader("Commented.shader", "Custom/Commented", withHeader: true);

        Assert.Equal("Custom/Commented", Assets.GetShaderMenuPath(guid, "None"));
    }

    // The declaration lives in the file, so the catalog only learns it changed by way of a reimport.
    [Fact]
    public void ReimportingAShader_PicksUpAnEditedDeclaration()
    {
        Guid guid = WriteShader("Renamed.shader", "Custom/Before");
        Assert.Equal("Custom/Before", Assets.GetShaderMenuPath(guid, "None"));

        File.WriteAllText(AssetAbsolutePath("Renamed.shader"), "Shader \"Custom/After\"\n\nProperties\n{\n}\n");
        Assets.Reimport(guid);

        Assert.Equal("Custom/After", Assets.GetShaderMenuPath(guid, "None"));
        Assert.Contains(Assets.GetShaderCatalog(), e => e.MenuPath == "Custom/After");
        Assert.DoesNotContain(Assets.GetShaderCatalog(), e => e.MenuPath == "Custom/Before");
    }

    [Fact]
    public void DeletingAShader_DropsItFromTheCatalog()
    {
        Guid guid = WriteShader("Doomed.shader", "Custom/Doomed");
        Assert.Contains(Assets.GetShaderCatalog(), e => e.Guid == guid);

        Assets.DeleteAsset("Doomed.shader");

        Assert.DoesNotContain(Assets.GetShaderCatalog(), e => e.Guid == guid);
        Assert.Equal("None", Assets.GetShaderMenuPath(guid, "None"));
    }

    // A shader with no declaration would otherwise be unreachable in the picker.
    [Fact]
    public void ShaderWithNoDeclaration_FallsBackToItsFileName()
    {
        string absolute = AssetAbsolutePath("Nameless.shader");
        File.WriteAllText(absolute, "// nothing but a comment\n");
        Guid guid = Assets.ImportFile("Nameless.shader");

        Assert.Equal("Nameless", Assets.GetShaderMenuPath(guid, "None"));
    }

    [Fact]
    public void ProjectShaderUnderHidden_IsFilteredLikeABuiltIn()
    {
        Guid guid = WriteShader("Internal.shader", "Hidden/My Internal Shader");

        Assert.DoesNotContain(Assets.GetShaderCatalog(), e => e.Guid == guid);
        Assert.Contains(Assets.GetShaderCatalog(includeHidden: true), e => e.Guid == guid);
    }
}
