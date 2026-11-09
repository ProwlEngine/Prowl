// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Analyzers;
using Prowl.Editor.Projects.Scripting;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The analyzer that refuses comparing a live world with a scene asset, and warns on a component field holding a
/// scene asset where an AssetRef only names it.
/// </summary>
[Trait("Category", "Build")]
public class SceneAnalyzerTests : EditorTestHarness
{
    private ScriptCompiler.CompileResult Compile(string members)
    {
        WriteScript("Comp.cs", $"using Prowl.Runtime;\nusing Prowl.Runtime.Resources;\npublic class Comp : Component {{ {members} }}");
        return ScriptCompiler.CompileAll(Project);
    }

    [Theory]
    [InlineData("bool N(Scene level) => GameObject.Scene == level;")]
    [InlineData("bool N(Scene level, SceneInstance world) => level != world;")]
    [InlineData("bool N(Scene level, SceneInstance world) => ReferenceEquals(world, level);")]
    [InlineData("bool N(Scene level, SceneInstance world) => world.Equals(level);")]
    [InlineData("bool N(Scene level, SceneInstance world) => object.Equals(level, world);")]
    public void RefusesComparingAWorldWithAnAsset(string members)
    {
        ScriptCompiler.CompileResult result = Compile(members);
        Assert.False(result.Success);
        Assert.Contains(SceneAnalyzer.WorldComparedWithAssetId, result.Errors);
    }

    [Theory]
    [InlineData("public Scene? NextLevel;")]
    [InlineData("[Prowl.Echo.SerializeField] private Scene? _next;")]
    [InlineData("public Scene[] Levels = [];")]
    [InlineData("public System.Collections.Generic.List<Scene> Levels = [];")]
    public void WarnsOnAComponentFieldHoldingASceneAsset(string members)
    {
        ScriptCompiler.CompileResult result = Compile(members);
        Assert.True(result.Success, result.Errors);
        Assert.Contains(result.Diagnostics, d => d.Id == SceneAnalyzer.HardSceneFieldId);
    }

    [Theory]
    [InlineData("bool N(Scene level) => GameObject.Scene.IsValid() && GameObject.Scene.Source == level;")]
    [InlineData("bool N(SceneInstance a, SceneInstance b) => a == b;")]
    [InlineData("public AssetRef<Scene> NextLevel;")]
    [InlineData("public System.Collections.Generic.List<AssetRef<Scene>> Levels = [];")]
    [InlineData("private Scene? _cached;")]
    [InlineData("[System.NonSerialized] public Scene? Loaded;")]
    public void LeavesTheRightFormsAlone(string members)
    {
        ScriptCompiler.CompileResult result = Compile(members);
        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("PROWLSC"));
    }
}
