// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

public static class GlobalPropertySet
{
    private static PropertySet _globalSet = new();

    /// <summary>Sets a <c>float</c> uniform field.</summary>
    public static void SetFloat(PropertyID name, float v) => _globalSet.SetFloat(name, v);

    /// <summary>Sets a <c>float2</c> uniform field.</summary>
    public static void SetFloat2(PropertyID name, Float2 v) => _globalSet.SetFloat2(name, v);

    /// <summary>Sets a <c>float3</c> uniform field.</summary>
    public static void SetFloat3(PropertyID name, Float3 v) => _globalSet.SetFloat3(name, v);

    /// <summary>Sets a <c>float4</c> uniform field.</summary>
    public static void SetFloat4(PropertyID name, Float4 v) => _globalSet.SetFloat4(name, v);

    public static void SetVector(PropertyID name, Float2 v) => _globalSet.SetVector(name, v);

    public static void SetVector(PropertyID name, Float3 v) => _globalSet.SetVector(name, v);

    public static void SetVector(PropertyID name, Float4 v) => _globalSet.SetVector(name, v);

    public static void SetColor(PropertyID name, Color v) => _globalSet.SetColor(name, v);

    /// <summary>Sets an <c>int</c> uniform field.</summary>
    public static void SetInt(PropertyID name, int v) => _globalSet.SetInt(name, v);

    /// <summary>Sets an <c>int2</c> uniform field.</summary>
    public static void SetInt2(PropertyID name, Int2 v) => _globalSet.SetInt2(name, v);

    /// <summary>Sets an <c>int3</c> uniform field.</summary>
    public static void SetInt3(PropertyID name, Int3 v) => _globalSet.SetInt3(name, v);

    /// <summary>Sets an <c>int4</c> uniform field.</summary>
    public static void SetInt4(PropertyID name, Int4 v) => _globalSet.SetInt4(name, v);

    /// <summary>Sets a <c>double</c> uniform field.</summary>
    public static void SetDouble(PropertyID name, double v) => _globalSet.SetDouble(name, v);

    /// <summary>Sets a <c>double2</c> uniform field.</summary>
    public static void SetDouble2(PropertyID name, Double2 v) => _globalSet.SetDouble2(name, v);

    /// <summary>Sets a <c>double3</c> uniform field.</summary>
    public static void SetDouble3(PropertyID name, Double3 v) => _globalSet.SetDouble3(name, v);

    /// <summary>Sets a <c>double4</c> uniform field.</summary>
    public static void SetDouble4(PropertyID name, Double4 v) => _globalSet.SetDouble4(name, v);

    /// <summary>Sets a <c>float4x4</c> matrix uniform field.</summary>
    public static void SetMatrix(PropertyID name, Float4x4 v) => _globalSet.SetMatrix(name, v);

    /// <summary>Sets a <c>double4x4</c> matrix uniform field.</summary>
    public static void SetDoubleMatrix(PropertyID name, Double4x4 v) => _globalSet.SetDoubleMatrix(name, v);

    /// <inheritdoc cref="SetBuffer(PropertyID, DeviceBufferRange, bool)"/>
    public static void SetBuffer(PropertyID name, DeviceBuffer buffer, bool readOnly = true) => _globalSet.SetBuffer(name, buffer, readOnly);

    /// <summary>
    /// Binds a <see cref="DeviceBuffer"/> to the named property slot.
    /// </summary>
    public static void SetBuffer(PropertyID name, DeviceBufferRange range, bool readOnly = true) => _globalSet.SetBuffer(name, range, readOnly);

    /// <inheritdoc cref="SetTexture(PropertyID, TextureView, Sampler)"/>
    public static void SetTexture(PropertyID name, Graphite.Texture texture, Sampler? sampler = null) => _globalSet.SetTexture(name, texture, sampler);

    public static void SetTexture(PropertyID name, Texture2D texture) => _globalSet.SetTexture(name, texture.Handle, texture.Sampler);

    /// <summary>
    /// Binds a <see cref="Texture"/> to the named property slot with an optional paired sampler.
    /// On OpenGL the sampler is bound alongside the texture. On Vulkan and D3D11 the sampler is also
    /// applied to the matched sampler slot (see <see cref="CommandBuffer.SetProperties"/> remarks).
    /// When <paramref name="sampler"/> is null, <see cref="GraphicsDevice.LinearSampler"/> is used.
    /// </summary>
    public static void SetTexture(PropertyID name, TextureView view, Sampler? sampler = null) => _globalSet.SetTexture(name, view, sampler);

    /// <summary>
    /// Binds a <see cref="Sampler"/> to the named slot independently of any texture. On OpenGL this is
    /// a no-op; the sampler is sourced from the matching <see cref="SetTexture(PropertyID,Graphite.Texture,Sampler?)"/> call instead.
    /// </summary>
    public static void SetSampler(PropertyID name, Sampler sampler) => _globalSet.SetSampler(name, sampler);

    public static void ClearGlobals() => _globalSet.Clear();
}