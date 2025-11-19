using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Aspect.Weaver;

/// <summary>
/// Base class for aspect weavers with common utility methods.
/// </summary>
public abstract class WeaverBase
{
    protected readonly ModuleDefinition _module;

    protected WeaverBase(ModuleDefinition module)
    {
        _module = module;
    }

    /// <summary>
    /// Finds a type by name in the current module or referenced assemblies.
    /// </summary>
    protected TypeDefinition FindType(string fullName)
    {
        // First, try to find in the current module
        var type = _module.Types.FirstOrDefault(t => t.FullName == fullName);
        if (type != null)
            return type;

        // Then search in referenced assemblies
        foreach (var assemblyRef in _module.AssemblyReferences)
        {
            try
            {
                var assembly = _module.AssemblyResolver.Resolve(assemblyRef);
                type = assembly.MainModule.Types.FirstOrDefault(t => t.FullName == fullName);
                if (type != null)
                    return type;
            }
            catch
            {
                // If assembly cannot be resolved, continue to next
                continue;
            }
        }

        throw new InvalidOperationException($"Could not find type: {fullName}");
    }

    /// <summary>
    /// Emits boxing instruction if the type is a value type or generic parameter.
    /// </summary>
    protected void EmitBoxIfNeeded(ILProcessor processor, TypeReference type)
    {
        if (type.IsValueType || type.IsGenericParameter)
        {
            processor.Emit(OpCodes.Box, _module.ImportReference(type));
        }
    }

    /// <summary>
    /// Emits unboxing or casting instruction to convert from object to the target type.
    /// </summary>
    protected void EmitUnboxOrCast(ILProcessor processor, TypeReference type)
    {
        if (type.IsValueType || type.IsGenericParameter)
        {
            processor.Emit(OpCodes.Unbox_Any, _module.ImportReference(type));
        }
        else if (type.FullName != "System.Object")
        {
            processor.Emit(OpCodes.Castclass, _module.ImportReference(type));
        }
    }

    /// <summary>
    /// Finds a property by name in a type definition.
    /// </summary>
    protected PropertyDefinition? FindProperty(TypeDefinition type, string propertyName)
    {
        return type.Properties.FirstOrDefault(p => p.Name == propertyName);
    }

    /// <summary>
    /// Gets the getter method reference for a property.
    /// </summary>
    protected MethodReference? GetPropertyGetter(TypeDefinition type, string propertyName)
    {
        var property = FindProperty(type, propertyName);
        return property?.GetMethod != null ? _module.ImportReference(property.GetMethod) : null;
    }

    /// <summary>
    /// Gets the setter method reference for a property.
    /// </summary>
    protected MethodReference? GetPropertySetter(TypeDefinition type, string propertyName)
    {
        var property = FindProperty(type, propertyName);
        return property?.SetMethod != null ? _module.ImportReference(property.SetMethod) : null;
    }

    /// <summary>
    /// Finds a parameterless constructor for a type.
    /// </summary>
    protected MethodDefinition? FindParameterlessConstructor(TypeDefinition type)
    {
        return type.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);
    }

    /// <summary>
    /// Clones a single instruction for a target method.
    /// </summary>
    protected Instruction CloneInstruction(Instruction instruction, MethodDefinition targetMethod)
    {
        var newInstr = Instruction.Create(OpCodes.Nop);
        newInstr.OpCode = instruction.OpCode;

        // Import operands that reference members from other modules
        if (instruction.Operand is FieldReference fieldRef)
        {
            // Only import if from a different module or if it's not a FieldDefinition
            if (fieldRef.Module != _module || fieldRef is not FieldDefinition)
            {
                newInstr.Operand = _module.ImportReference(fieldRef);
            }
            else
            {
                newInstr.Operand = fieldRef;
            }
        }
        else if (instruction.Operand is MethodReference methodRef)
        {
            // Only import if from a different module or if it's not a MethodDefinition
            if (methodRef.Module != _module || methodRef is not MethodDefinition)
            {
                newInstr.Operand = _module.ImportReference(methodRef);
            }
            else
            {
                newInstr.Operand = methodRef;
            }
        }
        else if (instruction.Operand is TypeReference typeRef)
        {
            // Only import if from a different module or if it's not a TypeDefinition
            if (typeRef.Module != _module || typeRef is not TypeDefinition)
            {
                newInstr.Operand = _module.ImportReference(typeRef);
            }
            else
            {
                newInstr.Operand = typeRef;
            }
        }
        else
        {
            // For instructions, parameters, variables, etc., keep as-is
            // Branch targets will be fixed in the second pass
            newInstr.Operand = instruction.Operand;
        }

        return newInstr;
    }
}
