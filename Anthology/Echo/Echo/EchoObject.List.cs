// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

public sealed partial class EchoObject
{
    public List<EchoObject> List => (Value as List<EchoObject>)!;

    public EchoObject this[int tagIdx]
    {
        get { return Get(tagIdx); }
        set { List[tagIdx] = value; }
    }

    public EchoObject Get(int tagIdx)
    {
        if (TagType != EchoType.List)
            throw new System.InvalidOperationException("Cannot get tag from non-list tag");
        return List[tagIdx];
    }

    public void ListAdd(EchoObject tag)
    {
        if (TagType != EchoType.List)
            throw new System.InvalidOperationException("Cannot add tag to non-list tag");

        if (tag.Parent is not null)
            throw new System.InvalidOperationException("Tag is already in a list, did you mean to clone it?");

        List.Add(tag);
        tag.Parent = this;
        tag.ListIndex = List.Count - 1;
    }

    public void ListInsert(int index, EchoObject tag)
    {
        if (TagType != EchoType.List)
            throw new System.InvalidOperationException("Cannot insert tag into non-list tag");

        if (tag.Parent is not null)
            throw new System.InvalidOperationException("Tag already has a parent, did you mean to clone it?");

        if (index < 0 || index > List.Count)
            throw new System.ArgumentOutOfRangeException(nameof(index));

        List.Insert(index, tag);
        tag.Parent = this;

        // Update indices for all items from insertion point
        for (int i = index; i < List.Count; i++)
            List[i].ListIndex = i;
    }

    public void ListRemove(EchoObject tag)
    {
        if (TagType != EchoType.List)
            throw new System.InvalidOperationException("Cannot remove tag from non-list tag");

        int removedIndex = List.IndexOf(tag);
        if (removedIndex != -1)
            ListRemoveAt(removedIndex);
    }

    public void ListRemoveAt(int index)
    {
        if (TagType != EchoType.List)
            throw new System.InvalidOperationException("Cannot remove tag from non-list tag");

        if (index < 0 || index >= List.Count)
            throw new System.ArgumentOutOfRangeException(nameof(index));

        var tag = List[index];
        List.RemoveAt(index);

        tag.Parent = null;
        tag.ListIndex = null;

        // Update indices for all items after removal point
        for (int i = index; i < List.Count; i++)
            List[i].ListIndex = i;
    }

    public void ListClear()
    {
        if (TagType != EchoType.List)
            throw new System.InvalidOperationException("Cannot clear non-list tag");

        foreach (var tag in List)
        {
            tag.Parent = null;
            tag.ListIndex = null;
        }

        List.Clear();
    }
}
