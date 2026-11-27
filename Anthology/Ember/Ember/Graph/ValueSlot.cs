using System;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>
/// A place a reference can be stored. The engine never writes through a bare <see cref="FieldInfo"/>, it
/// writes through one of these, so a storage location that cannot be assigned is known before anything is
/// allocated rather than discovered at the write site.
/// </summary>
public readonly struct ValueSlot
{
    private enum SlotKind { Empty, StaticField, InstanceField, ArrayElement, Custom }

    private sealed class CustomSlot
    {
        public Func<object?> Read = null!;
        public Action<object?>? Write;
        public Type DeclaredType = null!;
        public string Description = string.Empty;
    }

    private readonly SlotKind _kind;
    private readonly object? _owner;
    private readonly FieldInfo? _field;
    private readonly int[]? _indices;
    private readonly CustomSlot? _custom;

    private ValueSlot(SlotKind kind, object? owner, FieldInfo? field, int[]? indices, CustomSlot? custom)
    {
        _kind = kind;
        _owner = owner;
        _field = field;
        _indices = indices;
        _custom = custom;
    }

    public static ValueSlot StaticField(FieldInfo field)
    {
        if (field == null) throw new ArgumentNullException(nameof(field));
        if (!field.IsStatic) throw new ArgumentException("Field is not static.", nameof(field));
        return new ValueSlot(SlotKind.StaticField, null, field, null, null);
    }

    public static ValueSlot InstanceField(object target, FieldInfo field)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (field == null) throw new ArgumentNullException(nameof(field));
        return new ValueSlot(SlotKind.InstanceField, target, field, null, null);
    }

    public static ValueSlot ArrayElement(Array array, params int[] indices)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));
        if (indices == null || indices.Length != array.Rank)
            throw new ArgumentException("Index count does not match the array rank.", nameof(indices));
        return new ValueSlot(SlotKind.ArrayElement, array, null, indices, null);
    }

    /// <summary>
    /// Storage the engine cannot see for itself, such as a native handle table or a component array behind an
    /// accessor. A null write makes the slot read only, which routes it through the in place path.
    /// </summary>
    public static ValueSlot Custom(Func<object?> read, Action<object?>? write, Type declaredType, string description)
    {
        if (read == null) throw new ArgumentNullException(nameof(read));
        if (declaredType == null) throw new ArgumentNullException(nameof(declaredType));

        return new ValueSlot(SlotKind.Custom, null, null, null, new CustomSlot
        {
            Read = read,
            Write = write,
            DeclaredType = declaredType,
            Description = description ?? string.Empty,
        });
    }

    public bool IsEmpty => _kind == SlotKind.Empty;

    public Type DeclaredType => _kind switch
    {
        SlotKind.StaticField or SlotKind.InstanceField => _field!.FieldType,
        SlotKind.ArrayElement => ((Array)_owner!).GetType().GetElementType()!,
        SlotKind.Custom => _custom!.DeclaredType,
        _ => typeof(object),
    };

    public bool CanWrite => _kind switch
    {
        SlotKind.StaticField or SlotKind.InstanceField => !_field!.IsInitOnly && !_field.IsLiteral,
        SlotKind.ArrayElement => true,
        SlotKind.Custom => _custom!.Write != null,
        _ => false,
    };

    public object? Read() => _kind switch
    {
        SlotKind.StaticField => _field!.GetValue(null),
        SlotKind.InstanceField => _field!.GetValue(_owner),
        SlotKind.ArrayElement => ((Array)_owner!).GetValue(_indices!),
        SlotKind.Custom => _custom!.Read(),
        _ => null,
    };

    public bool TryWrite(object? value)
    {
        if (!CanWrite) return false;

        switch (_kind)
        {
            case SlotKind.StaticField: _field!.SetValue(null, value); return true;
            case SlotKind.InstanceField: _field!.SetValue(_owner, value); return true;
            case SlotKind.ArrayElement: ((Array)_owner!).SetValue(value, _indices!); return true;
            case SlotKind.Custom: _custom!.Write!(value); return true;
            default: return false;
        }
    }

    /// <summary>The type whose storage this is, used to group diagnostics. Null when there is no owning type.</summary>
    internal Type? OwnerType => _kind switch
    {
        SlotKind.StaticField => _field!.DeclaringType,
        SlotKind.InstanceField => _owner!.GetType(),
        SlotKind.ArrayElement => _owner!.GetType(),
        _ => null,
    };

    public override string ToString() => _kind switch
    {
        SlotKind.StaticField => $"static {_field!.DeclaringType?.Name}.{_field.Name}",
        SlotKind.InstanceField => $"{_field!.DeclaringType?.Name}.{_field.Name}",
        SlotKind.ArrayElement => $"{_owner!.GetType().Name}[{string.Join(",", _indices!)}]",
        SlotKind.Custom => _custom!.Description,
        _ => "<empty>",
    };
}
