using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Primitives;
using System;

namespace Prowl.Runtime
{
    /// <summary>
    /// This is the base class for all texture types and manages some of their internal workings.
    /// </summary>
    public abstract class Texture : EngineObject
    {

        private protected const TextureMin DefaultMinFilter = TextureMin.Nearest, DefaultMipmapMinFilter = TextureMin.NearestMipmapNearest;
        private protected const TextureMag DefaultMagFilter = TextureMag.Nearest;

        /// <summary>The handle for the GL Texture Object.</summary>
        public readonly GraphicsTexture Handle;

        /// <summary>The type of this <see cref="Texture"/>, such as 1D, 2D, Multisampled 2D, Array 2D, CubeMap, etc.</summary>
        public readonly TextureType Type;

        public TextureMin MinFilter { get; protected set; }
        public TextureMag MagFilter { get; protected set; }
        public TextureWrap WrapMode { get; protected set; }

        /// <summary>The format for this <see cref="Texture"/>'s image.</summary>
        public readonly TextureImageFormat ImageFormat;

        /// <summary>Gets whether this <see cref="Texture"/> is mipmapped.</summary>
        public bool IsMipmapped { get; private set; }

        /// <summary>False if this <see cref="Texture"/> can be mipmapped (depends on texture type).</summary>
        private readonly bool isNotMipmappable;

        /// <summary>Gets whether this <see cref="Texture"/> can be mipmapped (depends on texture type).</summary>
        public bool IsMipmappable => !isNotMipmappable;

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
            IsMipmapped = false;
            isNotMipmappable = !IsTextureTypeMipmappable(type);
            Handle = Graphics.Device.CreateTexture(type, imageFormat);
            Graphics.Device.SetWrapS(Handle, TextureWrap.Repeat);
            Graphics.Device.SetWrapT(Handle, TextureWrap.Repeat);
            Graphics.Device.SetTextureFilters(Handle, DefaultMinFilter, DefaultMagFilter);
            MinFilter = DefaultMinFilter;
            MagFilter = DefaultMagFilter;
            WrapMode = TextureWrap.Repeat;
        }

        /// <summary>
        /// Sets this <see cref="Texture"/>'s minifying and magnifying filters.
        /// </summary>
        /// <param name="minFilter">The desired minifying filter for the <see cref="Texture"/>.</param>
        /// <param name="magFilter">The desired magnifying filter for the <see cref="Texture"/>.</param>
        public void SetTextureFilters(TextureMin minFilter, TextureMag magFilter)
        {
            Graphics.Device.SetTextureFilters(Handle, minFilter, magFilter);
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

            Graphics.Device.GenerateMipmap(Handle);
            IsMipmapped = true;
        }

        public void Dispose()
        {
            Handle.Dispose();
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
