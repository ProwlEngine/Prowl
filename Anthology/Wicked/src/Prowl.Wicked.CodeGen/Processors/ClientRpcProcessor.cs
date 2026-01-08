namespace Prowl.Wicked.CodeGen.Processors;

using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Processes methods marked with [ClientRpc] attribute.
/// Transforms them to send an RPC message to all clients when called on the server.
/// With Echo serialization, args are deserialized as object?[] before invoker is called.
/// </summary>
public class ClientRpcProcessor : RpcProcessorBase
{
    private const string ClientRpcAttributeName = "Prowl.Wicked.Attributes.ClientRpcAttribute";

    public ClientRpcProcessor(ModuleDefinition module) : base(module) { }

    /// <summary>
    /// Processes a method if it has the [ClientRpc] attribute.
    /// Returns true if the method was processed.
    /// </summary>
    public bool Process(MethodDefinition method)
    {
        if (!HasAttribute(method, ClientRpcAttributeName))
            return false;

        Console.WriteLine($"  Processing [ClientRpc] {method.Name}");

        var attr = GetAttribute(method, ClientRpcAttributeName)!;
        var includeHost = GetAttributeProperty(attr, "IncludeHost", true);

        // Step 1: Substitute the method - swaps bodies so UserLogic has original code
        var userLogicMethod = SubstituteMethod(method);
        method.DeclaringType.Methods.Add(userLogicMethod);

        // Step 2: Create the invoker that reads args from object[] and calls user logic
        var invokerMethod = CreateRpcInvoker(method, userLogicMethod);
        method.DeclaringType.Methods.Add(invokerMethod);

        // Step 3: Rewrite the original method to send an RPC when on server
        RewriteMethodToSendRpc(method, userLogicMethod, includeHost);

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
        var invokerName = $"InvokeRpc{original.Name}";
        if (original.Name.StartsWith("Rpc"))
            invokerName = $"Invoke{original.Name}";

        // Create invoker with object?[] args parameter
        var invoker = CreateInvokerMethod(invokerName);

        var il = invoker.Body.GetILProcessor();

        // Read each parameter from the args array and store in locals
        var locals = new List<VariableDefinition>();
        for (int i = 0; i < original.Parameters.Count; i++)
        {
            var param = original.Parameters[i];
            var local = new VariableDefinition(Module.ImportReference(param.ParameterType));
            invoker.Body.Variables.Add(local);
            locals.Add(local);

            EmitReadParameterFromArgs(il, param.ParameterType, i);
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

    private void RewriteMethodToSendRpc(MethodDefinition method, MethodDefinition userLogic, bool includeHost)
    {
        // Clear the original body
        method.Body.Instructions.Clear();
        method.Body.Variables.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.InitLocals = true;

        var il = method.Body.GetILProcessor();

        // if (!IsServer) { invoke locally on client and return; }
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

        // Send RPC to clients
        il.Append(sendRpcLabel);

        // Call SendClientRpc(methodName, includeHost, args)
        var sendClientRpcMethodInfo = typeof(Prowl.Wicked.Core.EntityBehaviour).GetMethod("SendClientRpc",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (sendClientRpcMethodInfo == null)
            throw new InvalidOperationException("Could not find SendClientRpc method on EntityBehaviour");
        var sendRpcMethod = Module.ImportReference(sendClientRpcMethodInfo);

        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldstr, method.Name); // method name
        il.Emit(includeHost ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); // includeHost

        // Create object[] for args
        il.Emit(OpCodes.Ldc_I4, method.Parameters.Count);
        il.Emit(OpCodes.Newarr, Module.TypeSystem.Object);

        for (int i = 0; i < method.Parameters.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
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
