// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
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
/// binding layout, Graphite assigns them and binds by name.
/// <para/>
/// Values set with <see cref="SetFloat"/> and the like apply to every kernel, buffers and textures are set per kernel.
/// A dispatch runs at once, ahead of anything submitted later, so a buffer read afterwards sees its results. For work
/// inside the frame, write a Graphite RenderFeature or RenderProvider instead.
/// </summary>
public sealed class ComputeShader : Asset
{
    [SerializeField] private string _source = "";
    [SerializeField] private ComputeKernel[] _kernels = [];

    [SerializeIgnore] private ComputeProgram?[] _programs = [];
    [SerializeIgnore] private bool[] _failed = [];
    [SerializeIgnore] private PropertySet[] _kernelProperties = [];
    [SerializeIgnore] private Dictionary<string, Action<PropertySet>> _shared = [];
    [SerializeIgnore] private Dictionary<string, ComputeBuffer>[] _kernelBuffers = [];

    private static readonly Regex s_kernelPragma = new(@"^[ \t]*#pragma[ \t]+kernel[ \t]+(\w+)(?:[ \t]+(\d+))?(?:[ \t]+(\d+))?(?:[ \t]+(\d+))?[ \t]*\r?$", RegexOptions.Multiline);

    internal ComputeShader() : base("New Compute Shader") { }

    /// <summary>Whether compute can run, which needs a device.</summary>
    public static bool IsSupported => Graphics.Device != null;

    /// <summary>Makes a compute shader from source declaring its kernels with <c>#pragma kernel</c>.</summary>
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
        foreach (ComputeProgram? program in _programs)
            Graphics.DeferDispose(program);
        _programs = new ComputeProgram?[_kernels.Length];
        _failed = new bool[_kernels.Length];
        _kernelProperties = new PropertySet[_kernels.Length];
        _kernelBuffers = new Dictionary<string, ComputeBuffer>[_kernels.Length];
        for (int i = 0; i < _kernels.Length; i++)
        {
            _kernelProperties[i] = new PropertySet();
            _kernelBuffers[i] = [];
        }
    }

    // ---------------------------------------------------------------- kernels

    /// <summary>The index of the kernel named <paramref name="name"/>. Throws when there is none.</summary>
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

    /// <summary>The kernel as one compute program: its thread group size, the shared source and a main calling it.</summary>
    internal string KernelSource(int kernel)
    {
        ComputeKernel k = Kernel(kernel);
        var sb = new StringBuilder();
        sb.Append("Compute\n{\n");
        sb.Append($"#define KERNEL_{k.Name} 1\n");
        sb.Append($"layout(local_size_x = {k.X}, local_size_y = {k.Y}, local_size_z = {k.Z}) in;\n");
        sb.Append(_source);
        sb.Append($"\nvoid main() {{ {k.Name}(); }}\n}}\n");
        return sb.ToString();
    }

    private ComputeProgram? ProgramFor(int kernel)
    {
        Kernel(kernel);
        if (Graphics.Device == null || _failed[kernel]) return null;
        if (_programs[kernel] != null) return _programs[kernel];
        try
        {
            ShaderPass pass = new() { State = new PassState(), Source = KernelSource(kernel), SourceName = $"{Name}/{_kernels[kernel].Name}" };
            new ShaderDefinition { Name = pass.SourceName, Passes = [pass] }.Create(Graphics.Device, Graphics.ShaderCompiler);
            _programs[kernel] = Graphics.Device.ResourceFactory.CreateComputeProgram(pass.CompileCompute());
            _programs[kernel]!.Name = pass.SourceName;
        }
        catch (Exception ex)
        {
            _failed[kernel] = true;
            Debug.LogError($"Compute kernel '{_kernels[kernel].Name}' of '{Name}' failed to compile: {ex.Message}");
        }
        return _programs[kernel];
    }

    // ---------------------------------------------------------------- parameters

    private void Shared(string name, Action<PropertySet> set)
    {
        EnsureLoaded();
        _shared[name] = set;
    }

    public void SetFloat(string name, float value) => Shared(name, p => p.SetFloat(name, value));
    public void SetInt(string name, int value) => Shared(name, p => p.SetInt(name, value));
    public void SetBool(string name, bool value) => Shared(name, p => p.SetInt(name, value ? 1 : 0));
    public void SetVector(string name, Float2 value) => Shared(name, p => p.SetFloat2(name, value));
    public void SetVector(string name, Float3 value) => Shared(name, p => p.SetFloat3(name, value));
    public void SetVector(string name, Float4 value) => Shared(name, p => p.SetFloat4(name, value));
    public void SetMatrix(string name, Float4x4 value) => Shared(name, p => p.SetMatrix(name, value));

    /// <summary>Binds a buffer to the storage block of that name for one kernel.</summary>
    public void SetBuffer(int kernel, string name, ComputeBuffer buffer)
    {
        Kernel(kernel);
        _kernelBuffers[kernel][name] = buffer;
        if (buffer.Buffer != null) _kernelProperties[kernel].SetBuffer(name, buffer.Buffer);
    }

    /// <summary>Binds a texture, sampled or as an image, for one kernel.</summary>
    public void SetTexture(int kernel, string name, Texture texture)
    {
        Kernel(kernel);
        if (texture.GpuTexture is { } gpu)
            _kernelProperties[kernel].SetTexture(name, gpu, Graphics.GetSampler(texture.Sampler));
    }

    public void SetTexture(int kernel, string name, RenderTexture texture) => SetTexture(kernel, name, texture.MainTexture);

    /// <summary>Runs <paramref name="kernel"/> over a grid of thread groups, before anything submitted after it.</summary>
    public void Dispatch(int kernel, int threadGroupsX, int threadGroupsY, int threadGroupsZ)
        => Run(kernel, cmd => cmd.Dispatch((uint)Math.Max(threadGroupsX, 1), (uint)Math.Max(threadGroupsY, 1), (uint)Math.Max(threadGroupsZ, 1)));

    /// <summary>Runs <paramref name="kernel"/> with its group counts read from <paramref name="arguments"/> at an offset.</summary>
    public void DispatchIndirect(int kernel, ComputeBuffer arguments, uint argumentsOffset = 0)
    {
        if (arguments.Buffer is not { } buffer) return;
        Run(kernel, cmd => cmd.DispatchIndirect(buffer, argumentsOffset));
    }

    private void Run(int kernel, Action<CommandBuffer> dispatch)
    {
        ComputeProgram? program = ProgramFor(kernel);
        if (program == null || Graphics.Device == null) return;

        PropertySet properties = _kernelProperties[kernel];
        foreach (Action<PropertySet> set in _shared.Values) set(properties);
        List<DeviceBuffer> buffers = [];
        foreach (ComputeBuffer buffer in _kernelBuffers[kernel].Values)
            if (buffer.Buffer != null) buffers.Add(buffer.Buffer);

        // The renderer's thread records frames on the same device, so it finishes its frame first
        Graphics.Renderer?.WaitForRenderThread();
        ComputeRun.Execute(program, properties, buffers, dispatch);
    }

    // ---------------------------------------------------------------- asset

    protected override void TakeContent(Asset staging)
    {
        base.TakeContent(staging);
        ResetKernels();
    }

    protected override void OnUnload()
    {
        foreach (ComputeProgram? program in _programs)
            Graphics.DeferDispose(program);
        _programs = new ComputeProgram?[_kernels.Length];
    }
}

/// <summary>One compute dispatch as a graph execution of its own, its buffers declared so it waits on writes before it.</summary>
internal static class ComputeRun
{
    private static FrameGraph? s_graph;

    public static void Execute(ComputeProgram program, PropertySet properties, List<DeviceBuffer> buffers, Action<CommandBuffer> dispatch)
    {
        lock (typeof(ComputeRun))
        {
            FrameGraph graph = s_graph ??= new FrameGraph(Graphics.Device!, "Compute");
            graph.AddPass("Compute", pass =>
            {
                foreach (DeviceBuffer buffer in buffers)
                    pass.Write(graph.Import(buffer));
            }, context =>
            {
                CommandBuffer cmd = context.Command;
                cmd.SetComputeShader(program);
                cmd.SetProperties(properties);
                dispatch(cmd);
            });
            graph.Execute();
        }
    }
}

/// <summary>A storage buffer of fixed sized elements for compute kernels, over a Graphite buffer.</summary>
public sealed class ComputeBuffer : IDisposable
{
    public int Count { get; }
    public int Stride { get; }

    /// <summary>The Graphite buffer, null headless.</summary>
    public DeviceBuffer? Buffer { get; }

    public bool IsDisposed { get; private set; }

    public ComputeBuffer(int count, int stride)
    {
        if (count <= 0) throw new ArgumentException("A compute buffer needs at least one element.", nameof(count));
        if (stride <= 0 || stride % 4 != 0) throw new ArgumentException($"Stride must be a positive multiple of 4, {stride} is not.", nameof(stride));
        Count = count;
        Stride = stride;
        if (Graphics.Device != null)
            Buffer = Graphics.Device.ResourceFactory.CreateBuffer(new BufferDescription((uint)((long)count * stride), BufferUsage.StructuredBufferReadWrite | BufferUsage.IndirectBuffer));
    }

    public void SetData<T>(T[] data) where T : unmanaged => SetData((ReadOnlySpan<T>)data);

    public void SetData<T>(ReadOnlySpan<T> data, int bufferStartIndex = 0) where T : unmanaged
    {
        long offset = CheckRange(MemoryMarshal.AsBytes(data).Length, bufferStartIndex);
        if (Buffer == null || data.Length == 0) return;
        Graphics.Device!.UpdateBuffer(Buffer, (uint)offset, data);
    }

    public void GetData<T>(T[] destination) where T : unmanaged => GetData((Span<T>)destination);

    /// <summary>Reads elements back, waiting for every dispatch before it. Zeroes headless.</summary>
    public void GetData<T>(Span<T> destination, int bufferStartIndex = 0) where T : unmanaged
    {
        Span<byte> target = MemoryMarshal.AsBytes(destination);
        long offset = CheckRange(target.Length, bufferStartIndex);
        if (Buffer == null || target.Length == 0) return;
        byte[] data = Graphics.ReadBuffer(Buffer, (uint)offset, (uint)target.Length);
        data.AsSpan(0, Math.Min(data.Length, target.Length)).CopyTo(target);
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
        Graphics.DeferDispose(Buffer);
    }
}
