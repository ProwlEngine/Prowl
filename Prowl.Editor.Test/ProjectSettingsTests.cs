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
