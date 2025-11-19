using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.Linq;

namespace Aspect.Weaver;

/// <summary>
/// Weaves LocationInterceptionAspect interceptors into property getters and setters.
/// </summary>
public class LocationInterceptionAspectWeaver
{
    private readonly ModuleDefinition _module;

    public LocationInterceptionAspectWeaver(ModuleDefinition module)
    {
        _module = module;
    }

    /// <summary>
    /// Transforms fields with LocationInterceptionAspect attributes into properties.
    /// The original field is renamed to a backing field and made private.
    /// </summary>
    public void TransformFieldsToProperties(TypeDefinition type)
    {
        Console.WriteLine($"DEBUG: TransformFieldsToProperties called for type: {type.FullName}");
        Console.WriteLine($"DEBUG: Type has {type.Fields.Count} fields");

        // Find all fields with LocationInterceptionAspect attributes
        var fieldsToTransform = type.Fields
            .Where(f => f.CustomAttributes.Any(attr => IsLocationInterceptionAspect(attr.AttributeType)))
            .ToList();

        Console.WriteLine($"DEBUG: Found {fieldsToTransform.Count} fields to transform");

        foreach (var field in fieldsToTransform)
        {
            Console.WriteLine($"  Transforming field to property: {field.FullName}");

            // Get the aspect attributes from the field
            var aspectAttributes = field.CustomAttributes
                .Where(attr => IsLocationInterceptionAspect(attr.AttributeType))
                .ToList();

            // Create a backing field with a unique name
            var backingFieldName = $"<{field.Name}>k__BackingField";
            var backingField = new FieldDefinition(backingFieldName, FieldAttributes.Private, field.FieldType);

            // Add the backing field to the type FIRST (before creating getter/setter that reference it)
            type.Fields.Add(backingField);

            // Remove the original field now
            type.Fields.Remove(field);

            // Create the property
            var property = new PropertyDefinition(field.Name, PropertyAttributes.None, field.FieldType);

            // Create getter
            var getter = new MethodDefinition($"get_{field.Name}",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
                (field.IsStatic ? MethodAttributes.Static : 0),
                field.FieldType);
            getter.Body.InitLocals = true;

            var getterProcessor = getter.Body.GetILProcessor();
            if (!field.IsStatic)
            {
                getterProcessor.Emit(OpCodes.Ldarg_0); // this
            }
            getterProcessor.Emit(field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, _module.ImportReference(backingField));
            getterProcessor.Emit(OpCodes.Ret);

            // Create setter
            var setter = new MethodDefinition($"set_{field.Name}",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
                (field.IsStatic ? MethodAttributes.Static : 0),
                _module.TypeSystem.Void);
            setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, field.FieldType));
            setter.Body.InitLocals = true;

            var setterProcessor = setter.Body.GetILProcessor();
            if (!field.IsStatic)
            {
                setterProcessor.Emit(OpCodes.Ldarg_0); // this
                setterProcessor.Emit(OpCodes.Ldarg_1); // value
            }
            else
            {
                setterProcessor.Emit(OpCodes.Ldarg_0); // value
            }
            setterProcessor.Emit(field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld, _module.ImportReference(backingField));
            setterProcessor.Emit(OpCodes.Ret);

            // Add getter and setter to the property
            property.GetMethod = getter;
            property.SetMethod = setter;

            // Transfer attributes from field to property
            foreach (var attr in aspectAttributes)
            {
                // Add to property (field is already removed, so no need to remove from it)
                property.CustomAttributes.Add(attr);
            }

            // Add the property and methods to the type
            type.Properties.Add(property);
            type.Methods.Add(getter);
            type.Methods.Add(setter);

            Console.WriteLine($"    Created property: {property.FullName}");
            Console.WriteLine($"    Created backing field: {backingField.FullName}");
        }
    }

    public void WeaveProperty(PropertyDefinition property)
    {
        // Find all LocationInterceptionAspect attributes on this property
        var aspectAttributes = property.CustomAttributes
            .Where(attr => IsLocationInterceptionAspect(attr.AttributeType))
            .ToList();

        // Also check class-level attributes (would need filtering)
        if (property.DeclaringType != null)
        {
            var classAspects = property.DeclaringType.CustomAttributes
                .Where(attr => IsLocationInterceptionAspect(attr.AttributeType))
                .ToList();
            aspectAttributes.AddRange(classAspects);
        }

        if (!aspectAttributes.Any())
            return;

        Console.WriteLine($"  Weaving property: {property.FullName} with {aspectAttributes.Count} aspect(s)");

        // Weave getter if it exists
        if (property.GetMethod != null)
        {
            foreach (var aspectAttr in aspectAttributes)
            {
                WeavePropertyGetter(property, property.GetMethod, aspectAttr);
            }
        }

        // Weave setter if it exists
        if (property.SetMethod != null)
        {
            foreach (var aspectAttr in aspectAttributes)
            {
                WeavePropertySetter(property, property.SetMethod, aspectAttr);
            }
        }
    }

    private void WeavePropertyGetter(PropertyDefinition property, MethodDefinition getter, CustomAttribute aspectAttribute)
    {
        if (getter.Body == null || !getter.HasBody)
            return;

        var processor = getter.Body.GetILProcessor();
        getter.Body.InitLocals = true;

        // Save original instructions
        var originalInstructions = getter.Body.Instructions.ToList();
        var originalVariables = getter.Body.Variables.ToList();

        // Clear current body
        getter.Body.Instructions.Clear();
        getter.Body.Variables.Clear();

        // Re-add original variables
        foreach (var v in originalVariables)
        {
            getter.Body.Variables.Add(v);
        }

        // Create new local variables
        var aspectVar = new VariableDefinition(_module.ImportReference(aspectAttribute.AttributeType));
        var argsVar = new VariableDefinition(_module.ImportReference(FindType("Aspect.LocationInterceptionArgs")));
        getter.Body.Variables.Add(aspectVar);
        getter.Body.Variables.Add(argsVar);

        // 1. Create aspect instance
        var aspectTypeDef = aspectAttribute.AttributeType.Resolve();
        var aspectCtor = aspectTypeDef.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);
        if (aspectCtor != null)
        {
            processor.Emit(OpCodes.Newobj, _module.ImportReference(aspectCtor));
            processor.Emit(OpCodes.Stloc, aspectVar);
        }

        // 2. Create LocationInterceptionArgs
        var argsType = FindType("Aspect.LocationInterceptionArgs");
        var argsCtor = argsType.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);
        processor.Emit(OpCodes.Newobj, _module.ImportReference(argsCtor));
        processor.Emit(OpCodes.Stloc, argsVar);

        // 3. Set Property property
        EmitSetProperty(processor, argsVar, property);

        // 4. Set Instance property (if non-static)
        if (!getter.IsStatic)
        {
            EmitSetInstance(processor, argsVar, getter);
        }

        // 5. Set GetValueAction delegate (simplified version)
        // TODO: Implement proper closure for ProceedGetValue
        // For now, create a simple helper method
        var helperMethod = CreateGetValueHelper(property, getter, originalInstructions);
        EmitSetGetValueAction(processor, argsVar, helperMethod, getter.IsStatic);

        // 6. Call aspect.OnGetValue(args)
        var onGetValueMethod = aspectTypeDef.Methods.FirstOrDefault(m => m.Name == "OnGetValue");
        if (onGetValueMethod != null)
        {
            processor.Emit(OpCodes.Ldloc, aspectVar);
            processor.Emit(OpCodes.Ldloc, argsVar);
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(onGetValueMethod));
        }

        // 7. Get value from args.Value and return
        var valueProperty = argsType.Properties.FirstOrDefault(p => p.Name == "Value");
        if (valueProperty?.GetMethod != null)
        {
            processor.Emit(OpCodes.Ldloc, argsVar);
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(valueProperty.GetMethod));

            // Unbox or cast to property type
            if (property.PropertyType.IsValueType)
            {
                processor.Emit(OpCodes.Unbox_Any, property.PropertyType);
            }
            else if (property.PropertyType.FullName != "System.Object")
            {
                processor.Emit(OpCodes.Castclass, property.PropertyType);
            }
        }

        processor.Emit(OpCodes.Ret);
    }

    private void WeavePropertySetter(PropertyDefinition property, MethodDefinition setter, CustomAttribute aspectAttribute)
    {
        if (setter.Body == null || !setter.HasBody)
            return;

        var processor = setter.Body.GetILProcessor();
        setter.Body.InitLocals = true;

        // Save original instructions
        var originalInstructions = setter.Body.Instructions.ToList();
        var originalVariables = setter.Body.Variables.ToList();

        // Clear current body
        setter.Body.Instructions.Clear();
        setter.Body.Variables.Clear();

        // Re-add original variables
        foreach (var v in originalVariables)
        {
            setter.Body.Variables.Add(v);
        }

        // Create new local variables
        var aspectVar = new VariableDefinition(_module.ImportReference(aspectAttribute.AttributeType));
        var argsVar = new VariableDefinition(_module.ImportReference(FindType("Aspect.LocationInterceptionArgs")));
        setter.Body.Variables.Add(aspectVar);
        setter.Body.Variables.Add(argsVar);

        // 1. Create aspect instance
        var aspectTypeDef = aspectAttribute.AttributeType.Resolve();
        var aspectCtor = aspectTypeDef.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);
        if (aspectCtor != null)
        {
            processor.Emit(OpCodes.Newobj, _module.ImportReference(aspectCtor));
            processor.Emit(OpCodes.Stloc, aspectVar);
        }

        // 2. Create LocationInterceptionArgs
        var argsType = FindType("Aspect.LocationInterceptionArgs");
        var argsCtor = argsType.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);
        processor.Emit(OpCodes.Newobj, _module.ImportReference(argsCtor));
        processor.Emit(OpCodes.Stloc, argsVar);

        // 3. Set Property property
        EmitSetProperty(processor, argsVar, property);

        // 4. Set Instance property (if non-static)
        if (!setter.IsStatic)
        {
            EmitSetInstance(processor, argsVar, setter);
        }

        // 5. Set Value property with the incoming parameter value
        var valueProperty = argsType.Properties.FirstOrDefault(p => p.Name == "Value");
        if (valueProperty?.SetMethod != null)
        {
            processor.Emit(OpCodes.Ldloc, argsVar);
            processor.Emit(setter.IsStatic ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1); // Load value parameter

            // Box if value type
            if (property.PropertyType.IsValueType)
            {
                processor.Emit(OpCodes.Box, property.PropertyType);
            }

            processor.Emit(OpCodes.Callvirt, _module.ImportReference(valueProperty.SetMethod));
        }

        // 6. Create helper and set SetValueAction
        var helperMethod = CreateSetValueHelper(property, setter, originalInstructions);
        EmitSetSetValueAction(processor, argsVar, helperMethod, setter.IsStatic);

        // 7. Call aspect.OnSetValue(args)
        var onSetValueMethod = aspectTypeDef.Methods.FirstOrDefault(m => m.Name == "OnSetValue");
        if (onSetValueMethod != null)
        {
            processor.Emit(OpCodes.Ldloc, aspectVar);
            processor.Emit(OpCodes.Ldloc, argsVar);
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(onSetValueMethod));
        }

        processor.Emit(OpCodes.Ret);
    }

    private bool IsLocationInterceptionAspect(TypeReference typeRef)
    {
        var typeDef = typeRef.Resolve();
        if (typeDef == null) return false;

        // Check if it inherits from LocationInterceptionAspect
        var current = typeDef;
        while (current != null)
        {
            if (current.FullName == "Aspect.LocationInterceptionAspect")
                return true;

            current = current.BaseType?.Resolve();
        }

        return false;
    }

    private TypeDefinition FindType(string fullName)
    {
        // Try current module first
        var type = _module.Types.FirstOrDefault(t => t.FullName == fullName)
            ?? _module.GetType(fullName);

        if (type != null)
            return type;

        // Search in referenced assemblies
        foreach (var assemblyRef in _module.AssemblyReferences)
        {
            try
            {
                var assembly = _module.AssemblyResolver.Resolve(assemblyRef);
                type = assembly.MainModule.GetType(fullName);
                if (type != null)
                    return type;
            }
            catch
            {
                // Skip assemblies that can't be resolved
            }
        }

        throw new InvalidOperationException($"Type {fullName} not found");
    }

    private MethodDefinition CreateGetValueHelper(PropertyDefinition property, MethodDefinition getter, List<Instruction> originalInstructions)
    {
        // Create helper method that reads from thread-static args field and executes original getter
        var helperName = $"<{property.Name}>__GetValueHelper";
        var helper = new MethodDefinition(helperName,
            MethodAttributes.Private | (getter.IsStatic ? MethodAttributes.Static : 0) | MethodAttributes.HideBySig,
            _module.TypeSystem.Void);

        helper.Body.InitLocals = getter.Body.InitLocals;
        var processor = helper.Body.GetILProcessor();

        // Get the thread-static args field
        var argsField = GetOrCreateArgsField(property);

        // Copy original local variables
        foreach (var variable in getter.Body.Variables)
        {
            helper.Body.Variables.Add(new VariableDefinition(variable.VariableType));
        }

        // Add local for args
        var argsLocal = new VariableDefinition(_module.ImportReference(FindType("Aspect.LocationInterceptionArgs")));
        helper.Body.Variables.Add(argsLocal);
        var argsLocalIndex = helper.Body.Variables.Count - 1;

        // Load args from thread-static field
        processor.Emit(OpCodes.Ldsfld, argsField);
        processor.Emit(OpCodes.Stloc, argsLocal);

        // Clone the original getter instructions
        var instructionMap = new Dictionary<Instruction, Instruction>();

        // First pass: create all instructions
        foreach (var originalInstr in originalInstructions)
        {
            var newInstr = CloneInstruction(originalInstr, helper);
            instructionMap[originalInstr] = newInstr;
            processor.Append(newInstr);
        }

        // Second pass: fix branch targets
        foreach (var kvp in instructionMap)
        {
            var newInstr = kvp.Value;
            if (newInstr.Operand is Instruction targetInstr && instructionMap.ContainsKey(targetInstr))
            {
                newInstr.Operand = instructionMap[targetInstr];
            }
            else if (newInstr.Operand is Instruction[] targetArray)
            {
                var newTargets = new Instruction[targetArray.Length];
                for (int i = 0; i < targetArray.Length; i++)
                {
                    if (instructionMap.ContainsKey(targetArray[i]))
                        newTargets[i] = instructionMap[targetArray[i]];
                }
                newInstr.Operand = newTargets;
            }
        }

        // Find all Ret instructions and replace them with code to set args.Value
        var retInstructions = helper.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
        foreach (var retInstr in retInstructions)
        {
            var index = helper.Body.Instructions.IndexOf(retInstr);

            // Before Ret, the return value is on the stack
            // We need to: box it (if needed), store it in args.Value, then return

            // Insert: stloc temp (to save return value)
            var tempVar = new VariableDefinition(property.PropertyType);
            helper.Body.Variables.Add(tempVar);
            processor.InsertBefore(retInstr, processor.Create(OpCodes.Stloc, tempVar));

            // Load args
            processor.InsertBefore(retInstr, processor.Create(OpCodes.Ldloc, argsLocal));

            // Load return value
            processor.InsertBefore(retInstr, processor.Create(OpCodes.Ldloc, tempVar));

            // Box if value type
            if (property.PropertyType.IsValueType)
            {
                processor.InsertBefore(retInstr, processor.Create(OpCodes.Box, property.PropertyType));
            }

            // Call args.set_Value
            var argsType = FindType("Aspect.LocationInterceptionArgs");
            var valueProp = argsType.Properties.FirstOrDefault(p => p.Name == "Value");
            if (valueProp?.SetMethod != null)
            {
                processor.InsertBefore(retInstr, processor.Create(OpCodes.Callvirt, _module.ImportReference(valueProp.SetMethod)));
            }
        }

        property.DeclaringType.Methods.Add(helper);
        return helper;
    }

    private FieldDefinition GetOrCreateArgsField(PropertyDefinition property)
    {
        var fieldName = $"<{property.Name}>__args";
        var existing = property.DeclaringType.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (existing != null)
            return existing;

        var argsType = FindType("Aspect.LocationInterceptionArgs");
        var field = new FieldDefinition(fieldName,
            FieldAttributes.Private | FieldAttributes.Static,
            _module.ImportReference(argsType));

        // Add ThreadStatic attribute
        var threadStaticAttr = new CustomAttribute(_module.ImportReference(
            typeof(System.ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)));
        field.CustomAttributes.Add(threadStaticAttr);

        property.DeclaringType.Fields.Add(field);
        return field;
    }

    private Instruction CloneInstruction(Instruction instr, MethodDefinition targetMethod)
    {
        var newInstr = Instruction.Create(OpCodes.Nop);
        newInstr.OpCode = instr.OpCode;
        newInstr.Operand = instr.Operand;
        return newInstr;
    }

    private void EmitSetProperty(ILProcessor processor, VariableDefinition argsVar, PropertyDefinition property)
    {
        var argsType = FindType("Aspect.LocationInterceptionArgs");
        var propertyProp = argsType.Properties.FirstOrDefault(p => p.Name == "Property");

        if (propertyProp?.SetMethod != null)
        {
            processor.Emit(OpCodes.Ldloc, argsVar);
            processor.Emit(OpCodes.Ldtoken, property.DeclaringType);
            processor.Emit(OpCodes.Call, _module.ImportReference(typeof(System.Type).GetMethod("GetTypeFromHandle")));
            processor.Emit(OpCodes.Ldstr, property.Name);

            // Call Type.GetProperty(string)
            var getPropertyMethod = typeof(System.Type).GetMethod("GetProperty", new[] { typeof(string) });
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(getPropertyMethod));
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(propertyProp.SetMethod));
        }
    }

    private void EmitSetInstance(ILProcessor processor, VariableDefinition argsVar, MethodDefinition getter)
    {
        var argsType = FindType("Aspect.LocationInterceptionArgs");
        var instanceProp = argsType.Properties.FirstOrDefault(p => p.Name == "Instance");

        if (instanceProp?.SetMethod != null)
        {
            processor.Emit(OpCodes.Ldloc, argsVar);
            processor.Emit(OpCodes.Ldarg_0); // this

            if (getter.DeclaringType.IsValueType)
            {
                processor.Emit(OpCodes.Ldobj, getter.DeclaringType);
                processor.Emit(OpCodes.Box, getter.DeclaringType);
            }

            processor.Emit(OpCodes.Callvirt, _module.ImportReference(instanceProp.SetMethod));
        }
    }

    private void EmitSetGetValueAction(ILProcessor processor, VariableDefinition argsVar, MethodDefinition helperMethod, bool isStatic)
    {
        var argsType = FindType("Aspect.LocationInterceptionArgs");
        var getValueActionProp = argsType.Properties.FirstOrDefault(p => p.Name == "GetValueAction");

        if (getValueActionProp?.SetMethod != null)
        {
            // Store args in thread-static field so helper can access it
            var argsField = helperMethod.DeclaringType.Fields.FirstOrDefault(f => f.Name.Contains(helperMethod.Name.Replace("__GetValueHelper", "__args")));
            if (argsField != null)
            {
                processor.Emit(OpCodes.Ldloc, argsVar);
                processor.Emit(OpCodes.Stsfld, argsField);
            }

            // Set GetValueAction property
            processor.Emit(OpCodes.Ldloc, argsVar);

            // Load instance if non-static (for delegate binding)
            if (!isStatic)
            {
                processor.Emit(OpCodes.Ldarg_0); // this
            }
            else
            {
                processor.Emit(OpCodes.Ldnull); // null for static
            }

            // Create delegate: ldftn + newobj Action
            processor.Emit(isStatic ? OpCodes.Ldftn : OpCodes.Ldftn, helperMethod);

            var actionCtor = typeof(Action).GetConstructor(new[] { typeof(object), typeof(IntPtr) });
            processor.Emit(OpCodes.Newobj, _module.ImportReference(actionCtor));

            processor.Emit(OpCodes.Callvirt, _module.ImportReference(getValueActionProp.SetMethod));
        }
    }

    private MethodDefinition CreateSetValueHelper(PropertyDefinition property, MethodDefinition setter, List<Instruction> originalInstructions)
    {
        // Create helper method that reads from thread-static args field and executes original setter
        var helperName = $"<{property.Name}>__SetValueHelper";
        var helper = new MethodDefinition(helperName,
            MethodAttributes.Private | (setter.IsStatic ? MethodAttributes.Static : 0) | MethodAttributes.HideBySig,
            _module.TypeSystem.Void);

        helper.Body.InitLocals = setter.Body.InitLocals;
        var processor = helper.Body.GetILProcessor();

        // Get the thread-static args field
        var argsField = GetOrCreateArgsField(property);

        // Copy original local variables
        foreach (var variable in setter.Body.Variables)
        {
            helper.Body.Variables.Add(new VariableDefinition(variable.VariableType));
        }

        // Add local for args
        var argsLocal = new VariableDefinition(_module.ImportReference(FindType("Aspect.LocationInterceptionArgs")));
        helper.Body.Variables.Add(argsLocal);

        // Load args from thread-static field
        processor.Emit(OpCodes.Ldsfld, argsField);
        processor.Emit(OpCodes.Stloc, argsLocal);

        // For auto-properties, directly access the backing field
        var argsType = FindType("Aspect.LocationInterceptionArgs");
        var valueProp = argsType.Properties.FirstOrDefault(p => p.Name == "Value");
        // Look for the backing field pattern: <PropertyName>k__BackingField
        var backingFieldName = $"<{property.Name}>k__BackingField";
        var backingField = property.DeclaringType.Fields.FirstOrDefault(f => f.Name == backingFieldName);

        if (backingField != null)
        {
            // CORRECT APPROACH: For non-static, load THIS first, then value
            // stfld expects: [instance, value]
            // stsfld expects: [value]

            // Load this (if non-static) - MUST come before loading value!
            if (!setter.IsStatic)
            {
                processor.Emit(OpCodes.Ldarg_0);
            }

            // Load value from args.Value
            processor.Emit(OpCodes.Ldloc, argsLocal);
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(valueProp.GetMethod));

            // Unbox/cast to property type
            if (property.PropertyType.IsValueType)
            {
                processor.Emit(OpCodes.Unbox_Any, property.PropertyType);
            }
            else if (property.PropertyType.FullName != "System.Object")
            {
                processor.Emit(OpCodes.Castclass, property.PropertyType);
            }

            // Store to backing field
            if (setter.IsStatic)
            {
                processor.Emit(OpCodes.Stsfld, backingField);
            }
            else
            {
                processor.Emit(OpCodes.Stfld, backingField);
            }

            processor.Emit(OpCodes.Ret);
        }
        else
        {
            // Complex property - need to clone original setter logic
            // This is more complex and we'll handle it later
            throw new NotImplementedException($"Property {property.Name} does not have an auto-property backing field. Complex property setters are not yet supported.");
        }

        property.DeclaringType.Methods.Add(helper);
        return helper;
    }

    private void EmitSetSetValueAction(ILProcessor processor, VariableDefinition argsVar, MethodDefinition helperMethod, bool isStatic)
    {
        var argsType = FindType("Aspect.LocationInterceptionArgs");
        var setValueActionProp = argsType.Properties.FirstOrDefault(p => p.Name == "SetValueAction");

        if (setValueActionProp?.SetMethod != null)
        {
            // Store args in thread-static field so helper can access it
            var argsField = helperMethod.DeclaringType.Fields.FirstOrDefault(f => f.Name.Contains(helperMethod.Name.Replace("__SetValueHelper", "__args")));
            if (argsField != null)
            {
                processor.Emit(OpCodes.Ldloc, argsVar);
                processor.Emit(OpCodes.Stsfld, argsField);
            }

            // Set SetValueAction property
            processor.Emit(OpCodes.Ldloc, argsVar);

            // Load instance if non-static (for delegate binding)
            if (!isStatic)
            {
                processor.Emit(OpCodes.Ldarg_0); // this
            }
            else
            {
                processor.Emit(OpCodes.Ldnull); // null for static
            }

            // Create delegate: ldftn + newobj Action
            processor.Emit(isStatic ? OpCodes.Ldftn : OpCodes.Ldftn, helperMethod);

            var actionCtor = typeof(Action).GetConstructor(new[] { typeof(object), typeof(IntPtr) });
            processor.Emit(OpCodes.Newobj, _module.ImportReference(actionCtor));

            processor.Emit(OpCodes.Callvirt, _module.ImportReference(setValueActionProp.SetMethod));
        }
    }
}
