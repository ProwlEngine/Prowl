using System;

namespace NeoVeldrid
{
    /// <summary>
    /// A structure describing Direct3D11-specific device creation options.
    /// </summary>
    public struct D3D11DeviceOptions
    {
        /// <summary>
        /// Native pointer to an adapter.
        /// </summary>
        public IntPtr AdapterPtr;

        /// <summary>
        /// Set of device specific flags.
        /// See the Direct3D 11 DeviceCreationFlags documentation for details.
        /// </summary>
        public uint DeviceCreationFlags;
    }
}
