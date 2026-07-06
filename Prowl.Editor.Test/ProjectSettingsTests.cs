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
        ProjectSettingsRegistry.Initialize();
        ProjectSettingsRegistry.OnProjectOpened();
    }

    // Settings persist as Echo YAML: a saved value must survive a save/load round-trip.
    [Fact]
    public void SettingsSaveLoad_RoundTripsYaml()
    {
        ProjectSettingsRegistry.Get<GeneralSettings>().ProductName = "RoundTripped";
        ProjectSettingsRegistry.SaveAll();

        Assert.True(File.Exists(Path.Combine(Project.ProjectSettingsPath, "General.yaml")));

        ProjectSettingsRegistry.Get<GeneralSettings>().ProductName = "overwritten";
        ProjectSettingsRegistry.LoadAll();

        Assert.Equal("RoundTripped", ProjectSettingsRegistry.Get<GeneralSettings>().ProductName);
    }
}
