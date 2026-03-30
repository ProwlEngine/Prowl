using Prowl.Vector;

namespace Prowl.Editor.Docking;

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
