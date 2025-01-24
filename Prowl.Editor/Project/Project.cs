// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;

using Prowl.Echo;
using Prowl.Editor.Assets;
using Prowl.Editor.ProjectSettings;
using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Editor;

/// <summary>
/// Project is a static class that handles all Project related information and actions
/// </summary>
public class Project
{
    #region Public Properties

    public DirectoryInfo ProjectDirectory;
    public bool Exists => ProjectDirectory.Exists;
    public string Name => ProjectDirectory.Name;
    public string ProjectPath => ProjectDirectory.FullName;

    public DirectoryInfo AssetDirectory;
    public DirectoryInfo LibraryDirectory;
    public DirectoryInfo DefaultsDirectory;
    public DirectoryInfo PackagesDirectory;
    public DirectoryInfo TempDirectory;

    public static string GameCSProjectName => "CSharp";
    public FileInfo GameCSProject;

    public static string EditorCSProjectName => "CSharp-Editor";
    public FileInfo EditorCSProject;

    public static event Action? OnProjectChanged;

    public static Project? Active { get; private set; }
    public static bool HasProject => Active is not null;

    #endregion

    #region Public Methods


    internal void Refresh()
    {
        ProjectDirectory.Refresh();
        AssetDirectory.Refresh();
        LibraryDirectory.Refresh();
        DefaultsDirectory.Refresh();
        PackagesDirectory.Refresh();
        TempDirectory.Refresh();

        GameCSProject.Refresh();
        EditorCSProject.Refresh();
    }

    private DirectoryInfo GetSubdirectory(string subdirectoryPath)
    {
        return new DirectoryInfo(Path.Combine(ProjectPath, subdirectoryPath));
    }

    private FileInfo GetFile(string filePath)
    {
        return new FileInfo(Path.Combine(ProjectPath, filePath));
    }

    public Project(DirectoryInfo directory)
    {
        ProjectDirectory = directory;

        AssetDirectory = GetSubdirectory(@"Assets");
        LibraryDirectory = GetSubdirectory(@"Library");
        DefaultsDirectory = GetSubdirectory(@"Defaults");
        PackagesDirectory = GetSubdirectory(@"Packages");
        TempDirectory = GetSubdirectory(@"Temp");

        GameCSProject = GetFile($"{GameCSProjectName}.csproj");
        EditorCSProject = GetFile($"{EditorCSProjectName}.csproj");
    }

    /// <summary>
    /// Loads a project from a given name
    /// </summary>
    /// <param name="project">The project to load</param>
    /// <exception cref="UnauthorizedAccessException">Throws if Project Name doesn't exist</exception>
    /// <exception cref="ArgumentNullException">Throws if projectName is null or empty</exception>
    public static bool Open(Project project)
    {
        if (!project.IsValid())
        {
            Runtime.Debug.LogError($"Invalid project '{project.Name}' at path '{project.ProjectPath}'. Validate that all core project directories are intact.");
            return false;
        }

        Active = project;

        project.ProjectDirectory.LastAccessTime = DateTime.Now;
        project.ProjectDirectory.Refresh();

        Program.RegisterReloadOfExternalAssemblies();

        CreateTempDirectories(project);

        Application.DataPath = project.ProjectPath;

        CreateDefaults(project);
        AssetDatabase.AddRootFolder("Defaults");
        AssetDatabase.Update(false, true); // Ensure defaults are all loaded in

        AssetDatabase.AddRootFolder("Packages");
        AssetDatabase.Update(false, true); // Ensure packages are all loaded in

        AssetDatabase.AddRootFolder("Assets");
        AssetDatabase.Update(true, true); // Not that all folders are in we can unload anything thats not in the project anymore since last session

#warning TODO: Record last opened scene and try to open it
        SceneManager.InstantiateNewScene();
        project.LoadTempScene();

        OnProjectChanged?.Invoke();

        AssetDatabase.ValidatePackages().Wait();

        return true;
    }

    /// <summary>
    /// Will create a new Project file and its main Folders in a Dedicated folder
    /// </summary>
    /// <param name="projectPath">Path for the new project</param>
    public static Project CreateNew(DirectoryInfo projectPath)
    {
        Project project = new Project(projectPath);

        if (project.Exists)
        {
            Runtime.Debug.LogError($"Project at path {project.ProjectPath} already exists.");
            return project;
        }

        // Create Path
        project.ProjectDirectory.Create();

        // Create Assets Folder
        CreateTempDirectories(project);
        CreateDefaults(project);

        // Create Config Folder
        string configPath = Path.Combine(project.ProjectPath, @"Config");
        Directory.CreateDirectory(configPath);

        return project;
    }


    public bool IsValid()
    {
        ProjectDirectory.Refresh();
        AssetDirectory.Refresh();

        return ProjectDirectory.Exists &&
            AssetDirectory.Exists;
    }

    internal void SaveTempScene()
    {
        DirectoryInfo temp = TempDirectory;
        EchoObject scene = Serializer.Serialize(SceneManager.Current.Res!);
        var file = new FileInfo(Path.Combine(temp.FullName, "temp_scene.tmp"));
        if (file.Exists)
            file.Delete();
        scene.WriteToBinary(file);
    }

    internal void LoadTempScene()
    {
        DirectoryInfo temp = TempDirectory;
        var file = new FileInfo(Path.Combine(temp.FullName, "temp_scene.tmp"));
        if (file.Exists)
        {
            try
            {
                EchoObject echoScene = EchoObject.ReadFromBinary(file);
                Scene scene = Serializer.Deserialize<Scene>(echoScene);
                SceneManager.LoadScene(scene);
            }
            catch (Exception e)
            {
                Runtime.Debug.LogError($"Failed to load last opened scene: {e.Message}");
            }
        }
    }


    private static void CreateTempDirectories(Project project)
    {
        project.ProjectDirectory.CreateSubdirectory(@"Assets");
        project.ProjectDirectory.CreateSubdirectory(@"Library");
        project.ProjectDirectory.CreateSubdirectory(@"Packages");
    }


    private static void CreateDefaults(Project project)
    {
        if (!project.DefaultsDirectory.Exists)
            project.DefaultsDirectory.Create();

#warning TODO: Only copy if the file doesn't exist, or if somehow if the engine version is different or something...

        // Copy embedded defaults to rootFolder, this is just actual Files, so Image.png, not the asset variants
        foreach (string file in Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(x => x.StartsWith("Prowl.Editor.EmbeddedResources.DefaultAssets.")))
        {
            string[] nodes = file.Split('.');
            string fileName = nodes[^2];
            string fileExtension = nodes[^1];

            string filePath = Path.Combine(project.DefaultsDirectory.FullName, fileName + "." + fileExtension);

            if (File.Exists(filePath))
                File.Delete(filePath);

            //if (!File.Exists(filePath))
            {
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(file)!;
                using FileStream fileStream = File.Create(filePath);
                stream.CopyTo(fileStream);
            }
        }
    }


    public void GenerateGameProject()
    {
        Assembly runtimeAssembly = typeof(Application).Assembly;

        DirectoryInfo bin = new DirectoryInfo(Path.Combine(TempDirectory.FullName, "bin"));
        DirectoryInfo tmpProject = new DirectoryInfo(Path.Combine(TempDirectory.FullName, "obj", GameCSProjectName));

        CSProjectOptions options = new();

        options.OutputExecutable = false;
        options.OutputName = GameCSProjectName;
        options.AllowUnsafeCode = BuildProjectSettings.Instance.AllowUnsafeBlocks;
        options.EnableAOTCompatibility = BuildProjectSettings.Instance.EnableAOTCompilation;
        options.PublishAOT = false;
        options.OutputPath = bin;
        options.IntermediateOutputPath = tmpProject;

        options.AddReference(runtimeAssembly, true);

        options.GenerateCSProject(
            GameCSProject,
            ProjectDirectory,
            RecursiveGetCSFiles(AssetDirectory, ("Editor", false), ("bin", false), ("obj", false))
        );

        ProjectAssemblyReferences.Instance.AddAssembly(GameCSProjectName);
    }


    /// <summary>
    /// Compiles the game assembly
    /// </summary>
    /// <returns>True if Compiling was sucessful</returns>
    public bool CompileGameAssembly(DotnetCompileOptions options)
    {
        return ProjectCompiler.CompileCSProject(GameCSProject, options) == 0;
    }


    public void GenerateEditorProject(Assembly gameAssembly)
    {
        Assembly runtimeAssembly = typeof(Application).Assembly;

        CSProjectOptions options = new();

        options.OutputExecutable = false;
        options.OutputName = EditorCSProjectName;
        options.AllowUnsafeCode = BuildProjectSettings.Instance.AllowUnsafeBlocks;
        options.EnableAOTCompatibility = false;
        options.PublishAOT = false;
        options.OutputPath = new DirectoryInfo(Path.Combine(TempDirectory.FullName, "bin"));
        options.IntermediateOutputPath = new DirectoryInfo(Path.Combine(TempDirectory.FullName, "obj", EditorCSProjectName));

        options.AddReference(runtimeAssembly, true);
        options.AddReference(gameAssembly, false);
        options.AddReference(typeof(Program).Assembly, false); // Just the editor assembly with none of its fancy references

        options.GenerateCSProject(
            EditorCSProject,
            ProjectDirectory,
            RecursiveGetCSFiles(AssetDirectory, ("Editor", true), ("bin", false), ("obj", false))
        );
    }


    /// <summary>
    /// Compiles the editor assembly
    /// </summary>
    /// <returns>True if Compiling was sucessful</returns>
    public bool CompileEditorAssembly(DotnetCompileOptions options)
    {
        return ProjectCompiler.CompileCSProject(EditorCSProject, options) == 0;
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


    public void NukeTemp()
    {
        foreach (FileInfo file in TempDirectory.EnumerateFiles())
            file.Delete();

        foreach (DirectoryInfo directory in TempDirectory.EnumerateDirectories())
            Directory.Delete(directory.FullName, true);
    }

    #endregion
}
