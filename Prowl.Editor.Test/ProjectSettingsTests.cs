// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Projects.Settings;

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
