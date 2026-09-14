// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>Tests for project settings persistence (<see cref="ProjectSettingsRegistry"/> load/save).</summary>
public class ProjectSettingsTests : EditorTestHarness
{
    public ProjectSettingsTests()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();
    }

    // ResetToDefaults is not a button — it runs when a project is opened, before that project's
    // own settings load. Deriving the "defaults" from the live NavMeshAreas table would therefore
    // carry the PREVIOUS project's area names and costs into the new one.
    [Fact]
    public void NavigationSettings_ResetToDefaults_IgnoresTheLiveAreaTable()
    {
        const int Custom = 3; // built-in areas are immutable, so only custom ones can drift
        Prowl.Runtime.NavMeshAreas.SetAreaName(Custom, "Swamp");
        Prowl.Runtime.NavMeshAreas.SetAreaCost(Custom, 5f);

        var settings = EditorRegistries.GetSettings<NavigationSettings>();
        settings.ResetToDefaults();

        Assert.Equal(string.Empty, settings.AreaNames[Custom]);
        Assert.Equal(1f, settings.AreaCosts[Custom]);
        Assert.Equal("Walkable", settings.AreaNames[Prowl.Runtime.NavMeshAreas.Walkable]);
        Assert.Equal("Jump", settings.AreaNames[Prowl.Runtime.NavMeshAreas.Jump]);
    }

    // A project seeded from the settings page used to put Jump at cost 1 while headless code and
    // tests kept the runtime's 2, so link costs differed between the editor and everything else.
    [Fact]
    public void NavigationSettings_Defaults_MatchTheRuntimeAreaCosts()
    {
        var settings = EditorRegistries.GetSettings<NavigationSettings>();
        settings.ResetToDefaults();

        for (int area = 0; area < Prowl.Runtime.NavMeshAreas.MaxAreas; area++)
            Assert.Equal(Prowl.Runtime.NavMeshAreas.GetDefaultAreaCost(area), settings.AreaCosts[area]);
        Assert.Equal(2f, settings.AreaCosts[Prowl.Runtime.NavMeshAreas.Jump]);
    }

    // The mask field only lists defined areas. Unticking one must rewrite only those bits, or every
    // area defined afterwards starts out excluded for an agent that only ever excluded Jump.
    [Fact]
    public void NavMeshAreaMaskField_Pick_LeavesUndefinedAreasAlone()
    {
        const int Jump = Prowl.Runtime.NavMeshAreas.Jump;
        int[] shown = [0, 1, Jump];

        NavMeshAreaMask withoutJump = NavMeshAreaMaskPropertyEditor.ApplyPicked(NavMeshAreaMask.Everything, shown, [0, 1]);
        Assert.Equal(NavMeshAreaMask.FromMask(~(1u << Jump)), withoutJump);

        NavMeshAreaMask everything = NavMeshAreaMaskPropertyEditor.ApplyPicked(withoutJump, shown, [0, 1, Jump]);
        Assert.Equal(NavMeshAreaMask.Everything, everything);

        const int LaterArea = 7;
        NavMeshAreaMask excludedLater = NavMeshAreaMask.FromMask(~(1u << LaterArea));
        NavMeshAreaMask kept = NavMeshAreaMaskPropertyEditor.ApplyPicked(excludedLater, shown, [0]);
        Assert.False(kept.HasArea(LaterArea));
        Assert.False(kept.HasArea(Jump));
        Assert.True(kept.HasArea(0));
    }

    // Settings persist as Echo YAML: a saved value must survive a save/load round-trip.
    [Fact]
    public void SettingsSaveLoad_RoundTripsYaml()
    {
        EditorRegistries.GetSettings<GeneralSettings>().ProductName = "RoundTripped";
        EditorRegistries.SaveSettings();

        Assert.True(File.Exists(Path.Combine(Project.ProjectSettingsPath, "General.yaml")));

        EditorRegistries.GetSettings<GeneralSettings>().ProductName = "overwritten";
        EditorRegistries.OnProjectOpened();

        Assert.Equal("RoundTripped", EditorRegistries.GetSettings<GeneralSettings>().ProductName);
    }
}
