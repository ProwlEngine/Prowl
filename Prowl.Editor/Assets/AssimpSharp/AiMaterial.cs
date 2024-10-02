using System.Numerics;

namespace AssimpSharp
{
    public class AiTexture
    {
        public int Width { get; set; } = 0;
        public int Height { get; set; } = 0;
        public string FormatHint { get; set; } = "";
        public byte[] Data { get; set; } = new byte[0];

        public enum Op
        {
            Multiply = 0x0,
            Add = 0x1,
            Subtract = 0x2,
            Divide = 0x3,
            SmoothAdd = 0x4,
            SignedAdd = 0x5
        }

        public enum MapMode
        {
            Wrap = 0x0,
            Clamp = 0x1,
            Decal = 0x3,
            Mirror = 0x2
        }

        public enum Mapping
        {
            UV = 0x0,
            Sphere = 0x1,
            Cylinder = 0x2,
            Box = 0x3,
            Plane = 0x4,
            Other = 0x5
        }

        public enum Type
        {
            None = 0x0,
            Diffuse = 0x1,
            Specular = 0x2,
            Ambient = 0x3,
            Emissive = 0x4,
            Height = 0x5,
            Normals = 0x6,
            Shininess = 0x7,
            Opacity = 0x8,
            Displacement = 0x9,
            Lightmap = 0xA,
            Reflection = 0xB,
            Unknown = 0xC
        }

        [Flags]
        public enum Flags
        {
            Invert = 0x1,
            UseAlpha = 0x2,
            IgnoreAlpha = 0x4
        }
    }

    public enum AiShadingMode
    {
        Flat = 0x1,
        Gouraud = 0x2,
        Phong = 0x3,
        Blinn = 0x4,
        Toon = 0x5,
        OrenNayar = 0x6,
        Minnaert = 0x7,
        CookTorrance = 0x8,
        NoShading = 0x9,
        Fresnel = 0xa
    }

    public enum AiBlendMode
    {
        Default = 0x0,
        Additive = 0x1
    }

    public struct AiUVTransform
    {
        public Vector2 Translation { get; set; }
        public Vector2 Scaling { get; set; }
        public float Rotation { get; set; }
    }

    public class AiMaterial
    {
        public bool HasName => Name != null;
        public string? Name { get; set; }
        public bool HasTwoSided => IsTwoSided != null;
        public bool? IsTwoSided { get; set; }
        public bool HasShadingModel => ShadingModel != null;
        public AiShadingMode? ShadingModel { get; set; }
        public bool HasWireframe => IsWireframeEnabled != null;
        public bool? IsWireframeEnabled { get; set; }
        public bool HasBlendMode => Blendmode != null;
        public AiBlendMode? Blendmode { get; set; }
        public bool HasOpacity => Opacity != null;
        public float? Opacity { get; set; }
        public bool HasBumpScaling => BumpScaling != null;
        public float? BumpScaling { get; set; }
        public bool HasShininess => Shininess != null;
        public float? Shininess { get; set; }
        public bool HasShininessStrength => ShininessStrength != null;
        public float? ShininessStrength { get; set; }
        public bool HasReflectivity => Reflectivity != null;
        public float? Reflectivity { get; set; }
        public bool HasRefracti => Refracti != null;
        public float? Refracti { get; set; }
        public bool HasTransparent => Transparent != null;
        public Vector3? Transparent { get; set; }
        public bool HasAlpha => Alpha != null;
        public float? Alpha { get; set; }


        public bool HasColorDiffuse => Color.Diffuse != null;
        public Vector4? ColorDiffuse => Color.Diffuse;
        public bool HasColorAmbient => Color.Ambient != null;
        public Vector4? ColorAmbient => Color.Ambient;
        public bool HasColorSpecular => Color.Specular != null;
        public Vector4? ColorSpecular => Color.Specular;
        public bool HasColorEmissive => Color.Emissive != null;
        public Vector4? ColorEmissive => Color.Emissive;
        public bool HasColorTransparent => Color.Transparent != null;
        public Vector4? ColorTransparent => Color.Transparent;
        public bool HasColorReflective => Color.Reflective != null;
        public Vector4? ColorReflective => Color.Reflective;
        public MatColor Color { get; set; }


        public bool HasTextures => Textures.Count > 0;
        public bool HasTextureDiffuse => Textures.Any(t => t.Type == AiTexture.Type.Diffuse);
        public MatTexture? TextureDiffuse => Textures.FirstOrDefault(t => t.Type == AiTexture.Type.Diffuse);
        public bool HasTextureSpecular => Textures.Any(t => t.Type == AiTexture.Type.Specular);
        public MatTexture? TextureSpecular => Textures.FirstOrDefault(t => t.Type == AiTexture.Type.Specular);
        public bool HasTextureAmbient => Textures.Any(t => t.Type == AiTexture.Type.Ambient);
        public MatTexture? TextureAmbient => Textures.FirstOrDefault(t => t.Type == AiTexture.Type.Ambient);
        public bool HasTextureEmissive => Textures.Any(t => t.Type == AiTexture.Type.Emissive);
        public MatTexture? TextureEmissive => Textures.FirstOrDefault(t => t.Type == AiTexture.Type.Emissive);
        public bool HasTextureHeight => Textures.Any(t => t.Type == AiTexture.Type.Height);
        public MatTexture? TextureHeight => Textures.FirstOrDefault(t => t.Type == AiTexture.Type.Height);
        public bool HasTextureNormal => Textures.Any(t => t.Type == AiTexture.Type.Normals);
        public MatTexture? TextureNormal => Textures.FirstOrDefault(t => t.Type == AiTexture.Type.Normals);
        public bool HasTextureShininess => Textures.Any(t => t.Type == AiTexture.Type.Shininess);
        public MatTexture? TextureShininess => Textures.FirstOrDefault(t => t.Type == AiTexture.Type.Shininess);
        public bool HasTextureOpacity => Textures.Any(t => t.Type == AiTexture.Type.Opacity);
        public MatTexture? TextureOpacity => Textures.FirstOrDefault(t => t.Type == AiTexture.Type.Opacity);
        public bool HasTextureDisplacement => Textures.Any(t => t.Type == AiTexture.Type.Displacement);
        public MatTexture? TextureDisplacement => Textures.FirstOrDefault(t => t.Type == AiTexture.Type.Displacement);
        public bool HasTextureLightmap => Textures.Any(t => t.Type == AiTexture.Type.Lightmap);
        public MatTexture? TextureLightmap => Textures.FirstOrDefault(t => t.Type == AiTexture.Type.Lightmap);
        public bool HasTextureReflection => Textures.Any(t => t.Type == AiTexture.Type.Reflection);
        public MatTexture? TextureReflection => Textures.FirstOrDefault(t => t.Type == AiTexture.Type.Reflection);
        public bool HasTextureUnknown => Textures.Any(t => t.Type == AiTexture.Type.Unknown);
        public MatTexture? TextureUnknown => Textures.FirstOrDefault(t => t.Type == AiTexture.Type.Unknown);
        public List<MatTexture> Textures { get; set; } = new List<MatTexture>();

        public struct MatColor
        {
            public Vector4? Diffuse { get; set; }
            public Vector4? Ambient { get; set; }
            public Vector4? Specular { get; set; }
            public Vector4? Emissive { get; set; }
            public Vector4? Transparent { get; set; }
            public Vector4? Reflective { get; set; }
        }

        public class MatTexture
        {
            public AiTexture.Type? Type { get; set; }
            public string FilePath { get; set; }
            public float? Blend { get; set; }
            public AiTexture.Op? Op { get; set; }
            public AiTexture.Mapping? Mapping { get; set; }
            public int? UVWSource { get; set; }
            public AiTexture.MapMode? MapModeU { get; set; }
            public AiTexture.MapMode? MapModeV { get; set; }
            public Vector3? MapAxis { get; set; }
            public int? Flags { get; set; }
            public AiUVTransform? UVTrafo { get; set; }
        }
    }
}
