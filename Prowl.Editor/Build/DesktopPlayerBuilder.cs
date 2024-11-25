// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;
using System.Runtime.InteropServices;

using Prowl.Editor.Assets;
using Prowl.Editor.ProjectSettings;

using Prowl.Runtime;
using Prowl.Runtime.Utils;
using Prowl.Runtime.Rendering;
using Prowl.Echo;

namespace Prowl.Editor.Build;

#if !EXCLUDE_DESKTOP_PLAYER

public class Desktop_Player : ProjectBuilder
{
    public Platform platform = RuntimeUtils.GetOSPlatform();
    public Architecture architecture = RuntimeInformation.OSArchitecture;

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
        string buildDataPath = Path.Combine(output.FullName, "GameData");
        Directory.CreateDirectory(buildDataPath);

        // Debug.Log($"Compiling project assembly.");
        CompileProject(out string projectLib);

        // Debug.Log($"Compiling player executable.");
        CompilePlayer(output, projectLib);

        // Debug.Log($"Exporting and Packing assets to {buildDataPath}.");
        PackAssets(scenes, buildDataPath);

        // Debug.Log($"Packing scenes.");
        PackScenes(scenes, buildDataPath);

        // Debug.Log($"Preparing project settings.");
        PackProjectSettings(buildDataPath);

        Debug.Log($"Successfully built project to {output}");

        // Open the Build folder
        AssetDatabase.OpenPath(output, type: FileOpenType.FileExplorer);
    }


    private void CompileProject(out string projectLib)
    {
        Project active = Project.Active!;

        DirectoryInfo temp = active.TempDirectory;
        DirectoryInfo bin = new DirectoryInfo(Path.Combine(temp.FullName, "bin"));
        DirectoryInfo project = new DirectoryInfo(Path.Combine(bin.FullName, Project.GameCSProjectName, "Build"));

        DirectoryInfo tmpProject = new DirectoryInfo(Path.Combine(temp.FullName, "obj", Project.GameCSProjectName));

        active.GenerateGameProject();

        projectLib = Path.Combine(project.FullName, Project.GameCSProjectName + ".dll");

        DotnetCompileOptions projectOptions = new DotnetCompileOptions()
        {
            isRelease = configuration == Configuration.Release,
            isSelfContained = false,
            architecture = architecture,
            platform = platform,
            outputPath = project,
            tempPath = tmpProject
        };

        if (!active.CompileGameAssembly(projectOptions))
        {
            Debug.LogError($"Failed to compile Project assembly.");
            return;
        }
    }


    private void CompilePlayer(DirectoryInfo output, string gameLibrary)
    {
        Project active = Project.Active!;

        DirectoryInfo temp = active.TempDirectory;
        DirectoryInfo bin = new DirectoryInfo(Path.Combine(temp.FullName, "bin"));
        DirectoryInfo player = new DirectoryInfo(Path.Combine(temp.FullName, "DesktopPlayer"));
        DirectoryInfo tmpPlayer = new DirectoryInfo(Path.Combine(temp.FullName, "obj", "DesktopPlayer"));

        string playerSource = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Players", "Desktop");
        if (!Directory.Exists(playerSource))
        {
            Debug.LogError($"Failed to find Desktop player (at {playerSource})");
            return;
        }

        // Copy the template desktop player to the temp directory for builds
        CloneDirectory(playerSource, player.FullName);

        FileInfo? playerProj = player.GetFiles("*.csproj").FirstOrDefault();

        if (playerProj == null)
        {
            Debug.LogError($"Failed to find Desktop player project (at {player.FullName})");
            return;
        }

        bool publishAOT = BuildProjectSettings.Instance.EnableAOTCompilation;

        Assembly runtimeAssembly = typeof(Application).Assembly;

        CSProjectOptions options = new();

        options.OutputName = "DesktopPlayer";
        options.OutputExecutable = true;
        options.AllowUnsafeCode = true;
        options.EnableAOTCompatibility = true;
        options.PublishAOT = publishAOT;

        options.OutputPath = bin;
        options.IntermediateOutputPath = tmpPlayer;
        options.ReferencesArePrivate = true;

        options.AddReference(runtimeAssembly, true);
        options.AddReference(gameLibrary);

        options.GenerateCSProject(
            playerProj,
            playerProj.Directory!,
            RecursiveGetCSFiles(playerProj.Directory!, ("bin", false), ("obj", false))
        );

        DotnetCompileOptions playerOptions = new DotnetCompileOptions()
        {
            isRelease = configuration == Configuration.Release,
            isSelfContained = true,
            architecture = architecture,
            platform = platform,
            outputPath = output,
            tempPath = tmpPlayer
        };

        if (ProjectCompiler.CompileCSProject(playerProj, playerOptions) != 0)
        {
            Debug.LogError($"Failed to compile player assembly.");
            return;
        }
    }


    private static List<FileInfo> RecursiveGetCSFiles(DirectoryInfo baseDirectory, params (string, bool)[] checkFolders)
    {
        List<FileInfo> result = [];
        Stack<DirectoryInfo> directoriesToProcess = new([baseDirectory]);

        while (directoriesToProcess.Count > 0)
        {
            DirectoryInfo directory = directoriesToProcess.Pop();

            foreach (DirectoryInfo subdirectory in directory.GetDirectories())
                directoriesToProcess.Push(subdirectory);

            bool isValid = true;

            foreach ((string folderName, bool include) in checkFolders)
            {
                bool hasParent = HasParent(directory, baseDirectory, folderName);

                if (hasParent != include)
                {
                    isValid = false;
                    break;
                }

                if (hasParent && include)
                {
                    isValid = true;
                    break;
                }
            }

            if (!isValid)
                continue;

            result.AddRange(directory.GetFiles("*.cs"));
        }

        return result;
    }


    private static bool HasParent(DirectoryInfo? directory, DirectoryInfo root, string name)
    {
        while (directory != null && directory.FullName != root.FullName)
        {
            if (string.Equals(directory.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;

            directory = directory.Parent;
        }

        return false;
    }


    static void CloneDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);

        foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));

        foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
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


    private static void PackScenes(AssetRef<Scene>[] scenes, string dataPath)
    {
        for (int i = 0; i < scenes.Length; i++)
        {
            // Debug.Log($"Packing scene_{i}.prowl.");
            AssetRef<Scene> scene = scenes[i];
            EchoObject tag = Serializer.Serialize(scene.Res!);
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
