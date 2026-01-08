namespace Prowl.Wicked.CodeGen.Processors;

using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Base class for RPC processors with common IL generation utilities.
/// With Echo serialization, invokers receive object?[] args directly instead of NetworkReader.
/// Uses function hash (ushort) for efficient network transmission.
/// </summary>
public abstract class RpcProcessorBase
{
    protected readonly ModuleDefinition Module;

    // Cached type references
    protected TypeReference? EntityBehaviourType;
    protected TypeReference? NetworkConnectionType;
    protected TypeReference? ObjectType;
    protected TypeReference? ObjectArrayType;
    protected TypeReference? VoidType;
    protected TypeReference? BoolType;
    protected TypeReference? StringType;

    protected RpcProcessorBase(ModuleDefinition module)
    {
        Module = module;
        CacheTypes();
    }

    private void CacheTypes()
    {
        VoidType = Module.TypeSystem.Void;
        BoolType = Module.TypeSystem.Boolean;
        StringType = Module.TypeSystem.String;
        ObjectType = Module.TypeSystem.Object;
        ObjectArrayType = new ArrayType(ObjectType);
    }

    /// <summary>
    /// Imports a type reference into the module.
    /// </summary>
    protected TypeReference Import(Type type)
    {
        return Module.ImportReference(type);
    }

    /// <summary>
    /// Imports a method reference into the module.
    /// </summary>
    protected MethodReference Import(System.Reflection.MethodInfo method)
    {
        return Module.ImportReference(method);
    }

    /// <summary>
    /// Checks if a method has a specific attribute.
    /// </summary>
    protected bool HasAttribute(MethodDefinition method, string attributeFullName)
    {
        return method.CustomAttributes.Any(a => a.AttributeType.FullName == attributeFullName);
    }

    /// <summary>
    /// Gets an attribute from a method.
    /// </summary>
    protected CustomAttribute? GetAttribute(MethodDefinition method, string attributeFullName)
    {
        return method.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == attributeFullName);
    }

    /// <summary>
    /// Gets an attribute property value.
    /// </summary>
    protected T GetAttributeProperty<T>(CustomAttribute attr, string propertyName, T defaultValue)
    {
        var prop = attr.Properties.FirstOrDefault(p => p.Name == propertyName);
        if (prop.Argument.Value != null)
            return (T)prop.Argument.Value;
        return defaultValue;
    }

    /// <summary>
    /// Computes a stable 16-bit function hash for a behaviour type and method name.
    /// Uses FNV-1a algorithm for good distribution.
    /// </summary>
    protected ushort ComputeFunctionHash(string behaviourTypeName, string methodName)
    {
        var fullName = $"{behaviourTypeName}.{methodName}";
        return GetStableHash16(fullName);
    }

    /// <summary>
    /// Computes a stable 16-bit hash for a string.
    /// </summary>
    private static ushort GetStableHash16(string text)
    {
        return (ushort)(GetStableHash32(text) & 0xFFFF);
    }

    /// <summary>
    /// Computes a stable 32-bit hash for a string using FNV-1a algorithm.
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
    /// Creates an invoker method that receives object?[] args.
    /// Signature: void InvokeXxx(object?[] args)
    /// </summary>
    protected MethodDefinition CreateInvokerMethod(string invokerName)
    {
        var invoker = new MethodDefinition(
            invokerName,
            MethodAttributes.Public | MethodAttributes.HideBySig,
            VoidType);

        // Add object?[] parameter
        invoker.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, ObjectArrayType));

        return invoker;
    }

    /// <summary>
    /// Emits code to read a parameter from the args array at the specified index.
    /// The value is loaded and cast/unboxed to the target type.
    /// </summary>
    protected void EmitReadParameterFromArgs(ILProcessor il, TypeReference paramType, int argIndex)
    {
        var resolvedParamType = Module.ImportReference(paramType);

        // Load args array (arg1)
        il.Emit(OpCodes.Ldarg_1);
        // Load index
        il.Emit(OpCodes.Ldc_I4, argIndex);
        // Get element from array
        il.Emit(OpCodes.Ldelem_Ref);

        // Unbox/cast to the expected type
        if (paramType.IsValueType)
        {
            il.Emit(OpCodes.Unbox_Any, resolvedParamType);
        }
        else
        {
            il.Emit(OpCodes.Castclass, resolvedParamType);
        }
    }
}
