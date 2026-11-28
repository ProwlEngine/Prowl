// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

#if NETSTANDARD2_1
#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System;

/// <summary>Newer runtime helpers that netstandard2.1 lacks, so the same calls build on every target.</summary>
internal static class NetStandardPolyfills
{
    extension(ArgumentNullException)
    {
        public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null)
                throw new ArgumentNullException(paramName);
        }
    }

    extension(ArgumentOutOfRangeException)
    {
        public static void ThrowIfNegative(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} ('{value}') must be a non-negative value.");
        }
    }

    extension(Enum)
    {
        public static TEnum[] GetValues<TEnum>() where TEnum : struct, Enum => (TEnum[])Enum.GetValues(typeof(TEnum));
    }

    extension(string)
    {
        public static string Concat(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
        {
            char[] chars = new char[first.Length + second.Length];
            first.CopyTo(chars);
            second.CopyTo(chars.AsSpan(first.Length));
            return new string(chars);
        }
    }
}
#endif
