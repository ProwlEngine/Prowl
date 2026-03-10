// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

/// <summary>
/// Interface for reading and writing EchoObject trees to different file formats.
/// Implement this interface to add support for custom serialization formats.
/// </summary>
public interface IFileFormat
{
    /// <summary>
    /// Write an EchoObject to a stream.
    /// </summary>
    void WriteTo(EchoObject tag, Stream stream);

    /// <summary>
    /// Read an EchoObject from a stream.
    /// </summary>
    EchoObject ReadFrom(Stream stream);
}

/// <summary>
/// Extension methods providing string, file, and byte[] convenience methods for IFileFormat.
/// </summary>
public static class FileFormatExtensions
{
    /// <summary>
    /// Write an EchoObject to a UTF-8 string.
    /// Best suited for text-based formats (JSON, YAML, XML, EchoText).
    /// </summary>
    public static string WriteToString(this IFileFormat format, EchoObject tag)
    {
        using var stream = new MemoryStream();
        format.WriteTo(tag, stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Read an EchoObject from a UTF-8 string.
    /// Best suited for text-based formats (JSON, YAML, XML, EchoText).
    /// </summary>
    public static EchoObject ReadFromString(this IFileFormat format, string input)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(input));
        return format.ReadFrom(stream);
    }

    /// <summary>
    /// Write an EchoObject to a byte array.
    /// Works for both text and binary formats.
    /// </summary>
    public static byte[] WriteToBytes(this IFileFormat format, EchoObject tag)
    {
        using var stream = new MemoryStream();
        format.WriteTo(tag, stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Read an EchoObject from a byte array.
    /// Works for both text and binary formats.
    /// </summary>
    public static EchoObject ReadFromBytes(this IFileFormat format, byte[] data)
    {
        using var stream = new MemoryStream(data);
        return format.ReadFrom(stream);
    }

    /// <summary>
    /// Write an EchoObject to a file.
    /// </summary>
    public static void WriteToFile(this IFileFormat format, EchoObject tag, string path)
    {
        using var stream = File.Create(path);
        format.WriteTo(tag, stream);
    }

    /// <summary>
    /// Read an EchoObject from a file.
    /// </summary>
    public static EchoObject ReadFromFile(this IFileFormat format, string path)
    {
        using var stream = File.OpenRead(path);
        return format.ReadFrom(stream);
    }
}
