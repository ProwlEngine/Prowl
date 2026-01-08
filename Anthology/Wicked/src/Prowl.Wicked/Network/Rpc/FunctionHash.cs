namespace Prowl.Wicked.Network.Rpc;

/// <summary>
/// Computes stable hash codes for RPC function names.
/// Uses a 16-bit hash (ushort) to minimize network bandwidth while maintaining low collision probability.
/// Inspired by Mirror's approach.
/// </summary>
public static class FunctionHash
{
    /// <summary>
    /// Computes a stable 16-bit hash for a function name.
    /// The hash is deterministic across different runs and platforms.
    /// </summary>
    /// <param name="functionName">The full function name (typically "TypeName.MethodName")</param>
    /// <returns>A 16-bit hash code</returns>
    public static ushort GetStableHash16(string functionName)
    {
        return (ushort)(GetStableHash32(functionName) & 0xFFFF);
    }

    /// <summary>
    /// Computes a stable 32-bit hash for a string.
    /// Uses FNV-1a algorithm for good distribution.
    /// </summary>
    private static uint GetStableHash32(string text)
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
