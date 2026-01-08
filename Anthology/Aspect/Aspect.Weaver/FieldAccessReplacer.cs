using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;

namespace Aspect.Weaver;

/// <summary>
/// Replaces field access instructions (ldfld, stfld, ldflda) with property getter/setter calls.
/// This is used after fields are transformed into properties to ensure all field access
/// throughout the assembly uses the new property accessors.
/// </summary>
public class FieldAccessReplacer
{
    private readonly ModuleDefinition _module;
    private readonly Dictionary<FieldDefinition, MethodDefinition> _fieldGetterMap;
    private readonly Dictionary<FieldDefinition, MethodDefinition> _fieldSetterMap;

    public FieldAccessReplacer(ModuleDefinition module)
    {
        _module = module;
        _fieldGetterMap = new Dictionary<FieldDefinition, MethodDefinition>();
        _fieldSetterMap = new Dictionary<FieldDefinition, MethodDefinition>();
    }

    /// <summary>
    /// Registers a field that has been converted to a property.
    /// </summary>
    public void RegisterFieldReplacement(FieldDefinition field, MethodDefinition getter, MethodDefinition setter)
    {
        if (getter != null)
            _fieldGetterMap[field] = getter;
        if (setter != null)
            _fieldSetterMap[field] = setter;
    }

    /// <summary>
    /// Processes the entire module, replacing all field access instructions with property calls.
    /// </summary>
    public void ProcessModule()
    {
        if (_fieldGetterMap.Count == 0 && _fieldSetterMap.Count == 0)
            return;

        Console.WriteLine($"  Processing module for field access replacement ({_fieldGetterMap.Count} getters, {_fieldSetterMap.Count} setters registered)");

        foreach (var type in _module.Types)
        {
            ProcessType(type);
        }
    }

    private void ProcessType(TypeDefinition type)
    {
        // Process all methods in this type
        foreach (var method in type.Methods)
        {
            ProcessMethod(method);
        }

        // Process nested types recursively
        foreach (var nested in type.NestedTypes)
        {
            ProcessType(nested);
        }
    }

    private void ProcessMethod(MethodDefinition method)
    {
        // Skip abstract methods
        if (method.IsAbstract)
            return;

        // Skip methods without body
        if (method.Body == null || method.Body.Instructions == null)
            return;

        // Go through all instructions
        for (int i = 0; i < method.Body.Instructions.Count;)
        {
            var instruction = method.Body.Instructions[i];
            i += ProcessInstruction(method, instruction, i);
        }
    }

    private int ProcessInstruction(MethodDefinition method, Instruction instruction, int index)
    {
        // stfld - field write
        if (instruction.OpCode == OpCodes.Stfld)
        {
            if (instruction.Operand is FieldDefinition field)
            {
                return ProcessSetInstruction(method, instruction, field);
            }
            else if (instruction.Operand is FieldReference fieldRef)
            {
                var resolved = fieldRef.Resolve();
                if (resolved != null)
                {
                    return ProcessSetInstruction(method, instruction, resolved);
                }
            }
        }

        // ldfld - field read
        if (instruction.OpCode == OpCodes.Ldfld)
        {
            if (instruction.Operand is FieldDefinition field)
            {
                return ProcessGetInstruction(method, instruction, field);
            }
            else if (instruction.Operand is FieldReference fieldRef)
            {
                var resolved = fieldRef.Resolve();
                if (resolved != null)
                {
                    return ProcessGetInstruction(method, instruction, resolved);
                }
            }
        }

        // ldflda - load field address (for ref/out parameters or struct method calls)
        if (instruction.OpCode == OpCodes.Ldflda)
        {
            if (instruction.Operand is FieldDefinition field)
            {
                return ProcessLoadAddressInstruction(method, instruction, field, index);
            }
            else if (instruction.Operand is FieldReference fieldRef)
            {
                var resolved = fieldRef.Resolve();
                if (resolved != null)
                {
                    return ProcessLoadAddressInstruction(method, instruction, resolved, index);
                }
            }
        }

        // stsfld - static field write
        if (instruction.OpCode == OpCodes.Stsfld)
        {
            if (instruction.Operand is FieldDefinition field)
            {
                return ProcessSetInstruction(method, instruction, field);
            }
            else if (instruction.Operand is FieldReference fieldRef)
            {
                var resolved = fieldRef.Resolve();
                if (resolved != null)
                {
                    return ProcessSetInstruction(method, instruction, resolved);
                }
            }
        }

        // ldsfld - static field read
        if (instruction.OpCode == OpCodes.Ldsfld)
        {
            if (instruction.Operand is FieldDefinition field)
            {
                return ProcessGetInstruction(method, instruction, field);
            }
            else if (instruction.Operand is FieldReference fieldRef)
            {
                var resolved = fieldRef.Resolve();
                if (resolved != null)
                {
                    return ProcessGetInstruction(method, instruction, resolved);
                }
            }
        }

        // ldsflda - load static field address
        if (instruction.OpCode == OpCodes.Ldsflda)
        {
            if (instruction.Operand is FieldDefinition field)
            {
                return ProcessLoadAddressInstruction(method, instruction, field, index);
            }
            else if (instruction.Operand is FieldReference fieldRef)
            {
                var resolved = fieldRef.Resolve();
                if (resolved != null)
                {
                    return ProcessLoadAddressInstruction(method, instruction, resolved, index);
                }
            }
        }

        return 1;
    }

    private int ProcessSetInstruction(MethodDefinition method, Instruction instruction, FieldDefinition field)
    {
        // Don't replace in constructors - allow direct field initialization
        if (method.Name == ".ctor" || method.Name == ".cctor")
            return 1;

        // Check if this field has a replacement setter
        if (_fieldSetterMap.TryGetValue(field, out var setter))
        {
            Console.WriteLine($"    Replacing stfld {field.Name} with call to {setter.Name} in {method.Name}");
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = _module.ImportReference(setter);
        }

        return 1;
    }

    private int ProcessGetInstruction(MethodDefinition method, Instruction instruction, FieldDefinition field)
    {
        // Don't replace in constructors
        if (method.Name == ".ctor" || method.Name == ".cctor")
            return 1;

        // Check if this field has a replacement getter
        if (_fieldGetterMap.TryGetValue(field, out var getter))
        {
            Console.WriteLine($"    Replacing ldfld {field.Name} with call to {getter.Name} in {method.Name}");
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = _module.ImportReference(getter);
        }

        return 1;
    }

    private int ProcessLoadAddressInstruction(MethodDefinition method, Instruction instruction, FieldDefinition field, int index)
    {
        // Don't replace in constructors
        if (method.Name == ".ctor" || method.Name == ".cctor")
            return 1;

        // Check if we have a replacement for this field
        if (!_fieldSetterMap.TryGetValue(field, out var setter))
            return 1;

        // Get the next instruction to see what operation is being performed
        if (index + 1 >= method.Body.Instructions.Count)
            return 1;

        var nextInstruction = method.Body.Instructions[index + 1];

        // Handle initobj (initializing a struct to default value)
        if (nextInstruction.OpCode == OpCodes.Initobj)
        {
            // Replace:
            //   ldflda field
            //   initobj StructType
            // With:
            //   ldloca temp
            //   initobj StructType
            //   ldloc temp
            //   call setter

            var processor = method.Body.GetILProcessor();
            var fieldType = field.FieldType;

            // Create temp variable
            var tempVar = new VariableDefinition(fieldType);
            method.Body.Variables.Add(tempVar);

            // Check if this is static
            bool isStatic = field.IsStatic;

            // Insert new instructions before the ldflda
            // For non-static: stack already has 'this' from before ldflda
            // We need to keep 'this' for the setter call

            // For non-static fields, the sequence is:
            //   ldarg.0  (this) - already on stack before ldflda
            //   ldflda field
            //   initobj StructType
            // We need to change this to:
            //   ldarg.0  (this) - keep this
            //   ldloca temp
            //   initobj StructType
            //   ldloc temp
            //   call setter

            // Actually, for initobj the 'this' is consumed by ldflda, so we need to reload it
            // Sequence becomes:
            //   [remove ldflda, initobj]
            //   ldloca temp
            //   initobj StructType
            //   ldarg.0  (if non-static)
            //   ldloc temp
            //   call setter

            // Remove original instructions first
            processor.Remove(instruction);
            processor.Remove(nextInstruction);

            // Get the instruction that was before ldflda (to insert after it)
            // Since we removed them, we insert at the current position (index)
            var insertPoint = index < method.Body.Instructions.Count
                ? method.Body.Instructions[index]
                : null;

            // Build the replacement sequence in reverse order (since we're inserting before)
            var callSetter = processor.Create(OpCodes.Call, _module.ImportReference(setter));
            var ldlocTemp = processor.Create(OpCodes.Ldloc, tempVar);
            var initObj = processor.Create(OpCodes.Initobj, _module.ImportReference(fieldType));
            var ldlocaTemp = processor.Create(OpCodes.Ldloca, tempVar);

            if (insertPoint != null)
            {
                // Insert in reverse order since we're inserting before
                processor.InsertBefore(insertPoint, ldlocaTemp);
                processor.InsertBefore(insertPoint, initObj);

                if (!isStatic)
                {
                    // Simplest approach: pop 'this', do init, reload 'this', load value, call
                    var popThis = processor.Create(OpCodes.Pop);
                    processor.InsertBefore(ldlocaTemp, popThis);

                    var ldThis = processor.Create(OpCodes.Ldarg_0);
                    processor.InsertBefore(insertPoint, ldThis);
                }

                processor.InsertBefore(insertPoint, ldlocTemp);
                processor.InsertBefore(insertPoint, callSetter);
            }
            else
            {
                // Append at end
                if (!isStatic)
                {
                    processor.Append(processor.Create(OpCodes.Pop)); // Pop 'this'
                }
                processor.Append(ldlocaTemp);
                processor.Append(initObj);
                if (!isStatic)
                {
                    processor.Append(processor.Create(OpCodes.Ldarg_0)); // Reload 'this'
                }
                processor.Append(ldlocTemp);
                processor.Append(callSetter);
            }

            Console.WriteLine($"    Replaced ldflda+initobj for {field.Name} with property setter in {method.Name}");

            // We removed 2 instructions and added 4-6, return how many to skip
            // Since we modified the instruction list, just return 1 to continue
            return 1;
        }

        // For other ldflda uses (like calling methods on struct fields),
        // this is more complex and might require get+modify+set pattern
        // For now, log a warning
        Console.WriteLine($"    WARNING: Cannot replace ldflda for {field.Name} followed by {nextInstruction.OpCode} in {method.Name}");

        return 1;
    }
}
