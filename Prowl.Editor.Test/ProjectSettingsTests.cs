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

    // When only a JSON settings file exists (no YAML), loading must fall back to it.
    [Fact]
    public void SettingsLoad_FallsBackToJson_WhenYamlMissing()
    {
        File.WriteAllText(Path.Combine(Project.ProjectSettingsPath, "General.json"), "{\"ProductName\":\"FromJson\"}");
        File.Delete(Path.Combine(Project.ProjectSettingsPath, "General.yaml")); // ensure absent

        ProjectSettingsRegistry.Get<GeneralSettings>().ProductName = "overwritten";
        ProjectSettingsRegistry.LoadAll();

        Assert.Equal("FromJson", ProjectSettingsRegistry.Get<GeneralSettings>().ProductName);
    }
}
