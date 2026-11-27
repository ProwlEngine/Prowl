// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.OrigamiUI;

/// <summary>
/// A floating window that draws on top of the root dock space.
/// Contains a DockNode (which can be a single tab leaf or a full split tree).
/// </summary>
public class FloatingWindow
{
    public DockNode Node;
    public Float2 Position;
    public Float2 Size;

    public FloatingWindow(DockNode node, Float2 position, Float2 size)
    {
        Node = node;
        Position = position;
        Size = size;
    }
}
