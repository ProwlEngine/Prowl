using System;

namespace Prowl.Veldrid;

/// <summary>
/// A device resource used to bind a particular set of <see cref="BindableResource"/> objects to a <see cref="CommandBuffer"/>.
/// See <see cref="ResourceSetDescription"/>.
/// </summary>
public abstract partial class ResourceSet : DeviceResource, IDisposable
{
        internal ResourceSet(ref ResourceSetDescription description)
        {
                ResourceSet_StoreLayoutAndResources(ref description);
        }

        /// <summary>
        /// A string identifying this instance. Can be used to differentiate between objects in graphics debuggers and other
        /// tools.
        /// </summary>
        public abstract string Name { get; set; }

        /// <summary>
        /// A bool indicating whether this instance has been disposed.
        /// </summary>
        public abstract bool IsDisposed { get; }

        /// <summary>
        /// Frees unmanaged device resources controlled by this instance.
        /// </summary>
        public abstract void Dispose();
}
