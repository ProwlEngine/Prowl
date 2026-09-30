// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class MeshDefaultStreamTests
{
    private static readonly Float3[] s_triangle = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)];

    private static VertexLayoutDescription Layout(string name, VertexElementFormat format, uint stride)
        => new(0, stride, new VertexElementDescription(name, format));

    private static float[] ReadFloats(DeviceBuffer buffer, uint offset, int count)
    {
        GraphicsDevice device = Graphics.Device;
        uint bytes = (uint)count * 4;
        DeviceBuffer staging = device.ResourceFactory.CreateBuffer(new BufferDescription(bytes, BufferUsage.Staging));

        TransferCommandBuffer cmd = device.ResourceFactory.CreateTransferCommandBuffer();
        cmd.Begin();
        cmd.CopyBuffer(buffer, offset, staging, 0, bytes);
        cmd.End();
        device.SubmitAndWait(cmd);
        cmd.Dispose();

        float[] result = new float[count];
        MappedResourceView<float> view = device.Map<float>(staging, MapMode.Read);
        try
        {
            for (int i = 0; i < count; i++)
                result[i] = view[i];
        }
        finally
        {
            device.Unmap(staging);
        }

        staging.Dispose();
        return result;
    }

    private static float[] ResolveFloats(Mesh mesh, string name, VertexElementFormat format, uint stride, int floatCount)
    {
        VertexLayoutDescription layout = Layout(name, format, stride);
        ((IVertexSource)mesh).ResolveSlot(0, in layout, out VertexBinding binding);
        return ReadFloats(binding.Buffer, binding.Offset, floatCount);
    }

    [Fact]
    public void ResolveSlot_MissingColor0_BindsWhite()
    {
        if (Graphics.Device == null)
            return;

        using Mesh mesh = new() { Vertices = s_triangle, Indices = [0, 1, 2] };
        Assert.False(mesh.HasColors);

        float[] colors = ResolveFloats(mesh, "COLOR0", VertexElementFormat.Float4, 16, s_triangle.Length * 4);
        Assert.All(colors, c => Assert.Equal(1f, c));
    }

    [Fact]
    public void ResolveSlot_MissingTangent_BindsZero()
    {
        if (Graphics.Device == null)
            return;

        using Mesh mesh = new() { Vertices = s_triangle, Indices = [0, 1, 2] };

        float[] tangents = ResolveFloats(mesh, "TANGENT0", VertexElementFormat.Float4, 16, s_triangle.Length * 4);
        Assert.All(tangents, t => Assert.Equal(0f, t));
    }

    [Fact]
    public void ResolveSlot_WithColors_BindsMeshColors()
    {
        if (Graphics.Device == null)
            return;

        using Mesh mesh = new()
        {
            Vertices = s_triangle,
            Indices = [0, 1, 2],
            Colors = [new Color(1f, 0f, 0f, 1f), new Color(0f, 1f, 0f, 1f), new Color(0f, 0f, 1f, 0.5f)],
        };
        Assert.True(mesh.HasColors);

        float[] colors = ResolveFloats(mesh, "COLOR0", VertexElementFormat.Float4, 16, s_triangle.Length * 4);
        float[] expected = [1f, 0f, 0f, 1f, 0f, 1f, 0f, 1f, 0f, 0f, 1f, 0.5f];
        Assert.Equal(expected, colors);
    }
}
