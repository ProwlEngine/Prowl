using System;
using System.Collections.Generic;

namespace Prowl.Editor.Docking;

public enum SplitDirection { Horizontal, Vertical }

public enum DockZone { None, Top, Bottom, Left, Right, Center }

public class DockNode
{
    // Leaf node data
    public List<DockPanel>? Tabs;
    public int ActiveTabIndex;

    // Internal node data
    public SplitDirection Direction;
    public float SplitRatio;
    public DockNode? ChildA;
    public DockNode? ChildB;

    // Parent reference (set during draw traversal)
    public DockNode? Parent;

    public bool IsLeaf => Tabs != null;

    public static DockNode Leaf(params DockPanel[] panels)
    {
        return new DockNode { Tabs = new List<DockPanel>(panels), ActiveTabIndex = 0 };
    }

    public static DockNode Split(SplitDirection dir, float ratio, DockNode a, DockNode b)
    {
        return new DockNode { Direction = dir, SplitRatio = ratio, ChildA = a, ChildB = b };
    }

    public DockPanel? RemoveTab(int index)
    {
        if (Tabs == null || index < 0 || index >= Tabs.Count) return null;
        var panel = Tabs[index];
        Tabs.RemoveAt(index);
        if (ActiveTabIndex >= Tabs.Count)
            ActiveTabIndex = Math.Max(0, Tabs.Count - 1);
        return panel;
    }

    public void InsertTab(DockPanel panel, int index = -1)
    {
        Tabs ??= new List<DockPanel>();
        if (index < 0 || index >= Tabs.Count)
            Tabs.Add(panel);
        else
            Tabs.Insert(index, panel);
        ActiveTabIndex = Tabs.IndexOf(panel);
    }

    /// <summary>
    /// Replace a child node reference. Used when splitting a leaf.
    /// </summary>
    public bool ReplaceChild(DockNode target, DockNode replacement)
    {
        if (ChildA == target) { ChildA = replacement; return true; }
        if (ChildB == target) { ChildB = replacement; return true; }
        return false;
    }
}
