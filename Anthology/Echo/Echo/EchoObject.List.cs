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

        OnPropertyChanged(new EchoChangeEventArgs(
            this, tag, null, tag.Value, ChangeType.ListTagAdded));
    }

    public void ListRemove(EchoObject tag)
    {
        if (TagType != EchoType.List)
            throw new System.InvalidOperationException("Cannot remove tag from non-list tag");

        int removedIndex = List.IndexOf(tag);
        if (removedIndex != -1)
        {
            List.RemoveAt(removedIndex);

            // Track removal before updating indices
            OnPropertyChanged(new EchoChangeEventArgs(
                this, tag, tag.Value, null, ChangeType.ListTagRemoved));

            tag.Parent = null;
            tag.ListIndex = null;

            // Update indices and track moves
            for (int i = removedIndex; i < List.Count; i++)
            {
                var movedItem = List[i];
                var oldIndex = movedItem.ListIndex;
                movedItem.ListIndex = i;

                OnPropertyChanged(new EchoChangeEventArgs(
                    this, movedItem, oldIndex, i, ChangeType.ListTagMoved));
            }
        }
    }
}
