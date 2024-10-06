// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;

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

        BoundedLog($"{(exitCode == 0 ? "Successfully" : "Failed to")} compile external assembly.");
        return exitCode == 0;
    }

    #endregion


    #region Private Methods

    private static List<FileInfo> EnumerateDirectories(DirectoryInfo baseDirectory, Func<DirectoryInfo, FileInfo[]> getFiles)
    {
        List<FileInfo> result = new();
        Stack<DirectoryInfo> directoriesToProcess = new([baseDirectory]);

        while (directoriesToProcess.Count > 0)
        {
            DirectoryInfo directory = directoriesToProcess.Pop();

            foreach (DirectoryInfo subdirectory in directory.GetDirectories())
                directoriesToProcess.Push(subdirectory);

            result.AddRange(getFiles.Invoke(directory));
        }

        return result;
    }

    private static void GenerateCSProjectFiles(Project project, DirectoryInfo output)
    {
        if (!HasProject)
            throw new Exception("No Project Loaded, Cannot generate CS Project Files.");

        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

        Assembly gameEngineAssembly = loadedAssemblies.FirstOrDefault(assembly => assembly.GetName().Name == "Prowl.Runtime")
            ?? throw new Exception("Failed to find Prowl.Runtime Assembly.");

        // Get all nonstandard references Prowl.Runtime has
        List<(string, string)> runtimeRefs = gameEngineAssembly.GetReferencedAssemblies()
            .Where(IsNotDefault)
            .Select(Assembly.Load)
            .Select(x => (x.GetName().Name!, x.Location))
            .ToList();

        runtimeRefs.Add((gameEngineAssembly.GetName().Name!, gameEngineAssembly.Location));

        Runtime.Debug.Log($"Updating {project.Assembly_Proj.FullName}...");

        List<FileInfo> gameScripts = EnumerateDirectories(project.AssetDirectory, x =>
            !HasParentOfName(x, project.AssetDirectory, "Editor") ? x.GetFiles("*.cs") : []);

        XDocument gameProjXml = new XDocument(GenerateCSProjectXML(project, output, gameScripts, runtimeRefs));

        gameProjXml.Save(project.Assembly_Proj.FullName);


        Runtime.Debug.Log($"Updating {project.Editor_Assembly_Proj.FullName}...");

        Assembly gameEditorAssembly = loadedAssemblies.FirstOrDefault(assembly => assembly.GetName().Name == "Prowl.Editor")
            ?? throw new Exception("Failed to find Prowl.Editor Assembly.");

        List<FileInfo> editorScripts = EnumerateDirectories(project.AssetDirectory, x =>
            HasParentOfName(x, project.AssetDirectory, "Editor") ? x.GetFiles("*.cs") : []);

        XDocument editorProjXml = new XDocument(GenerateCSProjectXML(project, output, editorScripts,
            runtimeRefs.Concat([
                (gameEditorAssembly.GetName().Name!, gameEditorAssembly.Location),
                ("CSharp", project.Assembly_DLL.FullName)
            ])
        ));

        editorProjXml.Save(project.Editor_Assembly_Proj.FullName);


        Runtime.Debug.Log("Finished Updating Build Information");
    }


    private static XElement GenerateCSProjectXML(
        Project project,
        DirectoryInfo outputPath,
        IEnumerable<FileInfo> scriptPaths,
        IEnumerable<(string, string)> references)
    {
        XElement propertyGroupXML = new XElement("PropertyGroup",
            new XElement("TargetFramework", "net8.0"),
            new XElement("ImplicitUsings", "enable"),
            new XElement("Nullable", "enable"),
            new XElement("ProjectRoot", project.ProjectPath)
        );

        XElement debugPropertyGroupXML = new XElement("PropertyGroup",
            new XAttribute("Condition", "'$(Configuration)' == 'Debug'"),
            new XElement("OutputPath", outputPath.FullName)
        );

        XElement releasePropertyGroupXML = new XElement("PropertyGroup",
            new XAttribute("Condition", "'$(Configuration)' == 'Release'"),
            new XElement("OutputPath", outputPath.FullName)
        );

        XElement scriptsXML = new XElement("ItemGroup",
            new XElement("Compile", new XAttribute("Remove", "**/*.cs")),
            scriptPaths.Select(x =>
                new XElement("Compile", new XAttribute("Include", x.FullName))
            )
        );

        XElement referencesXML = new XElement("ItemGroup",
            references.Select(x => new XElement("Reference",
                new XAttribute("Include", x.Item1),
                new XElement("HintPath", x.Item2)
            ))
        );

        XElement projectXML = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            propertyGroupXML,
            debugPropertyGroupXML,
            releasePropertyGroupXML,
            scriptsXML,
            referencesXML
        );

        return projectXML;
    }


    private static bool HasParentOfName(DirectoryInfo? directory, DirectoryInfo root, string name)
    {
        while (directory != null && directory.FullName != root.FullName)
        {
            if (string.Equals(directory.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;

            directory = directory.Parent;
        }

        return false;
    }


    // All the assembies included by the .NET SDK when building a self-contained executable
    private static string[] s_defaultCSAssemblyNames =
    [
        "System.Runtime",
        "System.IO.FileSystem.Primitives",
        "System.Net.NetworkInformation",
        "System.Runtime.Intrinsics",
        "System.Security.Cryptography",
        "System.Threading.ThreadPool",
        "System.Diagnostics.Debug",
        "System.Net.Sockets",
        "System.Runtime.InteropServices.JavaScript",
        "System.Net.Ping",
        "System.Net.Mail",
        "System.Diagnostics.DiagnosticSource",
        "System.Reflection.DispatchProxy",
        "System.Private.Xml",
        "mscorlib",
        "Microsoft.VisualBasic",
        "System.IO.FileSystem",
        "System.Net.Security",
        "netstandard",
        "System.Reflection.Primitives",
        "System.Globalization",
        "System.Runtime.InteropServices.RuntimeInformation",
        "System.Formats.Asn1",
        "System.Runtime.Serialization.Json",
        "System.Diagnostics.StackTrace",
        "System.Text.Json",
        "System.Globalization.Extensions",
        "System.Reflection.TypeExtensions",
        "System.Threading.Tasks.Parallel",
        "System.IO.FileSystem.Watcher",
        "System.Threading.Tasks.Dataflow",
        "System.ServiceModel.Web",
        "System.Console",
        "System.ComponentModel.Annotations",
        "System.Threading.Channels",
        "System.Net.Http",
        "System.Reflection.Emit.Lightweight",
        "System.Buffers",
        "System.Security.Cryptography.X509Certificates",
        "System.Net.Quic",
        "System.Reflection.Extensions",
        "System.IO.Pipes.AccessControl",
        "System.ComponentModel.Primitives",
        "System.Security.AccessControl",
        "System.Text.Encoding.CodePages",
        "System.Collections.NonGeneric",
        "System.Net",
        "System.Runtime.Numerics",
        "System.Transactions",
        "System.IO.Compression.ZipFile",
        "System.Text.Encoding",
        "System.Runtime.Serialization.Formatters",
        "System.Dynamic.Runtime",
        "System.Transactions.Local",
        "System.Xml",
        "System.Net.Requests",
        "System.Security.Cryptography.Encoding",
        "Microsoft.VisualBasic.Core",
        "System.Security.Cryptography.Algorithms",
        "System.Net.WebSockets.Client",
        "System.Xml.XDocument",
        "System.Private.Uri",
        "System.Net.ServicePoint",
        "System.Reflection.Emit",
        "System.Net.Http.Json",
        "System.Formats.Tar",
        "System.Runtime.Serialization.Xml",
        "System.Data.Common",
        "System.Net.Primitives",
        "System.Drawing.Primitives",
        "System.Diagnostics.Tracing",
        "System.Xml.Serialization",
        "System.IO.UnmanagedMemoryStream",
        "System.Diagnostics.FileVersionInfo",
        "System.Security.Claims",
        "System.Threading.Overlapped",
        "System.Private.Xml.Linq",
        "System.Data",
        "System.Text.Encoding.Extensions",
        "System.Threading.Timer",
        "System.Collections",
        "System.Linq",
        "System.Collections.Immutable",
        "System.Security.Principal",
        "System.Security.Cryptography.OpenSsl",
        "Microsoft.CSharp",
        "System.IO.MemoryMappedFiles",
        "System.Globalization.Calendars",
        "System.ObjectModel",
        "System.Security.Cryptography.Cng",
        "System.Net.WebSockets",
        "System.Security.Cryptography.Primitives",
        "System.Security",
        "System.Collections.Concurrent",
        "System",
        "System.ComponentModel.TypeConverter",
        "System.ComponentModel",
        "System.Xml.XmlSerializer",
        "System.Diagnostics.Tools",
        "System.Xml.XmlDocument",
        "System.Security.SecureString",
        "System.IO.Compression.Brotli",
        "System.Resources.Writer",
        "System.Diagnostics.Process",
        "System.Linq.Queryable",
        "System.IO.Compression.FileSystem",
        "System.Net.NameResolution",
        "System.Runtime.Handles",
        "System.Resources.ResourceManager",
        "System.Threading.Tasks.Extensions",
        "System.ComponentModel.DataAnnotations",
        "System.Diagnostics.TraceSource",
        "System.Web",
        "System.IO.Pipes",
        "System.Text.Encodings.Web",
        "System.IO",
        "System.Runtime.Extensions",
        "System.Numerics",
        "System.ServiceProcess",
        "System.Text.RegularExpressions",
        "System.Runtime.CompilerServices.VisualC",
        "System.AppContext",
        "System.Linq.Parallel",
        "System.ValueTuple",
        "System.Xml.XPath.XDocument",
        "System.ComponentModel.EventBasedAsync",
        "System.IO.Compression",
        "System.Reflection.Emit.ILGeneration",
        "System.Runtime.Serialization",
        "System.Memory",
        "System.Runtime.InteropServices",
        "System.Reflection",
        "System.Diagnostics.TextWriterTraceListener",
        "System.Runtime.CompilerServices.Unsafe",
        "System.Collections.Specialized",
        "System.Security.Principal.Windows",
        "System.Net.HttpListener",
        "System.Numerics.Vectors",
        "System.Configuration",
        "System.Private.DataContractSerialization",
        "System.IO.IsolatedStorage",
        "WindowsBase",
        "Microsoft.Win32.Registry",
        "System.Drawing",
        "Microsoft.Win32.Primitives",
        "System.Diagnostics.Contracts",
        "System.Reflection.Metadata",
        "System.Xml.Linq",
        "System.Windows",
        "System.Resources.Reader",
        "System.Threading.Tasks",
        "System.Threading",
        "System.Security.Cryptography.Csp",
        "System.IO.FileSystem.DriveInfo",
        "System.Threading.Thread",
        "System.Net.WebProxy",
        "System.Net.WebHeaderCollection",
        "System.Xml.XPath",
        "System.Core",
        "System.Web.HttpUtility",
        "System.Xml.ReaderWriter",
        "System.Runtime.Loader",
        "System.IO.FileSystem.AccessControl",
        "System.Linq.Expressions",
        "System.Data.DataSetExtensions",
        "System.Private.CoreLib",
        "System.Net.WebClient",
        "System.Runtime.Serialization.Primitives",
    ];


    private static bool IsNotDefault(AssemblyName assembly)
    {
        return !s_defaultCSAssemblyNames.Contains(assembly.Name);
    }


    private static void BoundedLog(string message)
    {
        Runtime.Debug.Log("**********************************************************************************************************************");
        Runtime.Debug.Log(message);
        Runtime.Debug.Log("**********************************************************************************************************************");
    }

    #endregion
}
