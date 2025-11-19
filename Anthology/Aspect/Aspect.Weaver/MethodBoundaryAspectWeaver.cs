using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.Linq;

namespace Aspect.Weaver;

/// <summary>
/// Weaves OnMethodBoundaryAspect interceptors into methods.
/// </summary>
public class MethodBoundaryAspectWeaver
{
    private readonly ModuleDefinition _module;

    public MethodBoundaryAspectWeaver(ModuleDefinition module)
    {
        _module = module;
    }

    public void WeaveMethod(MethodDefinition method)
    {
        // Find all OnMethodBoundaryAspect attributes on this method
        var aspectAttributes = method.CustomAttributes
            .Where(attr => IsMethodBoundaryAspect(attr.AttributeType))
            .ToList();

        // Also check class-level attributes
        if (method.DeclaringType != null)
        {
            var classAspects = method.DeclaringType.CustomAttributes
                .Where(attr => IsMethodBoundaryAspect(attr.AttributeType))
                .ToList();
            aspectAttributes.AddRange(classAspects);
        }

        if (!aspectAttributes.Any())
            return;

        Console.WriteLine($"  Weaving method: {method.FullName} with {aspectAttributes.Count} aspect(s)");

        // Weave each aspect (in reverse order for proper nesting)
        foreach (var aspectAttr in aspectAttributes.AsEnumerable().Reverse())
        {
            WeaveMethodWithAspect(method, aspectAttr);
        }
    }

    private void WeaveMethodWithAspect(MethodDefinition method, CustomAttribute aspectAttribute)
    {
        // Skip methods without bodies (abstract, interface methods, etc.)
        if (method.Body == null || !method.HasBody)
            return;

        var processor = method.Body.GetILProcessor();
        method.Body.SimplifyMacros(); // Need this for branching instructions
        method.Body.InitLocals = true;

        // Create ALL local variables FIRST before emitting any IL
        var aspectVar = new VariableDefinition(_module.ImportReference(aspectAttribute.AttributeType));
        var argsVar = new VariableDefinition(_module.ImportReference(FindType("Aspect.MethodExecutionArgs")));
        var returnVar = method.ReturnType.FullName != "System.Void"
            ? new VariableDefinition(_module.ImportReference(method.ReturnType))
            : null;
        var exceptionVar = new VariableDefinition(_module.ImportReference(typeof(System.Exception)));

        // Temp variable for swapping Arguments property (always needed for all methods)
        var tempArgumentsVar = new VariableDefinition(_module.ImportReference(FindType("Aspect.Arguments")));

        // Add all variables to method body
        method.Body.Variables.Add(aspectVar);
        method.Body.Variables.Add(argsVar);
        if (returnVar != null)
        {
            method.Body.Variables.Add(returnVar);
        }
        method.Body.Variables.Add(exceptionVar);
        method.Body.Variables.Add(tempArgumentsVar);

        // Save the original first instruction (this will be tryStart)
        var originalFirstInstruction = method.Body.Instructions.First();

        // Insert prologue code before originalFirstInstruction
        // Use a simpler approach: insert each piece after the previous one
        Instruction? lastInserted = null;

        // Helper to insert after last or before original
        void InsertNext(Instruction instr)
        {
            if (lastInserted == null)
                processor.InsertBefore(originalFirstInstruction, instr);
            else
                processor.InsertAfter(lastInserted, instr);
            lastInserted = instr;
        }

        // 1. Create aspect instance
        var aspectTypeDef = aspectAttribute.AttributeType.Resolve();
        var aspectCtor = aspectTypeDef.GetConstructors().FirstOrDefault(c => !c.IsStatic && c.Parameters.Count == 0);
        if (aspectCtor != null)
        {
            InsertNext(Instruction.Create(OpCodes.Newobj, _module.ImportReference(aspectCtor)));
            InsertNext(Instruction.Create(OpCodes.Stloc, aspectVar));
        }

        // 2. Create MethodExecutionArgs
        var argsType = FindType("Aspect.MethodExecutionArgs");
        var argsCtor = argsType.GetConstructors().FirstOrDefault(c => !c.IsStatic && c.Parameters.Count == 0);
        if (argsCtor != null)
        {
            InsertNext(Instruction.Create(OpCodes.Newobj, _module.ImportReference(argsCtor)));
            InsertNext(Instruction.Create(OpCodes.Stloc, argsVar));
        }

        // 3. Populate MethodExecutionArgs (Method, Instance, Arguments properties)
        lastInserted = EmitPopulateMethodExecutionArgs(processor, method, argsVar, tempArgumentsVar, lastInserted, originalFirstInstruction);

        // 4. Call OnEntry
        var onEntryMethod = aspectTypeDef.Methods.FirstOrDefault(m => m.Name == "OnEntry");
        if (onEntryMethod != null)
        {
            InsertNext(Instruction.Create(OpCodes.Ldloc, aspectVar));
            InsertNext(Instruction.Create(OpCodes.Ldloc, argsVar));
            InsertNext(Instruction.Create(OpCodes.Callvirt, _module.ImportReference(onEntryMethod)));
        }

        // 5. Copy modified arguments back to method parameters
        if (method.Parameters.Count > 0)
        {
            lastInserted = EmitCopyModifiedArgumentsToParameters(processor, method, argsVar, lastInserted, originalFirstInstruction);
        }

        // 6. Check FlowBehavior after OnEntry
        // This handles Return (skip method) and ThrowException cases
        EmitCheckFlowBehaviorAfterOnEntry(processor, method, argsVar, returnVar, aspectVar, exceptionVar, ref lastInserted, originalFirstInstruction);

        // Wrap method in try-catch-finally (protect original method code starting from originalFirstInstruction)
        EmitTryCatchFinally(processor, method, aspectAttribute.AttributeType, aspectVar, argsVar, returnVar, exceptionVar, originalFirstInstruction);

        // Re-enable macro optimization
        method.Body.OptimizeMacros();
    }

    private Instruction EmitPopulateMethodExecutionArgs(ILProcessor processor, MethodDefinition method, VariableDefinition argsVar, VariableDefinition? tempArgumentsVar, Instruction? lastInserted, Instruction originalFirstInstruction)
    {
        void InsertNext(ref Instruction? last, Instruction instr)
        {
            if (last == null)
                processor.InsertBefore(originalFirstInstruction, instr);
            else
                processor.InsertAfter(last, instr);
            last = instr;
        }

        var argsType = FindType("Aspect.MethodExecutionArgs");

        // Set Method property
        var methodProperty = argsType.Properties.FirstOrDefault(p => p.Name == "Method");
        if (methodProperty?.SetMethod != null)
        {
            var setMethodRef = _module.ImportReference(methodProperty.SetMethod);
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldloc, argsVar));
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldtoken, method));
            if (method.DeclaringType.HasGenericParameters)
            {
                InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldtoken, method.DeclaringType));
            }
            var getMethodFromHandle = _module.ImportReference(
                typeof(System.Reflection.MethodBase).GetMethod(
                    "GetMethodFromHandle",
                    method.DeclaringType.HasGenericParameters
                        ? new[] { typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle) }
                        : new[] { typeof(RuntimeMethodHandle) }
                )
            );
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Call, getMethodFromHandle));
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Callvirt, setMethodRef));
        }

        // Set Instance property (for non-static methods)
        if (!method.IsStatic)
        {
            var instanceProperty = argsType.Properties.FirstOrDefault(p => p.Name == "Instance");
            if (instanceProperty?.SetMethod != null)
            {
                var setInstanceRef = _module.ImportReference(instanceProperty.SetMethod);
                InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldloc, argsVar));
                InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldarg_0)); // this
                if (method.DeclaringType.IsValueType)
                {
                    InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldobj, method.DeclaringType));
                    InsertNext(ref lastInserted, Instruction.Create(OpCodes.Box, method.DeclaringType));
                }
                InsertNext(ref lastInserted, Instruction.Create(OpCodes.Callvirt, setInstanceRef));
            }
        }

        // Set Arguments property (always, even if there are no parameters)
        var argumentsProperty = argsType.Properties.FirstOrDefault(p => p.Name == "Arguments");
        var argumentsTypeDef = FindType("Aspect.Arguments");
        var argumentsCtor = argumentsTypeDef.GetConstructors()
            .FirstOrDefault(c => !c.IsStatic && c.Parameters.Count == 1);

        if (argumentsProperty?.SetMethod != null && argumentsCtor != null)
        {
            var setArgumentsRef = _module.ImportReference(argumentsProperty.SetMethod);
            var argumentsCtorRef = _module.ImportReference(argumentsCtor);

            // Create object array for arguments
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldc_I4, method.Parameters.Count));
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Newarr, _module.TypeSystem.Object));

            // Fill array with arguments (if there are any)
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                InsertNext(ref lastInserted, Instruction.Create(OpCodes.Dup));
                InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldc_I4, i));
                InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldarg, method.Parameters[i]));

                // Box value types
                var paramType = method.Parameters[i].ParameterType;
                if (paramType.IsValueType)
                {
                    InsertNext(ref lastInserted, Instruction.Create(OpCodes.Box, paramType));
                }

                InsertNext(ref lastInserted, Instruction.Create(OpCodes.Stelem_Ref));
            }

            // Create Arguments instance
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Newobj, argumentsCtorRef));

            // Set Arguments property
            // After Newobj, stack has Arguments instance
            // We need [argsVar, Arguments] for the setter call
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Stloc, tempArgumentsVar));  // Save Arguments
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldloc, argsVar));  // Load MethodExecutionArgs
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldloc, tempArgumentsVar));  // Load Arguments
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Callvirt, setArgumentsRef));  // Call setter
        }

        return lastInserted;
    }

    private Instruction EmitCopyModifiedArgumentsToParameters(ILProcessor processor, MethodDefinition method, VariableDefinition argsVar, Instruction? lastInserted, Instruction originalFirstInstruction)
    {
        void InsertNext(ref Instruction? last, Instruction instr)
        {
            if (last == null)
                processor.InsertBefore(originalFirstInstruction, instr);
            else
                processor.InsertAfter(last, instr);
            last = instr;
        }

        var argsType = FindType("Aspect.MethodExecutionArgs");
        var argumentsProperty = argsType.Properties.FirstOrDefault(p => p.Name == "Arguments");
        if (argumentsProperty?.GetMethod == null)
            return lastInserted;

        var argumentsTypeDef = FindType("Aspect.Arguments");
        var indexerProperty = argumentsTypeDef.Properties.FirstOrDefault(p => p.Name == "Item");
        if (indexerProperty?.GetMethod == null)
            return lastInserted;

        var getArgumentsRef = _module.ImportReference(argumentsProperty.GetMethod);
        var getItemRef = _module.ImportReference(indexerProperty.GetMethod);

        // For each parameter, copy args.Arguments[i] back to the parameter
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            var parameter = method.Parameters[i];

            // Load args
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldloc, argsVar));

            // Get Arguments property
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Callvirt, getArgumentsRef));

            // Load index
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Ldc_I4, i));

            // Call Arguments[i] getter
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Callvirt, getItemRef));

            // Unbox/cast to parameter type
            var paramType = parameter.ParameterType;
            if (paramType.IsValueType)
            {
                InsertNext(ref lastInserted, Instruction.Create(OpCodes.Unbox_Any, paramType));
            }
            else if (paramType.FullName != "System.Object")
            {
                InsertNext(ref lastInserted, Instruction.Create(OpCodes.Castclass, paramType));
            }

            // Store to parameter
            InsertNext(ref lastInserted, Instruction.Create(OpCodes.Starg, parameter));
        }

        return lastInserted;
    }

    private void EmitCreateAspectInstance(ILProcessor processor, CustomAttribute aspectAttribute, VariableDefinition aspectVar, Instruction insertBefore)
    {
        // Create new instance of the aspect
        var aspectTypeDef = aspectAttribute.AttributeType.Resolve();
        var ctor = aspectTypeDef.GetConstructors().FirstOrDefault(c => !c.IsStatic && c.Parameters.Count == 0);

        if (ctor != null)
        {
            var ctorRef = _module.ImportReference(ctor);
            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Newobj, ctorRef));
            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Stloc, aspectVar));
        }
    }

    private void EmitCreateMethodExecutionArgs(ILProcessor processor, MethodDefinition method, VariableDefinition argsVar, VariableDefinition aspectVar, Instruction insertBefore)
    {
        var argsType = FindType("Aspect.MethodExecutionArgs");
        var argsCtor = argsType.GetConstructors().FirstOrDefault(c => !c.IsStatic && c.Parameters.Count == 0);

        if (argsCtor == null)
            throw new InvalidOperationException("MethodExecutionArgs constructor not found");

        var argsCtorRef = _module.ImportReference(argsCtor);

        // Create new MethodExecutionArgs()
        processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Newobj, argsCtorRef));
        processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Stloc, argsVar));

        // Set Method property
        var methodProperty = argsType.Properties.FirstOrDefault(p => p.Name == "Method");
        if (methodProperty?.SetMethod != null)
        {
            var setMethodRef = _module.ImportReference(methodProperty.SetMethod);
            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldloc, argsVar));

            // Get MethodBase from current method using reflection
            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldtoken, method));
            if (method.DeclaringType.HasGenericParameters)
            {
                processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldtoken, _module.ImportReference(method.DeclaringType)));
            }

            var getMethodFromHandle = _module.ImportReference(
                typeof(System.Reflection.MethodBase).GetMethod(
                    "GetMethodFromHandle",
                    method.DeclaringType.HasGenericParameters
                        ? new[] { typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle) }
                        : new[] { typeof(RuntimeMethodHandle) }
                )
            );
            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Call, getMethodFromHandle));
            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Callvirt, setMethodRef));
        }

        // Set Instance property (for non-static methods)
        if (!method.IsStatic)
        {
            var instanceProperty = argsType.Properties.FirstOrDefault(p => p.Name == "Instance");
            if (instanceProperty?.SetMethod != null)
            {
                var setInstanceRef = _module.ImportReference(instanceProperty.SetMethod);
                processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldloc, argsVar));
                processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldarg_0)); // this
                if (method.DeclaringType.IsValueType)
                {
                    processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldobj, method.DeclaringType));
                    processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Box, method.DeclaringType));
                }
                processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Callvirt, setInstanceRef));
            }
        }

        // Set Arguments property
        if (method.Parameters.Count > 0)
        {
            EmitSetArguments(processor, method, argsVar, insertBefore);
        }
    }

    private void EmitSetArguments(ILProcessor processor, MethodDefinition method, VariableDefinition argsVar, Instruction insertBefore)
    {
        var argsType = FindType("Aspect.MethodExecutionArgs");
        var argumentsProperty = argsType.Properties.FirstOrDefault(p => p.Name == "Arguments");

        if (argumentsProperty?.SetMethod == null)
            return;

        var argumentsTypeDef = FindType("Aspect.Arguments");
        var argumentsCtor = argumentsTypeDef.GetConstructors()
            .FirstOrDefault(c => !c.IsStatic && c.Parameters.Count == 1);

        if (argumentsCtor == null)
            return;

        var setArgumentsRef = _module.ImportReference(argumentsProperty.SetMethod);
        var argumentsCtorRef = _module.ImportReference(argumentsCtor);

        // Create object array for arguments
        processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldc_I4, method.Parameters.Count));
        processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Newarr, _module.TypeSystem.Object));

        // Fill array with arguments
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Dup));
            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldc_I4, i));

            // Load argument using parameter definition
            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldarg, method.Parameters[i]));

            // Box value types
            var paramType = method.Parameters[i].ParameterType;
            if (paramType.IsValueType)
            {
                processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Box, paramType));
            }

            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Stelem_Ref));
        }

        // Create Arguments instance (stack now has: object[])
        processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Newobj, argumentsCtorRef));

        // Set Arguments property (stack now has: Arguments)
        // We need: args.Arguments = argumentsInstance
        // Load args first
        processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldloc, argsVar));
        // Swap so we have: args, argumentsInstance
        // (Unfortunately IL doesn't have swap, so we use a temp variable)
        var tempArgumentsVar = new VariableDefinition(_module.ImportReference(argumentsTypeDef));
        method.Body.Variables.Add(tempArgumentsVar);

        // Store Arguments instance in temp
        processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Stloc, tempArgumentsVar));
        // Now load it back after args
        processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldloc, tempArgumentsVar));
        // Call setter
        processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Callvirt, setArgumentsRef));
    }

    private void EmitCallAspectMethod(ILProcessor processor, TypeReference aspectType, string methodName, VariableDefinition aspectVar, VariableDefinition argsVar, Instruction? insertBefore)
    {
        var aspectTypeDef = aspectType.Resolve();
        var method = FindMethodInHierarchy(aspectTypeDef, methodName);

        if (method != null)
        {
            var methodRef = _module.ImportReference(method);
            var instructions = new[]
            {
                Instruction.Create(OpCodes.Ldloc, aspectVar),
                Instruction.Create(OpCodes.Ldloc, argsVar),
                Instruction.Create(OpCodes.Callvirt, methodRef)
            };

            foreach (var instr in instructions)
            {
                if (insertBefore != null)
                    processor.InsertBefore(insertBefore, instr);
                else
                    processor.Append(instr);
            }
        }
    }

    private void EmitCheckFlowBehaviorAfterOnEntry(ILProcessor processor, MethodDefinition method, VariableDefinition argsVar, VariableDefinition? returnVar, VariableDefinition aspectVar, VariableDefinition exceptionVar, ref Instruction? lastInserted, Instruction originalFirstInstruction)
    {
        var argsType = FindType("Aspect.MethodExecutionArgs");
        var flowBehaviorProperty = argsType.Properties.FirstOrDefault(p => p.Name == "FlowBehavior");

        if (flowBehaviorProperty?.GetMethod == null)
            return;

        var getFlowBehaviorRef = _module.ImportReference(flowBehaviorProperty.GetMethod);
        var exceptionProperty = argsType.Properties.FirstOrDefault(p => p.Name == "Exception");
        var getExceptionRef = exceptionProperty?.GetMethod != null ? _module.ImportReference(exceptionProperty.GetMethod) : null;

        // Create temp variable to store FlowBehavior (so stack is empty when branching)
        var flowBehaviorType = _module.ImportReference(typeof(Aspect.FlowBehavior));
        var tempFlowBehaviorVar = new VariableDefinition(flowBehaviorType);
        method.Body.Variables.Add(tempFlowBehaviorVar);

        // Create labels for different flow paths
        var handleReturn = Instruction.Create(OpCodes.Nop);   // FlowBehavior.Return case
        var handleThrow = Instruction.Create(OpCodes.Nop);    // FlowBehavior.ThrowException case

        // Load args.FlowBehavior and store in temp variable
        var loadFlowBehavior = Instruction.Create(OpCodes.Ldloc, argsVar);
        if (lastInserted == null)
            processor.InsertBefore(originalFirstInstruction, loadFlowBehavior);
        else
            processor.InsertAfter(lastInserted, loadFlowBehavior);
        lastInserted = loadFlowBehavior;

        var callGetFlowBehavior = Instruction.Create(OpCodes.Callvirt, getFlowBehaviorRef);
        processor.InsertAfter(lastInserted, callGetFlowBehavior);
        lastInserted = callGetFlowBehavior;

        var storeFlowBehavior = Instruction.Create(OpCodes.Stloc, tempFlowBehaviorVar);
        processor.InsertAfter(lastInserted, storeFlowBehavior);
        lastInserted = storeFlowBehavior;

        // Switch on FlowBehavior value:
        // 0 = Continue (normal execution)
        // 1 = Return (skip method, return early)
        // 2 = ThrowException (throw exception from args)

        // Check if FlowBehavior == Continue (0)
        var ldFlowBehavior1 = Instruction.Create(OpCodes.Ldloc, tempFlowBehaviorVar);
        processor.InsertAfter(lastInserted, ldFlowBehavior1);
        lastInserted = ldFlowBehavior1;

        var brfalse = Instruction.Create(OpCodes.Brfalse, originalFirstInstruction);
        processor.InsertAfter(lastInserted, brfalse);
        lastInserted = brfalse;

        // Check if FlowBehavior == Return (1)
        var ldFlowBehavior2 = Instruction.Create(OpCodes.Ldloc, tempFlowBehaviorVar);
        processor.InsertAfter(lastInserted, ldFlowBehavior2);
        lastInserted = ldFlowBehavior2;

        var ldc1 = Instruction.Create(OpCodes.Ldc_I4_1);
        processor.InsertAfter(lastInserted, ldc1);
        lastInserted = ldc1;

        var beqReturn = Instruction.Create(OpCodes.Beq, handleReturn);
        processor.InsertAfter(lastInserted, beqReturn);
        lastInserted = beqReturn;

        // Check if FlowBehavior == ThrowException (2)
        var ldFlowBehavior3 = Instruction.Create(OpCodes.Ldloc, tempFlowBehaviorVar);
        processor.InsertAfter(lastInserted, ldFlowBehavior3);
        lastInserted = ldFlowBehavior3;

        var ldc2 = Instruction.Create(OpCodes.Ldc_I4_2);
        processor.InsertAfter(lastInserted, ldc2);
        lastInserted = ldc2;

        var beqThrow = Instruction.Create(OpCodes.Beq, handleThrow);
        processor.InsertAfter(lastInserted, beqThrow);
        lastInserted = beqThrow;

        // Default: Continue (shouldn't reach here, but safe fallback)
        var brContinue = Instruction.Create(OpCodes.Br, originalFirstInstruction);
        processor.InsertAfter(lastInserted, brContinue);
        lastInserted = brContinue;

        // === HANDLE RETURN (FlowBehavior.Return) ===
        processor.InsertAfter(lastInserted, handleReturn);
        lastInserted = handleReturn;

        // Get return value from args and store in returnVar (if non-void)
        if (returnVar != null)
        {
            var returnValueProperty = argsType.Properties.FirstOrDefault(p => p.Name == "ReturnValue");
            if (returnValueProperty?.GetMethod != null)
            {
                var getReturnValueRef = _module.ImportReference(returnValueProperty.GetMethod);

                var ldarg = Instruction.Create(OpCodes.Ldloc, argsVar);
                processor.InsertAfter(lastInserted, ldarg);
                lastInserted = ldarg;

                var callGet = Instruction.Create(OpCodes.Callvirt, getReturnValueRef);
                processor.InsertAfter(lastInserted, callGet);
                lastInserted = callGet;

                // Unbox/cast to return type
                if (method.ReturnType.IsValueType)
                {
                    var unbox = Instruction.Create(OpCodes.Unbox_Any, method.ReturnType);
                    processor.InsertAfter(lastInserted, unbox);
                    lastInserted = unbox;
                }
                else if (method.ReturnType.FullName != "System.Object")
                {
                    var cast = Instruction.Create(OpCodes.Castclass, method.ReturnType);
                    processor.InsertAfter(lastInserted, cast);
                    lastInserted = cast;
                }

                var stloc = Instruction.Create(OpCodes.Stloc, returnVar);
                processor.InsertAfter(lastInserted, stloc);
                lastInserted = stloc;
            }
        }

        // Call OnExit before returning
        var aspectType = aspectVar.VariableType.Resolve();
        var onExitMethod = aspectType.Methods.FirstOrDefault(m => m.Name == "OnExit");
        if (onExitMethod != null)
        {
            var ldAspect = Instruction.Create(OpCodes.Ldloc, aspectVar);
            processor.InsertAfter(lastInserted, ldAspect);
            lastInserted = ldAspect;

            var ldArgs = Instruction.Create(OpCodes.Ldloc, argsVar);
            processor.InsertAfter(lastInserted, ldArgs);
            lastInserted = ldArgs;

            var callExit = Instruction.Create(OpCodes.Callvirt, _module.ImportReference(onExitMethod));
            processor.InsertAfter(lastInserted, callExit);
            lastInserted = callExit;
        }

        // Load return value if needed and return
        if (returnVar != null)
        {
            var ldRet = Instruction.Create(OpCodes.Ldloc, returnVar);
            processor.InsertAfter(lastInserted, ldRet);
            lastInserted = ldRet;
        }

        var retInstr = Instruction.Create(OpCodes.Ret);
        processor.InsertAfter(lastInserted, retInstr);
        lastInserted = retInstr;

        // === HANDLE THROW (FlowBehavior.ThrowException) ===
        processor.InsertAfter(lastInserted, handleThrow);
        lastInserted = handleThrow;

        // Get exception from args and store it
        if (getExceptionRef != null)
        {
            var ldArgsForEx = Instruction.Create(OpCodes.Ldloc, argsVar);
            processor.InsertAfter(lastInserted, ldArgsForEx);
            lastInserted = ldArgsForEx;

            var callGetEx = Instruction.Create(OpCodes.Callvirt, getExceptionRef);
            processor.InsertAfter(lastInserted, callGetEx);
            lastInserted = callGetEx;

            var stlocEx = Instruction.Create(OpCodes.Stloc, exceptionVar);
            processor.InsertAfter(lastInserted, stlocEx);
            lastInserted = stlocEx;
        }
        else
        {
            // Fallback: create generic exception
            var exceptionCtor = _module.ImportReference(typeof(System.Exception).GetConstructor(new[] { typeof(string) }));

            var ldstr = Instruction.Create(OpCodes.Ldstr, "FlowBehavior.ThrowException but args.Exception is null");
            processor.InsertAfter(lastInserted, ldstr);
            lastInserted = ldstr;

            var newobj = Instruction.Create(OpCodes.Newobj, exceptionCtor);
            processor.InsertAfter(lastInserted, newobj);
            lastInserted = newobj;

            var stlocEx = Instruction.Create(OpCodes.Stloc, exceptionVar);
            processor.InsertAfter(lastInserted, stlocEx);
            lastInserted = stlocEx;
        }

        // Set args.Exception from exceptionVar
        var exceptionProp = argsType.Properties.FirstOrDefault(p => p.Name == "Exception");
        if (exceptionProp?.SetMethod != null)
        {
            var setExceptionRef2 = _module.ImportReference(exceptionProp.SetMethod);
            var ldArgs2 = Instruction.Create(OpCodes.Ldloc, argsVar);
            processor.InsertAfter(lastInserted, ldArgs2);
            lastInserted = ldArgs2;

            var ldEx = Instruction.Create(OpCodes.Ldloc, exceptionVar);
            processor.InsertAfter(lastInserted, ldEx);
            lastInserted = ldEx;

            var setEx = Instruction.Create(OpCodes.Callvirt, setExceptionRef2);
            processor.InsertAfter(lastInserted, setEx);
            lastInserted = setEx;
        }

        // Call OnException
        var aspectType2 = aspectVar.VariableType.Resolve();
        var onExceptionMethod = aspectType2.Methods.FirstOrDefault(m => m.Name == "OnException");
        if (onExceptionMethod != null)
        {
            var ldAspect2 = Instruction.Create(OpCodes.Ldloc, aspectVar);
            processor.InsertAfter(lastInserted, ldAspect2);
            lastInserted = ldAspect2;

            var ldArgs3 = Instruction.Create(OpCodes.Ldloc, argsVar);
            processor.InsertAfter(lastInserted, ldArgs3);
            lastInserted = ldArgs3;

            var callOnException = Instruction.Create(OpCodes.Callvirt, _module.ImportReference(onExceptionMethod));
            processor.InsertAfter(lastInserted, callOnException);
            lastInserted = callOnException;
        }

        // TODO: Check FlowBehavior after OnException to see if exception should be thrown or suppressed
        // For now, we always throw after OnException

        // Call OnExit before throwing/returning
        var onExitMethod2 = aspectType2.Methods.FirstOrDefault(m => m.Name == "OnExit");
        if (onExitMethod2 != null)
        {
            var ldAspect3 = Instruction.Create(OpCodes.Ldloc, aspectVar);
            processor.InsertAfter(lastInserted, ldAspect3);
            lastInserted = ldAspect3;

            var ldArgs4 = Instruction.Create(OpCodes.Ldloc, argsVar);
            processor.InsertAfter(lastInserted, ldArgs4);
            lastInserted = ldArgs4;

            var callExit2 = Instruction.Create(OpCodes.Callvirt, _module.ImportReference(onExitMethod2));
            processor.InsertAfter(lastInserted, callExit2);
            lastInserted = callExit2;
        }

        // Throw the exception (which may have been modified in OnException)
        var ldExToThrow = Instruction.Create(OpCodes.Ldloc, exceptionVar);
        processor.InsertAfter(lastInserted, ldExToThrow);
        lastInserted = ldExToThrow;

        var throwInstr = Instruction.Create(OpCodes.Throw);
        processor.InsertAfter(lastInserted, throwInstr);
        lastInserted = throwInstr;

        // === CONTINUE NORMAL (FlowBehavior.Continue) ===
        // Execution branches directly to originalFirstInstruction (try block start)
        // No need to insert anything - branches go directly to try block
    }

    private void EmitTryCatchFinally(ILProcessor processor, MethodDefinition method, TypeReference aspectType, VariableDefinition aspectVar, VariableDefinition argsVar, VariableDefinition? returnVar, VariableDefinition exceptionVar, Instruction tryStart)
    {
        // Find all existing ret instructions AFTER tryStart (original method body only)
        // This excludes ret instructions added by FlowBehavior handling before tryStart
        var existingReturns = method.Body.Instructions
            .SkipWhile(i => i != tryStart)  // Start from tryStart onwards
            .Where(i => i.OpCode == OpCodes.Ret)
            .ToList();

        // Create markers for exception handler boundaries
        // tryStart is passed as parameter (originalFirstInstruction)
        var tryEnd = Instruction.Create(OpCodes.Nop);
        var catchStart = Instruction.Create(OpCodes.Nop);
        var finallyStart = Instruction.Create(OpCodes.Nop);
        var methodEnd = Instruction.Create(OpCodes.Nop);

        // Replace all ret instructions with: stloc (if needed) + leave tryEnd
        foreach (var retInstruction in existingReturns)
        {
            if (returnVar != null)
            {
                // Store return value before leaving
                processor.InsertBefore(retInstruction, Instruction.Create(OpCodes.Stloc, returnVar));
            }

            // Insert leave before ret, then remove ret
            processor.InsertBefore(retInstruction, Instruction.Create(OpCodes.Leave, tryEnd));
            processor.Remove(retInstruction);
        }

        // === AFTER TRY BLOCK (OnSuccess) ===
        processor.Append(tryEnd);

        // Set args.ReturnValue from the actual return value (if non-void)
        if (returnVar != null)
        {
            var argsType = FindType("Aspect.MethodExecutionArgs");
            var returnValueProperty = argsType.Properties.FirstOrDefault(p => p.Name == "ReturnValue");
            if (returnValueProperty?.SetMethod != null)
            {
                var setReturnValueRef = _module.ImportReference(returnValueProperty.SetMethod);
                processor.Append(Instruction.Create(OpCodes.Ldloc, argsVar));
                processor.Append(Instruction.Create(OpCodes.Ldloc, returnVar));
                if (method.ReturnType.IsValueType)
                {
                    processor.Append(Instruction.Create(OpCodes.Box, method.ReturnType));
                }
                processor.Append(Instruction.Create(OpCodes.Callvirt, setReturnValueRef));
            }
        }

        // Call OnSuccess
        EmitCallAspectMethod(processor, aspectType, "OnSuccess", aspectVar, argsVar, null);

        // Get potentially modified return value from args
        if (returnVar != null)
        {
            EmitGetReturnValueFromArgs(processor, method, argsVar, returnVar, null);
        }

        // Leave to method end (will execute finally automatically)
        processor.Append(Instruction.Create(OpCodes.Leave, methodEnd));

        // === CATCH BLOCK ===
        processor.Append(catchStart);

        // Store exception in local variable (from runtime exception)
        processor.Append(Instruction.Create(OpCodes.Stloc, exceptionVar));

        // Set args.Exception
        EmitSetException(processor, argsVar, exceptionVar, null);

        // Call OnException
        EmitCallAspectMethod(processor, aspectType, "OnException", aspectVar, argsVar, null);

        // Reload exception from args.Exception (it may have been modified in OnException)
        EmitGetException(processor, argsVar, exceptionVar, null);

        // Check FlowBehavior after OnException
        EmitCheckFlowBehaviorAfterOnException(processor, method, argsVar, returnVar, exceptionVar, null);

        // Leave catch block (will execute finally automatically)
        processor.Append(Instruction.Create(OpCodes.Leave, methodEnd));
        // NOTE: catchEnd is just a marker, not an actual instruction

        // === FINALLY BLOCK ===
        processor.Append(finallyStart);

        // Call OnExit
        EmitCallAspectMethod(processor, aspectType, "OnExit", aspectVar, argsVar, null);

        // End finally
        processor.Append(Instruction.Create(OpCodes.Endfinally));
        // NOTE: finallyEnd is just a marker, not an actual instruction

        // === METHOD END ===
        processor.Append(methodEnd);

        // Load return value if needed
        if (returnVar != null)
        {
            processor.Append(Instruction.Create(OpCodes.Ldloc, returnVar));
        }

        // Return
        processor.Append(Instruction.Create(OpCodes.Ret));

        // Add exception handlers at the very end
        // CRITICAL: TryEnd and HandlerEnd must point to instruction.Next AFTER the last instruction

        // Catch handler: protects try block
        var tryHandler = new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = tryEnd,                // tryEnd is the first NOP after try block
            HandlerStart = catchStart,
            HandlerEnd = finallyStart,      // finallyStart is first instruction after catch
            CatchType = _module.ImportReference(typeof(System.Exception))
        };

        // Finally handler: protects try + catch blocks
        var finallyHandler = new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = tryStart,
            TryEnd = finallyStart,          // Finally must cover try + catch
            HandlerStart = finallyStart,
            HandlerEnd = methodEnd          // methodEnd is first instruction after finally
        };

        method.Body.ExceptionHandlers.Add(tryHandler);
        method.Body.ExceptionHandlers.Add(finallyHandler);
    }

    private void EmitGetReturnValueFromArgs(ILProcessor processor, MethodDefinition method, VariableDefinition argsVar, VariableDefinition returnVar, Instruction? insertBefore)
    {
        var argsType = FindType("Aspect.MethodExecutionArgs");
        var returnValueProperty = argsType.Properties.FirstOrDefault(p => p.Name == "ReturnValue");

        if (returnValueProperty?.GetMethod != null)
        {
            var getReturnValueRef = _module.ImportReference(returnValueProperty.GetMethod);
            var instructions = new List<Instruction>
            {
                Instruction.Create(OpCodes.Ldloc, argsVar),
                Instruction.Create(OpCodes.Callvirt, getReturnValueRef)
            };

            // Unbox/cast to return type
            if (method.ReturnType.IsValueType)
            {
                instructions.Add(Instruction.Create(OpCodes.Unbox_Any, method.ReturnType));
            }
            else if (method.ReturnType.FullName != "System.Object")
            {
                instructions.Add(Instruction.Create(OpCodes.Castclass, method.ReturnType));
            }

            instructions.Add(Instruction.Create(OpCodes.Stloc, returnVar));

            foreach (var instr in instructions)
            {
                if (insertBefore != null)
                    processor.InsertBefore(insertBefore, instr);
                else
                    processor.Append(instr);
            }
        }
    }

    private void EmitSetException(ILProcessor processor, VariableDefinition argsVar, VariableDefinition exceptionVar, Instruction? insertBefore)
    {
        var argsType = FindType("Aspect.MethodExecutionArgs");
        var exceptionProperty = argsType.Properties.FirstOrDefault(p => p.Name == "Exception");

        if (exceptionProperty?.SetMethod != null)
        {
            var setExceptionRef = _module.ImportReference(exceptionProperty.SetMethod);
            var instructions = new[]
            {
                Instruction.Create(OpCodes.Ldloc, argsVar),
                Instruction.Create(OpCodes.Ldloc, exceptionVar),
                Instruction.Create(OpCodes.Castclass, _module.ImportReference(typeof(System.Exception))),
                Instruction.Create(OpCodes.Callvirt, setExceptionRef)
            };

            foreach (var instr in instructions)
            {
                if (insertBefore != null)
                    processor.InsertBefore(insertBefore, instr);
                else
                    processor.Append(instr);
            }
        }
    }

    private void EmitGetException(ILProcessor processor, VariableDefinition argsVar, VariableDefinition exceptionVar, Instruction? insertBefore)
    {
        var argsType = FindType("Aspect.MethodExecutionArgs");
        var exceptionProperty = argsType.Properties.FirstOrDefault(p => p.Name == "Exception");

        if (exceptionProperty?.GetMethod != null)
        {
            var getExceptionRef = _module.ImportReference(exceptionProperty.GetMethod);
            var instructions = new[]
            {
                Instruction.Create(OpCodes.Ldloc, argsVar),
                Instruction.Create(OpCodes.Callvirt, getExceptionRef),
                Instruction.Create(OpCodes.Stloc, exceptionVar)
            };

            foreach (var instr in instructions)
            {
                if (insertBefore != null)
                    processor.InsertBefore(insertBefore, instr);
                else
                    processor.Append(instr);
            }
        }
    }

    private void EmitCheckFlowBehaviorAfterOnException(ILProcessor processor, MethodDefinition method, VariableDefinition argsVar, VariableDefinition? returnVar, VariableDefinition exceptionVar, Instruction? insertBefore)
    {
        var argsType = FindType("Aspect.MethodExecutionArgs");
        var flowBehaviorProperty = argsType.Properties.FirstOrDefault(p => p.Name == "FlowBehavior");

        if (flowBehaviorProperty?.GetMethod == null)
            return;

        var getFlowBehaviorRef = _module.ImportReference(flowBehaviorProperty.GetMethod);
        var continueLabel = Instruction.Create(OpCodes.Nop);
        var afterRethrow = Instruction.Create(OpCodes.Nop);

        var instructions = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldloc, argsVar),
            Instruction.Create(OpCodes.Callvirt, getFlowBehaviorRef),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Bne_Un_S, continueLabel)
        };

        // Emit initial check
        foreach (var instr in instructions)
        {
            if (insertBefore != null)
                processor.InsertBefore(insertBefore, instr);
            else
                processor.Append(instr);
        }

        // FlowBehavior.Return: Suppress exception, set return value
        if (returnVar != null)
        {
            EmitGetReturnValueFromArgs(processor, method, argsVar, returnVar, insertBefore);
        }

        // Jump past rethrow
        if (insertBefore != null)
        {
            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Br_S, afterRethrow));
            processor.InsertBefore(insertBefore, continueLabel);

            // Load exception from exceptionVar (it may have been modified in OnException)
            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldloc, exceptionVar));
            processor.InsertBefore(insertBefore, Instruction.Create(OpCodes.Throw));
            processor.InsertBefore(insertBefore, afterRethrow);
        }
        else
        {
            processor.Append(Instruction.Create(OpCodes.Br_S, afterRethrow));
            processor.Append(continueLabel);

            // Load exception from exceptionVar (it may have been modified in OnException)
            processor.Append(Instruction.Create(OpCodes.Ldloc, exceptionVar));
            processor.Append(Instruction.Create(OpCodes.Throw));
            processor.Append(afterRethrow);
        }
    }

    private bool IsMethodBoundaryAspect(TypeReference typeRef)
    {
        try
        {
            var typeDef = typeRef.Resolve();
            if (typeDef == null) return false;

            var current = typeDef;
            while (current != null)
            {
                if (current.FullName == "Aspect.OnMethodBoundaryAspect")
                    return true;

                current = current.BaseType?.Resolve();
            }

            return false;
        }
        catch
        {
            // If we can't resolve the type (missing assembly), it's not an aspect
            return false;
        }
    }

    private TypeDefinition FindType(string fullName)
    {
        // Try current module
        var type = _module.Types.FirstOrDefault(t => t.FullName == fullName);
        if (type != null) return type;

        // Try referenced assemblies
        foreach (var assemblyRef in _module.AssemblyReferences)
        {
            try
            {
                var assembly = _module.AssemblyResolver.Resolve(assemblyRef);
                type = assembly.MainModule.Types.FirstOrDefault(t => t.FullName == fullName);
                if (type != null) return type;
            }
            catch
            {
                // Continue
            }
        }

        throw new InvalidOperationException($"Could not find type: {fullName}");
    }

    private MethodDefinition? FindMethodInHierarchy(TypeDefinition type, string methodName)
    {
        var current = type;
        while (current != null)
        {
            var method = current.Methods.FirstOrDefault(m => m.Name == methodName);
            if (method != null) return method;

            current = current.BaseType?.Resolve();
        }

        return null;
    }
}
