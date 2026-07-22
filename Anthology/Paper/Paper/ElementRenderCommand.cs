// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI;

/// <summary>
/// Represents a drawable UI element command.
/// </summary>
internal class ElementRenderCommand
{
    public LayoutEngine.ElementHandle Element { get; set; }
    public Action<Canvas, Rect>? RenderAction { get; set; }
}
