// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

namespace Prowl.Editor.Docking;

public enum DockZone
{
    Left,
    Right,
    Top,
    Bottom,
    Center
}

public struct DockPlacement
{
    public DockNode Leaf;
    public DockZone Zone;
    public Vector2[] PolygonVerts;

    public static implicit operator bool(DockPlacement placement)
    {
        return placement.Leaf != null;
    }
}

public class DockNode
{
    public enum NodeType
    {
        SplitVertical,
        SplitHorizontal,
        Leaf
    }

    public NodeType Type { get; set; } = NodeType.Leaf;
    public DockNode[] Child { get; } = new DockNode[2];
    public List<EditorWindow> LeafWindows { get; internal set; } = new List<EditorWindow>();
    public int WindowNum { get; set; } = 0;
    public Vector2 Mins { get; set; }
    public Vector2 Maxs { get; set; }
    public double SplitDistance { get; set; } = 0.5f;

    public DockNode? TraceLeaf(double x, double y)
    {
        if (Rect.CreateFromMinMax(Mins, Maxs).Contains(new Vector2(x, y)))
        {
            if (Type == NodeType.Leaf)
                return this;

            for (int i = 0; i < 2; ++i)
            {
                DockNode? leaf = Child[i].TraceLeaf(x, y);
                if (leaf != null)
                    return leaf;
            }
        }

        return null;
    }

    public DockNode? TraceSeparator(double x, double y)
    {
        if (Type == NodeType.Leaf)
        {
            return null;
        }

        if (!Rect.CreateFromMinMax(Mins, Maxs).Contains(new Vector2(x, y)))
        {
            return null;
        }

        const double splitterWidth = 8;
        Vector2 bmins, bmaxs;
        GetSplitterBounds(out bmins, out bmaxs, splitterWidth);

        if (Rect.CreateFromMinMax(bmins, bmaxs).Contains(new Vector2(x, y)))
        {
            return this;
        }

        DockNode? node = Child[0].TraceSeparator(x, y);
        if (node != null)
            return node;

        return Child[1].TraceSeparator(x, y);
    }

    public void UpdateRecursive(Vector2 mins, Vector2 maxs)
    {
        UpdateRecursive(mins.x, mins.y, maxs.x - mins.x, maxs.y - mins.y);
    }

    public void UpdateRecursive(double x, double y, double w, double h)
    {
        Mins = new Vector2(x, y);
        Maxs = new Vector2(x + w, y + h);

        if (Type == NodeType.Leaf)
        {
            if (LeafWindows.Count > 0)
            {
                EditorWindow dockWidget = LeafWindows[WindowNum];

                dockWidget.DockPosition = new Vector2(x, y);
                dockWidget.DockSize = new Vector2(w, h);

                //dockWidget.MeasureLayout(false, false, new Vector2(w, h));
            }
            return;
        }

        if (Type == NodeType.SplitVertical)
        {
            double d = Math.Floor(SplitDistance * w);

            // Left
            Child[0].UpdateRecursive(x, y, d, h);

            // Right
            Child[1].UpdateRecursive(x + d, y, w - d, h);
            return;
        }

        if (Type == NodeType.SplitHorizontal)
        {
            double d = Math.Floor(SplitDistance * h);

            // Top
            Child[0].UpdateRecursive(x, y, w, d);

            // Bottom
            Child[1].UpdateRecursive(x, y + d, w, h - d);
            return;
        }
    }

    public DockNode? FindParent(DockNode node)
    {
        if (Type == NodeType.Leaf)
            return null;

        for (int i = 0; i < 2; ++i)
            if (Child[i] == node)
                return this;

        DockNode? n = Child[0].FindParent(node);
        if (n != null)
            return n;

        return Child[1].FindParent(node);
    }

    public void GetSplitterBounds(out Vector2 bmins, out Vector2 bmaxs, double splitterWidth)
    {
        double splitHalfWidth = splitterWidth * 0.5;

        if (Type == NodeType.SplitVertical)
        {
            double d = MathD.Lerp(Mins.x, Maxs.x, SplitDistance);

            bmins.x = d - splitHalfWidth;
            bmaxs.x = d + splitHalfWidth;

            bmins.y = Mins.y;
            bmaxs.y = Maxs.y;
        }
        else if (Type == NodeType.SplitHorizontal)
        {
            double d = MathD.Lerp(Mins.y, Maxs.y, SplitDistance);

            bmins.y = d - splitHalfWidth;
            bmaxs.y = d + splitHalfWidth;

            bmins.x = Mins.x;
            bmaxs.x = Maxs.x;
        }
        else
        {
            bmins = new Vector2();
            bmaxs = new Vector2();
        }
    }

    public void GetWindows(List<EditorWindow> widgetList)
    {
        if (Type == NodeType.Leaf)
        {
            widgetList.AddRange(LeafWindows);
            return;
        }

        Child[0].GetWindows(widgetList);
        Child[1].GetWindows(widgetList);
    }
}
