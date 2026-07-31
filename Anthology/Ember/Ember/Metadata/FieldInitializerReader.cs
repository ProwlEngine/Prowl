using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

using EmitOpCodes = System.Reflection.Emit.OpCodes;
using CecilOpCode = Mono.Cecil.Cil.OpCode;
using CecilStackBehaviour = Mono.Cecil.Cil.StackBehaviour;
using CecilFlowControl = Mono.Cecil.Cil.FlowControl;

namespace Prowl.Ember;

/// <summary>
/// Recovers the value a field initializer would have produced, without running any constructor body, so a
/// newly added field starts at its declared value rather than at zero.
/// </summary>
/// <remarks>
/// Field initializers are emitted at the top of every instance constructor, before the base constructor call,
/// and are omitted in constructors that chain to a peer constructor. An initializer also cannot read a
/// constructor parameter. Those three facts are what make the right slice identifiable: take the constructor
/// with the fewest parameters whose leading actions neither chain nor load an argument, and lift out the
/// expression assigned to the field of interest.
/// </remarks>
internal static class FieldInitializerReader
{
    public static Func<object?>? Build(MetadataCache cache, FieldInfo field, ReportBuilder report)
    {
        var declaringType = field.DeclaringType!;

        var typeDefinition = cache.FindType(declaringType);
        if (typeDefinition == null) return null;

        var fieldDefinition = typeDefinition.Fields.FirstOrDefault(x => x.Name == field.Name);
        if (fieldDefinition == null) return null;

        var action = typeDefinition.GetConstructors()
            .Select(ctor => ReadActions(ctor).ToArray())
            .Where(actions => actions.Length > 0)
            .Where(actions => actions.All(a => !a.ChainsToPeerConstructor && !a.ReadsParameter))
            .Select(actions => actions.FirstOrDefault(a => SameField(fieldDefinition, a.Field)))
            .Where(a => a != null)
            .MinBy(a => a!.Constructor.Parameters.Count);

        if (action == null) return null;

        var genericTypeArguments = declaringType.IsGenericType ? declaringType.GetGenericArguments() : null;

        try
        {
            // Owned by the module rather than the type: a constructed generic type is not a legal owner, and
            // skipping visibility keeps access to the private members an initializer may touch.
            var method = new DynamicMethod($"<{field.Name}>__initializer", typeof(object), Type.EmptyTypes,
                declaringType.Module, skipVisibility: true);
            var il = method.GetILGenerator();

            // Emit only the value expression, between the leading 'this' load and the trailing field store.
            ILTranslator.Emit(il, declaringType.Module, action.First!.Next, action.Last!, genericTypeArguments, null);

            if (field.FieldType.IsValueType)
                il.Emit(EmitOpCodes.Box, field.FieldType);
            il.Emit(EmitOpCodes.Ret);

            return method.CreateDelegate<Func<object?>>();
        }
        catch (Exception e)
        {
            report.Report(ReloadCode.InitializerExpressionUnsupported, ReloadSeverity.Warning,
                $"Could not rebuild the declared value: {e.Message}. The field starts at its zero value.",
                $"{declaringType.Name}.{field.Name}");
            return null;
        }
    }

    private static bool SameField(FieldReference a, FieldReference? b)
        => b != null
           && a.Name == b.Name
           && OpenName(a.DeclaringType) == OpenName(b.DeclaringType);

    private static string OpenName(TypeReference type) => (type.GetElementType() ?? type).FullName;

    /// <summary>One statement of a constructor prologue: everything from a 'this' load until the stack empties.</summary>
    private sealed class CtorAction
    {
        public MethodDefinition Constructor = null!;
        public Instruction? First;
        public Instruction? Last;
        public FieldReference? Field;
        public bool IsFieldStore;
        public bool ChainsToPeerConstructor;
        public bool ReadsParameter;
    }

    private static IEnumerable<CtorAction> ReadActions(MethodDefinition constructor)
    {
        if (!constructor.HasBody || constructor.Body.Instructions.Count == 0) yield break;

        Instruction? at = constructor.Body.Instructions[0];

        while (ReadAction(constructor, ref at) is { } action)
        {
            // The prologue ends at the first thing that is not a field assignment.
            if (!action.IsFieldStore) yield break;
            yield return action;
        }
    }

    private static CtorAction? ReadAction(MethodDefinition constructor, ref Instruction? at)
    {
        if (at?.OpCode.Code != Code.Ldarg_0) return null;

        var action = new CtorAction { Constructor = constructor, First = at };
        at = at.Next;

        int stack = 0;
        while (at != null)
        {
            if (IsLoadArgument(at)) action.ReadsParameter = true;

            stack += StackDelta(constructor, at);
            if (stack < 0) break; // the statement's stack frame emptied, this instruction completes it

            at = at.Next;
        }

        if (at == null) return null;

        action.Last = at;
        action.IsFieldStore = at.OpCode.Code == Code.Stfld;

        if (action.IsFieldStore)
            action.Field = at.Operand as FieldReference;
        else if (at.OpCode.Code == Code.Call && at.Operand is MethodReference call && call.Name == ".ctor")
            action.ChainsToPeerConstructor = call.DeclaringType == constructor.DeclaringType;

        at = at.Next;
        return action;
    }

    private static bool IsLoadArgument(Instruction instruction) => instruction.OpCode.Code switch
    {
        Code.Ldarg or Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3
            or Code.Ldarg_S or Code.Ldarga or Code.Ldarga_S => true,
        _ => false,
    };

    // Stack size delta per Cecil StackBehaviour, indexed by the enum value.
    private static readonly int[] s_stackBehaviour =
    {
        0, 1, 2, 1, 2, 2, 2, 3, 2, 2, 1, 2, 2, 3, 3, 3, 3, 3, 0, 0, 1, 2, 1, 1, 1, 1, 1, 0, 0,
    };

    private static int StackDelta(MethodDefinition method, Instruction instruction)
    {
        int delta = 0;

        if (instruction.OpCode.StackBehaviourPop != CecilStackBehaviour.Varpop)
        {
            delta -= s_stackBehaviour[(int)instruction.OpCode.StackBehaviourPop];
        }
        else if (instruction.OpCode.FlowControl == CecilFlowControl.Return)
        {
            return method.ReturnType.FullName == "System.Void" ? 0 : 1;
        }
        else
        {
            delta -= instruction.Operand switch
            {
                MethodReference reference => ArgumentCount(instruction.OpCode, reference),
                MethodBase runtime => ArgumentCount(instruction.OpCode, runtime),
                _ => throw new NotSupportedException($"Unsupported variable pop for {instruction.OpCode.Name}."),
            };
        }

        if (instruction.OpCode.StackBehaviourPush != CecilStackBehaviour.Varpush)
        {
            delta += s_stackBehaviour[(int)instruction.OpCode.StackBehaviourPush];
        }
        else
        {
            delta += instruction.Operand switch
            {
                MethodReference reference => reference.ReturnType.FullName == "System.Void" ? 0 : 1,
                ConstructorInfo => 1,
                MethodInfo runtime => runtime.ReturnType != typeof(void) ? 1 : 0,
                _ => throw new NotSupportedException($"Unsupported variable push for {instruction.OpCode.Name}."),
            };
        }

        return delta;
    }

    private static int ArgumentCount(CecilOpCode opCode, MethodReference method)
    {
        int count = method.HasParameters ? method.Parameters.Count : 0;
        if (method.HasThis && opCode.Code != Code.Newobj) count++;
        return count;
    }

    private static int ArgumentCount(CecilOpCode opCode, MethodBase method)
    {
        int count = method.GetParameters().Length;
        if (!method.IsStatic && opCode.Code != Code.Newobj) count++;
        return count;
    }
}
