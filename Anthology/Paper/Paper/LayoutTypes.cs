namespace Prowl.PaperUI;
/// <summary>Alignment across a row, column, grid cell or overlay.</summary>
public enum LayoutAlignment
{
    Start,
    Center,
    End,
    Stretch
}

/// <summary>Distribution of unused space along the layout direction.</summary>
public enum LayoutJustification
{
    Start,
    Center,
    End,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
    /// <summary>Grow every item on a line so the line is filled. Wrapping containers only.</summary>
    Fill
}

/// <summary>Layout operations and cache hits from the last frame. Measurements may count a node more than once.</summary>
public readonly record struct LayoutStatistics(int MeasuredNodes, int ArrangedNodes, int MeasureCacheHits, int ArrangeCacheHits);
