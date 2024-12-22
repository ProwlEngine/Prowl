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
        List.Add(tag);
        tag.Parent = this;
    }

    public void ListRemove(EchoObject tag)
    {
        if (TagType != EchoType.List)
            throw new System.InvalidOperationException("Cannot remove tag from non-list tag");
        List.Remove(tag);
        tag.Parent = null;
    }
}
