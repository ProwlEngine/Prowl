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
        builder.Item($"{EditorIcons.FileCode}  C# Script", () => NewScriptDialog.Open(currentFolder, onCreated));
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
        MenuRegistry.Register("Assets/Create C# Script", () => NewScriptDialog.Open(GetCurrentFolder()));
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

    /// <inheritdoc cref="Utils.UniqueNames.ForFile" />
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
            #include ""Fragment""
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
            #include ""Fragment""
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
            #include ""Fragment""
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
            #include ""Fragment""

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
            #include ""Fragment""
            #include ""VertexAttributes""

            void main()
            {
                gl_Position = TransformClip(vertexPosition);
            }
        }

        Fragment
        {
            #include ""Fragment""

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
