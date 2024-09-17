// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Utilities;
using Prowl.Runtime;

namespace Prowl.Editor.Build
{
    public abstract class ProjectBuilder
    {
        public void StartBuild(AssetRef<Scene>[] scenes, DirectoryInfo output)
        {
            if (!Project.HasProject)
            {
                Debug.LogError($"No Project Loaded...");
                return;
            }

            if (!AreScenesValid(scenes))
                return;

            Debug.Log($"Starting Project Build...");
            Project.BoundedLog($"Creating Directories...");

            if (output.Exists)
            {
                Project.BoundedLog($"Deleting existing build directory...");
                output.Delete(true);
            }

            try
            {
                Build(scenes, output);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to build project: {e.Message}");
            }
        }

        protected abstract void Build(AssetRef<Scene>[] scenes, DirectoryInfo output);

        private bool AreScenesValid(AssetRef<Scene>[] scenes)
        {
            if (scenes == null)
            {
                Debug.LogError($"At least 1 Scene must be assigned in the Build Project Settings Window");
                return false;
            }

            if (scenes.Length <= 0)
            {
                Debug.LogError($"At least 1 Scene must be assigned in the Build Project Settings Window");
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

        public static IEnumerable<ProjectBuilder> GetAll()
        {
            foreach (Assembly editorAssembly in AssemblyManager.ExternalAssemblies.Append(typeof(Program).Assembly))
            {
                List<Type> derivedTypes = EditorUtils.GetDerivedTypes(typeof(ProjectBuilder), editorAssembly);
                foreach (Type type in derivedTypes)
                {
                    if (type.IsAbstract)
                        continue;

                    yield return (ProjectBuilder)Activator.CreateInstance(type);
                }
            }
        }
    }
}
