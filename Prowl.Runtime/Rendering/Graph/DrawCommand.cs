// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// The default per-object draw payload a culler emits and a pass consumes. Minimal for now; the
/// concrete forward-render port will grow this (per-object properties, submesh, instance data).
/// </summary>
public struct DrawCommand
{
    public Mesh Mesh;
    public Material Material;
    public Float4x4 Model;
    public int Layer;
}
