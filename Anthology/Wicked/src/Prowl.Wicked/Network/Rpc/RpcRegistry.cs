namespace Prowl.Wicked.Network.Rpc;

using System.Linq;
using System.Reflection;
using Prowl.Wicked.Core;

/// <summary>
/// Invoke type for Cmd/Rpc - like Mirror's RemoteCallType.
/// </summary>
public enum RemoteCallType { Command, ClientRpc }

/// <summary>
/// Delegate for invoking an RPC method.
/// Args is the deserialized object[] from the message.
/// </summary>
public delegate void RpcInvoker(EntityBehaviour behaviour, object?[] args);

/// <summary>
/// Internal invoker info - like Mirror's Invoker class.
/// </summary>
internal class Invoker
{
    public Type ComponentType { get; set; } = null!;
    public RemoteCallType CallType { get; set; }
    public RpcInvoker Function { get; set; } = null!;
    public bool CmdRequiresAuthority { get; set; } = true;
    public string MethodName { get; set; } = "";
}

/// <summary>
/// Registry for RPC method handlers.
/// Maps (behaviour type, function hash) to invoker delegates.
/// Uses 16-bit function hashes for efficient network transmission.
/// </summary>
public static class RpcRegistry
{
    private static readonly Dictionary<(Type, ushort), Invoker> _invokers = new();
    private static readonly Dictionary<(Type, ushort), MethodInfo> _methods = new();

    /// <summary>
    /// Registers a command invoker for a behaviour type and function hash.
    /// Like Mirror's RegisterCommand.
    /// </summary>
    public static void RegisterCommand<TBehaviour>(ushort functionHash, RpcInvoker invoker, bool requiresAuthority = true, string methodName = "") where TBehaviour : EntityBehaviour
    {
        var key = (typeof(TBehaviour), functionHash);
        _invokers[key] = new Invoker
        {
            ComponentType = typeof(TBehaviour),
            CallType = RemoteCallType.Command,
            Function = invoker,
            CmdRequiresAuthority = requiresAuthority,
            MethodName = methodName
        };
    }

    /// <summary>
    /// Registers a command invoker for a behaviour type and method name (computes hash automatically).
    /// Like Mirror's RegisterCommand.
    /// </summary>
    public static void RegisterCommand<TBehaviour>(string methodName, RpcInvoker invoker, bool requiresAuthority = true) where TBehaviour : EntityBehaviour
    {
        var hash = FunctionHash.ComputeHash(typeof(TBehaviour), methodName);
        RegisterCommand<TBehaviour>(hash, invoker, requiresAuthority, methodName);
    }

    /// <summary>
    /// Registers a client RPC invoker for a behaviour type and function hash.
    /// Like Mirror's RegisterRpc.
    /// </summary>
    public static void RegisterRpc<TBehaviour>(ushort functionHash, RpcInvoker invoker, string methodName = "") where TBehaviour : EntityBehaviour
    {
        var key = (typeof(TBehaviour), functionHash);
        _invokers[key] = new Invoker
        {
            ComponentType = typeof(TBehaviour),
            CallType = RemoteCallType.ClientRpc,
            Function = invoker,
            CmdRequiresAuthority = false,
            MethodName = methodName
        };
    }

    /// <summary>
    /// Registers a client RPC invoker for a behaviour type and method name (computes hash automatically).
    /// Like Mirror's RegisterRpc.
    /// </summary>
    public static void RegisterRpc<TBehaviour>(string methodName, RpcInvoker invoker) where TBehaviour : EntityBehaviour
    {
        var hash = FunctionHash.ComputeHash(typeof(TBehaviour), methodName);
        RegisterRpc<TBehaviour>(hash, invoker, methodName);
    }

    /// <summary>
    /// Registers an RPC invoker (backward compatible method).
    /// Defaults to Command type with requiresAuthority=true.
    /// </summary>
    public static void Register<TBehaviour>(ushort functionHash, RpcInvoker invoker, string methodName = "") where TBehaviour : EntityBehaviour
    {
        RegisterCommand<TBehaviour>(functionHash, invoker, true, methodName);
    }

    /// <summary>
    /// Registers an RPC invoker (backward compatible method).
    /// Defaults to Command type with requiresAuthority=true.
    /// </summary>
    public static void Register<TBehaviour>(string methodName, RpcInvoker invoker) where TBehaviour : EntityBehaviour
    {
        var hash = FunctionHash.ComputeHash(typeof(TBehaviour), methodName);
        Register<TBehaviour>(hash, invoker, methodName);
    }

    /// <summary>
    /// Registers an RPC method info for reflection-based invocation.
    /// </summary>
    public static void RegisterMethod(Type behaviourType, ushort functionHash, MethodInfo method, string methodName = "")
    {
        var key = (behaviourType, functionHash);
        _methods[key] = method;

        // Also create an invoker entry if not exists
        if (!_invokers.ContainsKey(key))
        {
            _invokers[key] = new Invoker
            {
                ComponentType = behaviourType,
                CallType = RemoteCallType.Command, // Default to command
                Function = (b, args) => method.Invoke(b, new object[] { args }),
                CmdRequiresAuthority = true, // Default to requiring authority
                MethodName = methodName
            };
        }
    }

    /// <summary>
    /// Checks if a command requires authority to execute.
    /// Like Mirror's CommandRequiresAuthority.
    /// </summary>
    public static bool CommandRequiresAuthority(Type behaviourType, ushort functionHash)
    {
        var key = (behaviourType, functionHash);
        return _invokers.TryGetValue(key, out var invoker) &&
               invoker.CallType == RemoteCallType.Command &&
               invoker.CmdRequiresAuthority;
    }

    /// <summary>
    /// Gets the invoker for a behaviour type and function hash.
    /// </summary>
    public static RpcInvoker? GetInvoker(Type behaviourType, ushort functionHash)
    {
        var key = (behaviourType, functionHash);
        return _invokers.TryGetValue(key, out var invoker) ? invoker.Function : null;
    }

    /// <summary>
    /// Gets the full invoker info for a behaviour type and function hash.
    /// </summary>
    internal static Invoker? GetInvokerInfo(Type behaviourType, ushort functionHash)
    {
        var key = (behaviourType, functionHash);
        return _invokers.TryGetValue(key, out var invoker) ? invoker : null;
    }

    /// <summary>
    /// Gets the method info for a behaviour type and function hash.
    /// </summary>
    public static MethodInfo? GetMethod(Type behaviourType, ushort functionHash)
    {
        var key = (behaviourType, functionHash);
        return _methods.TryGetValue(key, out var method) ? method : null;
    }

    /// <summary>
    /// Gets the method name for a function hash (for debugging).
    /// </summary>
    public static string GetMethodName(Type behaviourType, ushort functionHash)
    {
        var key = (behaviourType, functionHash);
        if (_invokers.TryGetValue(key, out var invoker) && !string.IsNullOrEmpty(invoker.MethodName))
            return invoker.MethodName;
        return $"0x{functionHash:X4}";
    }

    /// <summary>
    /// Invokes an RPC on a behaviour with deserialized arguments using function hash.
    /// Returns true if the RPC was found and invoked.
    /// </summary>
    public static bool Invoke(EntityBehaviour behaviour, ushort functionHash, object?[] args)
    {
        var behaviourType = behaviour.GetType();

        // Try registered invoker first (from IL weaving)
        var invoker = GetInvoker(behaviourType, functionHash);
        if (invoker != null)
        {
            invoker(behaviour, args);
            return true;
        }

        // Try cached method
        var method = GetMethod(behaviourType, functionHash);
        if (method != null)
        {
            try
            {
                method.Invoke(behaviour, new object[] { args });
            }
            catch (TargetInvocationException ex)
            {
                var methodName = GetMethodName(behaviourType, functionHash);
                Console.WriteLine($"RpcRegistry: Error invoking {methodName}: {ex.InnerException?.Message ?? ex.Message}");
                Console.WriteLine(ex.InnerException?.StackTrace ?? ex.StackTrace);
                throw;
            }
            return true;
        }

        // Try to find invoker via reflection (scan all methods for matching hash)
        method = FindInvokerByHash(behaviourType, functionHash);
        if (method != null)
        {
            RegisterMethod(behaviourType, functionHash, method, method.Name);
            try
            {
                method.Invoke(behaviour, new object[] { args });
            }
            catch (TargetInvocationException ex)
            {
                Console.WriteLine($"RpcRegistry: Error invoking {method.Name}: {ex.InnerException?.Message ?? ex.Message}");
                Console.WriteLine(ex.InnerException?.StackTrace ?? ex.StackTrace);
                throw;
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Invokes an RPC on a behaviour with deserialized arguments using method name.
    /// Computes the hash and delegates to the hash-based invoke.
    /// </summary>
    public static bool Invoke(EntityBehaviour behaviour, string methodName, object?[] args)
    {
        var functionHash = FunctionHash.ComputeHash(behaviour.GetType(), methodName);
        return Invoke(behaviour, functionHash, args);
    }

    /// <summary>
    /// Finds the IL-weaved invoker method by scanning for methods with matching function hash.
    /// </summary>
    private static MethodInfo? FindInvokerByHash(Type behaviourType, ushort targetHash)
    {
        var methods = behaviourType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            // Check if this is an invoker method (starts with Invoke)
            if (!method.Name.StartsWith("Invoke"))
                continue;

            // Check if it has the right signature (one object[] parameter)
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(object?[]))
                continue;

            // Extract the original method name from the invoker name
            // InvokeCmdMove -> CmdMove, InvokeRpcOnShoot -> RpcOnShoot
            var originalName = method.Name.Substring("Invoke".Length);

            // Compute the hash for this method
            var hash = FunctionHash.ComputeHash(behaviourType, originalName);

            if (hash == targetHash)
            {
                return method;
            }
        }

        return null;
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
