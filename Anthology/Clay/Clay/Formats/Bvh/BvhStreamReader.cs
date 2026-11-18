// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Globalization;

namespace Prowl.Clay.Formats.Bvh;

/// <summary>
/// A simple wrapper around a <see cref="StreamReader"/>.
/// </summary>
/// <param name="stream">The stream to create the reader from.</param>
internal sealed class BvhStreamReader(Stream stream) : IDisposable
{
    /// <summary>
    /// Disposes of any unmanaged resources.
    /// </summary>
    void IDisposable.Dispose() => Reader.Dispose();

    /// <summary>
    /// The underlying stream reader.
    /// </summary>
    private StreamReader Reader { get; } = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);

    /// <summary>
    /// The line number of the most recently read line (0 if no lines have been read).
    /// </summary>
    internal int Line { get; private set; }

    /// <summary>Reads a non-empty line from the stream reader (returns null at EOF).</summary>
    internal string? ReadLine()
    {
        do
        {
            string? line = Reader.ReadLine();
            if (line is null)
                return null;
            Line++;
            if (!string.IsNullOrWhiteSpace(line))
                return line;
        } while (true);
    }

    /// <summary>Reads a non-empty line from the stream reader (throws if it's empty).</summary>
    internal string ExpectLine()
    {
        return ReadLine() ?? throw new InvalidDataException($"Unexpected EOF while parsing (line {Line}).");
    }

    /// <summary>Compares two strings and throws an error if they are different.</summary>
    internal void ExpectToken(string expected, string token)
    {
        if (!string.Equals(token, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected '{expected}' but found '{token}' (line {Line}).");
    }

    /// <summary>Splits a line of text using BVH parsing rules.</summary>
    internal int Split(ReadOnlySpan<char> source, Span<Range> destination)
    {
        return MemoryExtensions.SplitAny(source, destination, " \t", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Parses a string as a float using BVH parsing rules (throws if parsing fails).</summary>
    internal float ParseFloat(ReadOnlySpan<char> text)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || !float.IsFinite(v))
            throw new InvalidDataException($"Couldn't parse '{text}' as a finite number (line {Line}).");
        return v;
    }

    /// <summary>Parses a string as an int using BVH parsing rules (throws if parsing fails).</summary>
    internal int ParseInt(ReadOnlySpan<char> text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            throw new InvalidDataException($"Couldn't parse '{text}' as an integer (line {Line}).");
        return v;
    }
}
