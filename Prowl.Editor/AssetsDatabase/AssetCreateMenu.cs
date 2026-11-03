using System;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.Core.Tasks;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.Popups;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor;

/// <summary> Provides methods to create project assets (folders, shaders, assembly definitions, prefabs, and arbitrary typed assets) with automatic unique naming and folder index invalidation. </summary>
public static class AssetCreateMenu
{
    [MenuItem("Assets/Create/Folder", priority: 0, Icon = EditorIcons.Folder)]
    static void CreateFolderItem() => CreateAssetTask.Begin(GetCurrentFolder(), "New Folder", "", WriteFolder, isFolder: true);

    [MenuItem("Assets/Create/Prefab From Selection", priority: 100, Icon = EditorIcons.Cubes, Separator = true)]
    static void CreatePrefabFromSelectionItem()
    {
        var selected = Selection.GetSelected<GameObject>().ToList();
        if (selected.Count == 0)
        {
            Runtime.Debug.LogWarning("[Prefab] Select a GameObject in the scene to make a prefab from it.");
            return;
        }

        // Roots only, so a prefab of a parent is not immediately torn apart by making one of its child.
        foreach (GameObject go in GameObjectClipboard.FilterToRoots(selected))
            CreatePrefabIn(go, GetCurrentFolder());
    }

    /// <summary>
    /// Save a GameObject as a new prefab in a project folder, named so it does not collide with what
    /// is already there.
    /// <para/>
    /// Shared by the three places that offer this, which each worked out the same name and path and
    /// each threw away the result. A refused write reached the console and nowhere else, so the user
    /// saw a menu item do nothing at all.
    /// </summary>
    internal static bool CreatePrefabIn(GameObject go, string relativeFolder)
    {
        string absoluteFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absoluteFolder)) return false;

        string name = FindUniqueName(absoluteFolder, go.Name, ".prefab");
        string relativePath = string.IsNullOrEmpty(relativeFolder) ? name : $"{relativeFolder}/{name}";

        if (PrefabUtility.SaveAsPrefabAssetAndConnect(go, relativePath)) return true;

        Toasts.Show(Loc.Get("toast.prefab_create_failed"),
            Loc.Get("toast.prefab_create_failed_body", new { name = go.Name }), ToastType.Error, 4f);
        return false;
    }

    [MenuItem("Assets/Create/Shader", priority: 1000, Icon = EditorIcons.WandMagicSparkles, Separator = true)]
    static void CreateShaderItem() => CreateAssetTask.Begin(GetCurrentFolder(), "New Shader", ".shader", WriteShader);

    [MenuItem("Assets/Create/Compute Shader", priority: 1001, Icon = EditorIcons.WandMagicSparkles)]
    static void CreateComputeShaderItem() => CreateAssetTask.Begin(GetCurrentFolder(), "New Compute Shader", ".compute", WriteComputeShader);

    [MenuItem("Assets/Create/C# Script", priority: 1010, Icon = EditorIcons.FileCode, Separator = true)]
    static void CreateScriptItem() => NewScriptDialog.Open(GetCurrentFolder());

    [MenuItem("Assets/Create/Assembly Definition", priority: 1011, Icon = EditorIcons.FileLines)]
    static void CreateAsmDefItem() => CreateAssetTask.Begin(GetCurrentFolder(), "NewAssembly", Projects.Scripting.AssemblyDefinitionDatabase.Extension, WriteAssemblyDefinition);

    /// <summary>Starts naming and creating an asset of a <c>[CreateAssetMenu]</c> type in a folder.</summary>
    public static void BeginCreateAsset(AssetMenuEntry entry, string relativeFolder)
        => CreateAssetTask.Begin(relativeFolder, DefaultName(entry), entry.Extension, AssetWriter(entry));

    private static string DefaultName(AssetMenuEntry entry)
    {
        int lastSlash = entry.Name.LastIndexOf('/');
        return "New " + (lastSlash >= 0 ? entry.Name.Substring(lastSlash + 1) : entry.Name);
    }

    private static CreateAssetTask.Writer AssetWriter(AssetMenuEntry entry) => (path, _) =>
    {
        object? instance = entry.Factory != null ? entry.Factory() : Activator.CreateInstance(entry.Type);
        EchoObject? echo = Prowl.Echo.Serializer.Serialize(typeof(object), instance);
        if (echo == null) return false;
        File.WriteAllText(path, echo.WriteToString());
        return true;
    };

    private static bool WriteFolder(string path, string _)
    {
        Directory.CreateDirectory(path);
        MetaFile.EnsureMeta(path, "DefaultImporter");
        return true;
    }

    private static bool WriteAssemblyDefinition(string path, string name)
    {
        new Projects.Scripting.AssemblyDefinition { Name = name }.WriteToFile(path);
        return true;
    }

    private static bool WriteShader(string path, string name)
    {
        File.WriteAllText(path, ShaderTemplate.Replace("Custom/New Shader", "Custom/" + name));
        return true;
    }

    private static bool WriteComputeShader(string path, string _)
    {
        File.WriteAllText(path, ComputeShaderTemplate);
        return true;
    }

    // Creates a file at once under a unique version of the default name, for callers that skip naming it.
    private static string? CreateNow(string relativeFolder, string defaultName, string extension, CreateAssetTask.Writer write)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;
        string name = FindUniqueName(absFolder, defaultName, extension);
        try
        {
            if (!write(Path.Combine(absFolder, name), Path.GetFileNameWithoutExtension(name))) return null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to create '{name}': {ex.Message}");
            return null;
        }
        EditorAssetBackend.Instance?.InvalidateFolderIndex();
        return string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
    }

    /// <summary> Creates an asset file from an AssetMenuEntry by instantiating its type, serializing it, and writing to disk. Returns the relative path of the created asset, or null if the folder does not exist or creation fails. </summary>
    public static string? CreateAsset(AssetMenuEntry entry, string relativeFolder)
        => CreateNow(relativeFolder, DefaultName(entry), entry.Extension, AssetWriter(entry));

    /// <summary>
    /// The folder new assets go into: the selected folder if there is one, otherwise the folder
    /// the project panel is currently browsing.
    /// </summary>
    public static string GetCurrentFolder()
    {
        ContentItem? selected = Selection.GetActiveAs<ContentItem>();
        if (selected != null && selected.IsFolder)
            return selected.RelativePath;
        return ProjectPanel.Instance?.CurrentFolder ?? "";
    }

    /// <summary> Converts a project-relative folder path to an absolute file-system path by combining it with the project's AssetsPath. Returns an empty string if no project is open. </summary>
    public static string GetAbsoluteFolder(string relativeFolder)
    {
        if (Project.Current == null) return "";
        return string.IsNullOrEmpty(relativeFolder)
            ? Project.Current.AssetsPath
            : Path.Combine(Project.Current.AssetsPath, relativeFolder);
    }

    /// <summary> Returns a unique file name in the given folder by appending a number suffix if the base name already exists. </summary>
    public static string FindUniqueName(string folder, string baseName, string ext)
        => Utils.UniqueNames.ForFile(folder, baseName, ext);

    /// <summary> Creates a new folder in the project at the given relative path, with a unique name and a DefaultImporter meta file. Returns the relative path of the created folder, or null if the parent folder does not exist. </summary>
    public static string? CreateFolder(string relativeFolder) => CreateNow(relativeFolder, "New Folder", "", WriteFolder);

    /// <summary> Creates a new assembly definition file in the project at the given relative path, with a unique name. Returns the relative path of the created file, or null if the parent folder does not exist. </summary>
    public static string? CreateAssemblyDefinition(string relativeFolder)
        => CreateNow(relativeFolder, "NewAssembly", Projects.Scripting.AssemblyDefinitionDatabase.Extension, WriteAssemblyDefinition);

    /// <summary> Creates a new .shader file in the project at the given relative path, with a unique name and a lit surface shader to start from. Returns the relative path of the created file, or null if the parent folder does not exist. </summary>
    public static string? CreateShader(string relativeFolder) => CreateNow(relativeFolder, "New Shader", ".shader", WriteShader);

    /// <summary> Creates a new .compute file in the project at the given relative path, with one kernel to start from. Returns the relative path of the created file, or null if the parent folder does not exist. </summary>
    public static string? CreateComputeShader(string relativeFolder) => CreateNow(relativeFolder, "New Compute Shader", ".compute", WriteComputeShader);

    private const string ShaderTemplate = @"// A lit surface with every feature of the standard Lit shader: shadows, lightmaps, probes, decals, fog, surface
// types and the Lit material inputs. Surface runs before lighting and may change any OpenPBR input of p, such as
// baseColor, specularRoughness, baseMetalness, geometryNormal, geometryOpacity, emissionColor and emissionLuminance in
// nits, reading uv, uv1, vertexColor, relativePos, viewDir, geometryNormal, tangent and bitangent of s.
// Surface ""Unlit"" gives the Unlit shader's passes instead.
Shader ""Custom/New Shader""
{
    Properties
    {
        _RimColor(""Rim Color"", Color) = (0.3, 0.6, 1.0, 1.0)
        _RimPower(""Rim Power"", Float) = 3.0 [Range(0.5, 8)]
        _RimLuminance(""Rim Luminance"", Float) = 1.0
    }

    Surface ""Lit""
    {
        GLSLPROGRAM
        uniform vec4 _RimColor;
        uniform float _RimPower;
        uniform float _RimLuminance;

        void Surface(inout PBR_Params p, Graphite_SurfaceData s)
        {
            float rim = pow(1.0 - max(dot(p.geometryNormal, s.viewDir), 0.0), _RimPower);
            p.emissionColor = _RimColor.rgb;
            p.emissionLuminance += _RimLuminance * rim;
        }
        ENDGLSL
    }
}
";

    private const string ComputeShaderTemplate = @"// Each kernel is a function, declared with its thread group size: #pragma kernel Name X Y Z
// Find it with shader.FindKernel(""Main""), set buffers and textures per kernel, then Dispatch.
// Storage blocks and images need no binding, the engine assigns them and binds by name.
#pragma kernel Main 64

layout(std430) buffer Values
{
    float values[];
};

uniform float _Scale;

void Main()
{
    uint i = gl_GlobalInvocationID.x;
    if (i >= uint(values.length())) return;
    values[i] *= _Scale;
}
";
}
