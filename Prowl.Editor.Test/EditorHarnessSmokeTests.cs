// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>Smoke test proving the headless editor harness writes an authored prefab under Assets.</summary>
public class EditorHarnessSmokeTests : EditorTestHarness
{
    [Fact]
    public void Prefab_FileIsWrittenUnderAssets()
    {
        var source = new GameObject("Thing");
        CreatePrefabAsset(source, "Sub/Thing.prefab");

        Assert.True(File.Exists(AssetAbsolutePath("Sub/Thing.prefab")));
    }
}
