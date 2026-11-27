// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace SoftwareRasterizer.Rasterizer;

public class VertexBuffer(object[] attributes, int vertexCount, int[] indices)
{
    public object[] VertexAttributes = attributes;
    public int VertexCount = vertexCount;
    public int[] Indices = indices;
}
