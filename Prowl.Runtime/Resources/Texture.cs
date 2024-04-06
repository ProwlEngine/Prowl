using Silk.NET.OpenGL;
using System;

namespace Prowl.Runtime
{
    /// <summary>
    /// Encapsulates an OpenGL texture object. This is the base class for all
    /// texture types and manages some of their internal workings.
    /// </summary>
    public abstract class Texture : EngineObject
    {
        public enum TextureType
        {
            Texture1D = 3552,
            Texture2D = 3553,
            Texture3D = 32879,
            TextureCubeMap = 34067,
            Texture1DArray = 35864,
            Texture2DArray = 35866,
            TextureCubeMapArray = 36873,
            Texture2DMultisample = 37120,
            Texture2DMultisampleArray = 37122,
        }

        public enum TextureImageFormat
        {
            // These are organized in such away so the base type (float, int, uint)
            // is distinguisable by dividing by 32 and the remainder indicates the amount of components
            // (amount of components: Color4b has 4, Vector2 has 2, Vector3i has 3, etc)
            // This is done in TrippyUtils.GetTextureFormatEnums()

            Color4b = 5,

            Float = 33,
            Float2 = 34,
            Float3 = 35,
            Float4 = 36,
            Depth16 = 37,
            Depth24 = 38,
            Depth32f = 39,

            Int = 65,
            Int2 = 66,
            Int3 = 67,
            Int4 = 68,

            UnsignedInt = 97,
            UnsignedInt2 = 98,
            UnsignedInt3 = 99,
            UnsignedInt4 = 100,

            Depth24Stencil8 = 129,
        }

        private protected const TextureMinFilter DefaultMinFilter = TextureMinFilter.Nearest, DefaultMipmapMinFilter = TextureMinFilter.NearestMipmapNearest;
        private protected const TextureMagFilter DefaultMagFilter = TextureMagFilter.Nearest;

        /// <summary>The handle for the GL Texture Object.</summary>
        public readonly uint Handle;

        /// <summary>The type of this <see cref="Texture"/>, such as 1D, 2D, Multisampled 2D, Array 2D, CubeMap, etc.</summary>
        public readonly TextureType Type;

        public TextureMinFilter MinFilter { get; protected set; }
        public TextureMagFilter MagFilter { get; protected set; }
        public TextureWrapMode WrapMode { get; protected set; }

        /// <summary>The internal format of the pixels, such as RGBA, RGB, R32f, or even different depth/stencil formats.</summary>
        public readonly InternalFormat PixelInternalFormat;

        /// <summary>The data type of the components of the <see cref="Texture"/>'s pixels.</summary>
        public readonly PixelType PixelType;

        /// <summary>The format of the pixel data.</summary>
        public readonly PixelFormat PixelFormat;

        /// <summary>The format for this <see cref="Texture"/>'s image.</summary>
        public readonly TextureImageFormat ImageFormat;

        /// <summary>Gets whether this <see cref="Texture"/> is mipmapped.</summary>
        public bool IsMipmapped { get; private set; }

        /// <summary>False if this <see cref="Texture"/> can be mipmapped (depends on texture type).</summary>
        private readonly bool isNotMipmappable;

        /// <summary>Gets whether this <see cref="Texture"/> can be mipmapped (depends on texture type).</summary>
        public bool IsMipmappable => !isNotMipmappable;

        public TextureUnit LastAssignedUnit { get; internal set; }

        /// <summary>
        /// Creates a <see cref="Texture"/> with specified <see cref="TextureType"/> and <see cref="TextureImageFormat"/>.
        /// </summary>
        /// <param name="type">The type of texture (or texture target) the texture will be.</param>
        /// <param name="imageFormat">The type of image format this texture will store.</param>
        internal Texture(TextureType type, TextureImageFormat imageFormat) : base("New Texture")
        {
            if (!Enum.IsDefined(typeof(TextureType), type))
                throw new FormatException("Invalid texture target");

            if (!Enum.IsDefined(typeof(TextureImageFormat), imageFormat))
                throw new FormatException("Invalid texture image format");

            Type = type;
            ImageFormat = imageFormat;
            GetTextureFormatEnums(imageFormat, out PixelInternalFormat, out PixelType, out PixelFormat);
            IsMipmapped = false;
            isNotMipmappable = !IsTextureTypeMipmappable(type);
            Handle = Graphics.Device.GenTexture();
            Graphics.Device.BindTexture((TextureTarget)Type, Handle);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureMinFilter, (int)DefaultMinFilter);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureMagFilter, (int)DefaultMagFilter);
            MinFilter = DefaultMinFilter;
            MagFilter = DefaultMagFilter;
            WrapMode = TextureWrapMode.Repeat;
        }

        /// <summary>
        /// Sets this <see cref="Texture"/>'s minifying and magnifying filters.
        /// </summary>
        /// <param name="minFilter">The desired minifying filter for the <see cref="Texture"/>.</param>
        /// <param name="magFilter">The desired magnifying filter for the <see cref="Texture"/>.</param>
        public void SetTextureFilters(TextureMinFilter minFilter, TextureMagFilter magFilter)
        {
            Graphics.Device.BindTexture((TextureTarget)Type, Handle);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureMinFilter, (int)minFilter);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureMagFilter, (int)magFilter);
            MinFilter = minFilter;
            MagFilter = magFilter;
        }

        /// <summary>
        /// Generates mipmaps for this <see cref="Texture"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        public void GenerateMipmaps()
        {
            if (isNotMipmappable)
                throw new InvalidOperationException(string.Concat("This texture type is not mipmappable! Type: ", Type.ToString()));

            Graphics.Device.BindTexture((TextureTarget)Type, Handle);
            Graphics.Device.GenerateMipmap((TextureTarget)Type);
            IsMipmapped = true;
        }

        public void Dispose()
        {
            Graphics.Device.DeleteTexture(Handle);
        }

        /// <summary>
        /// Turns a value from the <see cref="TextureImageFormat"/> enum into the necessary
        /// enums to create a <see cref="Texture"/>'s image/storage.
        /// </summary>
        /// <param name="imageFormat">The requested image format.</param>
        /// <param name="pixelInternalFormat">The pixel's internal format.</param>
        /// <param name="pixelType">The pixel's type.</param>
        /// <param name="pixelFormat">The pixel's format.</param>
        public static void GetTextureFormatEnums(TextureImageFormat imageFormat, out InternalFormat pixelInternalFormat, out PixelType pixelType, out PixelFormat pixelFormat)
        {
            // The workings of this function are related to the numbers assigned to each enum value
            int b = (int)imageFormat / 32;

            switch (b)
            {
                case 0:
                    #region UnsignedByteTypes
                    pixelType = PixelType.UnsignedByte;
                    switch ((int)imageFormat - b * 32)
                    {
                        case 5:
                            pixelInternalFormat = InternalFormat.Rgba8;
                            pixelFormat = PixelFormat.Rgba;
                            return;
                    }
                    break;
                #endregion
                case 1:
                    #region FloatTypes
                    pixelType = PixelType.Float;
                    switch ((int)imageFormat - b * 32)
                    {
                        case 1:
                            pixelInternalFormat = InternalFormat.R32f;
                            pixelFormat = PixelFormat.Red;
                            return;
                        case 2:
                            pixelInternalFormat = InternalFormat.RG32f;
                            pixelFormat = PixelFormat.RG;
                            return;
                        case 3:
                            pixelInternalFormat = InternalFormat.Rgb32f;
                            pixelFormat = PixelFormat.Rgb;
                            return;
                        case 4:
                            pixelInternalFormat = InternalFormat.Rgba32f;
                            pixelFormat = PixelFormat.Rgba;
                            return;
                        case 5:
                            pixelInternalFormat = InternalFormat.DepthComponent16;
                            pixelFormat = PixelFormat.DepthComponent;
                            return;
                        case 6:
                            pixelInternalFormat = InternalFormat.DepthComponent24Arb;
                            pixelFormat = PixelFormat.DepthComponent;
                            return;
                        case 7:
                            pixelInternalFormat = InternalFormat.DepthComponent32f;
                            pixelFormat = PixelFormat.DepthComponent;
                            return;
                    }
                    break;
                #endregion
                case 2:
                    #region IntTypes
                    pixelType = PixelType.Int;
                    switch ((int)imageFormat - b * 32)
                    {
                        case 1:
                            pixelInternalFormat = InternalFormat.R32i;
                            pixelFormat = PixelFormat.RgbaInteger;
                            return;
                        case 2:
                            pixelInternalFormat = InternalFormat.RG32i;
                            pixelFormat = PixelFormat.RgbaInteger;
                            return;
                        case 3:
                            pixelInternalFormat = InternalFormat.Rgb32i;
                            pixelFormat = PixelFormat.RgbaInteger;
                            return;
                        case 4:
                            pixelInternalFormat = InternalFormat.Rgba32i;
                            pixelFormat = PixelFormat.RgbaInteger;
                            return;
                    }
                    break;
                #endregion
                case 3:
                    #region UnsignedIntTypes
                    pixelType = PixelType.UnsignedInt;
                    switch ((int)imageFormat - b * 32)
                    {
                        case 1:
                            pixelInternalFormat = InternalFormat.R32ui;
                            pixelFormat = PixelFormat.RedInteger;
                            return;
                        case 2:
                            pixelInternalFormat = InternalFormat.RG32ui;
                            pixelFormat = PixelFormat.RGInteger;
                            return;
                        case 3:
                            pixelInternalFormat = InternalFormat.Rgb32ui;
                            pixelFormat = PixelFormat.RgbInteger;
                            return;
                        case 4:
                            pixelInternalFormat = InternalFormat.Rgba32ui;
                            pixelFormat = PixelFormat.RgbaInteger;
                            return;
                    }
                    break;
                #endregion
                case 4:
                    #region Depth24Stencil8
                    switch ((int)imageFormat - b * 32)
                    {
                        case 1:
                            pixelType = (PixelType)GLEnum.UnsignedInt248;
                            pixelInternalFormat = InternalFormat.Depth24Stencil8;
                            pixelFormat = PixelFormat.DepthStencil;
                            return;
                    }
                    break;
                    #endregion
            }

            throw new ArgumentException("Image format is not a valid TextureImageFormat value", nameof(imageFormat));
        }

        /// <summary>
        /// Gets whether the specified <see cref="TextureType"/> type is mipmappable.
        /// </summary>
        public static bool IsTextureTypeMipmappable(TextureType textureType)
        {
            return textureType == TextureType.Texture1D || textureType == TextureType.Texture2D || textureType == TextureType.Texture3D
                || textureType == TextureType.Texture1DArray || textureType == TextureType.Texture2DArray
                || textureType == TextureType.TextureCubeMap || textureType == TextureType.TextureCubeMapArray;
        }
    }
}
