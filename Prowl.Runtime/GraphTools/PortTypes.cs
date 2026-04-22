// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// Central rules for whether two ports' C# data types can be wired together. Shared
/// between editor port-drag validation, the node-creation menu's "compatible ports"
/// filter, and any future graph traversal that needs to know whether a connection is
/// legal. The shader compiler does the actual textual cast — this just decides whether
/// the cast is allowed at all.
/// </summary>
public static class PortTypes
{
    /// <summary>True for scalar/vector numeric types that the shader compiler can freely
    /// promote between (Float ↔ Vec2/3/4 ↔ Color). Samplers, matrices, and reference
    /// types are excluded — they need an exact-type match.</summary>
    public static bool IsNumeric(Type t)
        => t == typeof(float) || t == typeof(int) || t == typeof(bool)
        || t == typeof(Float2) || t == typeof(Float3) || t == typeof(Float4)
        || t == typeof(Color);

    /// <summary>
    /// Decide whether a port of type <paramref name="a"/> may connect to a port of type
    /// <paramref name="b"/>. Direction-agnostic — caller is expected to enforce that
    /// one side is an Output and the other is an Input.
    /// </summary>
    public static bool AreCompatible(Type a, Type b)
    {
        if (a == b) return true;
        if (a == typeof(object) || b == typeof(object)) return true;
        if (IsNumeric(a) && IsNumeric(b)) return true;
        return false;
    }
}
