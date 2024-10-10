// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

namespace Prowl.Editor.Build;

public abstract class ProjectBuilder
{
    public void StartBuild(AssetRef<Scene>[] scenes, DirectoryInfo output)
    {
        if (!Project.HasProject)
        {
            Debug.LogError($"No Project Loaded.");
            return;
        }

        if (!AreScenesValid(scenes))
            return;

        Debug.Log($"Starting Project Build.");

        if (output.Exists)
        {
            // Debug.Log($"Deleting existing build directory.");
            output.Delete(true);
        }

        output.Create();

        try
        {
            Build(scenes, output);
        }
        catch (Exception e)
        {
            Debug.LogException(new Exception($"Failed to build project", e));
        }
    }

    protected abstract void Build(AssetRef<Scene>[] scenes, DirectoryInfo output);

    private bool AreScenesValid(AssetRef<Scene>[] scenes)
    {
        if (scenes == null || scenes.Length == 0)
        {
            Debug.LogError($"No scenes assigned in the Build Project Settings Window");
            return false;
        }

        // Make sure all scenes are valid
        foreach (var scene in scenes)
            if (!scene.IsAvailable)
            {
                Debug.LogError($"Scene {scene.Name} is not available, please assign a valid available scene");
                return false;
            }

        return true;
    }
}
