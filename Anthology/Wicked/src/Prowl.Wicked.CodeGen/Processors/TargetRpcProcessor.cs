namespace Prowl.Wicked.CodeGen.Processors;

using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Processes methods marked with [TargetRpc] attribute.
/// Transforms them to send an RPC message to a specific client when called on the server.
/// The first parameter must be NetworkConnection specifying the target.
/// With Echo serialization, args are deserialized as object?[] before invoker is called.
/// Uses function hash (ushort) for efficient network transmission.
/// </summary>
public class TargetRpcProcessor : RpcProcessorBase
{
    private const string TargetRpcAttributeName = "Prowl.Wicked.Attributes.TargetRpcAttribute";

    public TargetRpcProcessor(ModuleDefinition module) : base(module) { }

    /// <summary>
    /// Processes a method if it has the [TargetRpc] attribute.
    /// Returns true if the method was processed.
    /// </summary>
    public bool Process(MethodDefinition method)
    {
        if (!HasAttribute(method, TargetRpcAttributeName))
            return false;

        // Validate first parameter is NetworkConnection
        if (method.Parameters.Count == 0 ||
            method.Parameters[0].ParameterType.FullName != "Prowl.Wicked.Network.NetworkConnection")
        {
            Console.WriteLine($"  Warning: [TargetRpc] {method.Name} first parameter must be NetworkConnection");
            return false;
        }

        Console.WriteLine($"  Processing [TargetRpc] {method.Name}");

        // Compute function hash at compile time
        var behaviourTypeName = method.DeclaringType.FullName;
        var functionHash = ComputeFunctionHash(behaviourTypeName, method.Name);
        Console.WriteLine($"    Function hash: 0x{functionHash:X4}");

        // Step 1: Substitute the method - swaps bodies so UserLogic has original code
        var userLogicMethod = SubstituteMethod(method);
        method.DeclaringType.Methods.Add(userLogicMethod);

        // Step 2: Create the invoker that reads args from object[] and calls user logic
        var invokerMethod = CreateRpcInvoker(method, userLogicMethod);
        method.DeclaringType.Methods.Add(invokerMethod);

        // Step 3: Rewrite the original method to send target RPC
        RewriteMethodToSendTargetRpc(method, userLogicMethod, functionHash);

        return true;
    }

    /// <summary>
    /// Creates a user logic method by SWAPPING bodies with the original method.
    /// This is how Mirror does it - much simpler than copying IL instructions.
    /// </summary>
    private MethodDefinition SubstituteMethod(MethodDefinition original)
    {
        var logicName = $"UserLogic_{original.Name}";

        var userLogic = new MethodDefinition(
            logicName,
            MethodAttributes.Private | MethodAttributes.HideBySig,
            original.ReturnType);

        // Copy parameters
        foreach (var param in original.Parameters)
        {
            userLogic.Parameters.Add(new ParameterDefinition(
                param.Name,
                param.Attributes,
                param.ParameterType));
        }

        // SWAP bodies - the key insight from Mirror!
        (userLogic.Body, original.Body) = (original.Body, userLogic.Body);

        // Move debug information
        foreach (var sequencePoint in original.DebugInformation.SequencePoints)
            userLogic.DebugInformation.SequencePoints.Add(sequencePoint);
        original.DebugInformation.SequencePoints.Clear();

        foreach (var customInfo in original.CustomDebugInformations)
            userLogic.CustomDebugInformations.Add(customInfo);
        original.CustomDebugInformations.Clear();

        (original.DebugInformation.Scope, userLogic.DebugInformation.Scope) =
            (userLogic.DebugInformation.Scope, original.DebugInformation.Scope);

        return userLogic;
    }

    private MethodDefinition CreateRpcInvoker(MethodDefinition original, MethodDefinition userLogicMethod)
    {
        var invokerName = $"InvokeTarget{original.Name}";
        if (original.Name.StartsWith("Target"))
            invokerName = $"Invoke{original.Name}";

        // Create invoker with object?[] args parameter
        var invoker = CreateInvokerMethod(invokerName);

        var il = invoker.Body.GetILProcessor();

        // Read each parameter from the args array and store in locals
        // Skip the first parameter (NetworkConnection) as it's not serialized
        var locals = new List<VariableDefinition>();

        // First parameter is NetworkConnection, which we pass as null on client
        var connType = Module.ImportReference(typeof(Prowl.Wicked.Network.NetworkConnection));
        var connLocal = new VariableDefinition(connType);
        invoker.Body.Variables.Add(connLocal);
        locals.Add(connLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, connLocal);

        // Read remaining parameters from args (starting at index 0 since NetworkConnection isn't serialized)
        for (int i = 1; i < original.Parameters.Count; i++)
        {
            var param = original.Parameters[i];
            var local = new VariableDefinition(Module.ImportReference(param.ParameterType));
            invoker.Body.Variables.Add(local);
            locals.Add(local);

            // Args array index is i-1 since NetworkConnection isn't in the args
            EmitReadParameterFromArgs(il, param.ParameterType, i - 1);
            il.Emit(OpCodes.Stloc, local);
        }

        // Call the user logic method
        il.Emit(OpCodes.Ldarg_0); // this
        foreach (var local in locals)
        {
            il.Emit(OpCodes.Ldloc, local);
        }

        var userLogicRef = Module.ImportReference(userLogicMethod);
        il.Emit(OpCodes.Call, userLogicRef);

        il.Emit(OpCodes.Ret);

        return invoker;
    }

    private void RewriteMethodToSendTargetRpc(MethodDefinition method, MethodDefinition userLogic, ushort functionHash)
    {
        // Clear the original body
        method.Body.Instructions.Clear();
        method.Body.Variables.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.InitLocals = true;

        var il = method.Body.GetILProcessor();

        // if (!IsServer) { invoke locally and return; }
        var isServerProp = typeof(Prowl.Wicked.Core.EntityBehaviour).GetProperty("IsServer")!.GetGetMethod()!;
        var isServerRef = Module.ImportReference(isServerProp);

        var sendRpcLabel = il.Create(OpCodes.Nop);

        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Call, isServerRef);
        il.Emit(OpCodes.Brtrue, sendRpcLabel); // if IsServer, jump to sending

        // We're on client - call user logic directly
        il.Emit(OpCodes.Ldarg_0);
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            il.Emit(OpCodes.Ldarg, i + 1);
        }
        il.Emit(OpCodes.Call, Module.ImportReference(userLogic));
        il.Emit(OpCodes.Ret);

        // Send RPC to target client
        il.Append(sendRpcLabel);

        // Call SendTargetRpc(target, functionHash, args)
        var sendRpcMethod = Module.ImportReference(
            typeof(Prowl.Wicked.Core.EntityBehaviour).GetMethod("SendTargetRpc",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));

        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldarg_1); // target (first parameter)
        il.Emit(OpCodes.Ldc_I4, (int)functionHash); // function hash (emitted as int32)
        il.Emit(OpCodes.Conv_U2); // convert to ushort

        // Create object[] for remaining args (skip first param which is target)
        il.Emit(OpCodes.Ldc_I4, method.Parameters.Count - 1);
        il.Emit(OpCodes.Newarr, Module.TypeSystem.Object);

        for (int i = 1; i < method.Parameters.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i - 1);
            il.Emit(OpCodes.Ldarg, i + 1);

            if (method.Parameters[i].ParameterType.IsValueType)
            {
                il.Emit(OpCodes.Box, Module.ImportReference(method.Parameters[i].ParameterType));
            }

            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Call, sendRpcMethod);
        il.Emit(OpCodes.Ret);
    }
}
