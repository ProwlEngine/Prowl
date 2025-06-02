// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Slang;


/// <summary>
/// A generic type argument used when specializing a generic type.
/// </summary>
public readonly struct GenericTypeArgument
{
    /// <summary>
    /// The type of the generic argument.
    /// </summary>
    public readonly GenericArgType Type;

    /// <summary>
    /// The generic argument value as a reflection type.
    /// </summary>
    public readonly TypeReflection TypeVal;

    /// <summary>
    /// The generic argument value as an integer.
    /// </summary>
    public readonly long IntVal;

    /// <summary>
    /// The generic argument value as a boolean.
    /// </summary>
    public readonly bool BoolVal;


    /// <summary>
    /// Creates a new generic type argument from a reflection type.
    /// </summary>
    public GenericTypeArgument(TypeReflection typeVal)
    {
        Type = GenericArgType.Type;
        TypeVal = typeVal;
        IntVal = 0;
        BoolVal = false;
    }


    /// <summary>
    /// Creates a new generic type argument from an integer value.
    /// </summary>
    public GenericTypeArgument(long intVal)
    {
        Type = GenericArgType.Int;
        TypeVal = default;
        IntVal = intVal;
        BoolVal = false;
    }


    /// <summary>
    /// Creates a new generic type argument from a boolean value.
    /// </summary>
    public GenericTypeArgument(bool boolVal)
    {
        Type = GenericArgType.Bool;
        TypeVal = default;
        IntVal = 0;
        BoolVal = boolVal;
    }


    ///
    public static implicit operator GenericTypeArgument(TypeReflection typeVal)
    {
        return new GenericTypeArgument(typeVal);
    }


    ///
    public static implicit operator GenericTypeArgument(long intVal)
    {
        return new GenericTypeArgument(intVal);
    }


    ///
    public static implicit operator GenericTypeArgument(bool boolVal)
    {
        return new GenericTypeArgument(boolVal);
    }


    internal unsafe Native.GenericArgReflection ToArgReflection()
    {
        if (Type == GenericArgType.Type)
            return new() { typeVal = TypeVal._ptr };

        if (Type == GenericArgType.Int)
            return new() { intVal = IntVal };

        return new() { boolVal = BoolVal };
    }
}
