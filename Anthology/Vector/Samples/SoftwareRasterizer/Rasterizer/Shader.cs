// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

using Prowl.Vector;

using SoftwareRasterizer.Rasterizer.Engines;

namespace SoftwareRasterizer.Rasterizer;

public abstract class Shader
{

    protected object[] _vertexAttributes;


    public struct ShaderOutput
    {
        public Float4 GlPosition;
        public Float4[] Varyings;
    }

    public struct FragmentOutput
    {
        public Float4 GlFragColor;
        public float? GlFragDepth;
    }

    public abstract bool CanWriteDepth { get; }
    public abstract void Prepare();
    public abstract ShaderOutput VertexShader(int vertexIndex);
    public abstract FragmentOutput FragmentShader(Float4[] varyings, FragmentContext context);


    public void BindVertexAttributes(object[] vertexBufferAttributes)
    {
        _vertexAttributes = vertexBufferAttributes;
    }

    // Helper methods for type-safe access
    protected T GetVertexAttribute<T>(int vertexIndex, int location)
    {
        return ((T[])_vertexAttributes[location])[vertexIndex];
    }

    protected T[] GetVertexAttributeArray<T>(int location)
    {
        return (T[])_vertexAttributes[location];
    }
}
