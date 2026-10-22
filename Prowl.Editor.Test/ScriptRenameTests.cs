// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.GUI.ScriptEditor;
using Prowl.OrigamiUI;

using Xunit;

namespace Prowl.Editor.Test;

public class ScriptRenameTests : EditorTestHarness
{
    [Fact]
    public void RenamingAScript_OffersToRenameItsClass()
    {
        WriteScript("Gun.cs", "public class Gun { }");
        Assets.Refresh();
        Modal.PopAll();

        Assert.True(Assets.MoveAsset("Gun.cs", "Rifle.cs"));

        Assert.Equal(1, Modal.Count);
        Modal.PopAll();
    }

    [Fact]
    public void RenamingAScript_WithoutAMatchingClass_AsksNothing()
    {
        WriteScript("Helpers.cs", "public static class Tools { }");
        Assets.Refresh();
        Modal.PopAll();

        Assert.True(Assets.MoveAsset("Helpers.cs", "Utility.cs"));

        Assert.Equal(0, Modal.Count);
    }

    [Fact]
    public void MovingAScript_KeepingItsName_AsksNothing()
    {
        WriteScript("Gun.cs", "public class Gun { }");
        Assets.Refresh();
        Modal.PopAll();

        Assert.True(Assets.MoveAsset("Gun.cs", "Weapons/Gun.cs"));

        Assert.Equal(0, Modal.Count);
    }

    [Fact]
    public async Task AcceptingTheRename_RenamesTheClassAndKeepsItsOldSerializedName()
    {
        WriteScript("Gun.cs", "namespace Game\n{\n    public class Gun\n    {\n        public Gun() { }\n        public static Gun Make() => new Gun();\n    }\n}\n");
        Assets.Refresh();
        Assert.True(Assets.MoveAsset("Gun.cs", "Rifle.cs"));
        Modal.PopAll();

        await ScriptRenames.RenameAsync(AssetAbsolutePath("Gun.cs"), AssetAbsolutePath("Rifle.cs"), "Gun", "Rifle");

        Assert.Equal("namespace Game\n{\n    [Prowl.Echo.FormerlySerializedAs(\"Gun\")]\n    public class Rifle\n    {\n        public Rifle() { }\n        public static Rifle Make() => new Rifle();\n    }\n}\n",
            File.ReadAllText(AssetAbsolutePath("Rifle.cs")));
    }

    [Fact]
    public async Task RenamingAStaticClass_AddsNoFormerName()
    {
        WriteScript("Tools.cs", "public static class Tools { }\n");
        Assets.Refresh();
        Assert.True(Assets.MoveAsset("Tools.cs", "Helpers.cs"));
        Modal.PopAll();

        await ScriptRenames.RenameAsync(AssetAbsolutePath("Tools.cs"), AssetAbsolutePath("Helpers.cs"), "Tools", "Helpers");

        Assert.Equal("public static class Helpers { }\n", File.ReadAllText(AssetAbsolutePath("Helpers.cs")));
    }
}
