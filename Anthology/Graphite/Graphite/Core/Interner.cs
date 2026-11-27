using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Prowl.Graphite;

internal sealed class Interner
{
    private readonly ConcurrentDictionary<string, int> _forward = new();
    private readonly List<string> _names = new();
    private readonly object _lock = new();

    public int Intern(string key)
    {
        if (_forward.TryGetValue(key, out int value))
            return value;

        lock (_lock)
        {
            if (_forward.TryGetValue(key, out value))
                return value;

            _names.Add(key);
            value = _names.Count;
            _forward[key] = value;
            return value;
        }
    }

    public bool TryGetKey(int value, out string key)
    {
        lock (_lock)
        {
            if (value >= 1 && value <= _names.Count)
            {
                key = _names[value - 1];
                return true;
            }
        }
        key = default!;
        return false;
    }
}
