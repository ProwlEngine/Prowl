using System.Runtime.InteropServices;

namespace NeoVeldrid.Tests
{
    [StructLayout(LayoutKind.Sequential)]
    public struct UInt4
    {
        public uint X, Y, Z, W;
    }
}
