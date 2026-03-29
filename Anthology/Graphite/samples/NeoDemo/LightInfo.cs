using System.Numerics;
using System.Runtime.InteropServices;

namespace NeoVeldrid.NeoDemo
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LightInfo
    {
        public Vector3 Direction;
        private float _padding;
    }
}
