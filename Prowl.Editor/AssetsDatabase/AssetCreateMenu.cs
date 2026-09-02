using System;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.OrigamiUI;
using Prowl.Rosetta;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.Popups;
using Prowl.Editor.Theming;
using Prowl.Editor.Projects;
using Prowl.Editor.Core.Tasks;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Prowl.Editor.GUI;
using Prowl.Editor.Prefabs;

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
        foreach (var go in GameObjectClipboard.FilterToRoots(selected))
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

    /// <summary>
    /// The folder new assets go into: the selected folder if there is one, otherwise the folder
    /// the project panel is currently browsing.
    /// </summary>
    public static string GetCurrentFolder()
    {
        var selected = Selection.GetActiveAs<ContentItem>();
        if (selected != null && selected.IsFolder)
            return selected.RelativePath;
        return ProjectPanel.Instance?.CurrentFolder ?? "";
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
                EditorAssetBackend.Instance?.InvalidateFolderIndex();

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
    _SurfaceTex (""Surface (G Roughness, B Metallic)"", Texture2D) = ""surface""
    _Metallic (""Metallic"", Range(0.0, 1.0)) = 1.0
    _Roughness (""Roughness"", Range(0.0, 1.0)) = 1.0
    _OcclusionTex (""Occlusion (R)"", Texture2D) = ""white""
    _EmissionTex (""Emission"", Texture2D) = ""emission""
    _EmissiveColor (""Emissive Color"", Color) = (1.0, 1.0, 1.0, 1.0)
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
            uniform float _Metallic;
            uniform float _Roughness;
            uniform sampler2D _OcclusionTex;
            uniform sampler2D _EmissionTex;
            uniform vec4 _EmissiveColor;
            uniform float _EmissionIntensity;
            uniform vec4 _MainColor;

            void main()
            {
                // Albedo. Colour textures are sRGB and get decoded here; _MainColor and the vertex
                // colour are linear and multiply in afterwards.
                vec4 albedoTexel = texture(_MainTex, texCoord0);
                vec3 baseColor = gammaToLinearSpace(albedoTexel.rgb) * _MainColor.rgb * vColor.rgb;
                float alpha = albedoTexel.a * _MainColor.a * vColor.a;

                // Normal mapping
                vec3 worldNormal = ApplyNormalMap(_NormalTex, texCoord0, vNormal, vTangent, vBitangent);

                // Surface: G = Roughness, B = Metallic, each scaled by its factor
                vec4 surface = texture(_SurfaceTex, texCoord0);
                float roughness = clamp(surface.g * _Roughness, 0.0, 1.0);
                float metallic = clamp(surface.b * _Metallic, 0.0, 1.0);

                // Ambient occlusion: R channel, 1 = unoccluded
                float ao = texture(_OcclusionTex, texCoord0).r;

                // Emission
                vec3 emission = gammaToLinearSpace(texture(_EmissionTex, texCoord0).rgb) * _EmissiveColor.rgb * _EmissionIntensity;

                // PBR lighting + ambient + fog
                vec3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                vec3 lighting = CalculateForwardLighting(worldPos, worldNormal, viewDir,
                                                         baseColor, metallic, roughness, ao);
                vec3 ambient = CalculateAmbient(worldNormal) * baseColor * ao * _AmbientStrength;
                vec3 color = ApplyFog(ambient + lighting + emission, worldPos);

                fragColor = vec4(color, alpha);
            }
        }
    ENDGLSL
}

// === Depth + Normals Pre-Pass (feeds GTAO, SSR, TAA and motion blur) ===
Pass ""Prepass""
{
    Tags { ""LightMode"" = ""Prepass"" }
    Cull Back
    ZWrite On

    GLSLPROGRAM

        Vertex
        {
            #include ""ProwlCG""
            #include ""VertexAttributes""

            out vec3 vNormal;
            out vec3 vTangent;
            out vec3 vBitangent;
            out vec2 texCoord0;
            out vec4 vCurrClipNJ;
            out vec4 vPrevClip;

            void main()
            {
                gl_Position = TransformClip(vertexPosition); // jittered, for raster + depth
                vNormal     = TransformDirection(vertexNormal);
#ifdef HAS_TANGENTS
                vTangent    = TransformDirection(vertexTangent.xyz);
                vBitangent  = cross(vNormal, vTangent);
#endif
                texCoord0   = vertexTexCoord0;

                // Jitter-free current and previous clip positions for motion vectors.
                vec4 worldPos = GetModelMatrix() * vec4(vertexPosition, 1.0);
                vCurrClipNJ = PROWL_MATRIX_VP_NONJITTERED * worldPos;
                vec4 prevWorldPos = PROWL_MATRIX_M_PREVIOUS * vec4(vertexPosition, 1.0);
                vPrevClip = PROWL_MATRIX_VP_PREVIOUS * prevWorldPos;
            }
        }

        Fragment
        {
            #include ""ProwlCG""

            layout (location = 0) out vec4 normalOut;
            layout (location = 1) out vec4 motionRM;

            in vec3 vNormal;
            in vec3 vTangent;
            in vec3 vBitangent;
            in vec2 texCoord0;
            in vec4 vCurrClipNJ;
            in vec4 vPrevClip;

            uniform sampler2D _NormalTex;
            uniform sampler2D _SurfaceTex;
            uniform float _Metallic;
            uniform float _Roughness;

            void main()
            {
                vec3 worldNormal = ApplyNormalMap(_NormalTex, texCoord0, vNormal, vTangent, vBitangent);
                normalOut = EncodeViewNormal(worldNormal);

                // Motion vectors plus the roughness/metallic SSR samples. These must match the
                // forward pass or reflections come off the wrong surface.
                vec2 currNDC = (vCurrClipNJ.xy / vCurrClipNJ.w) * 0.5 + 0.5;
                vec2 prevNDC = (vPrevClip.xy / vPrevClip.w) * 0.5 + 0.5;
                vec4 surface = texture(_SurfaceTex, texCoord0);
                motionRM = vec4(currNDC - prevNDC,
                                clamp(surface.g * _Roughness, 0.0, 1.0),
                                clamp(surface.b * _Metallic, 0.0, 1.0));
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
        EditorAssetBackend.Instance?.InvalidateFolderIndex();
        Debug.Log($"Created shader: {name}");
        return string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
    }

}
