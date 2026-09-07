// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.PaperUI.LayoutEngine;

internal sealed class LayoutArena
{
    private readonly List<ChildElementInfo> _childInfo = new();
    private int _childInfoUsed;

    private readonly List<StretchItem> _stretchItems = new();
    private int _stretchItemsUsed;

    private readonly List<List<int>> _intLists = new();
    private int _intListsUsed;

    private readonly List<List<ChildElementInfo>> _childInfoLists = new();
    private int _childInfoListsUsed;

    private readonly List<List<StretchItem>> _stretchLists = new();
    private int _stretchListsUsed;

    private readonly List<List<(int Start, int Count, float Used, float Cross)>> _lineLists = new();
    private int _lineListsUsed;

    public void Reset()
    {
        _childInfoUsed = 0;
        _stretchItemsUsed = 0;
        _intListsUsed = 0;
        _childInfoListsUsed = 0;
        _stretchListsUsed = 0;
        _lineListsUsed = 0;
    }

    public ChildElementInfo ChildInfo(ElementHandle element)
    {
        if (_childInfoUsed == _childInfo.Count)
            _childInfo.Add(new ChildElementInfo(element));

        ChildElementInfo info = _childInfo[_childInfoUsed++];
        info.Reset(element);
        return info;
    }

    public StretchItem Stretch(int index, float factor, StretchItem.ItemTypes itemType, float min, float max)
    {
        if (_stretchItemsUsed == _stretchItems.Count)
            _stretchItems.Add(new StretchItem(index, factor, itemType, min, max));

        StretchItem item = _stretchItems[_stretchItemsUsed++];
        item.Reset(index, factor, itemType, min, max);
        return item;
    }

    public List<int> IntList() => Take(_intLists, ref _intListsUsed);

    public List<ChildElementInfo> ChildInfoList() => Take(_childInfoLists, ref _childInfoListsUsed);

    public List<StretchItem> StretchList() => Take(_stretchLists, ref _stretchListsUsed);

    public List<(int Start, int Count, float Used, float Cross)> LineList()
        => Take(_lineLists, ref _lineListsUsed);

    private static List<T> Take<T>(List<List<T>> pool, ref int used)
    {
        if (used == pool.Count)
            pool.Add(new List<T>());

        List<T> list = pool[used++];
        list.Clear();
        return list;
    }
}
