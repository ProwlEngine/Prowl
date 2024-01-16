using Prowl.Editor.Assets;
using Prowl.Editor.EditorWindows;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace Prowl.Editor;

/// <summary>
/// Project is a static class that handles all Project related information and actions
/// </summary>
public static class Project
{
    #region Public Properties
    public static BuildSettings BuildSettings => Project.ProjectSettings.GetSetting<BuildSettings>();


    public static string Projects_Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prowl", "Projects");
    public static string ProjectDirectory => Path.Combine(Projects_Directory, Name);
    public static string ProjectAssetDirectory => Path.Combine(ProjectDirectory, @"Assets");
    public static string ProjectDefaultsDirectory => Path.Combine(ProjectDirectory, @"Defaults");
    public static string ProjectPackagesDirectory => Path.Combine(ProjectDirectory, @"Packages");
    public static string TempDirectory => Path.Combine(Project.ProjectDirectory, @"Temp");

    public static string Assembly_Proj => Path.Combine(ProjectDirectory, @"CSharp.csproj");
    public static string Assembly_DLL => Path.Combine(ProjectDirectory, @"Temp\bin\Debug\net8.0\CSharp.dll");

    public static string Editor_Assembly_Proj => Path.Combine(ProjectDirectory, @"CSharp-Editor.csproj");
    public static string Editor_Assembly_DLL => Path.Combine(ProjectDirectory, @"Temp\bin\Debug\net8.0\CSharp-Editor.dll");


    public static event Action OnProjectChanged;
    public static bool HasProject { get; private set; } = false;
    public static string Name { get; private set; } = "";
    public static ProjectSettings ProjectSettings;
    #endregion

    #region Public Methods

    /// <summary>
    /// Loads a project from a given name
    /// </summary>
    /// <param name="projectName">Name of project to load</param>
    /// <exception cref="UnauthorizedAccessException">Throws if Project Name doesn't exist</exception>
    /// <exception cref="ArgumentNullException">Throws if projectName is null or empty</exception>
    public static void Open(string projectName)
    {
        var projectDir = GetPath(projectName);
        if (!projectDir.Exists)
        {
            Runtime.Debug.LogError($"A project with the name {projectName} does not exists at path {projectDir.FullName}");
            return;
        }

        AssetDatabase.Clear();

        Name = projectName;
        HasProject = true;
        (EditorApplication.Instance as EditorApplication).RegisterReloadOfExternalAssemblies();
        ProjectSettings = ProjectSettings.Load();

        DefaultAssets.CreateDefaults("Defaults");
        AssetDatabase.AddRootFolder("Defaults");
        AssetDatabase.ReimportAll();
        AssetDatabase.AddRootFolder("Packages");

        AssetDatabase.AddRootFolder("Assets");
        AssetDatabase.CleanupCache();

        OnProjectChanged?.Invoke();
    }

    /// <summary>
    /// Will create a new Project file and its main Folders in a Dedicated folder
    /// </summary>
    /// <param name="ProjectName">Name of project</param>
    public static void CreateNew(string ProjectName)
    {
        Name = ProjectName;
        var projectDir = GetPath(ProjectName);
        if (projectDir.Exists)
        {
            Runtime.Debug.LogError($"A project with the name {ProjectName} already exists.");
            return;
        }

        // Create Path
        projectDir.Create();

        // Create Assets Folder
        Directory.CreateDirectory(Path.Combine(projectDir.FullName, @"Assets"));
        DefaultAssets.CreateDefaults("Defaults");
        Directory.CreateDirectory(Path.Combine(projectDir.FullName, @"Library"));
        Directory.CreateDirectory(Path.Combine(projectDir.FullName, @"Packages"));

        // Create Config Folder
        string configPath = Path.Combine(projectDir.FullName, @"Config");
        Directory.CreateDirectory(configPath);

        ProjectSettings = new();
        ProjectSettings.Save();
    }

    /// <summary>
    /// Compiles the game ssembly
    /// </summary>
    /// <param name="dir">Directory of the .csproj file to compile</param>
    /// <param name="isRelease">Is Release Build?</param>
    /// <returns>True if Compiling was successfull</returns>
    public static bool Compile(string dir, bool isRelease = false)
    {
        if (!HasProject)
        {
            Runtime.Debug.LogError($"No Project Loaded...");
            return false;
        }

        // Reload CSProject Files
        BoundedLog($"Starting Project Compilation...");
        GenerateCSProjectFiles();

        // Compile the Project Assembly using 'dotnet build'
        BoundedLog($"Compiling external assembly in {dir}...");
        ProcessStartInfo processInfo = new()
        {
            WorkingDirectory = Path.GetDirectoryName(dir),
            FileName = "cmd.exe",
            Arguments = $"/c dotnet build \"{Path.GetFileName(dir)}\"" + (isRelease ? " --configuration Release" : ""),
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
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

        BoundedLog($"Exit Code: '{exitCode}'");

        BoundedLog($"{(exitCode == 0 ? "Successfully" : "Failed to")} compile external assembly!");
        return exitCode == 0;
    }

    public static bool BuildProject()
    {
        if (!HasProject)
        {
            Runtime.Debug.LogError($"No Project Loaded...");
            return false;
        }

        if (BuildSettings.StartingScene.IsAvailable == false)
        {
            Runtime.Debug.LogError($"No Starting Scene Assigned...");
            return false;
        }

        Runtime.Debug.Log($"Starting Project Build...");
        BoundedLog($"Creating Directories...");

        string BuildPath = Path.Combine(ProjectDirectory, "Builds", "Latest");
        string BuildDataPath = Path.Combine(ProjectDirectory, "Builds", "Latest", "GameData");
        // Check if "Latest" folder already exists
        if (Directory.Exists(BuildPath))
        {
            // Increment the folder name
            int count = 1;
            string newBuildPath;
            do
            {
                newBuildPath = Path.Combine(ProjectDirectory, "Builds", $"Latest_{count}");
                count++;
            } while (Directory.Exists(newBuildPath));

            // Move the existing "Latest" folder to the new one
            Directory.Move(BuildPath, newBuildPath);
        }

        Directory.CreateDirectory(BuildPath);
        Directory.CreateDirectory(BuildDataPath);


        BoundedLog($"Compiling project assembly to {BuildPath}...");
        if (!Compile(Assembly_Proj, true))
        {
            Runtime.Debug.LogError($"Failed to compile Project assembly!");
            return false;
        }


        BoundedLog($"Exporting and Packing assets to {BuildDataPath}...");
#warning TODO: Needs Asset Dependencies to track what assets are used in built scenes rather then doing all assets
        AssetDatabase.ExportAllBuildPackages(new DirectoryInfo(BuildDataPath));


        BoundedLog($"Preparing default scene to {BuildDataPath}...");
        FileInfo StartingScene = new FileInfo(Path.Combine(BuildDataPath, "level.prowl"));
        Tag tag = TagSerializer.Serialize(BuildSettings.StartingScene.Res!);
        BinaryTagConverter.WriteToFile((CompoundTag)tag, StartingScene);


        BoundedLog($"Copying standalone player to {BuildPath}...");
        // Get the Standalone.zip file from embedded resources
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.Standalone.zip");
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);
        // Extract the Standalone.zip file to the BuildPath
        archive.ExtractToDirectory(BuildPath);


        Runtime.Debug.Log("**********************************************************************************************************************");
        Runtime.Debug.Log($"Successfully built project!");

        // Open the Build folder
        AssetDatabase.OpenPath(BuildPath);

        return true;
    }

    /// <summary>
    /// Return the DirectoryInfo of a Project with a given name
    /// </summary>
    /// <param name="name">The Project Name</param>
    /// <returns>Directory of given project</returns>
    public static DirectoryInfo GetPath(string name) => new DirectoryInfo(Path.Combine(Projects_Directory, name));

    #endregion


    #region Private Methods
    private static string GetIncludesFrom(IEnumerable<string> filePaths)
    {
        List<string> includeElements = new();
        foreach (string filePath in filePaths)
            includeElements.Add($"<Compile Include=\"{filePath}\" />");
        return string.Join("\n", includeElements);
    }

    private static void GenerateCSProjectFiles()
    {
        if (!HasProject) throw new Exception("No Project Loaded, Cannot generate CS Project Files!");

        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        Assembly gameEngineAssembly = loadedAssemblies.FirstOrDefault(assembly => assembly.GetName().Name == "Prowl.Runtime") 
            ?? throw new Exception("Failed to find Prowl.Runtime Assembly!");
        Assembly gameEditorAssembly = loadedAssemblies.FirstOrDefault(assembly => assembly.GetName().Name == "Prowl.Editor") 
            ?? throw new Exception("Failed to find Prowl.Editor Assembly!");

        Runtime.Debug.Log($"Updating {Assembly_Proj}...");

        IEnumerable<string> nonEditorScripts = Directory.GetFiles(ProjectAssetDirectory, "*.cs", SearchOption.AllDirectories)
        .Where(csFile => !string.Equals(Path.GetFileName(Path.GetDirectoryName(csFile)), "Editor", StringComparison.OrdinalIgnoreCase));

        string propertyGroupTemplate = 
            @$"<PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <ProjectRoot>{ProjectDirectory}</ProjectRoot>
            </PropertyGroup>
            <PropertyGroup Condition="" '$(Configuration)' == 'Debug' "">
                <OutputPath>$(ProjectRoot)\Temp\bin\Debug\</OutputPath>
            </PropertyGroup>
            <PropertyGroup Condition="" '$(Configuration)' == 'Release' "">
                <OutputPath>$(ProjectRoot)\Builds\Latest\</OutputPath>
            </PropertyGroup>";

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
                </ItemGroup>
            </Project>";

        File.WriteAllText(Assembly_Proj, gameproj);

        Runtime.Debug.Log($"Updating {Editor_Assembly_Proj}...");

        IEnumerable<string> editorScripts = Directory.GetDirectories(ProjectAssetDirectory, "Editor", SearchOption.AllDirectories)
        .SelectMany(editorFolder => Directory.GetFiles(editorFolder, "*.cs"));

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
                        <HintPath>{Assembly_Proj}</HintPath>
                        <Private>false</Private>
                    </Reference>
                </ItemGroup>
            </Project>";

        File.WriteAllText(Editor_Assembly_Proj, editorproj);

        Runtime.Debug.Log("Finished Updating Build Information");
    }

    private static void BoundedLog(string message)
    {
        Runtime.Debug.Log("**********************************************************************************************************************");
        Runtime.Debug.Log(message);
        Runtime.Debug.Log("**********************************************************************************************************************");
    }

    #endregion
}