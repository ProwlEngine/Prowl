using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using Mono.Cecil;
using Mono.Cecil.Cil;

using EmitOpCode = System.Reflection.Emit.OpCode;
using EmitOpCodes = System.Reflection.Emit.OpCodes;
using CecilOpCode = Mono.Cecil.Cil.OpCode;

namespace Prowl.Ember;

/// <summary>
/// Translates a slice of Cecil IL into a live <see cref="ILGenerator"/>, resolving each operand's metadata
/// token against the runtime module. Used to rebuild a field initializer expression as a standalone method,
/// so a newly added field can get its declared value without running any constructor body.
/// </summary>
internal static class ILTranslator
{
    private static readonly EmitOpCode[] s_conversionTable = BuildConversionTable();

    private static EmitOpCode[] BuildConversionTable()
    {
        var opCodes = new List<EmitOpCode>();
        int max = 0;

        foreach (var field in typeof(EmitOpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (EmitOpCode)field.GetValue(null)!;
            opCodes.Add(value);
            max = Math.Max(max, value.Value + 1);
        }

        var table = new EmitOpCode[max];
        foreach (var opCode in opCodes)
            if (opCode.Value >= 0)
                table[opCode.Value] = opCode;
        return table;
    }

    private static EmitOpCode Convert(CecilOpCode code) => s_conversionTable[code.Value];

    /// <summary>
    /// Emits the instructions from <paramref name="first"/> up to but not including <paramref name="until"/>.
    /// Operands are resolved into a side table rather than written back onto the Cecil instructions, so the
    /// underlying assembly definition is never mutated and a slice can be translated more than once.
    /// </summary>
    public static void Emit(ILGenerator il, Module context, Instruction first, Instruction until,
        Type[]? genericTypeArguments, Type[]? genericMethodArguments)
    {
        var instructions = new List<Instruction>();
        for (var inst = first; inst != until; inst = inst.Next)
        {
            if (inst == null) throw new InvalidOperationException("Instruction slice ran past the end of the method body.");
            instructions.Add(inst);
        }

        var labels = new Dictionary<Instruction, Label>();
        var locals = new Dictionary<VariableDefinition, LocalBuilder>();
        var operands = new object?[instructions.Count];

        for (int i = 0; i < instructions.Count; i++)
        {
            operands[i] = instructions[i].Operand switch
            {
                Instruction target => DefineLabel(il, labels, target),
                Instruction[] targets => DefineLabels(il, labels, targets),
                VariableDefinition variable => DeclareLocal(il, context, locals, variable, genericTypeArguments, genericMethodArguments),
                TypeReference type => ResolveType(context, type, genericTypeArguments, genericMethodArguments),
                FieldReference field => ResolveField(context, field, genericTypeArguments, genericMethodArguments),
                MethodReference method => ResolveMethod(context, method, genericTypeArguments, genericMethodArguments),
                var other => other,
            };
        }

        for (int i = 0; i < instructions.Count; i++)
        {
            if (labels.TryGetValue(instructions[i], out var label))
                il.MarkLabel(label);

            Emit(il, Convert(instructions[i].OpCode), operands[i]);
        }
    }

    private static void Emit(ILGenerator il, EmitOpCode opCode, object? operand)
    {
        switch (operand)
        {
            case null: il.Emit(opCode); break;
            case string s: il.Emit(opCode, s); break;
            case Label l: il.Emit(opCode, l); break;
            case Label[] ls: il.Emit(opCode, ls); break;
            case Type t: il.Emit(opCode, t); break;
            case FieldInfo f: il.Emit(opCode, f); break;
            case MethodInfo m: il.Emit(opCode, m); break;
            case ConstructorInfo c: il.Emit(opCode, c); break;
            case LocalBuilder lb: il.Emit(opCode, lb); break;
            case sbyte sb: il.Emit(opCode, sb); break;
            case byte b: il.Emit(opCode, b); break;
            case short sh: il.Emit(opCode, sh); break;
            case int i: il.Emit(opCode, i); break;
            case long lo: il.Emit(opCode, lo); break;
            case float fl: il.Emit(opCode, fl); break;
            case double d: il.Emit(opCode, d); break;
            default: throw new NotSupportedException($"Unsupported IL operand {operand.GetType().FullName}.");
        }
    }

    private static Label DefineLabel(ILGenerator il, Dictionary<Instruction, Label> labels, Instruction target)
    {
        if (labels.TryGetValue(target, out var label)) return label;
        label = il.DefineLabel();
        labels.Add(target, label);
        return label;
    }

    private static Label[] DefineLabels(ILGenerator il, Dictionary<Instruction, Label> labels, Instruction[] targets)
    {
        var defined = new Label[targets.Length];
        for (int i = 0; i < targets.Length; i++)
            defined[i] = DefineLabel(il, labels, targets[i]);
        return defined;
    }

    private static LocalBuilder DeclareLocal(ILGenerator il, Module context, Dictionary<VariableDefinition, LocalBuilder> locals,
        VariableDefinition variable, Type[]? genericTypeArguments, Type[]? genericMethodArguments)
    {
        if (locals.TryGetValue(variable, out var local)) return local;
        local = il.DeclareLocal(ResolveType(context, variable.VariableType, genericTypeArguments, genericMethodArguments), variable.IsPinned);
        locals.Add(variable, local);
        return local;
    }

    public static Type ResolveType(Module context, TypeReference type, Type[]? genericTypeArguments, Type[]? genericMethodArguments)
        => context.ResolveType(type.MetadataToken.ToInt32(), genericTypeArguments, genericMethodArguments);

    public static FieldInfo ResolveField(Module context, FieldReference field, Type[]? genericTypeArguments, Type[]? genericMethodArguments)
        => context.ResolveField(field.MetadataToken.ToInt32(), genericTypeArguments, genericMethodArguments)!;

    public static MethodBase ResolveMethod(Module context, MethodReference method, Type[]? genericTypeArguments, Type[]? genericMethodArguments)
        => context.ResolveMethod(method.MetadataToken.ToInt32(), genericTypeArguments, genericMethodArguments)!;
}
