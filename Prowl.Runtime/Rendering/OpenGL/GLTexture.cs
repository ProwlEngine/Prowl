using Prowl.Runtime.Rendering.Primitives;
using Silk.NET.OpenGL;
using System;

namespace Prowl.Runtime.Rendering.OpenGL
{
    public sealed unsafe class GLTexture : GraphicsTexture
    {
        public uint Handle { get; private set; }
        public override TextureType Type { get; protected set; }

        public readonly TextureTarget Target;

        /// <summary>The internal format of the pixels, such as RGBA, RGB, R32f, or even different depth/stencil formats.</summary>
        public readonly InternalFormat PixelInternalFormat;

        /// <summary>The data type of the components of the <see cref="Texture"/>'s pixels.</summary>
        public readonly PixelType PixelType;

        /// <summary>The format of the pixel data.</summary>
        public readonly PixelFormat PixelFormat;

        public GLTexture(TextureType type, TextureImageFormat format)
        {
            Handle = GLDevice.GL.GenTexture();
            Type = type;
            Target = type switch {
                TextureType.Texture1D => TextureTarget.Texture1D,
                TextureType.Texture2D => TextureTarget.Texture2D,
                TextureType.Texture3D => TextureTarget.Texture3D,
                TextureType.TextureCubeMap => TextureTarget.TextureCubeMap,
                TextureType.Texture2DArray => TextureTarget.Texture2DArray,
                TextureType.Texture2DMultisample => TextureTarget.Texture2DMultisample,
                TextureType.Texture2DMultisampleArray => TextureTarget.Texture2DMultisampleArray,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
            GetTextureFormatEnums(format, out PixelInternalFormat, out PixelType, out PixelFormat);
        }
        
        private static uint? currentlyBound = null;
        public void Bind(bool force = true)
        {
            if (!force && currentlyBound == Handle)
                return;

            GLDevice.GL.BindTexture(Target, Handle);
            currentlyBound = Handle;
        }

        public void GenerateMipmap()
        {
            Bind(false);
            GLDevice.GL.GenerateMipmap(Target);
        }

        public void SetWrapS(TextureWrap wrap)
        {
            Bind(false);
            var wrapMode = wrap switch {
                TextureWrap.Repeat => GLEnum.Repeat,
                TextureWrap.ClampToEdge => GLEnum.ClampToEdge,
                TextureWrap.MirroredRepeat => GLEnum.MirroredRepeat,
                TextureWrap.ClampToBorder => GLEnum.ClampToBorder,
                _ => throw new ArgumentException("Invalid texture wrap mode", nameof(wrap)),
            };
            GLDevice.GL.TexParameter(Target, GLEnum.TextureWrapS, (int)wrapMode);
        }

        public void SetWrapT(TextureWrap wrap)
        {
            Bind(false);
            var wrapMode = wrap switch {
                TextureWrap.Repeat => GLEnum.Repeat,
                TextureWrap.ClampToEdge => GLEnum.ClampToEdge,
                TextureWrap.MirroredRepeat => GLEnum.MirroredRepeat,
                TextureWrap.ClampToBorder => GLEnum.ClampToBorder,
                _ => throw new ArgumentException("Invalid texture wrap mode", nameof(wrap)),
            };
            GLDevice.GL.TexParameter(Target, GLEnum.TextureWrapT, (int)wrapMode);
        }

        public void SetWrapR(TextureWrap wrap)
        {
            Bind(false);
            var wrapMode = wrap switch {
                TextureWrap.Repeat => GLEnum.Repeat,
                TextureWrap.ClampToEdge => GLEnum.ClampToEdge,
                TextureWrap.MirroredRepeat => GLEnum.MirroredRepeat,
                TextureWrap.ClampToBorder => GLEnum.ClampToBorder,
                _ => throw new ArgumentException("Invalid texture wrap mode", nameof(wrap)),
            };
            GLDevice.GL.TexParameter(Target, GLEnum.TextureWrapR, (int)wrapMode);
        }

        public void SetTextureFilters(TextureMin min, TextureMag mag)
        {
            Bind(false);
            var minFilter = min switch {
                TextureMin.Nearest => GLEnum.Nearest,
                TextureMin.Linear => GLEnum.Linear,
                TextureMin.NearestMipmapNearest => GLEnum.NearestMipmapNearest,
                TextureMin.LinearMipmapNearest => GLEnum.LinearMipmapNearest,
                TextureMin.NearestMipmapLinear => GLEnum.NearestMipmapLinear,
                TextureMin.LinearMipmapLinear => GLEnum.LinearMipmapLinear,
                _ => throw new ArgumentException("Invalid texture min filter", nameof(min)),
            };
            var magFilter = mag switch {
                TextureMag.Nearest => GLEnum.Nearest,
                TextureMag.Linear => GLEnum.Linear,
                _ => throw new ArgumentException("Invalid texture mag filter", nameof(mag)),
            };
            GLDevice.GL.TexParameter(Target, GLEnum.TextureMinFilter, (int)minFilter);
            GLDevice.GL.TexParameter(Target, GLEnum.TextureMagFilter, (int)magFilter);
        }

        public void GetTexImage(int level, void* ptr)
        {
            Bind(false);
            GLDevice.GL.GetTexImage(Target, level, PixelFormat, PixelType, ptr);
        }

        public override bool IsDisposed { get; protected set; }

        public override void Dispose()
        {
            if (IsDisposed)
                return;

            if(currentlyBound == Handle)
                currentlyBound = null;

            GLDevice.GL.DeleteTexture(Handle);
            IsDisposed = true;
        }

        public override string ToString()
        {
            return Handle.ToString();
        }

        public void TexImage2D(TextureTarget type, int mip, uint width, uint height, int v2, void* data)
        {
            Bind(false);
            GLDevice.GL.TexImage2D(type, mip, PixelInternalFormat, width, height, v2, PixelFormat, PixelType, data);
        }

        public void TexImage3D(TextureTarget type, int mip, uint width, uint height, uint depth, int v2, void* data)
        {
            Bind(false);
            GLDevice.GL.TexImage3D(type, mip, PixelInternalFormat, width, height, depth, v2, PixelFormat, PixelType, data);
        }

        internal void TexSubImage2D(TextureTarget type, int mip, int x, int y, uint width, uint height, void* data)
        {
            Bind(false);
            GLDevice.GL.TexSubImage2D(type, mip, x, y, width, height, PixelFormat, PixelType, data);
        }

        internal void TexSubImage3D(TextureTarget type, int mip, int x, int y, int z, uint width, uint height, uint depth, void* data)
        {
            Bind(false);
            GLDevice.GL.TexSubImage3D(type, mip, x, y, z, width, height, depth, PixelFormat, PixelType, data);
        }

        /// <summary>
        /// Turns a value from the <see cref="Texture.TextureImageFormat"/> enum into the necessary
        /// enums to create a <see cref="Texture"/>'s image/storage.
        /// </summary>
        /// <param name="imageFormat">The requested image format.</param>
        /// <param name="pixelInternalFormat">The pixel's internal format.</param>
        /// <param name="pixelType">The pixel's type.</param>
        /// <param name="pixelFormat">The pixel's format.</param>
        public static void GetTextureFormatEnums(TextureImageFormat imageFormat, out InternalFormat pixelInternalFormat, out PixelType pixelType, out PixelFormat pixelFormat)
        {

            pixelType = imageFormat switch {
                TextureImageFormat.Color4b => PixelType.UnsignedByte,
                TextureImageFormat.Float => PixelType.Float,
                TextureImageFormat.Float2 => PixelType.Float,
                TextureImageFormat.Float3 => PixelType.Float,
                TextureImageFormat.Float4 => PixelType.Float,
                TextureImageFormat.Short => PixelType.Short,
                TextureImageFormat.Short2 => PixelType.Short,
                TextureImageFormat.Short3 => PixelType.Short,
                TextureImageFormat.Short4 => PixelType.Short,
                TextureImageFormat.Int => PixelType.Int,
                TextureImageFormat.Int2 => PixelType.Int,
                TextureImageFormat.Int3 => PixelType.Int,
                TextureImageFormat.Int4 => PixelType.Int,
                TextureImageFormat.UnsignedShort => PixelType.UnsignedShort,
                TextureImageFormat.UnsignedShort2 => PixelType.UnsignedShort,
                TextureImageFormat.UnsignedShort3 => PixelType.UnsignedShort,
                TextureImageFormat.UnsignedShort4 => PixelType.UnsignedShort,
                TextureImageFormat.UnsignedInt => PixelType.UnsignedInt,
                TextureImageFormat.UnsignedInt2 => PixelType.UnsignedInt,
                TextureImageFormat.UnsignedInt3 => PixelType.UnsignedInt,
                TextureImageFormat.UnsignedInt4 => PixelType.UnsignedInt,
                TextureImageFormat.Depth16 => PixelType.Float,
                TextureImageFormat.Depth24 => PixelType.Float,
                TextureImageFormat.Depth32f => PixelType.Float,
                TextureImageFormat.Depth24Stencil8 => (PixelType)GLEnum.UnsignedInt248,
                _ => throw new ArgumentException("Image format is not a valid TextureImageFormat value", nameof(imageFormat)),
            };

            pixelInternalFormat = imageFormat switch {
                TextureImageFormat.Color4b => InternalFormat.Rgba8,
                TextureImageFormat.Float => InternalFormat.R32f,
                TextureImageFormat.Float2 => InternalFormat.RG32f,
                TextureImageFormat.Float3 => InternalFormat.Rgb32f,
                TextureImageFormat.Float4 => InternalFormat.Rgba32f,
                TextureImageFormat.Short => InternalFormat.R16f,
                TextureImageFormat.Short2 => InternalFormat.RG16f,
                TextureImageFormat.Short3 => InternalFormat.Rgb16f,
                TextureImageFormat.Short4 => InternalFormat.Rgba16f,
                TextureImageFormat.Int => InternalFormat.R32i,
                TextureImageFormat.Int2 => InternalFormat.RG32i,
                TextureImageFormat.Int3 => InternalFormat.Rgb32i,
                TextureImageFormat.Int4 => InternalFormat.Rgba32i,
                TextureImageFormat.UnsignedShort => InternalFormat.R16f,
                TextureImageFormat.UnsignedShort2 => InternalFormat.RG16f,
                TextureImageFormat.UnsignedShort3 => InternalFormat.Rgb16f,
                TextureImageFormat.UnsignedShort4 => InternalFormat.Rgba16f,
                TextureImageFormat.UnsignedInt => InternalFormat.R32ui,
                TextureImageFormat.UnsignedInt2 => InternalFormat.RG32ui,
                TextureImageFormat.UnsignedInt3 => InternalFormat.Rgb32ui,
                TextureImageFormat.UnsignedInt4 => InternalFormat.Rgba32ui,
                TextureImageFormat.Depth16 => InternalFormat.DepthComponent16,
                TextureImageFormat.Depth24 => InternalFormat.DepthComponent24,
                TextureImageFormat.Depth32f => InternalFormat.DepthComponent32f,
                TextureImageFormat.Depth24Stencil8 => InternalFormat.Depth24Stencil8,
                _ => throw new ArgumentException("Image format is not a valid TextureImageFormat value", nameof(imageFormat)),
            };

            pixelFormat = imageFormat switch {
                TextureImageFormat.Color4b => PixelFormat.Rgba,
                TextureImageFormat.Short => PixelFormat.Red,
                TextureImageFormat.Short2 => PixelFormat.RG,
                TextureImageFormat.Short3 => PixelFormat.Rgb,
                TextureImageFormat.Short4 => PixelFormat.Rgba,
                TextureImageFormat.Float => PixelFormat.Red,
                TextureImageFormat.Float2 => PixelFormat.RG,
                TextureImageFormat.Float3 => PixelFormat.Rgb,
                TextureImageFormat.Float4 => PixelFormat.Rgba,
                TextureImageFormat.Int => PixelFormat.RgbaInteger,
                TextureImageFormat.Int2 => PixelFormat.RGInteger,
                TextureImageFormat.Int3 => PixelFormat.RgbInteger,
                TextureImageFormat.Int4 => PixelFormat.RgbaInteger,
                TextureImageFormat.UnsignedShort => PixelFormat.Red,
                TextureImageFormat.UnsignedShort2 => PixelFormat.RG,
                TextureImageFormat.UnsignedShort3 => PixelFormat.Rgb,
                TextureImageFormat.UnsignedShort4 => PixelFormat.Rgba,
                TextureImageFormat.UnsignedInt => PixelFormat.RedInteger,
                TextureImageFormat.UnsignedInt2 => PixelFormat.RGInteger,
                TextureImageFormat.UnsignedInt3 => PixelFormat.RgbInteger,
                TextureImageFormat.UnsignedInt4 => PixelFormat.RgbaInteger,
                TextureImageFormat.Depth16 => PixelFormat.DepthComponent,
                TextureImageFormat.Depth24 => PixelFormat.DepthComponent,
                TextureImageFormat.Depth32f => PixelFormat.DepthComponent,
                TextureImageFormat.Depth24Stencil8 => PixelFormat.DepthStencil,
                _ => throw new ArgumentException("Image format is not a valid TextureImageFormat value", nameof(imageFormat)),
            };
        }

    }
}
