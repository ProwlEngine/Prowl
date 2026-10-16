// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public class ComputeShaderTests
{
    private const string TwoKernels = """
        #pragma kernel Double 64
        #pragma kernel Clear 8 8 2

        layout(std430) buffer Values { float values[]; };

        void Double() { values[gl_GlobalInvocationID.x] *= 2.0; }
        void Clear() { values[gl_GlobalInvocationID.x] = 0.0; }
        """;

    [Fact]
    public void Kernels_AreFoundByName_WithTheirGroupSizes()
    {
        ComputeShader shader = ComputeShader.FromSource("Test", TwoKernels);

        int doubled = shader.FindKernel("Double");
        int cleared = shader.FindKernel("Clear");
        Assert.NotEqual(doubled, cleared);
        Assert.True(shader.HasKernel("Clear"));
        Assert.False(shader.HasKernel("Missing"));
        Assert.Throws<ArgumentException>(() => shader.FindKernel("Missing"));

        shader.GetKernelThreadGroupSizes(doubled, out uint x, out uint y, out uint z);
        Assert.Equal((64u, 1u, 1u), (x, y, z));
        shader.GetKernelThreadGroupSizes(cleared, out x, out y, out z);
        Assert.Equal((8u, 8u, 2u), (x, y, z));
    }

    [Fact]
    public void KernelSource_DeclaresTheGroupSize_AndCallsTheKernelFromMain()
    {
        ComputeShader shader = ComputeShader.FromSource("Test", TwoKernels);

        string source = shader.KernelSource(shader.FindKernel("Clear"));

        Assert.Contains("layout(local_size_x = 8, local_size_y = 8, local_size_z = 2) in;", source);
        Assert.Contains("void main() { Clear(); }", source);
        Assert.Contains("#define KERNEL_Clear", source);
        Assert.DoesNotContain("#pragma kernel", source);
    }

    [Fact]
    public void ASourceWithoutKernels_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => ComputeShader.FromSource("Empty", "void Nothing() { }"));
    }

    [Fact]
    public void ComputeBuffer_ChecksItsShapeAndRanges()
    {
        Assert.Throws<ArgumentException>(() => new ComputeBuffer(10, 6));
        Assert.Throws<ArgumentException>(() => new ComputeBuffer(0, 4));

        using var buffer = new ComputeBuffer(4, 8);
        Assert.Equal(4, buffer.Count);
        Assert.Equal(8, buffer.Stride);
        Assert.Throws<ArgumentException>(() => buffer.SetData(new float[10]));
        Assert.Throws<ArgumentException>(() => buffer.SetData(new float[4], bufferStartIndex: 3));
        buffer.SetData(new float[8]);
    }
}
