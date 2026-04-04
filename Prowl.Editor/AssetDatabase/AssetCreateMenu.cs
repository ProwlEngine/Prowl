using System;
using System.IO;

using Prowl.Echo;
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
    public static void Build(ContextMenuBuilder builder, string currentFolder)
    {
        builder.Item($"{EditorIcons.Folder}  Folder", () => CreateFolder(currentFolder));
        builder.Separator();
        builder.Item($"{EditorIcons.Cubes}  Scene", () => CreateScene(currentFolder));
        builder.Item($"{EditorIcons.Palette}  Material", () => CreateMaterial(currentFolder));
        builder.Item($"{EditorIcons.WandMagicSparkles}  Shader", () => CreateShader(currentFolder));
        builder.Separator();
        builder.Item($"{EditorIcons.FileCode}  C# Script", () => CreateScript(currentFolder));
    }

    /// <summary>
    /// Register the Assets top-level menu with the MenuRegistry.
    /// </summary>
    public static void RegisterMenus()
    {
        MenuRegistry.Register("Assets/Create Folder", () => CreateFolder(GetCurrentFolder()));
        MenuRegistry.RegisterSeparator("Assets");
        MenuRegistry.Register("Assets/Create Scene", () => CreateScene(GetCurrentFolder()));
        MenuRegistry.Register("Assets/Create Material", () => CreateMaterial(GetCurrentFolder()));
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
    }

    private static string GetCurrentFolder()
    {
        // Try to get the current folder from the active selection
        var selected = Selection.GetActiveAs<ContentItem>();
        if (selected != null && selected.IsFolder)
            return selected.RelativePath;
        return "";
    }

    private static string GetAbsoluteFolder(string relativeFolder)
    {
        if (Project.Current == null) return "";
        return string.IsNullOrEmpty(relativeFolder)
            ? Project.Current.AssetsPath
            : Path.Combine(Project.Current.AssetsPath, relativeFolder);
    }

    private static string FindUniqueName(string folder, string baseName, string ext)
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

    public static void CreateFolder(string relativeFolder)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return;

        string name = FindUniqueName(absFolder, "New Folder", "");
        string newPath = Path.Combine(absFolder, name);
        Directory.CreateDirectory(newPath);
        MetaFile.EnsureMeta(newPath, "DefaultImporter");
        Debug.Log($"Created folder: {name}");
    }

    public static void CreateScene(string relativeFolder)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return;

        string name = FindUniqueName(absFolder, "New Scene", ".scene");
        string filePath = Path.Combine(absFolder, name);

        // Create an empty scene
        var scene = new Runtime.Resources.Scene();
        var ctx = new SerializationContext();
        Runtime.AssetDatabase.ConfigureContext(ctx);
        var echo = Serializer.Serialize(scene, ctx);
        if (echo != null)
            File.WriteAllText(filePath, echo.WriteToString());

        Debug.Log($"Created scene: {name}");
    }

    public static void CreateMaterial(string relativeFolder)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return;

        string name = FindUniqueName(absFolder, "New Material", ".mat");
        string filePath = Path.Combine(absFolder, name);

        // Create a minimal material file (Echo format)
        var echo = EchoObject.NewCompound();
        echo["$type"] = new EchoObject(typeof(Runtime.Resources.Material).AssemblyQualifiedName);
        File.WriteAllText(filePath, echo.WriteToString());

        Debug.Log($"Created material: {name}");
    }

    public static void CreateShader(string relativeFolder)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return;

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
    }

    public static void CreateScript(string relativeFolder)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return;

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
    }
}
