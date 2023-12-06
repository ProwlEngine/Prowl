using Prowl.Runtime.Assets;
using Prowl.Editor.Assets;
using System.Diagnostics;
using System.Reflection;
using Prowl.Editor.EditorWindows;
using Prowl.Runtime.Serializer;
using System.IO.Compression;

namespace Prowl.Editor;

public static class Project {

    public static event Action OnProjectChanged;


    public static BuildSettings BuildSettings => Project.ProjectSettings.GetSetting<BuildSettings>();

    public static string Projects_Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prowl", "Projects");

    public static ProjectSettings ProjectSettings;

    public static bool HasProject { get; private set; } = false;
    public static string Name { get; private set; } = "";
    public static string ProjectDirectory => Path.Combine(Projects_Directory, Name);
    public static string ProjectAssetDirectory => Path.Combine(ProjectDirectory, @"Assets");
    public static string ProjectDefaultsDirectory => Path.Combine(ProjectDirectory, @"Defaults");
    public static string ProjectPackagesDirectory => Path.Combine(ProjectDirectory, @"Packages");
    public static string TempDirectory => Path.Combine(Project.ProjectDirectory, @"Temp");

    public static string Assembly_Proj => Path.Combine(ProjectDirectory, @"CSharp.csproj");
    public static string Assembly_DLL => Path.Combine(ProjectDirectory, @"Temp\bin\Debug\net8.0\CSharp.dll");

    public static string Editor_Assembly_Proj => Path.Combine(ProjectDirectory, @"CSharp-Editor.csproj");
    public static string Editor_Assembly_DLL => Path.Combine(ProjectDirectory, @"Temp\bin\Debug\net8.0\CSharp-Editor.dll");

    public static void Open(string projectName) 
    {
        string pathToProject = GetPath(projectName);
        if (!Directory.Exists(pathToProject)) throw new("Project doesn't exist!");
        if (string.IsNullOrEmpty(pathToProject)) throw new("Null or Empty Project Path");

        AssetDatabase.Clear();

        Name = projectName;
        HasProject = true;
        (EditorApplication.Instance as EditorApplication).RegisterReloadOfExternalAssemblies();
        ProjectSettings = ProjectSettings.Load();

        DefaultAssets.CreateDefaults("Defaults");
        AssetDatabase.AddRootFolder("Defaults");
        AssetDatabase.AddRootFolder("Packages");

        AssetDatabase.AddRootFolder("Assets");


        OnProjectChanged?.Invoke();
    }

    /// <summary>
    /// Will create a new Project file and its main Folders in a Dedicated folder
    /// </summary>
    /// <param name="ProjectName"></param>
    public static void CreateNew(string ProjectName)
    {
        Name = ProjectName;
        string pathToProject = GetPath(ProjectName);
        if (Directory.Exists(pathToProject)) throw new("Project already exists!");

        // Create Path
        Directory.CreateDirectory(pathToProject);

        // Create Assets Folder
        Directory.CreateDirectory(Path.Combine(pathToProject, @"Assets"));
        DefaultAssets.CreateDefaults("Defaults");
        Directory.CreateDirectory(Path.Combine(pathToProject, @"Library"));

        // Create Config Folder
        string configPath = Path.Combine(pathToProject, @"Config");
        Directory.CreateDirectory(configPath);

        ProjectSettings = new();
        ProjectSettings.Save();
    }

    public static string GetIncludesFrom(IEnumerable<string> filePaths)
    {
        List<string> includeElements = new();
        foreach (string filePath in filePaths)
            includeElements.Add($"<Compile Include=\"{filePath}\" />");
        return string.Join("\n", includeElements);
    }

    public static void GenerateCSProjectFiles()
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
                    <Reference Include=""ExampleGame"">
                        <HintPath>$(ProjectRoot)\Temp\bin\Debug\net8.0\ExampleGame.dll</HintPath>
                        <Private>false</Private>
                    </Reference>

                    <PackageReference Include=""ImGui.NET"" Version=""1.78.0"" />
                </ItemGroup>
            </Project>";

        File.WriteAllText(Editor_Assembly_Proj, editorproj);

        Runtime.Debug.Log("Finished Updating Build Information");
    }

    internal static bool Compile(string dir, bool isRelease = false)
    {
        if (!HasProject)
        {
            Runtime.Debug.LogError($"No Project Loaded...");
            return false;
        }

        Runtime.Debug.Log($"Starting Project Compilation...");
        Runtime.Debug.Log("**********************************************************************************************************************");

        // Reload CSProject Files
        GenerateCSProjectFiles();

        Runtime.Debug.Log($"Compiling external assembly in {dir}...");
        Runtime.Debug.Log("**********************************************************************************************************************");

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

        process.ErrorDataReceived += (sender, dataArgs) =>
        {
            if (dataArgs.Data is not null)
                Runtime.Debug.LogError(dataArgs.Data);
        };
        process.BeginErrorReadLine();

        process.WaitForExit();

        int exitCode = process.ExitCode;
        process.Close();

        Runtime.Debug.Log($"Exit Code: '{exitCode}'");
        Runtime.Debug.Log("**********************************************************************************************************************");

        Runtime.Debug.Log($"{(exitCode == 0 ? "Successfully" : "Failed to")} compile external assembly!");
        Runtime.Debug.Log("");
        return true;
    }

    internal static bool BuildProject()
    {
        if (!HasProject)
        {
            Runtime.Debug.LogError($"No Project Loaded...");
            return false;
        }

        if(BuildSettings.StartingScene.IsAvailable == false)
        {
            Runtime.Debug.LogError($"No Starting Scene Assigned...");
            return false;
        }

        Runtime.Debug.Log($"Starting Project Build...");
        Runtime.Debug.Log("**********************************************************************************************************************", false);
        Runtime.Debug.Log($"Creating Directories...");
        Runtime.Debug.Log("**********************************************************************************************************************", false);

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


        Runtime.Debug.Log("**********************************************************************************************************************", false);
        Runtime.Debug.Log($"Compiling project assembly to {BuildPath}...");
        Runtime.Debug.Log("**********************************************************************************************************************", false);
        if (!Compile(Assembly_Proj, true))
        {
            Runtime.Debug.LogError($"Failed to compile Project assembly!");
            return false;
        }


        Runtime.Debug.Log("**********************************************************************************************************************", false);
        Runtime.Debug.Log($"Exporting and Packing assets to {BuildDataPath}...");
        Runtime.Debug.Log("**********************************************************************************************************************", false);
#warning TODO: Needs Asset Dependencies to track what assets are used in built scenes rather then doing all assets
        AssetDatabase.ExportAllBuildPackages(new DirectoryInfo(BuildDataPath));


        Runtime.Debug.Log("**********************************************************************************************************************", false);
        Runtime.Debug.Log($"Preparing default scene to {BuildDataPath}...");
        Runtime.Debug.Log("**********************************************************************************************************************", false);

        FileInfo StartingScene = new FileInfo(Path.Combine(BuildDataPath, "level.prowl"));
        Tag tag = TagSerializer.Serialize(BuildSettings.StartingScene.Res!);
        BinaryTagConverter.WriteToFile((CompoundTag)tag, StartingScene);


        Runtime.Debug.Log("**********************************************************************************************************************", false);
        Runtime.Debug.Log($"Copying standalone player to {BuildPath}...");
        Runtime.Debug.Log("**********************************************************************************************************************", false);
        // Get the Standalone.zip file from embedded resources
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.Standalone.zip");
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);
        // Extract the Standalone.zip file to the BuildPath
        archive.ExtractToDirectory(BuildPath);
        

        Runtime.Debug.Log("**********************************************************************************************************************", false);

        Runtime.Debug.Log($"Successfully built project!");
        Runtime.Debug.Log("");
        return true;
    }

    internal static string GetPath(string name)
    {
        return Path.Combine(Projects_Directory, name);
    }
}