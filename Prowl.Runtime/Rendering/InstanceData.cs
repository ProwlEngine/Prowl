// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Per-instance data for GPU instanced rendering.
/// This struct is uploaded to a GPU buffer for instanced draw calls.
/// Layout matches standard shader instance attributes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InstanceData
{
    // Transform (4x4 matrix broken into 4 vec4s for GPU compatibility)
    public Float4 ModelRow0;
    public Float4 ModelRow1;
    public Float4 ModelRow2;
    public Float4 ModelRow3;

    // Per-instance color tint (RGBA as floats for GPU compatibility)
    public Color Color;

    // Custom data (can be used for anything - UV offsets, size, etc.)
    public Float4 CustomData;

    /// <summary>
    /// Creates instance data from a transform matrix.
    /// </summary>
    public InstanceData(Float4x4 model) : this(model, Color.White, Float4.Zero) { }

    /// <summary>
    /// Creates instance data from a transform matrix and color as Float4.
    /// </summary>
    public InstanceData(Float4x4 model, Color color) : this(model, color, Float4.Zero) { }

    /// <summary>
    /// Creates instance data with all properties.
    /// </summary>
    public InstanceData(Float4x4 model, Color color, Float4 customData)
    {
        // Float4x4 stores columns (c0, c1, c2, c3)
        // GLSL mat4() constructor also takes columns, so pass them directly (no transpose needed)
        ModelRow0 = model.c0;
        ModelRow1 = model.c1;
        ModelRow2 = model.c2;
        ModelRow3 = model.c3;
        Color = color;
        CustomData = customData;
    }

    /// <summary>
    /// Gets the transform matrix from this instance data.
    /// </summary>
    public readonly Float4x4 GetMatrix()
    {
        // ModelRow0-3 are actually columns, not rows (despite the name)
        return new Float4x4(ModelRow0, ModelRow1, ModelRow2, ModelRow3);
    }

    /// <summary>
    /// Size of the instance data structure in bytes.
    /// </summary>
    public static int SizeInBytes => Marshal.SizeOf<InstanceData>();
}
