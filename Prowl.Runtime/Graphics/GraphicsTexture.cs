// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;

using Silk.NET.OpenGL;

namespace Prowl.Runtime;

public unsafe class GraphicsTexture : IDisposable
{
    // Handle is 0 until the executor's CreateTexture opcode runs on the render
    // thread and writes the real GL name back.
    public uint Handle { get; internal set; }
    public TextureType Type { get; protected set; }

    public readonly TextureTarget Target;

    /// <summary>The internal format of the pixels, such as RGBA, RGB, R32f, or even different depth/stencil formats.</summary>
    public readonly InternalFormat PixelInternalFormat;

    /// <summary>The data type of the components of the <see cref="Texture"/>'s pixels.</summary>
    public readonly PixelType PixelType;

    /// <summary>The format of the pixel data.</summary>
    public readonly PixelFormat PixelFormat;

    /// <summary>
    /// Whether compute kernels and shaders may write the texture through an image. Such a texture is allocated once with
    /// immutable storage, which ES requires of an image, and keeps its size.
    /// </summary>
    public bool RandomWrite { get; }

    private readonly int _storageLevels;
    private uint _storageWidth, _storageHeight;

    public GraphicsTexture(TextureType type, TextureImageFormat format, bool randomWrite = false, int levels = 1)
    {
        RandomWrite = randomWrite;
        _storageLevels = Math.Max(1, levels);
        Type = type;
        Target = type switch
        {
            TextureType.Texture2D => TextureTarget.Texture2D,
            TextureType.Texture3D => TextureTarget.Texture3D,
            TextureType.TextureCubeMap => TextureTarget.TextureCubeMap,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
        GetTextureFormatEnums(format, out PixelInternalFormat, out PixelType, out PixelFormat);
        _esUploadType = EsUploadType(format, PixelType);
        Handle = 0;

        using var cmd = Graphics.GetCommandBuffer("GraphicsTexture.Create");
        cmd.EncodeCreateTexture(this);
        Graphics.Submit(cmd);
    }

    /// <summary>Binds the texture to the currently-active texture unit. Always
    /// emits the GL call no per-instance cache, because a single "last bound"
    /// flag can't represent per-unit state and would silently skip valid binds.</summary>
    public void Bind(bool force = true)
    {
        Graphics.GL.BindTexture(Target, Handle);
        Graphics.Executor.OnTextureBound(Target, Handle);
    }

    public void GenerateMipmap()
    {
        Bind(false);
        Graphics.GL.GenerateMipmap(Target);
    }

    public void SetWrapS(TextureWrap wrap)
    {
        Bind(false);
        GLEnum wrapMode = wrap switch
        {
            TextureWrap.Repeat => GLEnum.Repeat,
            TextureWrap.ClampToEdge => GLEnum.ClampToEdge,
            TextureWrap.MirroredRepeat => GLEnum.MirroredRepeat,
            TextureWrap.ClampToBorder => GLEnum.ClampToBorder,
            _ => throw new ArgumentException("Invalid texture wrap mode", nameof(wrap)),
        };
        Graphics.GL.TexParameter(Target, GLEnum.TextureWrapS, (int)wrapMode);
    }

    public void SetWrapT(TextureWrap wrap)
    {
        Bind(false);
        GLEnum wrapMode = wrap switch
        {
            TextureWrap.Repeat => GLEnum.Repeat,
            TextureWrap.ClampToEdge => GLEnum.ClampToEdge,
            TextureWrap.MirroredRepeat => GLEnum.MirroredRepeat,
            TextureWrap.ClampToBorder => GLEnum.ClampToBorder,
            _ => throw new ArgumentException("Invalid texture wrap mode", nameof(wrap)),
        };
        Graphics.GL.TexParameter(Target, GLEnum.TextureWrapT, (int)wrapMode);
    }

    public void SetWrapR(TextureWrap wrap)
    {
        Bind(false);
        GLEnum wrapMode = wrap switch
        {
            TextureWrap.Repeat => GLEnum.Repeat,
            TextureWrap.ClampToEdge => GLEnum.ClampToEdge,
            TextureWrap.MirroredRepeat => GLEnum.MirroredRepeat,
            TextureWrap.ClampToBorder => GLEnum.ClampToBorder,
            _ => throw new ArgumentException("Invalid texture wrap mode", nameof(wrap)),
        };
        Graphics.GL.TexParameter(Target, GLEnum.TextureWrapR, (int)wrapMode);
    }

    public void SetTextureFilters(TextureMin min, TextureMag mag)
    {
        Bind(false);
        GLEnum minFilter = min switch
        {
            TextureMin.Nearest => GLEnum.Nearest,
            TextureMin.Linear => GLEnum.Linear,
            TextureMin.NearestMipmapNearest => GLEnum.NearestMipmapNearest,
            TextureMin.LinearMipmapNearest => GLEnum.LinearMipmapNearest,
            TextureMin.NearestMipmapLinear => GLEnum.NearestMipmapLinear,
            TextureMin.LinearMipmapLinear => GLEnum.LinearMipmapLinear,
            _ => throw new ArgumentException("Invalid texture min filter", nameof(min)),
        };
        GLEnum magFilter = mag switch
        {
            TextureMag.Nearest => GLEnum.Nearest,
            TextureMag.Linear => GLEnum.Linear,
            _ => throw new ArgumentException("Invalid texture mag filter", nameof(mag)),
        };
        // 32 bit float textures only filter linearly where the target allows it, otherwise they would sample as black
        if (PixelInternalFormat is InternalFormat.R32f or InternalFormat.RG32f or InternalFormat.Rgb32f or InternalFormat.Rgba32f
            && (minFilter != GLEnum.Nearest || magFilter != GLEnum.Nearest)
            && !Graphics.Capabilities.Require(GraphicsFeature.FloatLinearFiltering, $"Linear filtering of a {PixelInternalFormat} texture"))
        {
            minFilter = GLEnum.Nearest;
            magFilter = GLEnum.Nearest;
        }
        Graphics.GL.TexParameter(Target, GLEnum.TextureMinFilter, (int)minFilter);
        Graphics.GL.TexParameter(Target, GLEnum.TextureMagFilter, (int)magFilter);
    }

    /// <summary>
    /// Enable or disable hardware depth comparison sampling. When enabled, a matching
    /// <c>sampler2DShadow</c> in a shader does the depth test in fixed-function hardware
    /// (with LINEAR filtering this gives free 2x2 PCF). Only valid on depth textures.
    /// </summary>
    public void SetCompareMode(bool enabled)
    {
        Bind(false);
        if (enabled)
        {
            Graphics.GL.TexParameter(Target, GLEnum.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
            Graphics.GL.TexParameter(Target, GLEnum.TextureCompareFunc, (int)GLEnum.Lequal);
        }
        else
        {
            Graphics.GL.TexParameter(Target, GLEnum.TextureCompareMode, (int)GLEnum.None);
        }
    }

    public void GetTexImage(int level, void* ptr)
    {
        Bind(false);
        if (Graphics.Capabilities.Has(GraphicsFeature.TextureReadback))
            Graphics.GL.GetTexImage(Target, level, PixelFormat, PixelType, ptr);
        else
            ReadThroughFramebuffer(Target, level, ptr);
    }

    /// <summary>Read back one cubemap face's mip level. <paramref name="face"/> is 0..5 in
    /// GL order (+X, -X, +Y, -Y, +Z, -Z).</summary>
    public void GetTexImageFace(int face, int level, void* ptr)
    {
        Bind(false);
        TextureTarget faceTarget = TextureTarget.TextureCubeMapPositiveX + face;
        if (Graphics.Capabilities.Has(GraphicsFeature.TextureReadback))
            Graphics.GL.GetTexImage(faceTarget, level, PixelFormat, PixelType, ptr);
        else
            ReadThroughFramebuffer(faceTarget, level, ptr);
    }

    // Immutable storage needs GL 4.2 or ES 3.0, below that a random write texture is an ordinary one
    private bool UsesStorage => RandomWrite && Graphics.Capabilities.Has(GraphicsFeature.ImageLoadStore);

    private void AllocateStorage(uint width, uint height, int mip)
    {
        if (mip != 0) return;
        if (_storageWidth == width && _storageHeight == height) return;
        if (_storageWidth != 0)
        {
            Debug.LogError($"A random write texture keeps the size it was made with ({_storageWidth}x{_storageHeight}), so it was not resized to {width}x{height}. Make a new one instead.");
            return;
        }
        Graphics.GL.TexStorage2D(Target, (uint)_storageLevels, (SizedInternalFormat)PixelInternalFormat, width, height);
        (_storageWidth, _storageHeight) = (width, height);
    }

    // ---------------------------------------------------------------- ES format rules

    // ES only takes a pixel type the internal format lists, while PixelType stays the layout of the caller's data
    private readonly PixelType _esUploadType;

    private PixelType UploadType => Graphics.Capabilities.IsES ? _esUploadType : PixelType;

    private static PixelType EsUploadType(TextureImageFormat format, PixelType dataType) => format switch
    {
        TextureImageFormat.Depth16f => PixelType.UnsignedShort,
        TextureImageFormat.Depth24f => PixelType.UnsignedInt,
        _ when dataType is PixelType.Short or PixelType.UnsignedShort => PixelType.HalfFloat,
        _ => dataType,
    };

    private int Components => PixelFormat switch
    {
        PixelFormat.Red or PixelFormat.RedInteger or PixelFormat.DepthComponent => 1,
        PixelFormat.RG or PixelFormat.RGInteger or PixelFormat.DepthStencil => 2,
        PixelFormat.Rgb or PixelFormat.RgbInteger => 3,
        _ => 4,
    };

    // Normalized shorts become the half floats ES wants for a 16 bit float texture. Rented, the caller returns it.
    private Half[]? HalfsForES(void* data, long texels)
    {
        if (data == null || UploadType != PixelType.HalfFloat || PixelType == PixelType.HalfFloat) return null;
        long count = texels * Components;
        Half[] halfs = ArrayPool<Half>.Shared.Rent((int)count);
        if (PixelType == PixelType.UnsignedShort)
        {
            ushort* source = (ushort*)data;
            for (long i = 0; i < count; i++) halfs[i] = (Half)(source[i] / 65535f);
        }
        else
        {
            short* source = (short*)data;
            for (long i = 0; i < count; i++) halfs[i] = (Half)Math.Max(source[i] / 32767f, -1f);
        }
        return halfs;
    }

    // ES has no direct texture read, so the level is attached to a framebuffer, read as RGBA and repacked
    private void ReadThroughFramebuffer(TextureTarget target, int level, void* destination)
    {
        GL gl = Graphics.GL;
        TextureTarget levelTarget = Target == TextureTarget.TextureCubeMap ? TextureTarget.TextureCubeMapPositiveX : target;
        gl.GetTexLevelParameter(levelTarget, level, GetTextureParameter.TextureWidth, out int width);
        gl.GetTexLevelParameter(levelTarget, level, GetTextureParameter.TextureHeight, out int height);
        long texels = (long)width * height;
        if (texels == 0) return;

        if (PixelFormat is PixelFormat.DepthComponent or PixelFormat.DepthStencil || Target == TextureTarget.Texture3D)
        {
            Debug.LogWarningOnce($"GraphicsTexture.ReadES.{PixelInternalFormat}.{Target}",
                $"OpenGL ES cannot read back a {PixelInternalFormat} {Target}, so it reads as zeros.");
            new Span<byte>(destination, (int)(texels * Components * TypeSize(PixelType))).Clear();
            return;
        }

        uint framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
        gl.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0, target, Handle, level);
        gl.ReadBuffer(ReadBufferMode.ColorAttachment0);

        bool integer = PixelFormat is PixelFormat.RedInteger or PixelFormat.RGInteger or PixelFormat.RgbInteger or PixelFormat.RgbaInteger;
        bool floating = !integer && PixelInternalFormat != InternalFormat.Rgba8;
        int components = Components;
        int typeSize = TypeSize(PixelType);
        try
        {
            if (integer)
            {
                int[] rgba = ArrayPool<int>.Shared.Rent((int)(texels * 4));
                fixed (int* p = rgba)
                    gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.RgbaInteger, PixelType == PixelType.UnsignedInt ? PixelType.UnsignedInt : PixelType.Int, p);
                byte* output = (byte*)destination;
                for (long t = 0; t < texels; t++)
                    for (int c = 0; c < components; c++)
                        WriteInteger(output + (t * components + c) * typeSize, rgba[t * 4 + c]);
                ArrayPool<int>.Shared.Return(rgba);
            }
            else if (floating)
            {
                float[] rgba = ArrayPool<float>.Shared.Rent((int)(texels * 4));
                fixed (float* p = rgba)
                    gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.Float, p);
                byte* output = (byte*)destination;
                for (long t = 0; t < texels; t++)
                    for (int c = 0; c < components; c++)
                        WriteFloat(output + (t * components + c) * typeSize, rgba[t * 4 + c]);
                ArrayPool<float>.Shared.Return(rgba);
            }
            else
            {
                gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, destination);
            }
        }
        finally
        {
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            gl.DeleteFramebuffer(framebuffer);
        }
    }

    private void WriteFloat(byte* at, float value)
    {
        switch (PixelType)
        {
            case PixelType.Float: *(float*)at = value; break;
            case PixelType.HalfFloat: *(Half*)at = (Half)value; break;
            case PixelType.UnsignedShort: *(ushort*)at = (ushort)Math.Clamp(value * 65535f + 0.5f, 0f, 65535f); break;
            case PixelType.Short: *(short*)at = (short)Math.Clamp(value * 32767f, -32767f, 32767f); break;
            case PixelType.UnsignedByte: *at = (byte)Math.Clamp(value * 255f + 0.5f, 0f, 255f); break;
        }
    }

    private void WriteInteger(byte* at, int value)
    {
        switch (PixelType)
        {
            case PixelType.Int or PixelType.UnsignedInt: *(int*)at = value; break;
            case PixelType.UnsignedByte: *at = (byte)value; break;
        }
    }

    private static int TypeSize(PixelType type) => type switch
    {
        PixelType.UnsignedByte or PixelType.Byte => 1,
        PixelType.Short or PixelType.UnsignedShort or PixelType.HalfFloat => 2,
        _ => 4,
    };

    public bool IsDisposed { get; protected set; }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;

        using var cmd = Graphics.GetCommandBuffer("GraphicsTexture.Dispose");
        cmd.EncodeDisposeTexture(this);
        Graphics.Submit(cmd);
    }

    public override string ToString()
    {
        return Handle.ToString();
    }

    public void TexImage2D(TextureTarget type, int mip, uint width, uint height, int v2, void* data)
    {
        Bind(false);
        if (UsesStorage)
        {
            AllocateStorage(width, height, mip);
            if (data != null) TexSubImage2D(type, mip, 0, 0, width, height, data);
            return;
        }
        Half[]? halfs = HalfsForES(data, (long)width * height);
        fixed (Half* h = halfs)
            Graphics.GL.TexImage2D(type, mip, PixelInternalFormat, width, height, v2, PixelFormat, UploadType, halfs != null ? h : data);
        if (halfs != null) ArrayPool<Half>.Shared.Return(halfs);
    }

    public void TexImage3D(TextureTarget type, int level, uint width, uint height, uint depth, void* data)
    {
        Bind(false);
        Half[]? halfs = HalfsForES(data, (long)width * height * depth);
        fixed (Half* h = halfs)
            Graphics.GL.TexImage3D(type, level, PixelInternalFormat, width, height, depth, 0, PixelFormat, UploadType, halfs != null ? h : data);
        if (halfs != null) ArrayPool<Half>.Shared.Return(halfs);
    }

    internal void TexSubImage2D(TextureTarget type, int mip, int x, int y, uint width, uint height, void* data)
    {
        Bind(false);
        Half[]? halfs = HalfsForES(data, (long)width * height);
        fixed (Half* h = halfs)
            Graphics.GL.TexSubImage2D(type, mip, x, y, width, height, PixelFormat, UploadType, halfs != null ? h : data);
        if (halfs != null) ArrayPool<Half>.Shared.Return(halfs);
    }

    internal void TexSubImage3D(TextureTarget type, int level, int x, int y, int z, uint width, uint height, uint depth, void* data)
    {
        Bind(false);
        Half[]? halfs = HalfsForES(data, (long)width * height * depth);
        fixed (Half* h = halfs)
            Graphics.GL.TexSubImage3D(type, level, x, y, z, width, height, depth, PixelFormat, UploadType, halfs != null ? h : data);
        if (halfs != null) ArrayPool<Half>.Shared.Return(halfs);
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

        pixelType = imageFormat switch
        {
            TextureImageFormat.Color4b => PixelType.UnsignedByte,
            TextureImageFormat.Byte => PixelType.UnsignedByte,
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
            TextureImageFormat.Depth16f => PixelType.Float,
            TextureImageFormat.Depth24f => PixelType.Float,
            TextureImageFormat.Depth32f => PixelType.Float,
            TextureImageFormat.Depth24Stencil8 => (PixelType)GLEnum.UnsignedInt248,
            _ => throw new ArgumentException("Image format is not a valid TextureImageFormat value", nameof(imageFormat)),
        };

        pixelInternalFormat = imageFormat switch
        {
            TextureImageFormat.Color4b => InternalFormat.Rgba8,
            TextureImageFormat.Byte => InternalFormat.R8ui,
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
            TextureImageFormat.Depth16f => InternalFormat.DepthComponent16,
            TextureImageFormat.Depth24f => InternalFormat.DepthComponent24,
            TextureImageFormat.Depth32f => InternalFormat.DepthComponent32f,
            TextureImageFormat.Depth24Stencil8 => InternalFormat.Depth24Stencil8,
            _ => throw new ArgumentException("Image format is not a valid TextureImageFormat value", nameof(imageFormat)),
        };

        pixelFormat = imageFormat switch
        {
            TextureImageFormat.Color4b => PixelFormat.Rgba,
            TextureImageFormat.Byte => PixelFormat.RedInteger,
            TextureImageFormat.Short => PixelFormat.Red,
            TextureImageFormat.Short2 => PixelFormat.RG,
            TextureImageFormat.Short3 => PixelFormat.Rgb,
            TextureImageFormat.Short4 => PixelFormat.Rgba,
            TextureImageFormat.Float => PixelFormat.Red,
            TextureImageFormat.Float2 => PixelFormat.RG,
            TextureImageFormat.Float3 => PixelFormat.Rgb,
            TextureImageFormat.Float4 => PixelFormat.Rgba,
            TextureImageFormat.Int => PixelFormat.RedInteger,
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
            TextureImageFormat.Depth16f => PixelFormat.DepthComponent,
            TextureImageFormat.Depth24f => PixelFormat.DepthComponent,
            TextureImageFormat.Depth32f => PixelFormat.DepthComponent,
            TextureImageFormat.Depth24Stencil8 => PixelFormat.DepthStencil,
            _ => throw new ArgumentException("Image format is not a valid TextureImageFormat value", nameof(imageFormat)),
        };
    }
}
