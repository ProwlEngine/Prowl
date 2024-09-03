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
public static class Project
{
    #region Public Properties
    public static string Projects_Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prowl", "Projects");
    public static string ProjectDirectory => Path.Combine(Projects_Directory, Name);
    public static string ProjectAssetDirectory => Path.Combine(ProjectDirectory, @"Assets");
    public static string ProjectDefaultsDirectory => Path.Combine(ProjectDirectory, @"Defaults");
    public static string ProjectPackagesDirectory => Path.Combine(ProjectDirectory, @"Packages");
    public static string TempDirectory => Path.Combine(ProjectDirectory, @"Temp");

    public static string Assembly_Proj => Path.Combine(ProjectDirectory, @"CSharp.csproj");
    public static string Assembly_DLL => Path.Combine(ProjectDirectory, @"Temp/bin/Debug/net8.0/CSharp.dll");

    public static string Editor_Assembly_Proj => Path.Combine(ProjectDirectory, @"CSharp-Editor.csproj");
    public static string Editor_Assembly_DLL => Path.Combine(ProjectDirectory, @"Temp/bin/Debug/net8.0/CSharp-Editor.dll");

    public static event Action OnProjectChanged;
    public static bool HasProject { get; private set; } = false;
    public static string Name { get; private set; } = "";
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

        Name = projectName;
        HasProject = true;
        Program.RegisterReloadOfExternalAssemblies();

        CreateProjectDirectories(projectDir);

        Application.DataPath = ProjectDirectory;

        CreateDefaults("Defaults");
        AssetDatabase.AddRootFolder("Defaults");
        AssetDatabase.Update(false, true); // Ensure defaults are all loaded in

        AssetDatabase.AddRootFolder("Packages");
        AssetDatabase.Update(false, true); // Ensure packages are all loaded in

        AssetDatabase.AddRootFolder("Assets");
        AssetDatabase.Update(true, true); // Not that all folders are in we can unload anything thats not in the project anymore since last session

#warning TODO: Record last opened scene and try to open it
        SceneManager.InstantiateNewScene();

        OnProjectChanged?.Invoke();

        AssetDatabase.LoadPackages();
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
        CreateProjectDirectories(projectDir);
        CreateDefaults("Defaults");

        // Create Config Folder
        string configPath = Path.Combine(projectDir.FullName, @"Config");
        Directory.CreateDirectory(configPath);

    }

    private static void CreateProjectDirectories(DirectoryInfo projectDir)
    {
        Directory.CreateDirectory(Path.Combine(projectDir.FullName, @"Assets"));
        Directory.CreateDirectory(Path.Combine(projectDir.FullName, @"Library"));
        Directory.CreateDirectory(Path.Combine(projectDir.FullName, @"Packages"));
    }

    static void CreateDefaults(string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(rootFolder)) throw new ArgumentException("Root Folder cannot be null or whitespace");
        DirectoryInfo info = new(Path.Combine(Project.ProjectDirectory, rootFolder));
        if (!info.Exists) info.Create();

#warning TODO: Only copy if the file doesn't exist, or if somehow if the engine version is different or something...

        // Copy embedded defaults to rootFolder, this is just actual Files, so Image.png, not the asset variants
        foreach (string file in Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(x => x.StartsWith("Prowl.Editor.EmbeddedResources.DefaultAssets.")))
        {
            string[] nodes = file.Split('.');
            string fileName = nodes[^2];
            string fileExtension = nodes[^1];
            string filePath = Path.Combine(info.FullName, fileName + "." + fileExtension);
            if (File.Exists(filePath))
                File.Delete(filePath);
            //if (!File.Exists(filePath))
            {
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(file);
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
        GenerateCSProjectFiles(output);

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
        processInfo.Arguments = $"/c dotnet build \"{Path.GetFileName(csprojPath)}\"" + (isRelease ? " --configuration Release" : "");

        if (RuntimeUtils.IsMac() || RuntimeUtils.IsLinux())
        {
            processInfo.FileName = "/bin/bash";
            processInfo.Arguments = $"-c \"dotnet build '{Path.GetFileName(csprojPath)}'\"" + (isRelease ? " --configuration Release" : "");
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

        BoundedLog($"Exit Code: '{exitCode}'");

        BoundedLog($"{(exitCode == 0 ? "Successfully" : "Failed to")} compile external assembly!");
        return exitCode == 0;
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

    private static void GenerateCSProjectFiles(DirectoryInfo output)
    {
        if (!HasProject) throw new Exception("No Project Loaded, Cannot generate CS Project Files!");

        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        Assembly gameEngineAssembly = loadedAssemblies.FirstOrDefault(assembly => assembly.GetName().Name == "Prowl.Runtime")
            ?? throw new Exception("Failed to find Prowl.Runtime Assembly!");
        Assembly gameEditorAssembly = loadedAssemblies.FirstOrDefault(assembly => assembly.GetName().Name == "Prowl.Editor")
            ?? throw new Exception("Failed to find Prowl.Editor Assembly!");

        // Get all references by Prowl.Runtime
        var references = gameEngineAssembly.GetReferencedAssemblies().Select(Assembly.Load).ToList();

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
