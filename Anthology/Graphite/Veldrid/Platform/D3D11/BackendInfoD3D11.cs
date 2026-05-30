#if !EXCLUDE_D3D11_BACKEND
using System;

using Prowl.Veldrid.D3D11;

namespace Prowl.Veldrid;

/// <summary>
/// Exposes Direct3D 11-specific functionality,
/// useful for interoperating with native components which interface directly with Direct3D 11.
/// Can only be used on <see cref="GraphicsBackend.Direct3D11"/>.
/// </summary>
public unsafe class BackendInfoD3D11
{
    private readonly D3D11GraphicsDevice _gd;

    internal BackendInfoD3D11(D3D11GraphicsDevice gd)
    {
        _gd = gd;
    }

    /// <summary>
    /// Gets a pointer to the ID3D11Device controlled by the GraphicsDevice.
    /// </summary>
    public IntPtr Device => (nint)_gd.Device;

    /// <summary>
    /// Gets a pointer to the IAdapter used to create the GraphicsDevice.
    /// </summary>
    public IntPtr Adapter => (nint)_gd.Adapter;

    /// <summary>
    /// Gets the PCI ID of the hardware device.
    /// </summary>
    public int DeviceId => _gd.DeviceId;
}
#endif
