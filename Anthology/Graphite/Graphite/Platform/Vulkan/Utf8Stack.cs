using System;
using System.Text;

namespace Prowl.Graphite.Vk;

// Managed string -> null-terminated UTF8 helper for stackalloc call sites. The caller still owns the
// stackalloc (it must live in the caller's frame), this just sizes and fills it in one step each.
internal static unsafe class Utf8Stack
{
    internal static int ByteCount(string value) => Encoding.UTF8.GetByteCount(value) + 1;

    internal static void Write(string value, byte* destination)
    {
        int written = Encoding.UTF8.GetBytes(value, new Span<byte>(destination, ByteCount(value)));
        destination[written] = 0;
    }
}
