namespace Prowl.Wicked.CodeGen.Processors;

using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Processes methods marked with [Command] attribute.
/// Transforms them to send a command message when called on a client.
/// </summary>
public class CommandProcessor : RpcProcessorBase
{
    private const string CommandAttributeName = "Prowl.Wicked.Attributes.CommandAttribute";

    public CommandProcessor(ModuleDefinition module) : base(module) { }

    /// <summary>
    /// Processes a method if it has the [Command] attribute.
    /// Returns true if the method was processed.
    /// </summary>
    public bool Process(MethodDefinition method)
    {
        if (!HasAttribute(method, CommandAttributeName))
            return false;

        Console.WriteLine($"  Processing [Command] {method.Name}");

        var attr = GetAttribute(method, CommandAttributeName)!;
        var requireOwnership = GetAttributeProperty(attr, "RequireOwnership", true);

        // Step 1: Substitute the method - swaps bodies so UserLogic has original code
        var userLogicMethod = SubstituteMethod(method);
        method.DeclaringType.Methods.Add(userLogicMethod);

        // Step 2: Create the invoker that deserializes args and calls user logic
        var invokerMethod = CreateCommandInvoker(method, userLogicMethod);
        method.DeclaringType.Methods.Add(invokerMethod);

        // Step 3: Rewrite the original method to send a command when on client
        RewriteMethodToSendCommand(method, userLogicMethod, requireOwnership);

        // Register the invoker with RpcRegistry
        AddRegistration(method, invokerMethod);

        return true;
    }

    /// <summary>
    /// Creates a user logic method by SWAPPING bodies with the original method.
    /// This is how Mirror does it - much simpler than copying IL instructions.
    /// The original body moves to UserLogic_X, and the original method gets an empty body
    /// that we then fill with the send/invoke logic.
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
        // UserLogic gets the original body, original method gets empty body
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

    private MethodDefinition CreateCommandInvoker(MethodDefinition original, MethodDefinition userLogicMethod)
    {
        var invokerName = $"InvokeCmd{original.Name}";
        if (original.Name.StartsWith("Cmd"))
            invokerName = $"Invoke{original.Name}";

        var invoker = new MethodDefinition(
            invokerName,
            MethodAttributes.Public | MethodAttributes.HideBySig,
            VoidType);

        // Add NetworkReader parameter
        var readerType = Module.ImportReference(typeof(Prowl.Wicked.Network.Serialization.NetworkReader));
        invoker.Parameters.Add(new ParameterDefinition("reader", ParameterAttributes.None, readerType));

        var il = invoker.Body.GetILProcessor();

        // Read each parameter from the reader and store in locals
        var locals = new List<VariableDefinition>();
        foreach (var param in original.Parameters)
        {
            var local = new VariableDefinition(Module.ImportReference(param.ParameterType));
            invoker.Body.Variables.Add(local);
            locals.Add(local);

            EmitReadParameter(il, param);
            il.Emit(OpCodes.Stloc, local);
        }

        // Call the user's logic method
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

    private void RewriteMethodToSendCommand(MethodDefinition method, MethodDefinition userLogicMethod, bool requireOwnership)
    {
        // Clear the original body
        method.Body.Instructions.Clear();
        method.Body.Variables.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.InitLocals = true;

        var il = method.Body.GetILProcessor();

        // Mirror's approach: check if (isServer && isClient) for HOST MODE
        // In host mode, execute the command directly without network roundtrip
        var isServerProp = typeof(Prowl.Wicked.Core.EntityBehaviour).GetProperty("IsServer")!.GetGetMethod()!;
        var isClientProp = typeof(Prowl.Wicked.Core.EntityBehaviour).GetProperty("IsClient")!.GetGetMethod()!;
        var isServerRef = Module.ImportReference(isServerProp);
        var isClientRef = Module.ImportReference(isClientProp);

        var sendCommandLabel = il.Create(OpCodes.Nop);
        var userLogicRef = Module.ImportReference(userLogicMethod);

        // Check if NOT server -> go to send command
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Call, isServerRef);
        il.Emit(OpCodes.Brfalse, sendCommandLabel); // if NOT server, jump to sending

        // Check if NOT client -> go to send command (pure server shouldn't call commands)
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Call, isClientRef);
        il.Emit(OpCodes.Brfalse, sendCommandLabel); // if NOT client, jump to sending

        // We're HOST (isServer && isClient) - call user logic directly
        il.Emit(OpCodes.Ldarg_0); // this
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            il.Emit(OpCodes.Ldarg, i + 1);
        }
        il.Emit(OpCodes.Call, userLogicRef);
        il.Emit(OpCodes.Ret);

        // Send command to server (for non-host clients)
        il.Append(sendCommandLabel);

        // Call SendCommand(methodName, args)
        var sendCommandMethodInfo = typeof(Prowl.Wicked.Core.EntityBehaviour).GetMethod("SendCommand",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (sendCommandMethodInfo == null)
            throw new InvalidOperationException("Could not find SendCommand method on EntityBehaviour");
        var sendCommandMethod = Module.ImportReference(sendCommandMethodInfo);

        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldstr, method.Name); // method name

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

        il.Emit(OpCodes.Call, sendCommandMethod);
        il.Emit(OpCodes.Ret);
    }

    private void AddRegistration(MethodDefinition method, MethodDefinition invoker)
    {
        // This would add code to register the invoker with RpcRegistry
        // For now, we rely on reflection-based discovery
        // A static constructor could be modified to call RpcRegistry.Register
    }
}
