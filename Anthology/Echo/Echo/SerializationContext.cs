// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

public enum TypeMode 
{
    /// <summary> Always include type information. </summary>
    Aggressive,
    /// <summary> Include type information only when necessary (When the type is not the expected type) </summary>
    Auto,
    /// <summary> Never include type information. (This may cause deserialization to fail if the type is not the expected type) </summary>
    None
}

public class SerializationContext
{
    private class NullKey { }

    internal TypeMode TypeMode = TypeMode.Auto;

    public Dictionary<object, int> objectToId = new(ReferenceEqualityComparer.Instance);
    public Dictionary<int, object> idToObject = new();
    public int nextId = 1;

    public SerializationContext(TypeMode typeMode = TypeMode.Auto)
    {
        TypeMode = typeMode;
        //objectToId.Clear(); // Not sure why we cleared these?
        objectToId.Add(new NullKey(), 0);
        //idToObject.Clear();
        idToObject.Add(0, new NullKey());
        nextId = 1;
    }
}
