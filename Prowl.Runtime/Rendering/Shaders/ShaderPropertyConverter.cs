// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using ParsedProperty = Prowl.Graphite.ShaderDef.ShaderProperty;
using ParsedPropertyType = Prowl.Graphite.ShaderDef.ShaderPropertyType;

namespace Prowl.Runtime.Rendering.Shaders;

/// <summary>
/// Converts a parsed <see cref="Prowl.Graphite.ShaderDef.ShaderProperty"/> (Range hints, string-named
/// texture defaults) into the material-facing <see cref="ShaderProperty"/> the engine actually uses.
/// Shared between <c>Prowl.Editor</c>'s ShaderImporter (user project shaders) and the runtime's
/// default-shader loader (<c>BuiltInAssets</c>), since resolving default textures has no editor
/// dependency.
/// </summary>
public static class ShaderPropertyConverter
{
    public static ShaderProperty Convert(ParsedProperty parsed)
    {
        ShaderProperty prop = parsed.PropertyType switch
        {
            ParsedPropertyType.Float => (float)parsed.Value.X,
            ParsedPropertyType.Integer => (int)parsed.Value.X,
            ParsedPropertyType.Color => new Color(parsed.Value.X, parsed.Value.Y, parsed.Value.Z, parsed.Value.W),
            ParsedPropertyType.Vector => parsed.Value,
            ParsedPropertyType.Matrix => parsed.MatrixValue,
            ParsedPropertyType.Texture2D => Texture2DParse(parsed.TextureValue),
            ParsedPropertyType.Texture3D => Texture3DParse(parsed.TextureValue),
            ParsedPropertyType.Texture2DArray => throw new ArgumentException("Texture2DArray does not currently have any loadable defaults"),
            ParsedPropertyType.TextureCubemap => throw new ArgumentException("TextureCubemap does not currently have any loadable defaults"),
            ParsedPropertyType.TextureCubemapArray => throw new ArgumentException("TextureCubemapArray does not currently have any loadable defaults"),
            _ => throw new NotSupportedException($"Format: {parsed.PropertyType} not supported")
        };

        prop.Name = parsed.Name;
        prop.DisplayName = parsed.DisplayName;
        prop.HasRange = false;
        prop.Range = Float2.One;

        return prop;
    }

    private static Texture2D Texture2DParse(string texture)
    {
        return texture switch
        {
            "white" => Texture2D.LoadDefault(DefaultTexture.White),
            "gray" or "grey" => Texture2D.LoadDefault(DefaultTexture.Gray18),
            "grid" => Texture2D.LoadDefault(DefaultTexture.Grid),
            "black" or "emission" => Texture2D.LoadDefault(DefaultTexture.Emission),
            "normal" => Texture2D.LoadDefault(DefaultTexture.Normal),
            "surface" => Texture2D.LoadDefault(DefaultTexture.Surface),
            "noise" => Texture2D.LoadDefault(DefaultTexture.Noise),
            _ => throw new ArgumentException($"Unknown Texture2D default: {texture}")
        };
    }

    private static Texture3D Texture3DParse(string texture)
    {
        return texture switch
        {
            "white" => Texture3D.White,
            _ => throw new ArgumentException($"Unknown Texture3D default: {texture}")
        };
    }
}
