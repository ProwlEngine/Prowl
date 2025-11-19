using Mono.Cecil;
using Mono.Cecil.Cil;
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
        // TODO: Implement property setter weaving
        // 1. Create aspect instance
        // 2. Create LocationInterceptionArgs with the new value
        // 3. Call OnSetValue
        // 4. Check if ProceedSetValue was called
        // 5. Set the backing field if proceeded

        Console.WriteLine($"    TODO: Weave setter for {property.Name}");
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
        return _module.Types.FirstOrDefault(t => t.FullName == fullName)
            ?? _module.GetType(fullName)
            ?? throw new InvalidOperationException($"Type {fullName} not found");
    }

    private MethodDefinition CreateGetValueHelper(PropertyDefinition property, MethodDefinition getter, List<Instruction> originalInstructions)
    {
        // Create helper method that reads from thread-static args field and executes original getter
        var helperName = $"<{property.Name}>__GetValueHelper";
        var helper = new MethodDefinition(helperName,
            MethodAttributes.Private | (getter.IsStatic ? MethodAttributes.Static : 0) | MethodAttributes.HideBySig,
            _module.TypeSystem.Void);

        helper.Body.InitLocals = true;
        var processor = helper.Body.GetILProcessor();

        // Get the thread-static args field
        var argsField = GetOrCreateArgsField(property);

        // Load args from field
        processor.Emit(OpCodes.Ldsfld, argsField);
        var argsLocal = new VariableDefinition(_module.ImportReference(FindType("Aspect.LocationInterceptionArgs")));
        helper.Body.Variables.Add(argsLocal);
        processor.Emit(OpCodes.Stloc, argsLocal);

        // Execute original getter body (simplified - just return value)
        // For now, call original getter and store result
        if (!getter.IsStatic)
        {
            processor.Emit(OpCodes.Ldarg_0); // this
        }

        // TODO: Clone original instructions properly
        // For now, create a simple value assignment

        processor.Emit(OpCodes.Ldloc, argsLocal);
        // Load value from original getter (call original)
        // Simplified: just set Value to null for now
        processor.Emit(OpCodes.Ldnull);

        // Set args.Value
        var argsType = FindType("Aspect.LocationInterceptionArgs");
        var valueProp = argsType.Properties.FirstOrDefault(p => p.Name == "Value");
        if (valueProp?.SetMethod != null)
        {
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(valueProp.SetMethod));
        }

        processor.Emit(OpCodes.Ret);

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
}
