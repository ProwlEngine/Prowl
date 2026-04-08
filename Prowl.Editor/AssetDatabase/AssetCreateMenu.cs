using System;
using System.IO;
using System.Linq;

using Prowl.Editor.Panels;
using Prowl.Editor.Widgets;
using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary>
/// Shared asset creation menu items used by both the Project panel Add button
/// and the Assets menu bar. All creation happens in the current folder.
/// </summary>
public static class AssetCreateMenu
{
    /// <summary>
    /// Populates a context menu builder with asset creation options.
    /// </summary>
    public static void Build(ContextMenuBuilder builder, string currentFolder, Action<string>? onCreated = null)
    {
        builder.Item($"{EditorIcons.Folder}  Folder", () => { var p = CreateFolder(currentFolder); if (p != null) onCreated?.Invoke(p); });
        builder.Separator();

        // Registry-discovered asset types (Scene, Material, InputActions, user-defined, etc.)
        CreateAssetMenuRegistry.BuildMenu(builder, currentFolder, onCreated);

        builder.Item($"{EditorIcons.WandMagicSparkles}  Shader", () => { var p = CreateShader(currentFolder); if (p != null) onCreated?.Invoke(p); });
        builder.Separator();
        builder.Item($"{EditorIcons.FileCode}  C# Script", () => { var p = CreateScript(currentFolder); if (p != null) onCreated?.Invoke(p); });
    }

    /// <summary>
    /// Register the Assets top-level menu with the MenuRegistry.
    /// </summary>
    public static void RegisterMenus()
    {
        MenuRegistry.Register("Assets/Create Folder", () => CreateFolder(GetCurrentFolder()));
        MenuRegistry.RegisterSeparator("Assets");

        // Registry-discovered asset types
        CreateAssetMenuRegistry.RegisterMenuBarItems();

        MenuRegistry.Register("Assets/Create Shader", () => CreateShader(GetCurrentFolder()));
        MenuRegistry.RegisterSeparator("Assets");
        MenuRegistry.Register("Assets/Create C# Script", () => CreateScript(GetCurrentFolder()));
        MenuRegistry.RegisterSeparator("Assets");
        MenuRegistry.Register("Assets/Refresh", () =>
        {
            if (Project.Current != null)
            {
                var db = new EditorAssetDatabase(Project.Current);
                db.Initialize();
            }
        });
        MenuRegistry.Register("Assets/Reimport All", () =>
        {
            var db = EditorAssetDatabase.Instance;
            if (db == null) return;
            foreach (var entry in db.GetAllEntries().ToList())
                db.Reimport(entry.Guid);
            Runtime.Debug.Log("[AssetDatabase] Reimported all assets.");
        });
    }

    public static string GetCurrentFolder()
    {
        // Try to get the current folder from the active selection
        var selected = Selection.GetActiveAs<ContentItem>();
        if (selected != null && selected.IsFolder)
            return selected.RelativePath;
        return "";
    }

    public static string GetAbsoluteFolder(string relativeFolder)
    {
        if (Project.Current == null) return "";
        return string.IsNullOrEmpty(relativeFolder)
            ? Project.Current.AssetsPath
            : Path.Combine(Project.Current.AssetsPath, relativeFolder);
    }

    public static string FindUniqueName(string folder, string baseName, string ext)
    {
        string path = Path.Combine(folder, baseName + ext);
        if (!File.Exists(path) && !Directory.Exists(path)) return baseName + ext;

        for (int i = 1; i < 999; i++)
        {
            string name = $"{baseName} ({i}){ext}";
            path = Path.Combine(folder, name);
            if (!File.Exists(path) && !Directory.Exists(path)) return name;
        }
        return baseName + ext;
    }

    public static string? CreateFolder(string relativeFolder)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;

        string name = FindUniqueName(absFolder, "New Folder", "");
        string newPath = Path.Combine(absFolder, name);
        Directory.CreateDirectory(newPath);
        MetaFile.EnsureMeta(newPath, "DefaultImporter");
        Debug.Log($"Created folder: {name}");
        string relPath = string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
        return relPath;
    }

    public static string? CreateShader(string relativeFolder)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;

        string name = FindUniqueName(absFolder, "New Shader", ".shader");
        string filePath = Path.Combine(absFolder, name);

        File.WriteAllText(filePath, @"Shader ""NewShader""
{
    Properties
    {
        _MainTex(""Main Texture"", Texture2D) = ""white""
        _Color(""Color"", Color) = (1, 1, 1, 1)
    }

    Pass ""Default""
    {
        Tags { ""RenderOrder"" = ""Opaque"" }

        GLSLPROGRAM

        #version 410 core

        layout(location = 0) in vec3 vertexPosition;
        layout(location = 1) in vec2 vertexTexCoord;
        layout(location = 2) in vec3 vertexNormal;

        uniform mat4 _MatMVP;

        out vec2 TexCoords;

        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = _MatMVP * vec4(vertexPosition, 1.0);
        }

        #FRAGMENT

        in vec2 TexCoords;

        uniform sampler2D _MainTex;
        uniform vec4 _Color;

        out vec4 FragColor;

        void main()
        {
            FragColor = texture(_MainTex, TexCoords) * _Color;
        }

        ENDGLSL
    }
}
");

        Debug.Log($"Created shader: {name}");
        return string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
    }

    public static string? CreateScript(string relativeFolder)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;

        string name = FindUniqueName(absFolder, "NewScript", ".cs");
        string className = Path.GetFileNameWithoutExtension(name);
        string filePath = Path.Combine(absFolder, name);

        File.WriteAllText(filePath, $@"using Prowl.Runtime;

public class {className} : MonoBehaviour
{{
    public override void Start()
    {{
    }}

    public override void Update()
    {{
    }}
}}
");

        Debug.Log($"Created script: {name}");
        return string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
    }
}
