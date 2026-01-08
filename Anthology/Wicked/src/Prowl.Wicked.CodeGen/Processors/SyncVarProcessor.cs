namespace Prowl.Wicked.CodeGen.Processors;

using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Processes [SyncVar] fields in EntityBehaviour classes.
/// Transforms fields into properties with automatic dirty tracking.
/// </summary>
public class SyncVarProcessor
{
    private readonly ModuleDefinition _module;
    private const string SyncVarAttributeName = "Prowl.Wicked.Attributes.SyncVarAttribute";
    private const string EntityBehaviourTypeName = "Prowl.Wicked.Core.EntityBehaviour";

    // Track all processed SyncVars for field reference rewriting
    // Key: original field, Value: generated setter method (Mirror's approach)
    private readonly Dictionary<FieldDefinition, MethodDefinition> _replacementSetterProperties = new();
    private readonly Dictionary<FieldDefinition, MethodDefinition> _replacementGetterProperties = new();

    public SyncVarProcessor(ModuleDefinition module)
    {
        _module = module;
    }

    /// <summary>
    /// Processes all SyncVars in a type. Returns true if any changes were made.
    /// </summary>
    public bool Process(TypeDefinition type)
    {
        if (!InheritsFromEntityBehaviour(type))
            return false;

        var syncVarFields = GetSyncVarFields(type).ToList();
        if (syncVarFields.Count == 0)
            return false;

        Console.WriteLine($"SyncVarProcessor: Found {syncVarFields.Count} SyncVar(s) in {type.FullName}");

        // Assign slot indices
        int slotIndex = GetNextAvailableSlotIndex(type);

        foreach (var field in syncVarFields)
        {
            if (slotIndex >= 32)
            {
                Console.WriteLine($"SyncVarProcessor: ERROR - Too many SyncVars in {type.FullName}. Maximum is 32.");
                throw new InvalidOperationException($"Too many SyncVars in {type.FullName}. Maximum is 32.");
            }

            ProcessSyncVarField(type, field, slotIndex);
            slotIndex++;
        }

        // Rewrite all field references in the type's methods
        RewriteFieldReferences(type);

        return true;
    }

    /// <summary>
    /// Rewrites field references across the entire module.
    /// Call this after all types have been processed.
    /// </summary>
    public void RewriteAllFieldReferences()
    {
        if (_replacementSetterProperties.Count == 0)
            return;

        foreach (var type in _module.Types)
        {
            RewriteFieldReferencesInType(type);
        }
    }

    private void RewriteFieldReferencesInType(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
                continue;

            // Mirror's skip conditions:
            // Skip static constructor
            if (method.Name == ".cctor")
                continue;

            // Skip instance constructors - field initializers run before base class is initialized
            if (method.Name == ".ctor")
                continue;

            // Skip abstract methods
            if (method.IsAbstract)
                continue;

            // Skip generated property getter/setter methods - they should access the backing field directly
            if (method.IsSpecialName && (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")))
                continue;

            RewriteFieldReferencesInMethod(method);
        }

        foreach (var nested in type.NestedTypes)
        {
            RewriteFieldReferencesInType(nested);
        }
    }

    private IEnumerable<FieldDefinition> GetSyncVarFields(TypeDefinition type)
    {
        foreach (var field in type.Fields)
        {
            if (field.CustomAttributes.Any(a => a.AttributeType.FullName == SyncVarAttributeName))
            {
                yield return field;
            }
        }
    }

    private int GetNextAvailableSlotIndex(TypeDefinition type)
    {
        // Count existing SyncVars in base classes
        int count = 0;
        var baseType = type.BaseType;

        while (baseType != null && baseType.FullName != "System.Object")
        {
            try
            {
                var resolved = baseType.Resolve();
                if (resolved != null)
                {
                    count += GetSyncVarFields(resolved).Count();
                    baseType = resolved.BaseType;
                }
                else
                {
                    break;
                }
            }
            catch
            {
                break;
            }
        }

        return count;
    }

    private void ProcessSyncVarField(TypeDefinition type, FieldDefinition field, int slotIndex)
    {
        Console.WriteLine($"SyncVarProcessor: Processing {field.Name} at slot {slotIndex}");

        var attribute = field.CustomAttributes.First(a => a.AttributeType.FullName == SyncVarAttributeName);
        var hookName = GetHookName(attribute);

        var originalName = field.Name;

        // Mirror approach: Keep the original field as the backing field
        // Create a property with a different name (Network{Name}) to avoid confusion
        // Field access is rewritten to use property (except in constructors)

        // 1. Create the property (using Network prefix like Mirror)
        var propertyName = $"Network{originalName}";
        var property = new PropertyDefinition(propertyName, PropertyAttributes.None, field.FieldType);

        // 2. Create getter - returns the field value
        var getter = CreateGetter(type, field, propertyName, slotIndex);
        property.GetMethod = getter;
        type.Methods.Add(getter);

        // 3. Create setter with dirty tracking
        var setter = CreateSetter(type, field, propertyName, slotIndex, hookName);
        property.SetMethod = setter;
        type.Methods.Add(setter);

        type.Properties.Add(property);

        // Track field -> setter/getter for rewriting field references (Mirror's approach)
        _replacementSetterProperties[field] = setter;
        _replacementGetterProperties[field] = getter;
        Console.WriteLine($"SyncVarProcessor: Registered field '{field.FullName}' -> property '{propertyName}'");

        // Remove the [SyncVar] attribute from the field (it's been processed)
        field.CustomAttributes.Remove(attribute);
    }

    private string? GetHookName(CustomAttribute attribute)
    {
        var hookProp = attribute.Properties.FirstOrDefault(p => p.Name == "hook");
        if (hookProp.Argument.Value != null)
        {
            return hookProp.Argument.Value as string;
        }
        return null;
    }

    private MethodDefinition CreateGetter(TypeDefinition type, FieldDefinition backingField, string propertyName, int slotIndex)
    {
        var getter = new MethodDefinition(
            $"get_{propertyName}",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
            backingField.FieldType);

        var il = getter.Body.GetILProcessor();

        // Generated IL equivalent:
        // var syncValue = this.SyncData[slotIndex];
        // if (syncValue != null)
        //     return (T)syncValue;
        // return this._backingField;
        //
        // This ensures:
        // - Server with field initializers: SyncData is null, returns backing field (initial value)
        // - Client after network sync: SyncData has value from server, returns that

        var entityBehaviourType = GetEntityBehaviourType().Resolve();
        var syncDataProperty = entityBehaviourType.Properties.First(p => p.Name == "SyncData");
        var getSyncData = _module.ImportReference(syncDataProperty.GetMethod);

        // Local variable to store the sync value
        getter.Body.Variables.Add(new VariableDefinition(_module.TypeSystem.Object));

        var returnSyncValue = il.Create(OpCodes.Nop);
        var returnBackingField = il.Create(OpCodes.Nop);

        // var syncValue = this.SyncData[slotIndex];
        il.Emit(OpCodes.Ldarg_0);                              // this
        il.Emit(OpCodes.Call, getSyncData);                    // this.SyncData
        il.Emit(OpCodes.Ldc_I4, slotIndex);                    // slotIndex
        il.Emit(OpCodes.Ldelem_Ref);                           // SyncData[slotIndex]
        il.Emit(OpCodes.Stloc_0);                              // store in local

        // if (syncValue != null)
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Brtrue, returnSyncValue);

        // return this._backingField;
        il.Append(returnBackingField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, backingField);
        il.Emit(OpCodes.Ret);

        // return (T)syncValue;
        il.Append(returnSyncValue);
        il.Emit(OpCodes.Ldloc_0);
        if (backingField.FieldType.IsValueType)
        {
            il.Emit(OpCodes.Unbox_Any, backingField.FieldType);
        }
        else
        {
            il.Emit(OpCodes.Castclass, backingField.FieldType);
        }
        il.Emit(OpCodes.Ret);

        return getter;
    }

    private MethodDefinition CreateSetter(TypeDefinition type, FieldDefinition backingField, string propertyName, int slotIndex, string? hookName)
    {
        var setter = new MethodDefinition(
            $"set_{propertyName}",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
            _module.TypeSystem.Void);

        setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, backingField.FieldType));

        var il = setter.Body.GetILProcessor();

        // Get references to EntityBehaviour methods
        var entityBehaviourType = GetEntityBehaviourType();
        var syncDataProperty = entityBehaviourType.Resolve().Properties.First(p => p.Name == "SyncData");
        var getSyncData = _module.ImportReference(syncDataProperty.GetMethod);
        var setDirtyMethod = entityBehaviourType.Resolve().Methods.First(m => m.Name == "SetDirty");
        var setDirty = _module.ImportReference(setDirtyMethod);

        VariableDefinition? oldValueVar = null;
        MethodDefinition? hookMethod = null;

        // If we have a hook, we need to store the old value first
        if (hookName != null)
        {
            hookMethod = FindHookMethod(type, hookName, backingField.FieldType);
            if (hookMethod != null)
            {
                oldValueVar = new VariableDefinition(backingField.FieldType);
                setter.Body.Variables.Add(oldValueVar);

                // var oldValue = this.field;
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, backingField);
                il.Emit(OpCodes.Stloc, oldValueVar);
            }
            else
            {
                Console.WriteLine($"SyncVarProcessor: WARNING - Hook method '{hookName}' not found in {type.FullName}");
            }
        }

        // this.field = value;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, backingField);

        // this.SyncData[slotIndex] = value;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getSyncData);
        il.Emit(OpCodes.Ldc_I4, slotIndex);
        il.Emit(OpCodes.Ldarg_1);
        if (backingField.FieldType.IsValueType)
        {
            il.Emit(OpCodes.Box, backingField.FieldType);
        }
        il.Emit(OpCodes.Stelem_Ref);

        // this.SetDirty(slotIndex);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, slotIndex);
        il.Emit(OpCodes.Call, setDirty);

        // Call hook if present
        if (hookMethod != null && oldValueVar != null)
        {
            // this.OnXxxChanged(oldValue, value);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, oldValueVar);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _module.ImportReference(hookMethod));
        }

        il.Emit(OpCodes.Ret);

        return setter;
    }

    private MethodDefinition? FindHookMethod(TypeDefinition type, string hookName, TypeReference paramType)
    {
        // Look for method: void hookName(T oldValue, T newValue)
        foreach (var method in type.Methods)
        {
            if (method.Name == hookName &&
                method.Parameters.Count == 2 &&
                method.Parameters[0].ParameterType.FullName == paramType.FullName &&
                method.Parameters[1].ParameterType.FullName == paramType.FullName &&
                method.ReturnType.FullName == "System.Void")
            {
                return method;
            }
        }
        return null;
    }

    private void RewriteFieldReferences(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
                continue;

            // Mirror's skip conditions:
            if (method.Name == ".cctor" || method.Name == ".ctor")
                continue;

            if (method.IsAbstract)
                continue;

            // Skip the generated getter/setter methods
            if (method.IsSpecialName && (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")))
                continue;

            RewriteFieldReferencesInMethod(method);
        }
    }

    private void RewriteFieldReferencesInMethod(MethodDefinition method)
    {
        if (!method.HasBody)
            return;

        // Mirror's approach: iterate through instructions and check if operand is FieldDefinition
        for (int i = 0; i < method.Body.Instructions.Count; i++)
        {
            var instruction = method.Body.Instructions[i];

            // stfld (sets value of a field)?
            if (instruction.OpCode == OpCodes.Stfld)
            {
                // operand is a FieldDefinition in the same assembly? (Mirror's check)
                if (instruction.Operand is FieldDefinition opField)
                {
                    if (_replacementSetterProperties.TryGetValue(opField, out var replacement))
                    {
                        Console.WriteLine($"SyncVarProcessor: Rewriting stfld {opField.Name} -> {replacement.Name} in {method.FullName}");
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = replacement;
                    }
                }
            }
            // ldfld (load value of a field)?
            else if (instruction.OpCode == OpCodes.Ldfld)
            {
                // operand is a FieldDefinition in the same assembly?
                if (instruction.Operand is FieldDefinition opField)
                {
                    if (_replacementGetterProperties.TryGetValue(opField, out var replacement))
                    {
                        Console.WriteLine($"SyncVarProcessor: Rewriting ldfld {opField.Name} -> {replacement.Name} in {method.FullName}");
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = replacement;
                    }
                }
            }
            // ldflda (load field address) - warn for now
            else if (instruction.OpCode == OpCodes.Ldflda)
            {
                if (instruction.Operand is FieldDefinition opField)
                {
                    if (_replacementSetterProperties.ContainsKey(opField))
                    {
                        Console.WriteLine($"SyncVarProcessor: WARNING - Cannot take address of SyncVar field {opField.Name} in {method.FullName}");
                    }
                }
            }
        }
    }


    private TypeReference GetEntityBehaviourType()
    {
        // Find EntityBehaviour in referenced assemblies
        foreach (var asm in _module.AssemblyReferences)
        {
            try
            {
                var resolved = _module.AssemblyResolver.Resolve(asm);
                var type = resolved.MainModule.Types.FirstOrDefault(t => t.FullName == EntityBehaviourTypeName);
                if (type != null)
                {
                    return _module.ImportReference(type);
                }
            }
            catch
            {
                continue;
            }
        }

        // Check the current module
        var localType = _module.Types.FirstOrDefault(t => t.FullName == EntityBehaviourTypeName);
        if (localType != null)
        {
            return localType;
        }

        throw new InvalidOperationException("EntityBehaviour type not found");
    }

    private bool InheritsFromEntityBehaviour(TypeDefinition type)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (current.FullName == EntityBehaviourTypeName)
                return true;

            try
            {
                var resolved = current.Resolve();
                current = resolved?.BaseType;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }
}
