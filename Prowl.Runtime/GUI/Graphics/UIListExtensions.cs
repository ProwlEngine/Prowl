using System;
using System.Collections.Generic;

namespace Prowl.Runtime.GUI.Graphics
{
    public static class UIListExtensions
    {
        public static T Peek<T>(this List<T> list)
        {
            return list[list.Count - 1];
        }

        public static void Reserve<T>(this List<T> list, int new_capacity)
        {
            if (new_capacity <= list.Capacity) 
                return;
            
            list.Resize(new_capacity);
        }

        public static void Resize<T>(this List<T> list, int sz)
        {
            int cur = list.Count;
            if (sz < cur)
                list.RemoveRange(sz, cur - sz);
            
            else if (sz > cur)
            {
                if (sz > list.Capacity)
                    list.Capacity = sz;

                list.AddRange(System.Linq.Enumerable.Repeat<T>(default, sz - cur));
            }
        }

        public static void Pop<T>(this List<T> list)
        {
            list.RemoveAt(list.Count - 1);
        }
    }
}