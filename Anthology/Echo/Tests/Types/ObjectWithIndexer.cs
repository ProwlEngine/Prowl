// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Tests.Types;

public class ObjectWithIndexer
{
    private readonly Dictionary<string, object> _storage = new();
    public object this[string key] {
        get => _storage[key];
        set => _storage[key] = value;
    }
}
