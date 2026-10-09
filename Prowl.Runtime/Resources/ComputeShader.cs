// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;

/// <summary>One entry point of a <see cref="ComputeShader"/> and the size of its thread groups.</summary>
public struct ComputeKernel
{
    public string Name;
    public uint X;
    public uint Y;
    public uint Z;
}

/// <summary>
/// GLSL that runs on the GPU outside of drawing. A <c>.compute</c> file declares its entry points with
/// <c>#pragma kernel Name X Y Z</c>, the thread group size (Y and Z default to 1), and each one is a plain
/// <c>void Name()</c> function reading <c>gl_GlobalInvocationID</c> and friends. Storage blocks and images need no
/// binding layout, the engine assigns them and binds by name.
/// <para/>
/// Values set with <see cref="SetFloat"/> and the like apply to every kernel, buffers and textures are set per kernel.
/// Needs compute support, see <see cref="IsSupported"/>.
/// </summary>
public sealed class ComputeShader : Asset
{
    [SerializeField] private string _source = "";
    [SerializeField] private ComputeKernel[] _kernels = [];

    [SerializeIgnore] private GraphicsProgram?[] _programs = [];
    [SerializeIgnore] private bool[] _failed = [];
    [SerializeIgnore] private PropertyState[] _kernelProperties = [];
    [SerializeIgnore] private PropertyState _shared = new();

    private static readonly Regex s_kernelPragma = new(@"^[ \t]*#pragma[ \t]+kernel[ \t]+(\w+)(?:[ \t]+(\d+))?(?:[ \t]+(\d+))?(?:[ \t]+(\d+))?[ \t]*\r?$", RegexOptions.Multiline);

    internal ComputeShader() : base("New Compute Shader") { }

    /// <summary>Whether this device and graphics target can run compute shaders at all.</summary>
    public static bool IsSupported => Graphics.Capabilities.Has(GraphicsFeature.ComputeShaders);

    /// <summary>Builds a compute shader from GLSL, with its includes already expanded.</summary>
    public static ComputeShader FromSource(string name, string source)
    {
        var shader = new ComputeShader { Name = name };
        shader.Parse(source);
        return shader;
    }

    private void Parse(string source)
    {
        var kernels = new List<ComputeKernel>();
        foreach (Match match in s_kernelPragma.Matches(source))
        {
            kernels.Add(new ComputeKernel
            {
                Name = match.Groups[1].Value,
                X = match.Groups[2].Success ? uint.Parse(match.Groups[2].Value) : 1,
                Y = match.Groups[3].Success ? uint.Parse(match.Groups[3].Value) : 1,
                Z = match.Groups[4].Success ? uint.Parse(match.Groups[4].Value) : 1,
            });
        }
        if (kernels.Count == 0)
            throw new ArgumentException($"Compute shader '{Name}' declares no kernels. Add a line such as '#pragma kernel Main 64'.");

        _source = s_kernelPragma.Replace(source, "");
        _kernels = [.. kernels];
        ResetKernels();
    }

    private void ResetKernels()
    {
        foreach (GraphicsProgram? program in _programs)
            program?.Dispose();
        _programs = new GraphicsProgram?[_kernels.Length];
        _failed = new bool[_kernels.Length];
        _kernelProperties = new PropertyState[_kernels.Length];
        for (int i = 0; i < _kernels.Length; i++)
            _kernelProperties[i] = new PropertyState();
    }

    // ---------------------------------------------------------------- kernels

    /// <summary>The kernel's index, for the per kernel setters and <see cref="Dispatch"/>.</summary>
    public int FindKernel(string name)
    {
        int index = IndexOf(name);
        if (index < 0) throw new ArgumentException($"Compute shader '{Name}' has no kernel named '{name}'.", nameof(name));
        return index;
    }

    public bool HasKernel(string name) => IndexOf(name) >= 0;

    private int IndexOf(string name)
    {
        EnsureLoaded();
        for (int i = 0; i < _kernels.Length; i++)
            if (_kernels[i].Name == name) return i;
        return -1;
    }

    public void GetKernelThreadGroupSizes(int kernel, out uint x, out uint y, out uint z)
    {
        ComputeKernel k = Kernel(kernel);
        (x, y, z) = (k.X, k.Y, k.Z);
    }

    private ComputeKernel Kernel(int kernel)
    {
        EnsureLoaded();
        if ((uint)kernel >= (uint)_kernels.Length)
            throw new ArgumentOutOfRangeException(nameof(kernel), $"Compute shader '{Name}' has {_kernels.Length} kernels, {kernel} is not one of them.");
        // A loaded shader arrives with its kernels but none of the per kernel state
        if (_programs.Length != _kernels.Length) ResetKernels();
        return _kernels[kernel];
    }

    /// <summary>The kernel's GLSL without the version line: its group size, the shared source and a main that calls it.</summary>
    internal string KernelSource(int kernel)
    {
        ComputeKernel k = Kernel(kernel);
        var sb = new StringBuilder();
        sb.Append($"#define KERNEL_{k.Name} 1\n");
        sb.Append($"layout(local_size_x = {k.X}, local_size_y = {k.Y}, local_size_z = {k.Z}) in;\n");
        sb.Append(_source);
        sb.Append($"\nvoid main() {{ {k.Name}(); }}\n");
        return sb.ToString();
    }

    /// <summary>The kernel's program, compiled the first time it is asked for. Null when compute is unavailable or it failed to compile.</summary>
    internal GraphicsProgram? ProgramFor(int kernel)
    {
        Kernel(kernel);
        if (!Graphics.Capabilities.Require(GraphicsFeature.ComputeShaders, $"Compute shader '{Name}'")) return null;
        if (_failed[kernel]) return null;
        if (_programs[kernel] is { IsDisposed: false } program) return program;

        try
        {
            _programs[kernel] = new GraphicsProgram(Graphics.ShaderPrelude + KernelSource(kernel));
        }
        catch (Exception ex)
        {
            _failed[kernel] = true;
            Debug.LogError($"Compute kernel '{_kernels[kernel].Name}' of '{Name}' failed to compile: {ex.Message}");
        }
        return _programs[kernel];
    }

    internal PropertyState SharedProperties { get { EnsureLoaded(); return _shared; } }

    internal PropertyState KernelProperties(int kernel)
    {
        Kernel(kernel);
        return _kernelProperties[kernel];
    }

    // ---------------------------------------------------------------- parameters

    public void SetFloat(string name, float value) => SharedProperties.SetFloat(name, value);
    public void SetInt(string name, int value) => SharedProperties.SetInt(name, value);
    public void SetBool(string name, bool value) => SharedProperties.SetInt(name, value ? 1 : 0);
    public void SetVector(string name, Float2 value) => SharedProperties.SetVector(name, value);
    public void SetVector(string name, Float3 value) => SharedProperties.SetVector(name, value);
    public void SetVector(string name, Float4 value) => SharedProperties.SetVector(name, value);
    public void SetMatrix(string name, Float4x4 value) => SharedProperties.SetMatrix(name, value);
    public void SetMatrixArray(string name, Float4x4[] values) => SharedProperties.SetMatrices(name, values);

    /// <summary>Binds a buffer to the kernel's storage or uniform block of that name.</summary>
    public void SetBuffer(int kernel, string name, ComputeBuffer buffer) => KernelProperties(kernel).SetBuffer(name, buffer);

    /// <summary>Binds a texture to the kernel's sampler or image of that name. An image reads and writes the texture directly.</summary>
    public void SetTexture(int kernel, string name, Texture2D texture)
    {
        // The render thread only binds a texture whose GPU side exists, and a fresh one makes it on first use
        _ = texture.Handle;
        KernelProperties(kernel).SetTexture(name, texture);
    }

    public void SetTexture(int kernel, string name, RenderTexture texture) => SetTexture(kernel, name, texture.MainTexture);

    public void SetTexture(int kernel, string name, Texture3D texture)
    {
        _ = texture.Handle;
        KernelProperties(kernel).SetTexture3D(name, texture);
    }

    /// <summary>Runs the kernel over the given number of thread groups, in order with the rendering submitted around it.</summary>
    public void Dispatch(int kernel, int threadGroupsX, int threadGroupsY, int threadGroupsZ)
    {
        using CommandBuffer cmd = Graphics.GetCommandBuffer($"Dispatch {Name}");
        cmd.DispatchCompute(this, kernel, threadGroupsX, threadGroupsY, threadGroupsZ);
        Graphics.Submit(cmd);
    }

    /// <summary>Runs the kernel with its thread group counts read from three uints in <paramref name="arguments"/>.</summary>
    public void DispatchIndirect(int kernel, ComputeBuffer arguments, uint argumentsOffset = 0)
    {
        using CommandBuffer cmd = Graphics.GetCommandBuffer($"Dispatch {Name}");
        cmd.DispatchCompute(this, kernel, arguments, argumentsOffset);
        Graphics.Submit(cmd);
    }

    // ---------------------------------------------------------------- asset

    protected override void TakeContent(Asset staging)
    {
        base.TakeContent(staging);
        ResetKernels();
    }

    protected override void OnUnload()
    {
        foreach (GraphicsProgram? program in _programs)
            program?.Dispose();
        _programs = new GraphicsProgram?[_kernels.Length];
    }
}

/// <summary>
/// A GPU buffer of <see cref="Count"/> elements of <see cref="Stride"/> bytes, read and written by compute kernels and
/// shaders as a storage block. Fill it with <see cref="SetData{T}(ReadOnlySpan{T}, int)"/> and read it back with
/// <see cref="GetData{T}(Span{T}, int)"/>, which waits for the GPU.
/// </summary>
public sealed class ComputeBuffer : IDisposable
{
    public int Count { get; }
    public int Stride { get; }

    /// <summary>Null where the graphics target has no storage buffers, every call then does nothing.</summary>
    internal GraphicsBuffer? Buffer { get; }

    public bool IsDisposed { get; private set; }

    public ComputeBuffer(int count, int stride)
    {
        if (count <= 0) throw new ArgumentException("A compute buffer needs at least one element.", nameof(count));
        if (stride <= 0 || stride % 4 != 0) throw new ArgumentException($"Stride must be a positive multiple of 4, {stride} is not.", nameof(stride));
        Count = count;
        Stride = stride;

        if (Graphics.Capabilities.Require(GraphicsFeature.StorageBuffers, "ComputeBuffer"))
            Buffer = new GraphicsBuffer(BufferType.StructuredBuffer, new byte[(long)count * stride], dynamic: true);
    }

    public void SetData<T>(T[] data) where T : unmanaged => SetData((ReadOnlySpan<T>)data);

    /// <summary>Uploads <paramref name="data"/> starting at element <paramref name="bufferStartIndex"/>.</summary>
    public void SetData<T>(ReadOnlySpan<T> data, int bufferStartIndex = 0) where T : unmanaged
    {
        long offset = CheckRange(MemoryMarshal.AsBytes(data).Length, bufferStartIndex);
        if (Buffer == null || data.Length == 0) return;

        using CommandBuffer cmd = Graphics.GetCommandBuffer("ComputeBuffer.SetData");
        cmd.UpdateBuffer(Buffer, data, (uint)offset);
        Graphics.Submit(cmd);
    }

    public void GetData<T>(T[] destination) where T : unmanaged => GetData((Span<T>)destination);

    /// <summary>Reads elements back from <paramref name="bufferStartIndex"/> on, once everything submitted before has run.</summary>
    public unsafe void GetData<T>(Span<T> destination, int bufferStartIndex = 0) where T : unmanaged
    {
        int bytes = MemoryMarshal.AsBytes(destination).Length;
        long offset = CheckRange(bytes, bufferStartIndex);
        if (Buffer == null || bytes == 0) return;

        GraphicsBuffer buffer = Buffer;
        fixed (T* target = destination)
        {
            nint address = (nint)target;
            Graphics.SubmitRenderThreadCallbackAndWait(() => buffer.Read((uint)offset, (uint)bytes, (void*)address));
        }
    }

    private long CheckRange(int byteCount, int startIndex)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ComputeBuffer));
        long offset = (long)startIndex * Stride;
        if (startIndex < 0 || offset + byteCount > (long)Count * Stride)
            throw new ArgumentException($"{byteCount} bytes from element {startIndex} do not fit a buffer of {Count} x {Stride} bytes.");
        return offset;
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Buffer?.Dispose();
    }
}
