// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Assets;
using Prowl.Editor.ProjectSettings;

using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Build;

#if !EXCLUDE_DESKTOP_PLAYER

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
        string buildDataPath = Path.Combine(output.FullName, "GameData");
        Directory.CreateDirectory(buildDataPath);

        Debug.Log($"Compiling project assembly...");
        CompileProject(out string projectLib);

        Debug.Log($"Compiling player executable...");
        CompilePlayer(output, projectLib);

        Debug.Log($"Exporting and Packing assets to {buildDataPath}...");
        PackAssets(scenes, buildDataPath);

        Debug.Log($"Packing scenes...");
        PackScenes(scenes, buildDataPath);

        Debug.Log($"Preparing project settings...");
        PackProjectSettings(buildDataPath);

        Debug.Log($"Successfully built project.");

        // Open the Build folder
        AssetDatabase.OpenPath(output, type: FileOpenType.FileExplorer);
    }


    private void CompileProject(out string projectLib)
    {
        Project active = Project.Active!;

        DirectoryInfo temp = active.TempDirectory;
        DirectoryInfo bin = new DirectoryInfo(Path.Combine(temp.FullName, "bin"));
        DirectoryInfo project = new DirectoryInfo(Path.Combine(bin.FullName, "project", "build"));

        bool allowUnsafeBlocks = BuildProjectSettings.Instance.AllowUnsafeBlocks;
        bool enableAOT = BuildProjectSettings.Instance.EnableAOTCompilation;

        DotnetCompileOptions projectOptions = new DotnetCompileOptions()
        {
            isRelease = configuration == Configuration.Release,
            isSelfContained = false,
            outputExecutable = false,
        };

        active.GenerateGameProject(allowUnsafeBlocks, enableAOT);

        projectLib = Path.Combine(project.FullName, Project.GameCSProjectName + ".dll");

        if (!active.CompileGameAssembly(projectOptions, project))
        {
            Debug.LogError($"Failed to compile Project assembly.");
            return;
        }
    }


    private void CompilePlayer(DirectoryInfo output, string gameLibrary)
    {
        Project active = Project.Active!;

        DirectoryInfo temp = active.TempDirectory;
        DirectoryInfo player = new DirectoryInfo(Path.Combine(temp.FullName, "player"));

        string playerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Players", "Desktop");
        if (!Directory.Exists(playerPath))
        {
            Debug.LogError($"Failed to find Desktop player (at {playerPath})");
            return;
        }

        // Copy the template desktop player to the temp directory for builds
        CloneDirectory(playerPath, player.FullName);

        FileInfo? playerProj = player.GetFiles("*.csproj").FirstOrDefault();

        if (playerProj == null)
        {
            Debug.LogError($"Failed to find Desktop player project (at {player.FullName})");
            return;
        }

        bool enableAOT = BuildProjectSettings.Instance.EnableAOTCompilation;

        Assembly runtimeAssembly = typeof(Application).Assembly;

        ProjectCompiler.GenerateCSProject(
            "DesktopPlayer",
            playerProj,
            playerProj.Directory,
            RecursiveGetCSFiles(playerProj.Directory),
            ProjectCompiler.GetNonstandardReferences(runtimeAssembly)
                .Concat([runtimeAssembly]),
            [(Project.GameCSProjectName, gameLibrary)],
            true,
            enableAOT,
            true
        );

        DotnetCompileOptions playerOptions = new DotnetCompileOptions()
        {
            isRelease = configuration == Configuration.Release,
            isSelfContained = true,
            outputExecutable = true,
            publishAOT = enableAOT,
        };

        if (!ProjectCompiler.CompileCSProject(playerProj, output, playerOptions))
        {
            Debug.LogError($"Failed to compile player assembly.");
            return;
        }
    }


    private static List<FileInfo> RecursiveGetCSFiles(DirectoryInfo baseDirectory)
    {
        List<FileInfo> result = [];
        Stack<DirectoryInfo> directoriesToProcess = new([baseDirectory]);

        while (directoriesToProcess.Count > 0)
        {
            DirectoryInfo directory = directoriesToProcess.Pop();

            foreach (DirectoryInfo subdirectory in directory.GetDirectories())
                directoriesToProcess.Push(subdirectory);

            result.AddRange(directory.GetFiles("*.cs"));
        }

        return result;
    }


    static void CloneDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(destDir, fileName);
            File.Copy(file, destFile, true);
        }

        foreach (string directory in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(directory);
            string destSubDir = Path.Combine(destDir, dirName);
            CloneDirectory(directory, destSubDir);
        }
    }


    private void PackAssets(AssetRef<Scene>[] scenes, string dataPath)
    {
        if (assetPacking == AssetPacking.All)
        {
            AssetDatabase.ExportAllBuildPackages(new DirectoryInfo(dataPath));
        }
        else
        {
            HashSet<Guid> assets = [];
            foreach (AssetRef<Scene> scene in scenes)
                AssetDatabase.GetDependenciesDeep(scene.AssetID, ref assets);

            // Include all Shaders in the build for the time being
            foreach ((string, Guid, ushort) shader in AssetDatabase.GetAllAssetsOfType<Shader>())
                assets.Add(shader.Item2);

            AssetDatabase.ExportBuildPackages(assets.ToArray(), new DirectoryInfo(dataPath));
        }
    }


    private void PackScenes(AssetRef<Scene>[] scenes, string dataPath)
    {
        for (int i = 0; i < scenes.Length; i++)
        {
            BoundedLog($"Packing scene_{i}.prowl...");
            AssetRef<Scene> scene = scenes[i];
            SerializedProperty tag = Serializer.Serialize(scene.Res!);
            BinaryTagConverter.WriteToFile(tag, new FileInfo(Path.Combine(dataPath, $"scene_{i}.prowl")));
        }
    }


    private static MethodInfo? IterSearchFor(string methodName, Type type, BindingFlags flags, Type returnType, params Type[] paramTypes)
    {
        Type? searchType = type;

        do
        {
            MethodInfo? method = searchType.GetMethod(methodName, flags);

            if (method == null)
                continue;

            if (method.ReturnType != returnType)
                continue;

            if (!method.GetParameters().Select(x => x.ParameterType).SequenceEqual(paramTypes))
                continue;

            return method;
        }
        while ((searchType = searchType.BaseType) != null);

        return null;
    }


    private static void PackProjectSettings(string dataPath)
    {
        // Find all ScriptableSingletons with the specified location
        foreach (Type type in RuntimeUtils.GetTypesWithAttribute<FilePathAttribute>())
        {
            if (Attribute.GetCustomAttribute(type, typeof(FilePathAttribute)) is FilePathAttribute attribute)
            {
                if (attribute.FileLocation == FilePathAttribute.Location.Setting)
                {
                    MethodInfo? copyTo = IterSearchFor(
                        "CopyTo",
                        type,
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                        typeof(void),
                        typeof(string)
                    );

                    if (copyTo == null)
                    {
                        Debug.LogWarning($"Failed to find CopyTo method for {type.Name}. Skipping setting.");
                        continue;
                    }

                    copyTo.Invoke(null, [dataPath]);
                }
            }
        }
    }
}

#endif
