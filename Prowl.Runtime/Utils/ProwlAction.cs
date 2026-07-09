// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>The kind of value a <see cref="ProwlCall"/> passes to its target member.</summary>
public enum ProwlActionArgType
{
    /// <summary>No argument - the target is a parameterless method.</summary>
    None,
    Bool,
    Int,
    Float,
    String,
    /// <summary>A reference to another scene object or asset (a <see cref="EngineObject"/>).</summary>
    Object,
}

/// <summary>
/// A single persistent call in a <see cref="ProwlAction"/>: invoke a method, or set a property/field,
/// on a referenced scene object with one optional basic argument. The target is a polymorphic scene
/// reference (a GameObject or any Component) - Echo persists it through the same object-graph tracking
/// used by every other cross-object field, so no manual id bookkeeping is needed.
/// </summary>
public sealed class ProwlCall
{
    [SerializeField] private EngineObject? _target;
    [SerializeField] private string _member = "";
    [SerializeField] private ProwlActionArgType _argType = ProwlActionArgType.None;

    // Only the field matching _argType carries meaning; the rest stay at their defaults.
    [SerializeField] private bool _boolArg;
    [SerializeField] private int _intArg;
    [SerializeField] private float _floatArg;
    [SerializeField] private string _stringArg = "";
    [SerializeField] private EngineObject? _objectArg;

    /// <summary>The object the call is made on: a GameObject or any Component.</summary>
    public EngineObject? Target { get => _target; set => _target = value; }

    /// <summary>Name of the method, settable property, or field to invoke/set.</summary>
    public string Member { get => _member; set => _member = value ?? ""; }

    public ProwlActionArgType ArgType { get => _argType; set => _argType = value; }

    public bool BoolArg { get => _boolArg; set => _boolArg = value; }
    public int IntArg { get => _intArg; set => _intArg = value; }
    public float FloatArg { get => _floatArg; set => _floatArg = value; }
    public string StringArg { get => _stringArg; set => _stringArg = value ?? ""; }
    public EngineObject? ObjectArg { get => _objectArg; set => _objectArg = value; }

    private object? ArgValue() => _argType switch
    {
        ProwlActionArgType.Bool => _boolArg,
        ProwlActionArgType.Int => _intArg,
        ProwlActionArgType.Float => _floatArg,
        ProwlActionArgType.String => _stringArg,
        ProwlActionArgType.Object => _objectArg,
        _ => null,
    };

    /// <summary>
    /// Resolves and runs the call against its target. Tries, in order, a matching method, a settable
    /// property, then a field of the given name. No-op if the target or member is unset.
    /// </summary>
    public void Invoke()
    {
        if (_target == null || string.IsNullOrEmpty(_member)) return;

        Type type = _target.GetType();
        object? arg = ArgValue();

        MethodInfo? method = FindMethod(type, _member, _argType);
        if (method != null)
        {
            method.Invoke(_target, _argType == ProwlActionArgType.None ? null : new[] { arg });
            return;
        }

        PropertyInfo? prop = type.GetProperty(_member, BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(_target, arg);
            return;
        }

        FieldInfo? field = type.GetField(_member, BindingFlags.Public | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(_target, arg);
            return;
        }

        Debug.LogWarning($"[ProwlAction] Member '{_member}' not found on {type.Name}.");
    }

    private static MethodInfo? FindMethod(Type type, string name, ProwlActionArgType argType)
    {
        foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (m.Name != name || m.IsSpecialName) continue;
            ParameterInfo[] ps = m.GetParameters();

            if (argType == ProwlActionArgType.None)
            {
                if (ps.Length == 0) return m;
            }
            else if (ps.Length == 1 && ProwlActionArg.Matches(ps[0].ParameterType, argType))
            {
                return m;
            }
        }
        return null;
    }
}

/// <summary>
/// A serializable, editor-configurable list of persistent calls
/// Drop one on any component (public field or <c>[SerializeField]</c>) and the inspector lets authors
/// wire up scene references, pick a method/property/field by reflection, and supply a basic argument.
/// Invoke it from code (e.g. a button's click) to fire every configured call.
/// </summary>
public sealed class ProwlAction
{
    [SerializeField] private List<ProwlCall> _calls = new();

    /// <summary>The configured calls. Mutated in place by the inspector.</summary>
    public List<ProwlCall> Calls => _calls;

    /// <summary>Fires every call in order. A throwing call is logged and does not stop the rest.</summary>
    public void Invoke()
    {
        for (int i = 0; i < _calls.Count; i++)
        {
            try { _calls[i].Invoke(); }
            catch (Exception ex)
            {
                Debug.LogError($"[ProwlAction] Call {i} threw: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}

/// <summary>Maps CLR types to the basic <see cref="ProwlActionArgType"/> the action system supports.</summary>
public static class ProwlActionArg
{
    /// <summary>True (with the mapped <paramref name="argType"/>) when <paramref name="type"/> is a
    /// supported argument type. <c>void</c> maps to <see cref="ProwlActionArgType.None"/>.</summary>
    public static bool TryFromType(Type type, out ProwlActionArgType argType)
    {
        if (type == typeof(void)) { argType = ProwlActionArgType.None; return true; }
        if (type == typeof(bool)) { argType = ProwlActionArgType.Bool; return true; }
        if (type == typeof(int)) { argType = ProwlActionArgType.Int; return true; }
        if (type == typeof(float)) { argType = ProwlActionArgType.Float; return true; }
        if (type == typeof(string)) { argType = ProwlActionArgType.String; return true; }
        if (typeof(EngineObject).IsAssignableFrom(type)) { argType = ProwlActionArgType.Object; return true; }
        argType = ProwlActionArgType.None;
        return false;
    }

    /// <summary>Whether a parameter of <paramref name="paramType"/> can receive an argument stored as <paramref name="argType"/>.</summary>
    public static bool Matches(Type paramType, ProwlActionArgType argType) => argType switch
    {
        ProwlActionArgType.Bool => paramType == typeof(bool),
        ProwlActionArgType.Int => paramType == typeof(int),
        ProwlActionArgType.Float => paramType == typeof(float),
        ProwlActionArgType.String => paramType == typeof(string),
        ProwlActionArgType.Object => typeof(EngineObject).IsAssignableFrom(paramType),
        _ => false,
    };

    /// <summary>Short label for an argument type, used in the inspector's function dropdown.</summary>
    public static string Label(ProwlActionArgType argType) => argType switch
    {
        ProwlActionArgType.Bool => "bool",
        ProwlActionArgType.Int => "int",
        ProwlActionArgType.Float => "float",
        ProwlActionArgType.String => "string",
        ProwlActionArgType.Object => "Object",
        _ => "",
    };
}
