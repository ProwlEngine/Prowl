// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

// Allocation free helpers shared by the text based file formats.
internal static class TextFormatting
{
    private static readonly string?[] SpaceCache = new string?[128];

    public static string Spaces(int count)
    {
        if (count >= SpaceCache.Length)
            return new string(' ', count);
        return SpaceCache[count] ??= new string(' ', count);
    }

    // Same text as TextWriter.Write(long) or long.ToString(provider).
    public static void WriteNumber(TextWriter writer, long value, IFormatProvider? provider)
    {
        Span<char> buffer = stackalloc char[24];
        value.TryFormat(buffer, out int length, default, provider);
        writer.Write(buffer.Slice(0, length));
    }

    public static void WriteNumber(TextWriter writer, ulong value, IFormatProvider? provider)
    {
        Span<char> buffer = stackalloc char[24];
        value.TryFormat(buffer, out int length, default, provider);
        writer.Write(buffer.Slice(0, length));
    }

    // Writes value.ToString(format, provider) and returns the written text, valid until the caller returns.
    public static ReadOnlySpan<char> WriteNumber(TextWriter writer, double value, Span<char> buffer, string? format, IFormatProvider? provider)
    {
        value.TryFormat(buffer, out int length, format, provider);
        var text = buffer.Slice(0, length);
        writer.Write(text);
        return text;
    }

    public static ReadOnlySpan<char> WriteNumber(TextWriter writer, float value, Span<char> buffer, string? format, IFormatProvider? provider)
    {
        value.TryFormat(buffer, out int length, format, provider);
        var text = buffer.Slice(0, length);
        writer.Write(text);
        return text;
    }
}
