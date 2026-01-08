namespace Prowl.Wicked.Network;

using System.Reflection;
using Prowl.Wicked.Core;

/// <summary>
/// Registry for all EntityBehaviour types in the application.
/// Behaviours are discovered at initialization and assigned stable indices
/// that are consistent between server and client.
/// </summary>
public static class BehaviourRegistry
{
    private static readonly Dictionary<Type, byte> _typeToIndex = new();
    private static readonly Dictionary<byte, Type> _indexToType = new();
    private static bool _initialized;

    /// <summary>
    /// Returns true if the registry has been initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Gets the number of registered behaviour types.
    /// </summary>
    public static int Count => _typeToIndex.Count;

    /// <summary>
    /// Initializes the behaviour registry by scanning all loaded assemblies.
    /// Must be called before any networking operations.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        _typeToIndex.Clear();
        _indexToType.Clear();

        // Find all EntityBehaviour subclasses in all loaded assemblies
        var behaviourTypes = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // Skip system assemblies for performance
            var name = assembly.GetName().Name;
            if (name == null || name.StartsWith("System") || name.StartsWith("Microsoft") ||
                name.StartsWith("mscorlib") || name.StartsWith("netstandard"))
                continue;

            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsClass && !type.IsAbstract && typeof(EntityBehaviour).IsAssignableFrom(type))
                    {
                        behaviourTypes.Add(type);
                    }
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // Some assemblies may fail to load types, skip them
            }
        }

        // Sort alphabetically by full name for deterministic ordering
        behaviourTypes.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));

        // Check limit
        if (behaviourTypes.Count > 255)
        {
            throw new InvalidOperationException(
                $"Too many EntityBehaviour types found ({behaviourTypes.Count}). " +
                $"Maximum supported is 255.");
        }

        // Assign indices
        for (int i = 0; i < behaviourTypes.Count; i++)
        {
            var type = behaviourTypes[i];
            _typeToIndex[type] = (byte)i;
            _indexToType[(byte)i] = type;
        }

        _initialized = true;

        Console.WriteLine($"BehaviourRegistry: Registered {behaviourTypes.Count} behaviour types");
    }

    /// <summary>
    /// Gets the index for a behaviour type.
    /// </summary>
    public static byte GetIndex(Type behaviourType)
    {
        if (!_initialized)
            throw new InvalidOperationException("BehaviourRegistry has not been initialized. Call NetworkManager.Initialize() first.");

        if (_typeToIndex.TryGetValue(behaviourType, out var index))
            return index;

        throw new ArgumentException($"Unknown behaviour type: {behaviourType.FullName}. " +
            "Make sure the type is a concrete EntityBehaviour subclass.");
    }

    /// <summary>
    /// Gets the index for a behaviour type.
    /// </summary>
    public static byte GetIndex<T>() where T : EntityBehaviour
    {
        return GetIndex(typeof(T));
    }

    /// <summary>
    /// Gets the behaviour type for an index.
    /// </summary>
    public static Type GetType(byte index)
    {
        if (!_initialized)
            throw new InvalidOperationException("BehaviourRegistry has not been initialized. Call NetworkManager.Initialize() first.");

        if (_indexToType.TryGetValue(index, out var type))
            return type;

        throw new ArgumentException($"Unknown behaviour index: {index}");
    }

    /// <summary>
    /// Creates an instance of a behaviour by its index.
    /// </summary>
    public static EntityBehaviour CreateInstance(byte index)
    {
        var type = GetType(index);
        return (EntityBehaviour)Activator.CreateInstance(type)!;
    }

    /// <summary>
    /// Tries to get the index for a behaviour type.
    /// Returns false if the type is not registered.
    /// </summary>
    public static bool TryGetIndex(Type behaviourType, out byte index)
    {
        if (!_initialized)
        {
            index = 0;
            return false;
        }

        return _typeToIndex.TryGetValue(behaviourType, out index);
    }

    /// <summary>
    /// Gets all registered behaviour types and their indices.
    /// </summary>
    public static IEnumerable<(byte Index, Type Type)> GetAllTypes()
    {
        foreach (var kvp in _indexToType.OrderBy(x => x.Key))
        {
            yield return (kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Resets the registry. Used for testing.
    /// </summary>
    internal static void Reset()
    {
        _typeToIndex.Clear();
        _indexToType.Clear();
        _initialized = false;
    }
}
