using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Prowl.Slang.Native;


/// <summary>
/// Represents a UTF-8 encoded string primarily for interop with native code.
/// Manages the lifetime of the allocated native memory for the string.
/// </summary>
public readonly unsafe struct U8Str : IDisposable
{
    public readonly byte* Pointer; // Pointer to the beginning of the UTF-8 byte sequence.
    public readonly int Length;    // Length of the string content in bytes, excluding any null terminator.

    /// <summary>
    /// Allocates native memory and creates a null-terminated UTF-8 string from a managed string.
    /// </summary>
    /// <param name="text">The managed string to convert.</param>
    /// <returns>A U8Str instance pointing to the native UTF-8 string.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the input text is null.</exception>
    /// <exception cref="OutOfMemoryException">Thrown if native memory allocation fails.</exception>
    public static U8Str Alloc(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Get the number of bytes required to encode the string in UTF-8.
        // This count does NOT include the null terminator.
        int byteCount = Encoding.UTF8.GetByteCount(text);

        // Allocate native memory: string content + 1 byte for the null terminator.
        byte* bytes = (byte*)NativeMemory.Alloc((nuint)byteCount + 1);
        if (bytes == null) // Should ideally not happen with NativeMemory.Alloc as it throws on failure.
        {
            throw new OutOfMemoryException("Failed to allocate native memory for UTF-8 string.");
        }

        // Encode the managed string directly into the allocated native memory.
        // Pinning the managed string's characters to get a char* for GetBytes.
        fixed (char* chars = text)
        {
            Encoding.UTF8.GetBytes(chars, text.Length, bytes, byteCount);
        }

        // Add the null terminator at the end of the encoded string.
        bytes[byteCount] = 0;

        // Return a U8Str instance. The Length field stores the content length (excluding null terminator).
        return new U8Str(bytes, byteCount);
    }

    /// <summary>
    /// Frees the native memory previously allocated for this U8Str.
    /// </summary>
    /// <param name="val">The U8Str whose native memory should be freed.</param>
    public static void Free(U8Str val)
    {
        if (val.Pointer != null)
        {
            NativeMemory.Free(val.Pointer);
        }
    }

    public void Dispose()
    {
        Free(this);
    }

    /// <summary>
    /// Internal constructor used by Alloc and potentially by direct span usage.
    /// </summary>
    /// <param name="utf8">Pointer to the UTF-8 byte sequence.</param>
    /// <param name="len">Length of the content in bytes (excluding null terminator if present).</param>
    internal U8Str(byte* utf8, int len)
    {
        Pointer = utf8;
        Length = len;
    }

    /// <summary>
    /// Creates a U8Str from a ReadOnlySpan&lt;byte&gt; (e.g., from a u8 string literal).
    /// Assumes the span's memory is valid for the lifetime of this U8Str if not managed elsewhere.
    /// For u8 literals, the span is typically null-terminated and its memory is static.
    /// The Length will be the span's length. If the span includes a null terminator,
    /// Length will also include it. This constructor is mostly for u8 literals where
    /// memory management is handled by the runtime/compiler.
    /// </summary>
    public U8Str(ReadOnlySpan<byte> utf8)
    {
        // This gets a reference to the start of the span's data.
        // It's safe for u8 literals as their data is static.
        // For other spans, the caller must ensure the span's memory outlives the U8Str
        // or that this U8Str is only used temporarily while the span is valid.
        Pointer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(utf8));
        Length = utf8.Length; // This length might include the null terminator if the span does.
                              // For native calls expecting null-termination, this is fine as they read up to '\0'.
    }

    public static implicit operator byte*(U8Str str)
    {
        return str.Pointer;
    }

    /// <summary>
    /// Gets the string representation. Reads the native memory up to the first null terminator.
    /// </summary>
    public string String => Pointer == null ? "" : (Marshal.PtrToStringUTF8((IntPtr)Pointer) ?? "");
}

/// <summary>
/// Represents a constant (non-owned) pointer to a UTF-8 string,
/// typically for passing to native functions that expect a `const char*`.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ConstU8Str
{
    public byte* Data; // Pointer to the null-terminated UTF-8 byte sequence.

    public static implicit operator ConstU8Str(U8Str str)
    {
        return new ConstU8Str { Data = str.Pointer };
    }

    /// <summary>
    /// Gets the string representation. Reads the native memory up to the first null terminator.
    /// </summary>
    public string String => Data == null ? "" : (Marshal.PtrToStringUTF8((IntPtr)Data) ?? "");
}
