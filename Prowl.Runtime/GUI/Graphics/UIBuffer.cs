using System;

namespace Prowl.Runtime.GUI.Graphics
{
    public class UIBuffer<T> : IDisposable
    {
        public int Count = 0;
        public int _capacity = 0;

        public int Capacity
        {
            get => _capacity;
            set
            {
                reserve(value);
            }
        }

        public T[] Data;

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
            Count = _capacity = 0;
            Data = null;
        }

        public T Peek()
        {
            return Data[Count - 1];
        }

        public int _grow_capacity(int new_size)
        {
            int new_capacity = _capacity > 0 ? _capacity + _capacity / 2 : 8;
            return new_capacity > new_size ? new_capacity : new_size;
        }

        public void resize(int new_size)
        {
            if (new_size > _capacity)
                reserve(_grow_capacity(new_size));
            Count = new_size;
        }

        public void reserve(int new_capacity)
        {
            if (new_capacity <= _capacity) return;
            _capacity = new_capacity;
            if (Data == null)
                Data = new T[new_capacity];
            else
                Array.Resize(ref Data, new_capacity);
        }

        public void Add(T v)
        {
            if (Count == _capacity)
                reserve(_grow_capacity(Count + 1));
            Data[Count++] = v;
        }
        public void Pop()
        {
            Count--;
        }

        public void erase(int it)
        {
            System.Diagnostics.Debug.Assert(it >= 0 && it < Count);
            for (var i = it; i < Count - 1; i++)
                Data[i] = Data[i + 1];
            Count--;
        }

        public void insert(int it, T v)
        {
            System.Diagnostics.Debug.Assert(it >= 0 && it <= Count);
            var off = it;
            if (Count == _capacity)
                reserve(_capacity > 0 ? _capacity * 2 : 4);
            if (off < Count)
            {
                for (int i = Count; i > it; i--)
                    Data[i] = Data[i - 1];
            }
            Data[off] = v;
            Count++;
        }

        public void sort(Func<T, T, int> sorter)
        {
            Array.Sort(Data, new Comparison<T>(sorter));
        }

        public void swap(UIBuffer<T> rhs)
        {
            int rhs_size = rhs.Count;
            rhs.Count = Count;
            Count = rhs_size;

            int rhs_cap = rhs._capacity;
            rhs._capacity = _capacity;
            _capacity = rhs_cap;

            T[] rhs_data = rhs.Data;
            rhs.Data = Data;
            Data = rhs_data;
        }


        public void Dispose()
        {
            Data = null;
        }

    }
}
