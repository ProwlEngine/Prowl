namespace Prowl.Wicked.CodeGen.Processors;

using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Base class for RPC processors with common IL generation utilities.
/// </summary>
public abstract class RpcProcessorBase
{
    protected readonly ModuleDefinition Module;

    // Cached type references
    protected TypeReference? EntityBehaviourType;
    protected TypeReference? NetworkWriterType;
    protected TypeReference? NetworkReaderType;
    protected TypeReference? NetworkConnectionType;
    protected TypeReference? ObjectType;
    protected TypeReference? ObjectArrayType;
    protected TypeReference? VoidType;
    protected TypeReference? BoolType;
    protected TypeReference? StringType;

    // Cached method references
    protected MethodReference? WriteObject;
    protected MethodReference? ReadObject;
    protected MethodReference? GetIsServer;
    protected MethodReference? GetIsClient;
    protected MethodReference? GetIsHost;

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
    /// Creates an invoker method that calls the original method logic.
    /// </summary>
    protected MethodDefinition CreateInvokerMethod(MethodDefinition original, string invokerName)
    {
        // Create the invoker method signature: void InvokeXxx(NetworkReader reader)
        var invoker = new MethodDefinition(
            invokerName,
            MethodAttributes.Private | MethodAttributes.HideBySig,
            VoidType);

        // Add NetworkReader parameter
        var readerType = Module.ImportReference(typeof(Prowl.Wicked.Network.Serialization.NetworkReader));
        invoker.Parameters.Add(new ParameterDefinition("reader", ParameterAttributes.None, readerType));

        return invoker;
    }

    /// <summary>
    /// Emits code to read a parameter from the NetworkReader.
    /// Uses ReadTypedValue to match WriteTypedValue used in SendCommand/SendClientRpc.
    /// </summary>
    protected void EmitReadParameter(ILProcessor il, ParameterDefinition param)
    {
        var readerType = typeof(Prowl.Wicked.Network.Serialization.NetworkReader);
        var paramType = param.ParameterType;
        var resolvedParamType = Module.ImportReference(paramType);

        // Use ReadTypedValue for all types - matches WriteTypedValue in SendCommand
        var readTypedValueMethod = Module.ImportReference(readerType.GetMethod("ReadTypedValue"));

        il.Emit(OpCodes.Ldarg_1); // reader
        il.Emit(OpCodes.Callvirt, readTypedValueMethod);

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

    /// <summary>
    /// Emits code to write a parameter to the NetworkWriter.
    /// </summary>
    protected void EmitWriteParameter(ILProcessor il, ParameterDefinition param, int paramIndex, VariableDefinition writerVar)
    {
        var writerType = typeof(Prowl.Wicked.Network.Serialization.NetworkWriter);
        var paramType = param.ParameterType;

        il.Emit(OpCodes.Ldloc, writerVar);

        // Load the parameter value
        il.Emit(OpCodes.Ldarg, paramIndex);

        // Handle primitive types with specific write methods
        if (paramType.FullName == "System.Int32")
        {
            var method = Module.ImportReference(writerType.GetMethod("WriteInt"));
            il.Emit(OpCodes.Callvirt, method);
        }
        else if (paramType.FullName == "System.Single")
        {
            var method = Module.ImportReference(writerType.GetMethod("WriteFloat"));
            il.Emit(OpCodes.Callvirt, method);
        }
        else if (paramType.FullName == "System.Boolean")
        {
            var method = Module.ImportReference(writerType.GetMethod("WriteBool"));
            il.Emit(OpCodes.Callvirt, method);
        }
        else if (paramType.FullName == "System.String")
        {
            var method = Module.ImportReference(writerType.GetMethod("WriteString"));
            il.Emit(OpCodes.Callvirt, method);
        }
        else if (paramType.FullName == "System.Byte")
        {
            var method = Module.ImportReference(writerType.GetMethod("WriteByte"));
            il.Emit(OpCodes.Callvirt, method);
        }
        else if (paramType.FullName == "System.UInt32")
        {
            var method = Module.ImportReference(writerType.GetMethod("WriteUInt"));
            il.Emit(OpCodes.Callvirt, method);
        }
        else if (paramType.FullName == "System.Int64")
        {
            var method = Module.ImportReference(writerType.GetMethod("WriteLong"));
            il.Emit(OpCodes.Callvirt, method);
        }
        else if (paramType.FullName == "System.Double")
        {
            var method = Module.ImportReference(writerType.GetMethod("WriteDouble"));
            il.Emit(OpCodes.Callvirt, method);
        }
        else
        {
            // Box value types for WriteObject
            if (paramType.IsValueType)
            {
                il.Emit(OpCodes.Box, Module.ImportReference(paramType));
            }
            var method = Module.ImportReference(writerType.GetMethod("WriteObject"));
            il.Emit(OpCodes.Callvirt, method);
        }
    }
}
