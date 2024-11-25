// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

public sealed partial class Echo
{
    public List<Echo> List => (Value as List<Echo>)!;

    public Echo this[int tagIdx]
    {
        get { return Get(tagIdx); }
        set { List[tagIdx] = value; }
    }

    public Echo Get(int tagIdx)
    {
        if (TagType != PropertyType.List)
            throw new System.InvalidOperationException("Cannot get tag from non-list tag");
        return List[tagIdx];
    }

    public void ListAdd(Echo tag)
    {
        if (TagType != PropertyType.List)
            throw new System.InvalidOperationException("Cannot add tag to non-list tag");
        List.Add(tag);
        tag.Parent = this;
    }

    public void ListRemove(Echo tag)
    {
        if (TagType != PropertyType.List)
            throw new System.InvalidOperationException("Cannot remove tag from non-list tag");
        List.Remove(tag);
        tag.Parent = null;
    }
}
