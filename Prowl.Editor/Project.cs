// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;

using Prowl.Editor.Assets;
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

    public FileInfo Assembly_Proj;
    public FileInfo Assembly_DLL;

    public FileInfo Editor_Assembly_Proj;
    public FileInfo Editor_Assembly_DLL;

    public static event Action OnProjectChanged;

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

        Assembly_Proj.Refresh();
        Assembly_DLL.Refresh();
        Editor_Assembly_Proj.Refresh();
        Editor_Assembly_DLL.Refresh();
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

        Assembly_Proj = GetFile(@"CSharp.csproj");
        Assembly_DLL = GetFile(@"Temp/bin/Debug/net8.0/CSharp.dll");

        Editor_Assembly_Proj = GetFile(@"CSharp-Editor.csproj");
        Editor_Assembly_DLL = GetFile(@"Temp/bin/Debug/net8.0/CSharp-Editor.dll");
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
            Runtime.Debug.LogError(
                $"Invalid project '{project.Name}' at path '{project.ProjectPath}'. Validate that all core project directories are intact.");
            return false;
        }

        Active = project;
        Program.RegisterReloadOfExternalAssemblies();

        CreateTempDirectories(project);

        Application.DataPath = project.ProjectPath;

        CreateDefaults(project);
        AssetDatabase.AddRootFolder("Defaults");
        AssetDatabase.Update(false, true); // Ensure defaults are all loaded in

        AssetDatabase.AddRootFolder("Packages");
        AssetDatabase.Update(false, true); // Ensure packages are all loaded in

        AssetDatabase.AddRootFolder("Assets");
        AssetDatabase.Update(true,
            true); // Not that all folders are in we can unload anything thats not in the project anymore since last session

#warning TODO: Record last opened scene and try to open it
        SceneManager.InstantiateNewScene();

        OnProjectChanged?.Invoke();

        AssetDatabase.LoadPackages();

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
        foreach (string file in Assembly.GetExecutingAssembly().GetManifestResourceNames()
                                        .Where(x => x.StartsWith("Prowl.Editor.EmbeddedResources.DefaultAssets.")))
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

    /// <summary>
    /// Compiles the game ssembly
    /// </summary>
    /// <param name="csprojPath">Directory of the .csproj file to compile</param>
    /// <param name="isRelease">Is Release Build?</param>
    /// <returns>True if Compiling was successfull</returns>
    public static bool Compile(string csprojPath, DirectoryInfo output, bool isRelease = false)
    {
        if (!HasProject)
        {
            Runtime.Debug.LogError($"No Project Loaded...");
            return false;
        }

        // Reload CSProject Files
        BoundedLog($"Starting Project Compilation...");
        GenerateCSProjectFiles(Active, output);

        // Compile the Project Assembly using 'dotnet build'
        BoundedLog($"Compiling external assembly in {csprojPath}...");
        ProcessStartInfo processInfo = new()
        {
            WorkingDirectory = Path.GetDirectoryName(csprojPath),
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        // Default -> Windows
        processInfo.FileName = "cmd.exe";
        processInfo.Arguments = $"/c dotnet build \"{Path.GetFileName(csprojPath)}\"" +
                                (isRelease ? " --configuration Release" : "");

        if (RuntimeUtils.IsMac() || RuntimeUtils.IsLinux())
        {
            processInfo.FileName = "/bin/bash";
            processInfo.Arguments = $"-c \"dotnet build '{Path.GetFileName(csprojPath)}'\"" +
                                    (isRelease ? " --configuration Release" : "");
        }

        Process process = Process.Start(processInfo) ?? throw new Exception();
        process.OutputDataReceived += (sender, dataArgs) =>
        {
            string? data = dataArgs.Data;

            if (data is null)
                return;

            if (data.Contains("Warning", StringComparison.OrdinalIgnoreCase))
                Runtime.Debug.LogWarning(data);
            else if (data.Contains("Error", StringComparison.OrdinalIgnoreCase))
                Runtime.Debug.LogError(data);
            else
                Runtime.Debug.Log(data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.ErrorDataReceived += (sender, dataArgs) =>
        {
            if (dataArgs.Data is not null)
                Runtime.Debug.LogError(dataArgs.Data);
        };

        process.WaitForExit();

        int exitCode = process.ExitCode;
        process.Close();

        BoundedLog(
            $"""
             Exit Code: '{exitCode}'
             {(exitCode == 0 ? "Successfully" : "Failed to")} compile external assembly!
             """, exitCode == 0 ? LogSeverity.Success: LogSeverity.Error);
        return exitCode == 0;
    }

    #endregion


    #region Private Methods

    private static string GetIncludesFrom(IEnumerable<FileInfo> filePaths)
    {
        List<string> includeElements = new();

        foreach (FileInfo filePath in filePaths)
            includeElements.Add($"<Compile Include=\"{filePath.FullName}\" />");

        return string.Join("\n", includeElements);
    }

    private static void GenerateCSProjectFiles(Project project, DirectoryInfo output)
    {
        if (!HasProject) throw new Exception("No Project Loaded, Cannot generate CS Project Files!");

        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        Assembly gameEngineAssembly =
            loadedAssemblies.FirstOrDefault(assembly => assembly.GetName().Name == "Prowl.Runtime")
            ?? throw new Exception("Failed to find Prowl.Runtime Assembly!");
        Assembly gameEditorAssembly =
            loadedAssemblies.FirstOrDefault(assembly => assembly.GetName().Name == "Prowl.Editor")
            ?? throw new Exception("Failed to find Prowl.Editor Assembly!");

        // Get all references by Prowl.Runtime
        var references = gameEngineAssembly.GetReferencedAssemblies().Select(Assembly.Load).ToList();

        Runtime.Debug.Log($"Updating {project.Assembly_Proj.FullName}...");

        List<FileInfo> nonEditorScripts = new();
        List<FileInfo> editorScripts = new();
        Stack<DirectoryInfo> directoriesToProcess = new(project.AssetDirectory.GetDirectories());

        while (directoriesToProcess.Count > 0)
        {
            DirectoryInfo directory = directoriesToProcess.Pop();

            foreach (DirectoryInfo subdirectory in directory.GetDirectories())
                directoriesToProcess.Push(subdirectory);

            if (string.Equals(directory.Name, "Editor", StringComparison.OrdinalIgnoreCase))
                editorScripts.AddRange(directory.GetFiles("*.cs"));
            else
                nonEditorScripts.AddRange(directory.GetFiles("*.cs"));
        }

        string propertyGroupTemplate =
            @$"<PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <ProjectRoot>{project.ProjectPath}</ProjectRoot>
            </PropertyGroup>
            <PropertyGroup Condition="" '$(Configuration)' == 'Debug' "">
                <OutputPath>{output.FullName}</OutputPath>
            </PropertyGroup>
            <PropertyGroup Condition="" '$(Configuration)' == 'Release' "">
                <OutputPath>{output.FullName}</OutputPath>
            </PropertyGroup>";

        string referencesXML = string.Join("\n", references.Select(assembly =>
            $"<Reference Include=\"{assembly.GetName().Name}\">" +
            $"<HintPath>{assembly.Location}</HintPath>" +
            "<Private>false</Private>" +
            "</Reference>"));

        string gameproj =
            @$"<Project Sdk=""Microsoft.NET.Sdk"">
                {propertyGroupTemplate}
                <ItemGroup>
                    <Compile Remove=""**/*.cs"" />
                    {GetIncludesFrom(nonEditorScripts)}

                    <Reference Include=""Prowl.Runtime"">
                        <HintPath>{gameEngineAssembly.Location}</HintPath>
                        <Private>false</Private>
                    </Reference>
                    {referencesXML}
                </ItemGroup>
            </Project>";

        File.WriteAllText(project.Assembly_Proj.FullName, gameproj);

        Runtime.Debug.Log($"Updating {project.Editor_Assembly_Proj.FullName}...");

        string editorproj =
            @$"<Project Sdk=""Microsoft.NET.Sdk"">
                {propertyGroupTemplate}
                <ItemGroup>
                    <Compile Remove=""**/*.cs"" />
                    {GetIncludesFrom(editorScripts)}

                    <Reference Include=""Prowl.Editor"">
                        <HintPath>{gameEditorAssembly.Location}</HintPath>
                        <Private>false</Private>
                    </Reference>
                    <Reference Include=""Prowl.Runtime"">
                        <HintPath>{gameEngineAssembly.Location}</HintPath>
                        <Private>false</Private>
                    </Reference>
                    <Reference Include=""CSharp"">
                        <HintPath>{project.Assembly_Proj.FullName}</HintPath>
                        <Private>false</Private>
                    </Reference>
                </ItemGroup>
            </Project>";

        File.WriteAllText(project.Editor_Assembly_Proj.FullName, editorproj);

        Runtime.Debug.Log("Finished Updating Build Information");
    }

    public static void BoundedLog(string message, LogSeverity severity = LogSeverity.Normal)
    {
        Action<string> logFunc = severity switch
        {
            LogSeverity.Success => Runtime.Debug.LogSuccess,
            LogSeverity.Error   => Runtime.Debug.LogError,
            _                   => Runtime.Debug.Log
        };
        logFunc(
            "**********************************************************************************************************************");
        logFunc(message);
    }

    #endregion
}
