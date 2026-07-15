using System;
using System.IO;
using System.Linq;

using Prowl.Echo;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.Popups;
using Prowl.Editor.Theming;
using Prowl.Editor.Projects;
using Prowl.Editor.Core.Tasks;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor;

public static class AssetCreateMenu
{
    [MenuItem("Assets/Create/Folder", priority: 0, Icon = EditorIcons.Folder)]
    static void CreateFolderItem()
    {
        var task = new CreateAssetTask();
        task.TaskType = CreateAssetTask.AssetType.Folder;
        task.BeginCreateTask(new AssetMenuEntry { Name = "New Folder", Extension = "", Icon = EditorRegistries.GetFileIconForExtension("") }, GetCurrentFolder());
    }

    [MenuItem("Assets/Create/Shader", priority: 1000, Icon = EditorIcons.WandMagicSparkles, Separator = true)]
    static void CreateShaderItem() => CreateShader(GetCurrentFolder());

    [MenuItem("Assets/Create/C# Script", priority: 1010, Icon = EditorIcons.FileCode, Separator = true)]
    static void CreateScriptItem() => NewScriptDialog.Open(GetCurrentFolder());

    [MenuItem("Assets/Create/Assembly Definition", priority: 1011, Icon = EditorIcons.FileLines)]
    static void CreateAsmDefItem() => CreateAssemblyDefinition(GetCurrentFolder());

    public static string? CreateAsset(AssetMenuEntry entry, string relativeFolder, string? filename = null)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;

        int lastSlash = entry.Name.LastIndexOf('/');
        string baseName = lastSlash >= 0 ? entry.Name.Substring(lastSlash + 1) : entry.Name;
        string name = filename ?? FindUniqueName(absFolder, $"New {baseName}", entry.Extension);
        string filePath = Path.Combine(absFolder, name);

        try
        {
            object? instance = entry.Factory != null ? entry.Factory() : Activator.CreateInstance(entry.Type);
            var echo = Prowl.Echo.Serializer.Serialize(typeof(object), instance);
            if (echo != null) File.WriteAllText(filePath, echo.WriteToString());
            EditorAssetBackend.Instance?.InvalidateFolderIndex();
            Debug.Log($"Created {entry.Name}: {name}");
            return string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
        }
        catch (Exception ex) { Debug.LogError($"Failed to create {entry.Name}: {ex.Message}"); return null; }
    }

    public static string GetCurrentFolder()
    {
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
        => Utils.UniqueNames.ForFile(folder, baseName, ext);

    public static string? CreateFolder(string relativeFolder)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;

        string name = FindUniqueName(absFolder, "New Folder", "");
        string newPath = Path.Combine(absFolder, name);
        Directory.CreateDirectory(newPath);
        MetaFile.EnsureMeta(newPath, "DefaultImporter");
        EditorAssetBackend.Instance?.InvalidateFolderIndex();
        Debug.Log($"Created folder: {name}");
        string relPath = string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
        return relPath;
    }

    public static string? CreateAssemblyDefinition(string relativeFolder)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;

        string name = FindUniqueName(absFolder, "NewAssembly", Projects.Scripting.AssemblyDefinitionDatabase.Extension);
        string filePath = Path.Combine(absFolder, name);

        var def = new Projects.Scripting.AssemblyDefinition { Name = Path.GetFileNameWithoutExtension(name) };
        def.WriteToFile(filePath);

        Debug.Log($"Created assembly definition: {name}");
        return string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
    }

    public static string? CreateShader(string relativeFolder)
    {
        string absFolder = GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;

        string name = FindUniqueName(absFolder, "New Shader", ".shader");
        string filePath = Path.Combine(absFolder, name);

        File.WriteAllText(filePath, @"// Custom PBR Shader
// GPU instancing, skeletal animation, shadows, and fog are handled
// automatically by the VertexAttributes and Lighting includes.
//
// Vertex utilities (from VertexAttributes.glsl):
//   TransformClip(pos)       - position to clip space (handles instancing + skinning)
//   TransformPosition(pos)   - position to world space
//   TransformDirection(dir)  - normal/tangent to world space
//   GetInstanceColor()       - vertex color with instance tint
//   GetInstanceCustomData()  - per-instance custom vec4
//   GetModelMatrix()         - model matrix (instanced or per-object)
//   GetMVPMatrix()           - MVP matrix
//
// Lighting utilities (from Lighting.glsl):
//   CalculateForwardLighting(worldPos, normal, viewDir, albedo, metallic, roughness, ao)
//   CalculateAmbient(worldNormal)
//   ApplyFog(color, worldPos)

Shader ""Custom/NewShader""

Properties
{
    _MainTex (""Albedo"", Texture2D) = ""white""
    _MainColor (""Tint"", Color) = (1.0, 1.0, 1.0, 1.0)
    _NormalTex (""Normal"", Texture2D) = ""normal""
    _SurfaceTex (""Surface (AO, Roughness, Metallic)"", Texture2D) = ""surface""
    _EmissionTex (""Emission"", Texture2D) = ""emission""
    _EmissionIntensity (""Emission Intensity"", Float) = 1.0
}

// === Main Forward Lit Pass ===
Pass ""Default""
{
    Tags { ""RenderOrder"" = ""Opaque"" }
    Cull Back

    GLSLPROGRAM

        Vertex
        {
            #include ""ProwlCG""
            #include ""VertexAttributes""

            out vec2 texCoord0;
            out vec3 worldPos;
            out vec4 vColor;
            out vec3 vNormal;
            out vec3 vTangent;
            out vec3 vBitangent;

            void main()
            {
                gl_Position = TransformClip(vertexPosition);
                texCoord0   = vertexTexCoord0;
                worldPos    = TransformPosition(vertexPosition);
                vColor      = GetInstanceColor();
                vNormal     = TransformDirection(vertexNormal);
#ifdef HAS_TANGENTS
                vTangent    = TransformDirection(vertexTangent.xyz);
                vBitangent  = cross(vNormal, vTangent);
#endif
            }
        }

        Fragment
        {
            #include ""ProwlCG""
            #include ""Lighting""

            layout (location = 0) out vec4 fragColor;

            in vec2 texCoord0;
            in vec3 worldPos;
            in vec4 vColor;
            in vec3 vNormal;
            in vec3 vTangent;
            in vec3 vBitangent;

            uniform sampler2D _MainTex;
            uniform sampler2D _NormalTex;
            uniform sampler2D _SurfaceTex;
            uniform sampler2D _EmissionTex;
            uniform float _EmissionIntensity;
            uniform vec4 _MainColor;

            void main()
            {
                // Albedo
                vec4 albedo = texture(_MainTex, texCoord0) * vColor * _MainColor;
                vec3 baseColor = gammaToLinearSpace(albedo.rgb);

                // Normal mapping
                vec3 worldNormal = ApplyNormalMap(_NormalTex, texCoord0, vNormal, vTangent, vBitangent);

                // Surface: R = AO, G = Roughness, B = Metallic
                vec4 surface = texture(_SurfaceTex, texCoord0);
                float ao = 1.0 - surface.r;
                float roughness = surface.g;
                float metallic = surface.b;

                // Emission
                vec3 emission = texture(_EmissionTex, texCoord0).rgb * _EmissionIntensity;

                // PBR lighting + ambient + fog
                vec3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                vec3 lighting = CalculateForwardLighting(worldPos, worldNormal, viewDir,
                                                         baseColor, metallic, roughness, ao);
                vec3 ambient = CalculateAmbient(worldNormal) * baseColor * ao * _AmbientStrength;
                vec3 color = ApplyFog(ambient + lighting + emission, worldPos);

                fragColor = vec4(color, albedo.a);
            }
        }
    ENDGLSL
}

// === Depth + Normals Pre-Pass (for GTAO, SSR) ===
Pass ""DepthNormals""
{
    Tags { ""LightMode"" = ""DepthNormals"" }
    Cull Back

    GLSLPROGRAM

        Vertex
        {
            #include ""ProwlCG""
            #include ""VertexAttributes""

            out vec3 vNormal;
            out vec3 vTangent;
            out vec3 vBitangent;
            out vec2 texCoord0;

            void main()
            {
                gl_Position = TransformClip(vertexPosition);
                vNormal     = TransformDirection(vertexNormal);
#ifdef HAS_TANGENTS
                vTangent    = TransformDirection(vertexTangent.xyz);
                vBitangent  = cross(vNormal, vTangent);
#endif
                texCoord0   = vertexTexCoord0;
            }
        }

        Fragment
        {
            #include ""ProwlCG""

            layout (location = 0) out vec4 normalOut;
            in vec3 vNormal;
            in vec3 vTangent;
            in vec3 vBitangent;
            in vec2 texCoord0;

            uniform sampler2D _NormalTex;

            void main()
            {
                vec3 worldNormal = ApplyNormalMap(_NormalTex, texCoord0, vNormal, vTangent, vBitangent);
                normalOut = EncodeViewNormal(worldNormal);
            }
        }
    ENDGLSL
}

// === Shadow Caster Pass ===
Pass ""ShadowCaster""
{
    Tags { ""LightMode"" = ""ShadowCaster"" }
    Cull Back

    GLSLPROGRAM

        Vertex
        {
            #include ""ProwlCG""
            #include ""VertexAttributes""

            void main()
            {
                gl_Position = TransformClip(vertexPosition);
            }
        }

        Fragment
        {
            #include ""ProwlCG""

            void main()
            {
                gl_FragDepth = gl_FragCoord.z;
            }
        }
    ENDGLSL
}
");

        Debug.Log($"Created shader: {name}");
        return string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
    }

}
