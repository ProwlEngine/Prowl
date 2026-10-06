using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Prowl.Graphite.Debugging;

/// <summary>A texture subresource bound as a render target, with its version at bind time.</summary>
public readonly record struct AttachmentUse(ResourceVersion Texture, uint MipLevel, uint ArrayLayer);

/// <summary>Render target set by a framebuffer bind. The span is only valid for the duration of the call.</summary>
public readonly ref struct FramebufferInfo
{
    /// <summary>Color attachments in slot order.</summary>
    public ReadOnlySpan<AttachmentUse> Colors { get; }

    /// <summary>Depth attachment, if any.</summary>
    public AttachmentUse? Depth { get; }

    /// <summary>Output description of the framebuffer.</summary>
    public OutputDescription Outputs { get; }

    /// <summary>Width in texels.</summary>
    public uint Width { get; }

    /// <summary>Height in texels.</summary>
    public uint Height { get; }

    public FramebufferInfo(ReadOnlySpan<AttachmentUse> colors, AttachmentUse? depth, OutputDescription outputs, uint width, uint height)
    {
        Colors = colors;
        Depth = depth;
        Outputs = outputs;
        Width = width;
        Height = height;
    }
}

/// <summary>A vertex buffer resolved for one layout slot of the bound program.</summary>
public readonly record struct VertexBindingUse(uint Slot, ResourceVersion Buffer, uint Offset, uint Stride);

/// <summary>The index buffer resolved for an indexed draw.</summary>
public readonly record struct IndexBindingUse(ResourceVersion Buffer, IndexFormat Format, uint IndexCount);

/// <summary>What a changed property now holds.</summary>
public enum PropertyDeltaKind : byte
{
    /// <summary>Loose uniform value.</summary>
    Uniform,
    /// <summary>Structured buffer range.</summary>
    Buffer,
    /// <summary>Backed uniform block target range.</summary>
    UniformBuffer,
    /// <summary>Texture, whole or through a view, with optional sampler.</summary>
    Texture,
    /// <summary>Standalone sampler.</summary>
    Sampler,
}

/// <summary>Inline uniform payload, enough for a Float4x4.</summary>
[InlineArray(Size)]
public struct UniformValue
{
    /// <summary>Payload size in bytes.</summary>
    public const int Size = 64;

    private byte _element0;

    /// <summary>Payload as bytes.</summary>
    [UnscopedRef]
    public readonly ReadOnlySpan<byte> AsBytes() => System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in _element0, Size);

    /// <summary>Payload reinterpreted as T.</summary>
    public readonly T Read<T>() where T : unmanaged => System.Runtime.InteropServices.MemoryMarshal.Read<T>(AsBytes());

    /// <summary>Payload holding the value.</summary>
    public static UniformValue From<T>(in T value) where T : unmanaged
    {
        UniformValue result = default;
        System.Runtime.InteropServices.MemoryMarshal.Write(result, in value);
        return result;
    }
}

/// <summary>
/// One changed property of the resolved property table. Resources are reported by version and range.
/// Textures report the view's mip and layer range, and the view format when a view is bound.
/// </summary>
public readonly record struct PropertyDelta(
    PropertyID Name,
    PropertyDeltaKind Kind,
    UniformScalarType UniformType,
    UniformValue Uniform,
    ResourceVersion Resource,
    ResourceRange Range,
    PixelFormat? ViewFormat,
    SamplerDescription? Sampler);
