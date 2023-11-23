using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime
{
    public static class UnsafeUtils
    {
        #region Pointers

        public static unsafe T[] MakeArray<T>(void* t, int length) where T : struct
        {
            var tSizeInBytes = Marshal.SizeOf(typeof(T));
            T[] result = new T[length];
            for (int i = 0; i < length; i++)
            {
                IntPtr p = new IntPtr((byte*)t + (i * tSizeInBytes));
                result[i] = (T)System.Runtime.InteropServices.Marshal.PtrToStructure(p, typeof(T));
            }

            return result;
        }

        public static unsafe T* AllocateArray<T>(T[] array) where T : struct
        {
            var tSizeInBytes = Marshal.SizeOf(typeof(T));
            var ptr = Marshal.AllocHGlobal(tSizeInBytes * array.Length);
            for (int i = 0; i < array.Length; i++)
            {
                var byteOffset = new IntPtr((byte*)ptr.ToPointer() + i * tSizeInBytes);
                Marshal.StructureToPtr(array[i], byteOffset, false);
            }
            return (T*)ptr.ToPointer();
        }

        #endregion
    }
}
