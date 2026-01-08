namespace Prowl.Wicked.Network.Rpc;

using System.Linq;
using System.Reflection;
using Prowl.Wicked.Core;
using Prowl.Wicked.Network.Serialization;

/// <summary>
/// Delegate for invoking an RPC method.
/// </summary>
public delegate void RpcInvoker(EntityBehaviour behaviour, NetworkReader reader);

/// <summary>
/// Registry for RPC method handlers.
/// Maps (behaviour type, method name) to invoker delegates.
/// </summary>
public static class RpcRegistry
{
    private static readonly Dictionary<(Type, string), RpcInvoker> _invokers = new();
    private static readonly Dictionary<(Type, string), MethodInfo> _methods = new();

    /// <summary>
    /// Registers an RPC invoker for a behaviour type and method name.
    /// </summary>
    public static void Register<TBehaviour>(string methodName, RpcInvoker invoker) where TBehaviour : EntityBehaviour
    {
        var key = (typeof(TBehaviour), methodName);
        _invokers[key] = invoker;
    }

    /// <summary>
    /// Registers an RPC method info for reflection-based invocation.
    /// </summary>
    public static void RegisterMethod(Type behaviourType, string methodName, MethodInfo method)
    {
        var key = (behaviourType, methodName);
        _methods[key] = method;
    }

    /// <summary>
    /// Gets the invoker for a behaviour type and method name.
    /// </summary>
    public static RpcInvoker? GetInvoker(Type behaviourType, string methodName)
    {
        var key = (behaviourType, methodName);
        return _invokers.TryGetValue(key, out var invoker) ? invoker : null;
    }

    /// <summary>
    /// Gets the method info for a behaviour type and method name.
    /// </summary>
    public static MethodInfo? GetMethod(Type behaviourType, string methodName)
    {
        var key = (behaviourType, methodName);
        return _methods.TryGetValue(key, out var method) ? method : null;
    }

    /// <summary>
    /// Invokes an RPC on a behaviour.
    /// Returns true if the RPC was found and invoked.
    /// </summary>
    public static bool Invoke(EntityBehaviour behaviour, string methodName, NetworkReader reader)
    {
        var behaviourType = behaviour.GetType();

        // Try registered invoker first (from IL weaving)
        var invoker = GetInvoker(behaviourType, methodName);
        if (invoker != null)
        {
            invoker(behaviour, reader);
            return true;
        }

        // Try to find the IL-weaved invoker method
        // For [Command] CmdMove, look for InvokeCmdMove
        // For [ClientRpc] RpcOnShoot, look for InvokeRpcOnShoot
        var invokerName = GetInvokerMethodName(methodName);

        // Check if invoker is cached
        var method = GetMethod(behaviourType, invokerName);
        if (method != null)
        {
            try
            {
                method.Invoke(behaviour, new object[] { reader });
            }
            catch (TargetInvocationException ex)
            {
                Console.WriteLine($"RpcRegistry: Error invoking {invokerName}: {ex.InnerException?.Message ?? ex.Message}");
                Console.WriteLine(ex.InnerException?.StackTrace ?? ex.StackTrace);
                throw;
            }
            return true;
        }

        // Try to find invoker via reflection
        method = FindInvokerMethod(behaviourType, invokerName);
        if (method != null)
        {
            // Cache the invoker method under its own name for next time
            RegisterMethod(behaviourType, invokerName, method);
            try
            {
                method.Invoke(behaviour, new object[] { reader });
            }
            catch (TargetInvocationException ex)
            {
                Console.WriteLine($"RpcRegistry: Error invoking {invokerName}: {ex.InnerException?.Message ?? ex.Message}");
                Console.WriteLine(ex.InnerException?.StackTrace ?? ex.StackTrace);
                throw;
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the IL-weaved invoker method by name, handling duplicates by selecting the one with NetworkReader parameter.
    /// </summary>
    private static MethodInfo? FindInvokerMethod(Type behaviourType, string invokerName)
    {
        var methods = behaviourType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == invokerName)
            .ToArray();

        Console.WriteLine($"FindInvokerMethod: Looking for '{invokerName}' in {behaviourType.Name}, found {methods.Length} matches");
        foreach (var m in methods)
        {
            var paramStr = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name));
            Console.WriteLine($"  - {m.Name}({paramStr})");
        }

        if (methods.Length == 0)
            return null;

        if (methods.Length == 1)
            return methods[0];

        // Multiple methods found - look for the one with exactly one NetworkReader parameter
        foreach (var m in methods)
        {
            var parameters = m.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(NetworkReader))
                return m;
        }

        // Just return the first one as a fallback
        return methods[0];
    }

    /// <summary>
    /// Gets the IL-weaved invoker method name for a given RPC method name.
    /// </summary>
    private static string GetInvokerMethodName(string methodName)
    {
        // For CmdXxx -> InvokeCmdXxx
        if (methodName.StartsWith("Cmd"))
            return $"Invoke{methodName}";

        // For RpcXxx -> InvokeRpcXxx
        if (methodName.StartsWith("Rpc"))
            return $"Invoke{methodName}";

        // Fallback for non-standard names
        return $"InvokeCmd{methodName}";
    }

    private static object?[] DeserializeParameters(MethodInfo method, NetworkReader reader)
    {
        var paramInfos = method.GetParameters();
        var parameters = new object?[paramInfos.Length];

        for (int i = 0; i < paramInfos.Length; i++)
        {
            var paramType = paramInfos[i].ParameterType;

            // Skip NetworkConnection parameter for TargetRpc (it's not serialized)
            if (paramType == typeof(NetworkConnection))
            {
                parameters[i] = null; // Will be filled in by the caller if needed
                continue;
            }

            parameters[i] = ReadParameter(reader, paramType);
        }

        return parameters;
    }

    private static object? ReadParameter(NetworkReader reader, Type paramType)
    {
        // Read the typed value (includes type tag from WriteTypedValue)
        return reader.ReadTypedValue();
    }

    /// <summary>
    /// Clears all registered invokers. Used for testing.
    /// </summary>
    internal static void Clear()
    {
        _invokers.Clear();
        _methods.Clear();
    }
}
