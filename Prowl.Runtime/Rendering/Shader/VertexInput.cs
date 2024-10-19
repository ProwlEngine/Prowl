// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Veldrid;

namespace Prowl.Runtime.Rendering;

public readonly struct VertexInput : IEquatable<VertexInput>
{
    public readonly string semantic;
    public readonly VertexElementFormat format;

    public VertexInput(string semantic, VertexElementFormat format)
    {
        this.semantic = semantic;
        this.format = format;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not VertexInput other)
            return false;

        return Equals(other);
    }

    public bool Equals(VertexInput other)
        => semantic == other.semantic;

    public override int GetHashCode()
        => semantic.GetHashCode();
}
