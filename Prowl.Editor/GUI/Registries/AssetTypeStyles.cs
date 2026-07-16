// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Drawing;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;

namespace Prowl.Editor.GUI.Registries;

/// <summary>
/// Visual style for an asset/file type in the browser: either a vector <see cref="IOrigamiIcon"/> or a
/// short text <see cref="Badge"/> (e.g. "C#"), plus an accent <see cref="Color"/> used for the tinted
/// thumbnail tile. Folders are <see cref="Bare"/> (no tile, just the icon).
/// </summary>
public readonly struct AssetTypeStyle
{
    public IOrigamiIcon? Icon { get; init; }
    public string? Badge { get; init; }
    public Color Color { get; init; }
    public bool Bare { get; init; }
}

/// <summary>
/// Maps file extensions to a <see cref="AssetTypeStyle"/> using Origami's vector icon set (moving the
/// asset browser off the Font Awesome glyph font). Extend via <see cref="Register"/>.
/// </summary>
public static class AssetTypeStyles
{
    private static Color C(int r, int g, int b) => Color.FromArgb(255, r, g, b);
    private static readonly Color Amber = C(251, 191, 36), Green = C(74, 222, 128), Blue = C(96, 165, 250),
        Purple = C(168, 85, 247), Pink = C(217, 107, 216), Cyan = C(52, 211, 238), Orange = C(251, 146, 60),
        Gray = C(148, 143, 171), Red = C(251, 113, 133);

    public static AssetTypeStyle Folder => new() { Icon = EditorIcons.Folder_I, Color = Amber, Bare = true };
    public static AssetTypeStyle SubAsset => new() { Icon = EditorIcons.Cube_I, Color = Purple };

    private static readonly System.Collections.Generic.Dictionary<string, AssetTypeStyle> _map =
        new(System.StringComparer.OrdinalIgnoreCase)
    {
        // Code / binaries - text badges read better than an icon.
        [".cs"] = new() { Badge = "C#", Color = Green },
        [".dll"] = new() { Badge = "DLL", Color = Orange },
        [".so"] = new() { Badge = "DLL", Color = Orange },
        [".dylib"] = new() { Badge = "DLL", Color = Orange },
        [".asmdef"] = new() { Badge = "ASM", Color = Blue },
        // Shaders.
        [".shader"] = new() { Icon = EditorIcons.Bolt_I, Color = Pink },
        [".glsl"] = new() { Icon = EditorIcons.Bolt_I, Color = Pink },
        [".hlsl"] = new() { Icon = EditorIcons.Bolt_I, Color = Pink },
        [".compute"] = new() { Icon = EditorIcons.Bolt_I, Color = Pink },
        [".shadergraph"] = new() { Icon = EditorIcons.Bolt_I, Color = Pink },
        // Textures / images.
        [".png"] = new() { Icon = EditorIcons.Image_I, Color = Blue },
        [".jpg"] = new() { Icon = EditorIcons.Image_I, Color = Blue },
        [".jpeg"] = new() { Icon = EditorIcons.Image_I, Color = Blue },
        [".tga"] = new() { Icon = EditorIcons.Image_I, Color = Blue },
        [".dds"] = new() { Icon = EditorIcons.Image_I, Color = Blue },
        [".bmp"] = new() { Icon = EditorIcons.Image_I, Color = Blue },
        [".gif"] = new() { Icon = EditorIcons.Image_I, Color = Blue },
        [".hdr"] = new() { Icon = EditorIcons.Image_I, Color = Blue },
        [".exr"] = new() { Icon = EditorIcons.Image_I, Color = Blue },
        [".psd"] = new() { Icon = EditorIcons.Image_I, Color = Blue },
        // Materials.
        [".mat"] = new() { Icon = EditorIcons.Palette_I, Color = Pink },
        [".material"] = new() { Icon = EditorIcons.Palette_I, Color = Pink },
        // Meshes / models.
        [".fbx"] = new() { Icon = EditorIcons.Cubes_I, Color = Purple },
        [".obj"] = new() { Icon = EditorIcons.Cubes_I, Color = Purple },
        [".gltf"] = new() { Icon = EditorIcons.Cubes_I, Color = Purple },
        [".glb"] = new() { Icon = EditorIcons.Cubes_I, Color = Purple },
        [".dae"] = new() { Icon = EditorIcons.Cubes_I, Color = Purple },
        [".blend"] = new() { Icon = EditorIcons.Cubes_I, Color = Purple },
        [".mesh"] = new() { Icon = EditorIcons.Cubes_I, Color = Purple },
        // Scenes / prefabs.
        [".scene"] = new() { Icon = EditorIcons.Shapes_I, Color = Amber },
        [".prefab"] = new() { Icon = EditorIcons.Cube_I, Color = Blue },
        // Audio.
        [".mp3"] = new() { Icon = EditorIcons.Music_I, Color = Cyan },
        [".wav"] = new() { Icon = EditorIcons.Music_I, Color = Cyan },
        [".ogg"] = new() { Icon = EditorIcons.Music_I, Color = Cyan },
        [".flac"] = new() { Icon = EditorIcons.Music_I, Color = Cyan },
        // Fonts.
        [".ttf"] = new() { Icon = EditorIcons.Font_I, Color = Amber },
        [".otf"] = new() { Icon = EditorIcons.Font_I, Color = Amber },
        [".woff"] = new() { Icon = EditorIcons.Font_I, Color = Amber },
        // Data / text.
        [".json"] = new() { Icon = EditorIcons.Code_I, Color = Gray },
        [".txt"] = new() { Icon = EditorIcons.FileLines_I, Color = Gray },
        [".md"] = new() { Icon = EditorIcons.FileLines_I, Color = Gray },
        [".xml"] = new() { Icon = EditorIcons.Code_I, Color = Gray },
        [".yaml"] = new() { Icon = EditorIcons.Code_I, Color = Gray },
        [".yml"] = new() { Icon = EditorIcons.Code_I, Color = Gray },
        [".csv"] = new() { Icon = EditorIcons.FileLines_I, Color = Gray },
        // Video.
        [".mp4"] = new() { Icon = EditorIcons.Image_I, Color = Red },
        [".mov"] = new() { Icon = EditorIcons.Image_I, Color = Red },
        [".mkv"] = new() { Icon = EditorIcons.Image_I, Color = Red },
        [".webm"] = new() { Icon = EditorIcons.Image_I, Color = Red },
        // Archives / packages.
        [".zip"] = new() { Icon = EditorIcons.LayerGroup_I, Color = Gray },
        [".prowlpackage"] = new() { Icon = EditorIcons.LayerGroup_I, Color = Purple },
    };

    private static readonly AssetTypeStyle _default = new() { Icon = EditorIcons.FileLines_I, Color = Gray };

    /// <summary>Register (or override) the style for an extension (leading dot, e.g. ".prefab").</summary>
    public static void Register(string extension, AssetTypeStyle style)
    {
        if (!string.IsNullOrEmpty(extension)) _map[extension] = style;
    }

    /// <summary>Style for an extension; falls back to keywords in <paramref name="typeLabel"/> (the asset's
    /// main type name) for engine assets whose extension isn't a well-known one (e.g. a <c>.asset</c> that is
    /// really TerrainData / a Texture / a Material).</summary>
    public static AssetTypeStyle For(string extension, string? typeLabel = null)
    {
        if (!string.IsNullOrEmpty(extension) && _map.TryGetValue(extension, out var s))
            return s;

        if (!string.IsNullOrEmpty(typeLabel))
        {
            string t = typeLabel!;
            bool Has(string k) => t.Contains(k, System.StringComparison.OrdinalIgnoreCase);
            if (Has("Terrain"))                       return new() { Icon = EditorIcons.Mountain_I, Color = Green };
            if (Has("Texture") || Has("Sprite"))      return new() { Icon = EditorIcons.Image_I, Color = Blue };
            if (Has("Material"))                      return new() { Icon = EditorIcons.Palette_I, Color = Pink };
            if (Has("Mesh") || Has("Model"))          return new() { Icon = EditorIcons.Cubes_I, Color = Purple };
            if (Has("Shader"))                        return new() { Icon = EditorIcons.Bolt_I, Color = Pink };
            if (Has("Scene"))                         return new() { Icon = EditorIcons.Shapes_I, Color = Amber };
            if (Has("Prefab") || Has("GameObject"))   return new() { Icon = EditorIcons.Cube_I, Color = Blue };
            // Animation before Audio: "AnimationClip" must not get swallowed by the generic "Clip" rule.
            if (Has("Animation") || Has("Anim"))      return new() { Icon = EditorIcons.Film_I, Color = Cyan };
            if (Has("Audio") || Has("Sound") || Has("Clip")) return new() { Icon = EditorIcons.Music_I, Color = Cyan };
            if (Has("Font"))                          return new() { Icon = EditorIcons.Font_I, Color = Amber };
            if (Has("Script") || Has("MonoBehaviour")) return new() { Badge = "C#", Color = Green };
        }
        return _default;
    }
}
