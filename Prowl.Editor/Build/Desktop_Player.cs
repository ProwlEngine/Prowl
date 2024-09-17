// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Assets;
using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Build;

public class Desktop_Player : ProjectBuilder
{
    public enum Target
    {
        [Text("Win x64")] win_x64,
        [Text("Win ARM x64")] win_arm64,
        [Text("Win x86")] win_x86,

        [Text("Linux x64")] linux_x64,
        [Text("Linux x86")] linux_x86,

        [Text("OSX")] osx,
        [Text("OSX x64")] osx_x64,
        [Text("OSX ARM x64")] osx_arm64,

        Universal
    }
    public readonly Target target = Target.win_x64;

    public enum Configuration
    {
        Debug,
        Release
    }
    public readonly Configuration configuration = Configuration.Release;

    public enum AssetPacking
    {
        [Text("All Assets")] All,
        [Text("Used Assets")] Used
    }
    public readonly AssetPacking assetPacking = AssetPacking.Used;


    protected override void Build(AssetRef<Scene>[] scenes, DirectoryInfo output)
    {
        output.Create();
        string BuildDataPath = Path.Combine(output.FullName, "GameData");
        Directory.CreateDirectory(BuildDataPath);


        BoundedLog($"Compiling project assembly to {output.FullName}...");
        if (!Project.Compile(Project.Active.Assembly_Proj.FullName, output, configuration == Configuration.Release))
        {
            Debug.LogError($"Failed to compile Project assembly!");
            return;
        }

        BoundedLog($"Exporting and Packing assets to {BuildDataPath}...");
        if (assetPacking == AssetPacking.All)
        {
            AssetDatabase.ExportAllBuildPackages(new DirectoryInfo(BuildDataPath));
        }
        else
        {
            HashSet<Guid> assets = [];
            foreach (var scene in scenes)
                AssetDatabase.GetDependenciesDeep(scene.AssetID, ref assets);

            // Include all Shaders in the build for the time being
            foreach (var shader in AssetDatabase.GetAllAssetsOfType<Shader>())
                assets.Add(shader.Item2);

            AssetDatabase.ExportBuildPackages(assets.ToArray(), new DirectoryInfo(BuildDataPath));
        }


        BoundedLog($"Packing scenes...");
        for (int i = 0; i < scenes.Length; i++)
        {
            BoundedLog($"Packing scene_{i}.prowl...");
            var scene = scenes[i];
            SerializedProperty tag = Serializer.Serialize(scene.Res!);
            BinaryTagConverter.WriteToFile(tag, new FileInfo(Path.Combine(BuildDataPath, $"scene_{i}.prowl")));
        }


        BoundedLog($"Preparing project settings...");
        // Find all ScriptableSingletons with the specified location
        foreach (var type in RuntimeUtils.GetTypesWithAttribute<FilePathAttribute>())
            if (Attribute.GetCustomAttribute(type, typeof(FilePathAttribute)) is FilePathAttribute attribute)
                if (attribute.FileLocation == FilePathAttribute.Location.Setting)
                {
                    // Use Reflection to find the CopyTo method
                    MethodInfo copyTo = type.BaseType.GetMethod("CopyTo", BindingFlags.Static | BindingFlags.NonPublic);
                    if (copyTo is null)
                    {
                        Debug.LogError($"Failed to find CopyTo method for {type.Name}");
                        continue;
                    }

                    // Invoke the CopyTo method
                    string? test = BuildDataPath;
                    copyTo.Invoke(null, [test]);
                }


        BoundedLog($"Copying Desktop player to {output.FullName}...");
        // Our executable folder contains "Players\Desktop" which we need to copy over the contents
        string playerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Players", "Desktop");
        if (!Directory.Exists(playerPath))
        {
            Debug.LogError($"Failed to find Desktop player at {playerPath}");
            return;
        }

        // Copy the contents of the Desktop player to the output directory, Files and Directories
        var allDirectories = Directory.GetDirectories(playerPath, "*", SearchOption.AllDirectories);
        foreach (var directory in allDirectories)
            Directory.CreateDirectory(directory.Replace(playerPath, output.FullName));

        var allFiles = Directory.GetFiles(playerPath, "*", SearchOption.AllDirectories);
        foreach (var file in allFiles)
            File.Copy(file, file.Replace(playerPath, output.FullName), true);

        // Strip files we dont need for our target
        if (target != Target.Universal)
            CleanupRuntimes(output);

        Debug.Log("**********************************************************************************************************************");
        Debug.Log($"Successfully built project!");

        // Open the Build folder
        AssetDatabase.OpenPath(output);
    }

    private void CleanupRuntimes(DirectoryInfo output)
    {
        string runtimesPath = Path.Combine(output.FullName, "runtimes");
        if (!Directory.Exists(runtimesPath))
            return;

        // Remove all runtimes except the one we need
        string targetRuntime = target.ToString().ToLower().Replace("_", "-");
        // Remove all but the target runtime
        foreach (var runtime in Directory.GetDirectories(runtimesPath))
            if (!runtime.Contains(targetRuntime))
                Directory.Delete(runtime, true);

        // Copy all remaining files into the root output directory
        foreach (var file in Directory.GetFiles(runtimesPath, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(output.FullName, Path.GetFileName(file)), true);

        // Remove the runtimes folder
        Directory.Delete(runtimesPath, true);
    }
}
