using System;

namespace Prowl.PaperUI.Utilities
{
    /// <summary>
    /// A high-performance alternative to List<T> for VertexArray operations.
    /// Provides direct array access
    /// </summary>
    /// <remarks> Initializes a new instance of the ArrayBuffer class with the specified capacity. </remarks>
    /// <param name="capacity">The initial capacity of the buffer.</param>
    internal class ArrayBuffer<T>
    {
        private T[] _array;        // Internal storage array
        private int _count = 0;    // Number of elements currently in use

        public ArrayBuffer(int capacity)
        {
            _array = new T[capacity];
        }

        /// <summary>
        /// Gets the underlying array regardless of how many elements are in use.
        /// This is the main reason for using this class instead of List<T>.
        /// List<T> requires a ToArray() call which copies the data to a new array of the correct size.
        /// </summary>
        public T[] Array => _array;

        /// <summary> Gets the number of elements currently in use. </summary>
        public int Count => _count;

        /// <summary> Gets the total capacity of the buffer. </summary>
        public int Capacity => _array.Length;

        /// <summary> Gets or sets the element at the specified index. </summary>
        /// <param name="index">The zero-based index of the element.</param>
        /// <returns>The element at the specified index.</returns>
        public T this[int index] {
            get => _array[index];
            set => _array[index] = value;
        }

        /// <summary> Gets or sets the element at the specified unsigned index. </summary>
        /// <param name="index">The zero-based unsigned index of the element.</param>
        /// <returns>The element at the specified index.</returns>
        public T this[ulong index] {
            get => _array[(int)index];
            set => _array[(int)index] = value;
        }

        /// <summary> Resets the buffer by setting the count to zero without deallocating memory. </summary>
        public void Clear() => _count = 0;

        /// <summary>
        /// Ensures the buffer has enough capacity to store the required number of elements.
        /// Doubles the capacity repeatedly until it meets the required size.
        /// </summary>
        /// <param name="required">The required capacity.</param>
        public void EnsureSize(int required)
        {
            // If current array is already large enough, do nothing
            if (_array.Length >= required) return;

            // Calculate new size by doubling current size until it's large enough
            int newSize = _array.Length;
            newSize = newSize == 0 ? 4 : newSize; // Handle empty array case
            while (newSize < required)
                newSize <<= 1; // Use bit shifting for faster doubling (equivalent to newSize *= 2)

            // Use Array.Resize which can be more efficient than manual copy
            System.Array.Resize(ref _array, newSize);
        }

        /// <summary> Adds an item to the end of the buffer. </summary>
        /// <param name="item">The item to add.</param>
        public void Add(T item)
        {
            // Ensure there's room for one more item
            EnsureSize(_count + 1);

            // Add the item and increment count
            _array[_count++] = item;
        }

        /// <summary> Adds a range of items from an ArraySegment to the end of the buffer. </summary>
        /// <param name="data">The ArraySegment containing the items to add.</param>
        public void Add(ArraySegment<T> data)
        {
            // Skip empty segments
            if (data.Count == 0)
                return;

            // Ensure there's room for all new items
            EnsureSize(_count + data.Count);

            // Copy data from segment to the end of our array
            System.Array.Copy(data.Array, data.Offset, _array, _count, data.Count);
            _count += data.Count;
        }
    }
}
