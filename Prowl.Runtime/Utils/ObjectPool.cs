using System;
using System.Collections.Generic;

namespace Prowl.Runtime.Utils
{
    // Based on how https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.core/Runtime/Common/ObjectPools.cs works
     
    /// <summary>
    /// A pool of object instances. 
    /// </summary>
    /// <typeparam name="T">The instance type to pool.</typeparam>
    public class ObjectPool<T> where T : new()
    {
        protected readonly Stack<T> poolStack = new Stack<T>();

        /// <summary>The count of objects stored in the internal pool and the instances released by <see cref="Get"/>.</summary>
        protected int PoolCount { get; private set; }
        
        /// <summary>The count of active and released objects the pool contains.</summary>
        protected int ActiveCount => PoolCount - InactiveCount;
        
        /// <summary>The count of inactive objects the pool contains.</summary>
        protected int InactiveCount => poolStack.Count;

        /// <summary>
        /// Creates a new <see cref="ObjectPool{T}"/> instance.
        /// </summary>
        public ObjectPool() { }

        /// <summary>
        /// <para>
        /// Get an object from the pool. If no object is available, creates a new instance. 
        /// </para>
        /// Once this object has been created and given back to the caller, the caller does not truly need to call <see cref="Release"/>. 
        /// However, it is best practice to do so, otherwise the pool ends up as a glorified instantiator. 
        /// </summary>
        public T Get()
        {
            T element;
            if (poolStack.Count == 0)
            {
                element = new T();
                PoolCount++;
            }
            else
            {
                element = poolStack.Pop();
            }

            return element;
        }

        /// <summary>
        /// <para>
        /// Get an object from the pool with an instantiator to use if none exists.
        /// </para>
        /// Once this object has been created and given back to the caller, the caller does not truly need to call <see cref="Release"/>. 
        /// However, it is best practice to do so, otherwise the pool ends up as a glorified instantiator. 
        /// </summary>
        public T Get(Func<T> instantiator)
        {
            T element;
            if (poolStack.Count == 0)
            {
                element = instantiator.Invoke();
                PoolCount++;
            }
            else
            {
                element = poolStack.Pop();
            }

            return element;
        }

        /// <summary>
        /// Release an object back into the pool.
        /// </summary>
        /// <param name="element">Object to release.</param>
        public void Release(T element)
        {    
            poolStack.Push(element);
        }
    }
}