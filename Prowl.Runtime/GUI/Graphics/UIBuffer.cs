using System;
using System.Collections.Generic;

namespace Prowl.Runtime.GUI.Graphics
{
    public class UIBuffer<T>
    {
        public int Count => Data.Count;
        public int Capacity => Data.Capacity;

        public List<T> Data = new();


        public T this[int i]
        {
            get
            {
                return Data[i];
            }
            set
            {
                Data[i] = value;
            }

        }

        public void Clear()
        {
            Data.Clear();
        }

        public T Peek()
        {
            return Data[Count - 1];
        }

        public void Resize(int new_size)
        {
            int cur = Data.Count;

            if (new_size < cur)
                Data.RemoveRange(new_size, cur - new_size);
            else if(new_size > cur)
            {
                if (new_size > Data.Capacity) //this bit is purely an optimisation, to avoid multiple automatic capacity changes.
                    Data.Capacity = new_size;

                Data.AddRange(System.Linq.Enumerable.Repeat<T>(default, new_size - cur));
            }
        }

        public void Reserve(int new_capacity)
        {
            if (new_capacity <= Capacity) return;

            Resize(new_capacity);
        }

        public void Add(T v)
        {
            Data.Add(v);
        }

        public void Pop()
        {
            Data.RemoveAt(Data.Count - 1);
        }

        public void RemoveAt(int it)
        {
            Data.RemoveAt(it);
        }

        public void Insert(int it, T v)
        {
            Data.Insert(it, v);
        }

        public void Sort(Comparison<T> sorter)
        {
            Data.Sort(sorter);
        }

        public void Swap(UIBuffer<T> rhs)
        {
            List<T> rhs_data = rhs.Data;
            rhs.Data = Data;
            Data = rhs_data;
        }
    }
}
