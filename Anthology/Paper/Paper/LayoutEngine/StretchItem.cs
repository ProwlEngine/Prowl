// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.PaperUI.LayoutEngine;

internal class StretchItem
{
    public enum ItemTypes { Before, Size, After }

    public int Index { get; set; }
    public float Factor { get; set; }
    public ItemTypes ItemType { get; set; }
    public float Violation { get; set; } = 0f;
    public float Computed { get; set; } = 0f;
    public bool Frozen { get; set; } = false;
    public float Min { get; set; }
    public float Max { get; set; }

    /// <summary>The size the item had before any distribution. Shrinking measures down from it, and
    /// weights how much an item gives up by it, so a wide item yields more than a narrow one.</summary>
    public float Base { get; set; }

    public StretchItem(int index, float factor, ItemTypes itemType, float min, float max)
        => Reset(index, factor, itemType, min, max);

    /// <summary>Puts the instance back to how a fresh one would look, so it can serve another item.</summary>
    public void Reset(int index, float factor, ItemTypes itemType, float min, float max)
    {
        Index = index;
        Factor = factor;
        ItemType = itemType;
        Violation = 0f;
        Computed = 0f;
        Frozen = false;
        Min = min;
        Max = max;
        Base = 0f;
    }
}
