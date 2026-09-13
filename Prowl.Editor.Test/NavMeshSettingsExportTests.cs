// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;
using Prowl.Runtime.Navigation;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Exercises the build export path for the navigation project settings: writing the same
/// <c>TypeMode.None</c> YAML the real build pipeline produces (see
/// <c>BuildPipeline.ExportSettings</c>) and loading it back through
/// <see cref="PlayerSettingsLoader"/>, the same as a shipped player does at startup. Agent type ids
/// and area indices must survive that round trip unchanged even after a rename, since a surface or
/// agent baked/authored before the rename keeps pointing at the same id or slot.
/// </summary>
public class NavMeshSettingsExportTests : EditorTestHarness
{
    public NavMeshSettingsExportTests()
    {
        EditorRegistries.Initialize();
        NavMeshAgentTypes.ResetDefault();
        NavMeshAreas.ResetDefault();
    }

    public override void Dispose()
    {
        NavMeshAgentTypes.ResetDefault();
        NavMeshAreas.ResetDefault();
        base.Dispose();
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ProwlNavSettingsTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void ExportLikeBuildPipeline<T>(T settings, string dir) where T : ProjectSettingsBase
    {
        EchoObject echo = Serializer.Serialize(typeof(T), settings, TypeMode.None);
        File.WriteAllText(Path.Combine(dir, $"{typeof(T).Name}.yaml"), echo.WriteToYaml());
    }

    [Fact]
    public void AgentTypes_SurviveExportAndLoadAfterRename()
    {
        var settings = EditorRegistries.GetSettings<NavMeshAgentTypeSettings>();
        settings.AgentTypes.Add(new NavMeshAgentTypeInfo
        {
            Id = 500,
            Name = "Original",
            AgentRadius = 0.3f,
            AgentHeight = 1.0f,
            MaxSlopeAngle = 30f,
            MaxStepHeight = 0.2f,
        });
        settings.AgentTypes[^1].Name = "Renamed";
        settings.Apply();

        string dir = NewTempDir();
        try
        {
            ExportLikeBuildPipeline(settings, dir);

            // Simulate a fresh player process: no in-memory state carried over from the editor.
            NavMeshAgentTypes.ResetDefault();
            PlayerSettingsLoader.Apply(dir);

            NavMeshAgentTypeInfo? loaded = NavMeshAgentTypes.GetById(500);
            Assert.NotNull(loaded);
            Assert.Equal("Renamed", loaded!.Name);
            Assert.Equal(0.3f, loaded.AgentRadius);

            // The built-in type must still be there too.
            Assert.NotNull(NavMeshAgentTypes.GetById(NavMeshAgentTypes.HumanoidId));
        }
        finally { TryDeleteDir(dir); }
    }

    [Fact]
    public void Areas_SurviveExportAndLoadAfterRename()
    {
        var settings = EditorRegistries.GetSettings<NavMeshAreaSettings>();
        settings.Areas[5].Name = "Mud";
        settings.Areas[5].Cost = 3f;
        settings.Areas[5].Name = "Renamed Mud";
        settings.Apply();

        string dir = NewTempDir();
        try
        {
            ExportLikeBuildPipeline(settings, dir);

            NavMeshAreas.ResetDefault();
            PlayerSettingsLoader.Apply(dir);

            Assert.Equal("Renamed Mud", NavMeshAreas.GetAreaName(5));
            Assert.Equal(3f, NavMeshAreas.GetAreaCost(5));

            // Reserved slots keep their fixed names regardless of what was exported.
            Assert.Equal("Walkable", NavMeshAreas.GetAreaName(NavMeshAreas.Walkable));
            Assert.Equal("Not Walkable", NavMeshAreas.GetAreaName(NavMeshAreas.NotWalkable));
            Assert.Equal("Jump", NavMeshAreas.GetAreaName(NavMeshAreas.Jump));
        }
        finally { TryDeleteDir(dir); }
    }
}
