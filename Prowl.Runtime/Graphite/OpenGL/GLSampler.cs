// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.OpenGL;

namespace Prowl.Runtime.Graphite.OpenGL;

/// <summary>
/// OpenGL implementation of a sampler.
/// </summary>
public class GLSampler : Sampler
{
    private readonly GLGraphiteDevice _device;
    internal uint Handle { get; private set; }

    internal GLSampler(GLGraphiteDevice device, in SamplerDescriptor descriptor)
    {
        _device = device;

        MinFilter = descriptor.MinFilter;
        MagFilter = descriptor.MagFilter;
        MipmapFilter = descriptor.MipmapFilter;
        AddressModeU = descriptor.AddressModeU;
        AddressModeV = descriptor.AddressModeV;
        AddressModeW = descriptor.AddressModeW;
        MaxAnisotropy = descriptor.MaxAnisotropy;
        CompareFunction = descriptor.CompareFunction;
        DebugName = descriptor.DebugName;

        Handle = _device.GL.GenSampler();

        // Min filter (combines filter + mipmap filter)
        var minFilter = GetMinFilter(descriptor.MinFilter, descriptor.MipmapFilter);
        _device.GL.SamplerParameter(Handle, SamplerParameterI.TextureMinFilter, (int)minFilter);

        // Mag filter
        var magFilter = descriptor.MagFilter == TextureFilter.Nearest
            ? TextureMagFilter.Nearest
            : TextureMagFilter.Linear;
        _device.GL.SamplerParameter(Handle, SamplerParameterI.TextureMagFilter, (int)magFilter);

        // Address modes
        _device.GL.SamplerParameter(Handle, SamplerParameterI.TextureWrapS, (int)GetAddressMode(descriptor.AddressModeU));
        _device.GL.SamplerParameter(Handle, SamplerParameterI.TextureWrapT, (int)GetAddressMode(descriptor.AddressModeV));
        _device.GL.SamplerParameter(Handle, SamplerParameterI.TextureWrapR, (int)GetAddressMode(descriptor.AddressModeW));

        // LOD
        _device.GL.SamplerParameter(Handle, SamplerParameterF.TextureMinLod, descriptor.MinLod);
        _device.GL.SamplerParameter(Handle, SamplerParameterF.TextureMaxLod, descriptor.MaxLod);
        _device.GL.SamplerParameter(Handle, SamplerParameterF.TextureLodBias, descriptor.MipLodBias);

        // Anisotropy
        if (descriptor.MaxAnisotropy > 1)
        {
            _device.GL.SamplerParameter(Handle, SamplerParameterF.TextureMaxAnisotropy, descriptor.MaxAnisotropy);
        }

        // Comparison (for shadow samplers)
        if (descriptor.CompareFunction.HasValue)
        {
            _device.GL.SamplerParameter(Handle, SamplerParameterI.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
            _device.GL.SamplerParameter(Handle, SamplerParameterI.TextureCompareFunc, (int)GetCompareFunc(descriptor.CompareFunction.Value));
        }
        else
        {
            _device.GL.SamplerParameter(Handle, SamplerParameterI.TextureCompareMode, (int)GLEnum.None);
        }

        // Border color
        SetBorderColor(descriptor.BorderColor);
    }

    private void SetBorderColor(BorderColor borderColor)
    {
        Span<float> color = borderColor switch
        {
            BorderColor.TransparentBlack => [0, 0, 0, 0],
            BorderColor.OpaqueBlack => [0, 0, 0, 1],
            BorderColor.OpaqueWhite => [1, 1, 1, 1],
            _ => [0, 0, 0, 0],
        };
        _device.GL.SamplerParameter(Handle, SamplerParameterF.TextureBorderColor, color);
    }

    private static TextureMinFilter GetMinFilter(TextureFilter minFilter, TextureFilter mipFilter)
    {
        return (minFilter, mipFilter) switch
        {
            (TextureFilter.Nearest, TextureFilter.Nearest) => TextureMinFilter.NearestMipmapNearest,
            (TextureFilter.Nearest, TextureFilter.Linear) => TextureMinFilter.NearestMipmapLinear,
            (TextureFilter.Linear, TextureFilter.Nearest) => TextureMinFilter.LinearMipmapNearest,
            (TextureFilter.Linear, TextureFilter.Linear) => TextureMinFilter.LinearMipmapLinear,
            _ => TextureMinFilter.Linear,
        };
    }

    private static GLEnum GetAddressMode(TextureAddressMode mode) => mode switch
    {
        TextureAddressMode.Repeat => GLEnum.Repeat,
        TextureAddressMode.MirrorRepeat => GLEnum.MirroredRepeat,
        TextureAddressMode.ClampToEdge => GLEnum.ClampToEdge,
        TextureAddressMode.ClampToBorder => GLEnum.ClampToBorder,
        _ => GLEnum.Repeat,
    };

    private static DepthFunction GetCompareFunc(CompareFunction func) => func switch
    {
        Graphite.CompareFunction.Never => DepthFunction.Never,
        Graphite.CompareFunction.Less => DepthFunction.Less,
        Graphite.CompareFunction.Equal => DepthFunction.Equal,
        Graphite.CompareFunction.LessEqual => DepthFunction.Lequal,
        Graphite.CompareFunction.Greater => DepthFunction.Greater,
        Graphite.CompareFunction.NotEqual => DepthFunction.Notequal,
        Graphite.CompareFunction.GreaterEqual => DepthFunction.Gequal,
        Graphite.CompareFunction.Always => DepthFunction.Always,
        _ => DepthFunction.Less,
    };

    protected override void DisposeResources()
    {
        if (Handle != 0)
        {
            _device.GL.DeleteSampler(Handle);
            Handle = 0;
        }
    }
}
