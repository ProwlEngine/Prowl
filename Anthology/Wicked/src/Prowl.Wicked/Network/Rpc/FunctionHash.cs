namespace Prowl.Wicked.Network.Rpc;

/// <summary>
/// Computes stable hash codes for RPC function names and message types.
/// Uses a 16-bit hash (ushort) to minimize network bandwidth while maintaining low collision probability.
/// Like Mirror's StableHashCode extension methods.
/// </summary>
public static class FunctionHash
{
    /// <summary>
    /// Computes a stable 16-bit hash for a string using XOR folding.
    /// Gets the 32-bit FNV-1a hash, then folds the highest 16 bits into the lowest 16 bits.
    /// This creates a more uniform 16-bit hash distribution.
    /// See: http://www.isthe.com/chongo/tech/comp/fnv/ "xor-folding" section.
    /// </summary>
    /// <param name="text">The string to hash</param>
    /// <returns>A 16-bit hash code</returns>
    public static ushort GetStableHash16(string text)
    {
        uint hash32 = GetStableHash32(text);
        // XOR fold: take the highest 16 bits and XOR them into the lowest 16 bits
        return (ushort)((hash32 >> 16) ^ hash32);
    }

    /// <summary>
    /// Computes a stable 32-bit hash for a string.
    /// Uses FNV-1a algorithm for good distribution.
    /// Platform independent, deterministic across runs.
    /// </summary>
    public static uint GetStableHash32(string text)
    {
        unchecked
        {
            // FNV-1a algorithm
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash = (hash ^ c) * 16777619;
            }
            return hash;
        }
    }

    /// <summary>
    /// Computes the function hash for a behaviour type and method name.
    /// </summary>
    /// <param name="behaviourTypeName">The full name of the behaviour type</param>
    /// <param name="methodName">The method name</param>
    /// <returns>A 16-bit hash code</returns>
    public static ushort ComputeHash(string behaviourTypeName, string methodName)
    {
        return GetStableHash16($"{behaviourTypeName}.{methodName}");
    }

    /// <summary>
    /// Computes the function hash for a behaviour type and method name.
    /// </summary>
    public static ushort ComputeHash(Type behaviourType, string methodName)
    {
        return ComputeHash(behaviourType.FullName ?? behaviourType.Name, methodName);
    }
}
