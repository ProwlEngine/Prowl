// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Assets;
using Prowl.Editor.ProjectSettings;

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
    public Target target = Target.win_x64;

    public enum Configuration
    {
        Debug,
        Release
    }
    public Configuration configuration = Configuration.Release;

    public enum AssetPacking
    {
        [Text("All Assets")] All,
        [Text("Used Assets")] Used
    }
    public AssetPacking assetPacking = AssetPacking.Used;


    protected override void Build(AssetRef<Scene>[] scenes, DirectoryInfo output)
    {
        output.Create();
        string BuildDataPath = Path.Combine(output.FullName, "GameData");
        Directory.CreateDirectory(BuildDataPath);

        BoundedLog($"Compiling project assembly to {output.FullName}...");

        Project active = Project.Active!;

        bool allowUnsafeBlocks = BuildProjectSettings.Instance.AllowUnsafeBlocks;
        bool enableAOT = BuildProjectSettings.Instance.EnableAOTCompilation;

        EntrypointScript entry = EntrypointScript.Create(typeof(Desktop.DesktopPlayer));

        active.GenerateGameProject(allowUnsafeBlocks, enableAOT, true, [entry.referenceAssembly], [entry.startupScript]);

        DotnetCompileOptions options = new DotnetCompileOptions()
        {
            isRelease = configuration == Configuration.Release,
            isSelfContained = true,
            outputExecutable = true,
            startupObject = entry.startupObjectName,
            platform = Platform.Linux,
            architecture = System.Runtime.InteropServices.Architecture.X64
        };

        if (!active.CompileGameAssembly(options, output))
        {
            Debug.LogError($"Failed to compile Project assembly.");
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
        {
            if (Attribute.GetCustomAttribute(type, typeof(FilePathAttribute)) is FilePathAttribute attribute)
            {
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
            }
        }

        // Strip files we dont need for our target
        if (target != Target.Universal)
            CleanupRuntimes(output);

        Debug.Log("**********************************************************************************************************************");
        Debug.Log($"Successfully built project.");

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
