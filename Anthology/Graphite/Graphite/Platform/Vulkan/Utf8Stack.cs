using System.Text;

namespace Prowl.Graphite.Vk;

// Managed string -> null-terminated UTF8 helper for stackalloc call sites. The caller still owns the
// stackalloc (it must live in the caller's frame), this just sizes and fills it in one step each.
internal static unsafe class Utf8Stack
{
    internal static int ByteCount(string value) => Encoding.UTF8.GetByteCount(value) + 1;

    internal static void Write(string value, byte* destination)
    {
        int byteCount;
        fixed (char* namePtr = value)
        {
            byteCount = Encoding.UTF8.GetBytes(namePtr, value.Length, destination, Encoding.UTF8.GetByteCount(value));
        }
        destination[byteCount] = 0;
    }
}
