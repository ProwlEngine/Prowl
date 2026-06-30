using System;
using System.Collections.Generic;

namespace Prowl.PaperUI.Utilities
{
    /// <summary>
    /// Generic object pool for reusing objects
    /// </summary>
    public class ObjectPool<T> where T : class
    {
        private readonly Stack<T> _objects;
        private readonly Func<T> _objectGenerator;
        private readonly int _maxSize;

        /// <summary>
        /// Creates a new object pool
        /// </summary>
        /// <param name="objectGenerator">Function to create new objects when the pool is empty</param>
        /// <param name="initialSize">Initial number of objects to create</param>
        /// <param name="maxSize">Maximum number of objects to keep in the pool (0 for unlimited)</param>
        public ObjectPool(Func<T> objectGenerator, int initialSize = 0, int maxSize = 100)
        {
            _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            _maxSize = maxSize;
            _objects = new Stack<T>(initialSize);

            // Pre-populate the pool
            for (int i = 0; i < initialSize; i++)
            {
                _objects.Push(objectGenerator());
            }
        }

        /// <summary>
        /// Gets an object from the pool, or creates a new one if the pool is empty
        /// </summary>
        public T Get()
        {
            lock (_objects)
            {
                if (_objects.Count > 0)
                {
                    return _objects.Pop();
                }
            }

            return _objectGenerator();
        }

        /// <summary>
        /// Returns an object to the pool
        /// </summary>
        public void Return(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            lock (_objects)
            {
                // Only add to the pool if we're under the max size
                if (_maxSize <= 0 || _objects.Count < _maxSize)
                {
                    _objects.Push(item);
                }
            }
        }

        /// <summary>
        /// Gets the current count of objects in the pool
        /// </summary>
        public int Count
        {
            get
            {
                lock (_objects)
                {
                    return _objects.Count;
                }
            }
        }

        /// <summary>
        /// Clears all objects from the pool
        /// </summary>
        public void Clear()
        {
            lock (_objects)
            {
                _objects.Clear();
            }
        }
    }
}
