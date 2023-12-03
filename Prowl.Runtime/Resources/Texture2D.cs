using Prowl.Runtime.Utils;
using ImageMagick;
using Raylib_cs;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Prowl.Runtime.Serialization;

namespace Prowl.Runtime.Resources
{

    // TODO: Raylib.ImageFormat support changing formats
    // TODO: SetPixel/s, GetPixel/s
    // TODO: Default White, Block & Transparent textures all 1x1 pixels

    public sealed class Texture2D : EngineObject, ISerializable
    {
        internal Raylib_cs.Texture2D InternalTexture;

        public int Width => InternalTexture.width;
        public int Height => InternalTexture.height;
        public uint Handle => InternalTexture.id;
        public int MipMaps => InternalTexture.mipmaps;
        public PixelFormat Format => InternalTexture.format;
        public TextureWrap Wrap { get; private set; }
        public TextureFilter Filter { get; private set; }

        /// <summary>
        /// This constructor is used for Serializer and is not intended for use.
        /// </summary>
        public Texture2D() : base("Texture2D")
        {

        }

        internal Texture2D(Raylib_cs.Texture2D texture) : base("Texture2D")
        {
            InternalTexture = texture;
        }

        public Texture2D(string path) : base("Texture2D")
        {
            using var magic = new MagickImage(path);
            magic.Format = MagickFormat.Png;
            magic.Flip();
            var image = Raylib.LoadImageFromMemory(".png", magic.ToByteArray());
            InternalTexture = Raylib.LoadTextureFromImage(image);
            if (Handle == 0) throw new Exception($"Failed to load texture from path: {path}");
            Raylib.UnloadImage(image);
        }

        public Texture2D(Stream stream) : base("Texture2D")
        {
            using var magic = new MagickImage(stream);
            magic.Format = MagickFormat.Png;
            magic.Flip();
            var image = Raylib.LoadImageFromMemory(".png", magic.ToByteArray());
            InternalTexture = Raylib.LoadTextureFromImage(image);
            if (Handle == 0) throw new Exception($"Failed to load texture from stream");
            Raylib.UnloadImage(image);
        }

        public Texture2D(Image image) : base("Texture2D")
        {
            Raylib.ImageFlipVertical(ref image);
            InternalTexture = Raylib.LoadTextureFromImage(image);
            if (Handle == 0) throw new Exception($"Failed to load texture from image");
        }

        public Texture2D(MagickImage image) : base("Texture2D")
        {
            if (image.Format != MagickFormat.Png) throw new Exception($"Can only load PNG Magick Formats");
            var img = Raylib.LoadImageFromMemory(".png", image.ToByteArray());
            InternalTexture = Raylib.LoadTextureFromImage(img);
            if (Handle == 0) throw new Exception($"Failed to load texture from image");
            Raylib.UnloadImage(img);
        }

        public Texture2D(int width, int height, Color color) : base("Texture2D")
        {
            Image image = Raylib.GenImageColor(width, height, color);
            InternalTexture = Raylib.LoadTextureFromImage(image);
            if (Handle == 0) throw new Exception($"Failed to load texture from image");
            Raylib.UnloadImage(image);
        }

        public static Texture2D GenerateCellular(int width, int height, int tileSize)
        {
            Image image = Raylib.GenImageCellular(width, height, tileSize);
            var tex = new Texture2D(image);
            Raylib.UnloadImage(image);
            return tex;
        }

        public static Texture2D GenerateChecked(int width, int height, int checksX, int checksY, Color col1, Color col2)
        {
            Image image = Raylib.GenImageChecked(width, height, checksX, checksY, col1, col2);
            var tex = new Texture2D(image);
            Raylib.UnloadImage(image);
            return tex;
        }

        public unsafe static Texture2D GenerateGradientH(int width, int height, Color left, Color right)
        {
            Image image = Raylib.GenImageGradientH(width, height, left, right);
            var tex = new Texture2D(image);
            Raylib.UnloadImage(image);
            return tex;
        }

        public unsafe static Texture2D GenerateGradientV(int width, int height, Color top, Color bottom)
        {
            Image image = Raylib.GenImageGradientV(width, height, top, bottom);
            var tex = new Texture2D(image);
            Raylib.UnloadImage(image);
            return tex;
        }

        public unsafe static Texture2D GenerateGradientRadial(int width, int height, float density, Color inner, Color outer)
        {
            Image image = Raylib.GenImageGradientRadial(width, height, density, inner, outer);
            var tex = new Texture2D(image);
            Raylib.UnloadImage(image);
            return tex;
        }

        public unsafe static Texture2D GenerateWhiteNoise(int width, int height, float factor)
        {
            Image image = Raylib.GenImageWhiteNoise(width, height, factor);
            var tex = new Texture2D(image);
            Raylib.UnloadImage(image);
            return tex;
        }

        public unsafe static Texture2D GeneratePerlinNoise(int width, int height, int offsetX, int offsetY, float scale)
        {
            Image image = Raylib.GenImagePerlinNoise(width, height, offsetX, offsetY, scale);
            var tex = new Texture2D(image);
            Raylib.UnloadImage(image);
            return tex;
        }

        public override void OnDispose()
        {
            Raylib.UnloadTexture(InternalTexture);
        }

        public bool IsReady => Raylib.IsTextureReady(InternalTexture);

        public void Draw(int X, int Y, Color tint) => Raylib.DrawTexture(InternalTexture, X, Y, tint);
        public void DrawV(Vector2 Position, Color tint) => Raylib.DrawTextureV(InternalTexture, Position, tint);
        public void DrawEx(Vector2 Position, float Rotation, float Scale, Color Tint) => Raylib.DrawTextureEx(InternalTexture, Position, Rotation, Scale, Tint);
        public void DrawRec(Rectangle source, Vector2 position, Color tint) => Raylib.DrawTextureRec(InternalTexture, source, position, tint);
        public void DrawPro(Rectangle source, Rectangle dest, Vector2 origin, float rotation, Color tint) => Raylib.DrawTexturePro(InternalTexture, source, dest, origin, rotation, tint);
        public void DrawNPatch(NPatchInfo nPatchInfo, Rectangle dest, Vector2 origin, float rotation, Color tint) => Raylib.DrawTextureNPatch(InternalTexture, nPatchInfo, dest, origin, rotation, tint);

        public void SetShapes(Rectangle source) => Raylib.SetShapesTexture(InternalTexture, source);
        public unsafe void Update(void* pixels) => Raylib.UpdateTexture(InternalTexture, pixels);
        public unsafe void Update(Rectangle rec, void* pixels) => Raylib.UpdateTextureRec(InternalTexture, rec, pixels);

        public void SetFilter(TextureFilter filter)
        {
            Filter = filter;
            Raylib.SetTextureFilter(InternalTexture, filter);
        }
        public void SetWrap(TextureWrap wrap)
        {
            Wrap = wrap;
            Raylib.SetTextureWrap(InternalTexture, wrap);
        }
        public void GenerateMipMaps() => Raylib.GenTextureMipmaps(ref InternalTexture);

        /// <summary>sCall only if this texture is Generated From Code, If its Content or Asset based then only call this if you know what you are doing </summary>
        public void Unload() => Raylib.UnloadTexture(InternalTexture);

        public CompoundTag Serialize(string tagName, TagSerializer.SerializationContext ctx)
        {
            CompoundTag compoundTag = new CompoundTag(tagName);
            compoundTag.Add(new IntTag("Width", Width));
            compoundTag.Add(new IntTag("Height", Height));
            compoundTag.Add(new IntTag("MipMaps", MipMaps));
            compoundTag.Add(new ByteTag("Format", (byte)Format));
            compoundTag.Add(new ByteTag("Filter", (byte)Filter));
            compoundTag.Add(new ByteTag("Wrap", (byte)Wrap));
            Image image = Raylib.LoadImageFromTexture(InternalTexture);
            unsafe
            {
                int size = Raylib.GetPixelDataSize(Width, Height, Format);
                byte[] byteArray = new byte[size];
                Marshal.Copy((IntPtr)image.data, byteArray, 0, byteArray.Length);
                compoundTag.Add(new ByteArrayTag("Data", byteArray));
            }
            return compoundTag;
        }

        public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
        {
            unsafe
            {
                byte[] data = value.Get<ByteArrayTag>("Data").Value;
                fixed (void* ptr = data)
                {
                    int mipMaps = value["MipMaps"].IntValue;
                    Image image = new()
                    {
                        width = value["Width"].IntValue,
                        height = value["Height"].IntValue,
                        mipmaps = 1,
                        format = (PixelFormat)value["Format"].ByteValue,
                        data = ptr
                    };
                    InternalTexture = Raylib.LoadTextureFromImage(image);

                    if (mipMaps > 1)
                        Raylib.GenTextureMipmaps(ref InternalTexture);

                    Filter = (TextureFilter)value["Filter"].ByteValue;
                    Raylib.SetTextureFilter(InternalTexture, Filter);
                    Wrap = (TextureWrap)value["Wrap"].ByteValue;
                    Raylib.SetTextureWrap(InternalTexture, Wrap);
                }
            }
        }
    }
}
