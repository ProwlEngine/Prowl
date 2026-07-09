// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// Implement on a Node subclass to opt into user-resizable width/height. The default
/// renderer draws a grip in the bottom-right corner; the editor handles the drag and
/// writes the new size back via <see cref="SetSize"/>.
/// </summary>
/// <remarks>
/// The node owns its own size storage implementers expose serialised width/height
/// fields (or whatever shape they want). Returning Float2.Zero means "use the default
/// auto-fit size"; returning a positive value forces that explicit size.
/// </remarks>
public interface IResizableNode
{
    /// <summary>Current explicit size of the node in graph-space units. Return
    /// <see cref="Float2.Zero"/> to use the default renderer's auto-fit.</summary>
    Float2 GetSize();

    /// <summary>Persist the new size after a resize drag. Implementers should clamp
    /// to a sensible minimum.</summary>
    void SetSize(Float2 size);

    /// <summary>Smallest size the editor will allow a resize drag to produce. Renderer
    /// applies this clamp before calling <see cref="SetSize"/>.</summary>
    Float2 MinSize { get; }
}
