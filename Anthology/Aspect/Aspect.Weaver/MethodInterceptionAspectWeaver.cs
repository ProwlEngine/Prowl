using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.Linq;

namespace Aspect.Weaver;

/// <summary>
/// Weaves MethodInterceptionAspect interceptors into methods.
/// </summary>
public class MethodInterceptionAspectWeaver : WeaverBase
{
    public MethodInterceptionAspectWeaver(ModuleDefinition module) : base(module)
    {
    }

    public void WeaveMethod(MethodDefinition method)
    {
        var aspectAttributes = method.CustomAttributes
            .Where(attr => IsMethodInterceptionAspect(attr.AttributeType))
            .ToList();

        if (method.DeclaringType != null)
        {
            var classAspects = method.DeclaringType.CustomAttributes
                .Where(attr => IsMethodInterceptionAspect(attr.AttributeType))
                .ToList();
            aspectAttributes.AddRange(classAspects);
        }

        if (!aspectAttributes.Any())
            return;

        Console.WriteLine($"  Weaving method interception: {method.FullName}");

        // For now, only support one aspect
        WeaveMethodWithAspect(method, aspectAttributes[0]);
    }

    private void WeaveMethodWithAspect(MethodDefinition method, CustomAttribute aspectAttribute)
    {
        if (method.Body == null || !method.HasBody)
            return;

        method.Body.SimplifyMacros();
        method.Body.InitLocals = true;

        // Step 1: Clone the original method body into a helper
        var originalMethod = CloneMethodBody(method);

        // Step 2: Create a "Proceed helper" that takes MethodInterceptionArgs,
        // extracts arguments, calls the cloned method, and stores the return value
        var proceedHelper = CreateProceedHelper(method, originalMethod);

        // Step 3: Replace the original method body with interception code
        ReplaceMethodBodyWithInterception(method, aspectAttribute, proceedHelper);

        method.Body.OptimizeMacros();
    }

    private MethodDefinition CloneMethodBody(MethodDefinition method)
    {
        var cloneName = $"<{method.Name}>__Original";
        var clone = new MethodDefinition(
            cloneName,
            MethodAttributes.Private | (method.IsStatic ? MethodAttributes.Static : 0),
            method.ReturnType);

        // Copy parameters
        foreach (var param in method.Parameters)
        {
            clone.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, param.ParameterType));
        }

        // Copy body
        clone.Body.InitLocals = method.Body.InitLocals;
        foreach (var variable in method.Body.Variables)
        {
            clone.Body.Variables.Add(variable);
        }
        foreach (var instruction in method.Body.Instructions)
        {
            clone.Body.Instructions.Add(instruction);
        }
        foreach (var handler in method.Body.ExceptionHandlers)
        {
            clone.Body.ExceptionHandlers.Add(handler);
        }

        method.DeclaringType.Methods.Add(clone);
        return clone;
    }

    private MethodDefinition CreateProceedHelper(MethodDefinition originalMethod, MethodDefinition clonedMethod)
    {
        var helperName = $"<{originalMethod.Name}>__ProceedHelper";
        var argsType = FindType("Aspect.MethodInterceptionArgs");

        var helper = new MethodDefinition(
            helperName,
            MethodAttributes.Private | MethodAttributes.Static,
            _module.TypeSystem.Void);

        // Add parameter: MethodInterceptionArgs
        helper.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, _module.ImportReference(argsType)));

        helper.Body.InitLocals = true;
        var processor = helper.Body.GetILProcessor();

        // Load 'this' if non-static (from args.Instance)
        if (!originalMethod.IsStatic)
        {
            var instanceProp = argsType.Properties.FirstOrDefault(p => p.Name == "Instance");
            processor.Emit(OpCodes.Ldarg_0); // args
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(instanceProp.GetMethod));
            processor.Emit(OpCodes.Castclass, originalMethod.DeclaringType);
        }

        // Load arguments from args.Arguments
        var argumentsType = FindType("Aspect.Arguments");
        var argumentsProp = argsType.Properties.FirstOrDefault(p => p.Name == "Arguments");
        var indexerProp = argumentsType.Properties.FirstOrDefault(p => p.Name == "Item");

        for (int i = 0; i < originalMethod.Parameters.Count; i++)
        {
            processor.Emit(OpCodes.Ldarg_0); // args
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(argumentsProp.GetMethod));
            processor.Emit(OpCodes.Ldc_I4, i);
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(indexerProp.GetMethod));

            var paramType = originalMethod.Parameters[i].ParameterType;
            EmitUnboxOrCast(processor, paramType);
        }

        // Call the cloned original method
        processor.Emit(originalMethod.IsStatic ? OpCodes.Call : OpCodes.Callvirt, clonedMethod);

        // Store return value in args.ReturnValue (if not void)
        if (originalMethod.ReturnType.FullName != "System.Void")
        {
            var returnValueProp = argsType.Properties.FirstOrDefault(p => p.Name == "ReturnValue");

            EmitBoxIfNeeded(processor, originalMethod.ReturnType);

            processor.Emit(OpCodes.Stloc_0); // temp store
            processor.Emit(OpCodes.Ldarg_0); // args
            processor.Emit(OpCodes.Ldloc_0); // return value
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(returnValueProp.SetMethod));
        }

        processor.Emit(OpCodes.Ret);

        // Add local variable if we stored return value
        if (originalMethod.ReturnType.FullName != "System.Void")
        {
            helper.Body.Variables.Add(new VariableDefinition(_module.TypeSystem.Object));
        }

        originalMethod.DeclaringType.Methods.Add(helper);
        return helper;
    }

    private void ReplaceMethodBodyWithInterception(MethodDefinition method, CustomAttribute aspectAttribute, MethodDefinition proceedHelper)
    {
        // Clear the method body
        method.Body.Instructions.Clear();
        method.Body.Variables.Clear();
        method.Body.ExceptionHandlers.Clear();

        var processor = method.Body.GetILProcessor();
        var argsType = FindType("Aspect.MethodInterceptionArgs");

        // Create local variables
        var aspectVar = new VariableDefinition(_module.ImportReference(aspectAttribute.AttributeType));
        var argsVar = new VariableDefinition(_module.ImportReference(argsType));
        var returnVar = method.ReturnType.FullName != "System.Void"
            ? new VariableDefinition(_module.ImportReference(method.ReturnType))
            : null;

        method.Body.Variables.Add(aspectVar);
        method.Body.Variables.Add(argsVar);
        if (returnVar != null)
            method.Body.Variables.Add(returnVar);

        // Create aspect instance
        var aspectTypeDef = aspectAttribute.AttributeType.Resolve();
        var aspectCtor = aspectTypeDef.GetConstructors().FirstOrDefault(c => !c.IsStatic && c.Parameters.Count == 0);
        if (aspectCtor != null)
        {
            processor.Emit(OpCodes.Newobj, _module.ImportReference(aspectCtor));
            processor.Emit(OpCodes.Stloc, aspectVar);
        }

        // Create MethodInterceptionArgs
        var argsCtor = argsType.GetConstructors().FirstOrDefault(c => !c.IsStatic && c.Parameters.Count == 0);
        if (argsCtor != null)
        {
            processor.Emit(OpCodes.Newobj, _module.ImportReference(argsCtor));
            processor.Emit(OpCodes.Stloc, argsVar);
        }

        // Populate args.Method, args.Instance, args.Arguments
        PopulateMethodInterceptionArgs(processor, method, argsVar);

        // Set args.ProceedDelegate = new Action<MethodInterceptionArgs>(proceedHelper)
        var proceedDelegateProp = argsType.Properties.FirstOrDefault(p => p.Name == "ProceedDelegate");
        if (proceedDelegateProp != null)
        {
            processor.Emit(OpCodes.Ldloc, argsVar);
            processor.Emit(OpCodes.Ldnull); // target for static method
            processor.Emit(OpCodes.Ldftn, proceedHelper);

            var actionCtor = _module.ImportReference(
                typeof(Action<>).MakeGenericType(typeof(object)).GetConstructor(new[] { typeof(object), typeof(IntPtr) }));
            // We need Action<MethodInterceptionArgs>, let me import it properly
            var actionType = new GenericInstanceType(_module.ImportReference(typeof(Action<>)));
            actionType.GenericArguments.Add(_module.ImportReference(argsType));
            var actionCtorRef = new MethodReference(".ctor", _module.TypeSystem.Void, actionType)
            {
                HasThis = true,
                Parameters = {
                    new ParameterDefinition(_module.TypeSystem.Object),
                    new ParameterDefinition(_module.TypeSystem.IntPtr)
                }
            };

            processor.Emit(OpCodes.Newobj, actionCtorRef);
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(proceedDelegateProp.SetMethod));
        }

        // Call aspect.OnInvoke(args)
        var onInvokeMethod = aspectTypeDef.Methods.FirstOrDefault(m => m.Name == "OnInvoke");
        if (onInvokeMethod != null)
        {
            processor.Emit(OpCodes.Ldloc, aspectVar);
            processor.Emit(OpCodes.Ldloc, argsVar);
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(onInvokeMethod));
        }

        // Get return value from args.ReturnValue if needed
        if (returnVar != null)
        {
            var returnValueProp = argsType.Properties.FirstOrDefault(p => p.Name == "ReturnValue");
            processor.Emit(OpCodes.Ldloc, argsVar);
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(returnValueProp.GetMethod));

            EmitUnboxOrCast(processor, method.ReturnType);

            processor.Emit(OpCodes.Stloc, returnVar);
            processor.Emit(OpCodes.Ldloc, returnVar);
        }

        processor.Emit(OpCodes.Ret);
    }

    private void PopulateMethodInterceptionArgs(ILProcessor processor, MethodDefinition method, VariableDefinition argsVar)
    {
        var argsType = FindType("Aspect.MethodInterceptionArgs");

        // Set Method property
        var methodProp = argsType.Properties.FirstOrDefault(p => p.Name == "Method");
        if (methodProp != null)
        {
            processor.Emit(OpCodes.Ldloc, argsVar);
            processor.Emit(OpCodes.Ldtoken, method);
            if (method.DeclaringType.HasGenericParameters)
            {
                processor.Emit(OpCodes.Ldtoken, method.DeclaringType);
            }
            var getMethodFromHandle = _module.ImportReference(
                typeof(System.Reflection.MethodBase).GetMethod(
                    "GetMethodFromHandle",
                    method.DeclaringType.HasGenericParameters
                        ? new[] { typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle) }
                        : new[] { typeof(RuntimeMethodHandle) }
                )
            );
            processor.Emit(OpCodes.Call, getMethodFromHandle);
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(methodProp.SetMethod));
        }

        // Set Instance property (non-static only)
        if (!method.IsStatic)
        {
            var instanceProp = argsType.Properties.FirstOrDefault(p => p.Name == "Instance");
            if (instanceProp != null)
            {
                processor.Emit(OpCodes.Ldloc, argsVar);
                processor.Emit(OpCodes.Ldarg_0);
                if (method.DeclaringType.IsValueType)
                {
                    processor.Emit(OpCodes.Ldobj, method.DeclaringType);
                    processor.Emit(OpCodes.Box, method.DeclaringType);
                }
                processor.Emit(OpCodes.Callvirt, _module.ImportReference(instanceProp.SetMethod));
            }
        }

        // Set Arguments property
        var argumentsProp = argsType.Properties.FirstOrDefault(p => p.Name == "Arguments");
        var argumentsType = FindType("Aspect.Arguments");
        var argumentsCtor = argumentsType.GetConstructors().FirstOrDefault(c => !c.IsStatic && c.Parameters.Count == 1);

        if (argumentsProp != null && argumentsCtor != null)
        {
            // Create object[]
            processor.Emit(OpCodes.Ldc_I4, method.Parameters.Count);
            processor.Emit(OpCodes.Newarr, _module.TypeSystem.Object);

            // Fill array
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                processor.Emit(OpCodes.Dup);
                processor.Emit(OpCodes.Ldc_I4, i);
                processor.Emit(OpCodes.Ldarg, method.Parameters[i]);

                EmitBoxIfNeeded(processor, method.Parameters[i].ParameterType);

                processor.Emit(OpCodes.Stelem_Ref);
            }

            // Create Arguments(object[])
            processor.Emit(OpCodes.Newobj, _module.ImportReference(argumentsCtor));

            // Set property
            var tempVar = new VariableDefinition(_module.ImportReference(argumentsType));
            method.Body.Variables.Add(tempVar);
            processor.Emit(OpCodes.Stloc, tempVar);
            processor.Emit(OpCodes.Ldloc, argsVar);
            processor.Emit(OpCodes.Ldloc, tempVar);
            processor.Emit(OpCodes.Callvirt, _module.ImportReference(argumentsProp.SetMethod));
        }
    }

    private bool IsMethodInterceptionAspect(TypeReference typeRef)
    {
        try
        {
            var typeDef = typeRef.Resolve();
            if (typeDef == null) return false;

            var current = typeDef;
            while (current != null)
            {
                if (current.FullName == "Aspect.MethodInterceptionAspect")
                    return true;

                current = current.BaseType?.Resolve();
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

}
