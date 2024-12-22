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

        if (tag.Parent != null)
            throw new System.InvalidOperationException("Tag is already in a list, did you mean to clone it?");

        List.Add(tag);
        tag.Parent = this;
        tag.ListIndex = List.Count - 1;
    }

    public void ListRemove(EchoObject tag)
    {
        if (TagType != EchoType.List)
            throw new System.InvalidOperationException("Cannot remove tag from non-list tag");

        int removedIndex = List.IndexOf(tag);
        if (removedIndex != -1)
        {
            List.RemoveAt(removedIndex);
            tag.Parent = null;
            tag.ListIndex = null;

            // Update indices for all items after the removed one
            for (int i = removedIndex; i < List.Count; i++)
            {
                List[i].ListIndex = i;
            }
        }
    }
}
