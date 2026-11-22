// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.CSG;

/// <summary>
/// How a generated brush or group was made, so the editor can change its size and settings and build it again. Sits on
/// the object the generator created, whose position and rotation are the shape's frame.
/// </summary>
[AddComponentMenu("CSG/CSG Shape")]
public sealed class CSGShape : Component
{
    public CSGGeneratorKind Kind;
    public CSGGeneratorSettings Settings;

    /// <summary>The size of the box the shape fills.</summary>
    public Float3 Size;

    /// <summary>A polygon's corners in the shape's frame, and how far it was raised.</summary>
    public Float2[] Outline = [];
    public float Height;
}
