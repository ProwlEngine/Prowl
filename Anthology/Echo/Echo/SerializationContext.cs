// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

public class SerializationContext
{
    private class NullKey { }

    public Dictionary<object, int> objectToId = new(ReferenceEqualityComparer.Instance);
    public Dictionary<int, object> idToObject = new();
    public int nextId = 1;

    private int dependencyCounter = 0;
    public HashSet<Guid> dependencies = new();

    public SerializationContext()
    {
        objectToId.Clear();
        objectToId.Add(new NullKey(), 0);
        idToObject.Clear();
        idToObject.Add(0, new NullKey());
        nextId = 1;
        dependencies.Clear();
    }

    public void AddDependency(Guid guid)
    {
        if (dependencyCounter > 0)
            dependencies.Add(guid);
        else throw new InvalidOperationException("Cannot add a dependency outside of a BeginDependencies/EndDependencies block.");
    }

    public void BeginDependencies()
    {
        dependencyCounter++;
    }

    public HashSet<Guid> EndDependencies()
    {
        dependencyCounter--;
        if (dependencyCounter == 0)
            return dependencies;
        return new();
    }
}
