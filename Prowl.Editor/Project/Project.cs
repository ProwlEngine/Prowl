using System;
using System.IO;
using System.Text.Json;

namespace Prowl.Editor;

/// <summary>
/// Represents a Prowl project on disk.
/// A project is a folder containing an Assets/ directory and a .prowl marker file.
/// </summary>
public class Project
{
    public static Project? Current { get; private set; }

    public string Name { get; private set; }
    public string RootPath { get; private set; }

    // Standard directories
    public string AssetsPath => Path.Combine(RootPath, "Assets");
    public string LibraryPath => Path.Combine(RootPath, "Library");
    public string CachePath => Path.Combine(RootPath, "Library", "cache");
    public string ThumbnailsPath => Path.Combine(RootPath, "Library", "thumbnails");
    public string ProjectSettingsPath => Path.Combine(RootPath, "ProjectSettings");
    public string PackagesPath => Path.Combine(RootPath, "Packages");
    public string TempPath => Path.Combine(RootPath, "Temp");
    public string LogsPath => Path.Combine(RootPath, "Logs");
    public string ProwlFilePath => Path.Combine(RootPath, $"{Name}.prowl");
    public string MetadataDbPath => Path.Combine(LibraryPath, "metadata.db");
    public string EditorStatePath => Path.Combine(LibraryPath, "EditorState.json");

    // Build
    public string BuildTempPath => Path.Combine(TempPath, "PlayerBuild");

    // Script compilation
    public string ScriptAssemblyPath => Path.Combine(LibraryPath, "ScriptAssemblies");
    public string GameAssemblyPath => Path.Combine(ScriptAssemblyPath, $"{Name}.Game.dll");
    public string EditorAssemblyPath => Path.Combine(ScriptAssemblyPath, $"{Name}.Editor.dll");
    public string GameCsprojPath => Path.Combine(RootPath, $"{Name}.Game.csproj");
    public string EditorCsprojPath => Path.Combine(RootPath, $"{Name}.Editor.csproj");
    public string AutoSaveScenePath => Path.Combine(LibraryPath, "~autosave.scene");

    private Project(string rootPath, string name)
    {
        RootPath = Path.GetFullPath(rootPath);
        Name = name;
    }

    /// <summary>
    /// Create a new project at the given path.
    /// </summary>
    public static Project Create(string parentFolder, string projectName)
    {
        string rootPath = Path.Combine(parentFolder, projectName);

        if (Directory.Exists(rootPath) && Directory.GetFileSystemEntries(rootPath).Length > 0)
            throw new InvalidOperationException($"Directory '{rootPath}' already exists and is not empty.");

        var project = new Project(rootPath, projectName);
        project.EnsureDirectories();
        project.WriteProwlFile();

        // Create a default .gitignore
        string gitignore = Path.Combine(rootPath, ".gitignore");
        if (!File.Exists(gitignore))
        {
            File.WriteAllText(gitignore,
                "Library/\nTemp/\nLogs/\n*.csproj\n*.sln\n.vs/\nbin/\nobj/\n");
        }

        return project;
    }

    /// <summary>
    /// Open an existing project from a root folder or .prowl file path.
    /// </summary>
    public static Project Open(string path)
    {
        string rootPath;

        if (File.Exists(path) && path.EndsWith(".prowl", StringComparison.OrdinalIgnoreCase))
        {
            rootPath = Path.GetDirectoryName(path)!;
        }
        else if (Directory.Exists(path))
        {
            rootPath = path;
        }
        else
        {
            throw new DirectoryNotFoundException($"Project path not found: {path}");
        }

        rootPath = Path.GetFullPath(rootPath);

        // Validate it's a project — must have Assets/ folder
        string assetsDir = Path.Combine(rootPath, "Assets");
        if (!Directory.Exists(assetsDir))
            throw new InvalidOperationException($"Not a valid Prowl project: missing Assets/ folder in '{rootPath}'");

        // Find the project name from .prowl file or folder name
        string name = Path.GetFileName(rootPath);
        var prowlFiles = Directory.GetFiles(rootPath, "*.prowl");
        if (prowlFiles.Length > 0)
        {
            try
            {
                string json = File.ReadAllText(prowlFiles[0]);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("name", out var nameProp))
                    name = nameProp.GetString() ?? name;
            }
            catch { }
        }

        var project = new Project(rootPath, name);
        project.EnsureDirectories();

        // Write .prowl file if missing
        if (prowlFiles.Length == 0)
            project.WriteProwlFile();

        return project;
    }

    /// <summary>
    /// Set this project as the currently active project.
    /// </summary>
    public void SetActive()
    {
        Current = this;
        RecentProjects.AddRecent(RootPath, Name);
        Runtime.Debug.Log($"Opened project: {Name} at {RootPath}");
    }

    /// <summary>
    /// Check if a directory looks like a valid Prowl project.
    /// </summary>
    public static bool IsValidProject(string path)
    {
        if (!Directory.Exists(path)) return false;
        return Directory.Exists(Path.Combine(path, "Assets"));
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(AssetsPath);
        Directory.CreateDirectory(LibraryPath);
        Directory.CreateDirectory(CachePath);
        Directory.CreateDirectory(ThumbnailsPath);
        Directory.CreateDirectory(ProjectSettingsPath);
        Directory.CreateDirectory(PackagesPath);
        Directory.CreateDirectory(TempPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(ScriptAssemblyPath);
    }

    private void WriteProwlFile()
    {
        var data = new
        {
            name = Name,
            engine = "Prowl",
            version = "0.0.1",
            created = DateTime.UtcNow.ToString("o")
        };

        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ProwlFilePath, json);
    }
}
