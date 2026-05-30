using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Prowl.Veldrid.D3D11;


internal unsafe class D3D11GraphicsDevice : GraphicsDevice
{
    /// <summary>DXGI_DEBUG_ALL - reports live objects from all producers (D3D11, DXGI, etc.).</summary>
    private static readonly Guid DxgiDebugAll = new("e48ae283-da80-490b-87e6-43e9a9cfda08");

    private ComPtr<IDXGIAdapter> _dxgiAdapter;
    private ComPtr<ID3D11Device> _device;
    private readonly string _deviceName;
    private readonly string _vendorName;
    private readonly GraphicsApiVersion _apiVersion;
    private readonly int _deviceId;
    private ComPtr<ID3D11DeviceContext> _immediateContext;
    private readonly D3D11ResourceFactory _d3d11ResourceFactory;
    private readonly D3D11Swapchain _mainSwapchain;
    private readonly bool _supportsConcurrentResources;
    private readonly bool _supportsCommandBuffers;
    private readonly object _immediateContextLock = new();
    private readonly BackendInfoD3D11 _d3d11Info;

    private unsafe struct SlotState
    {
        public ID3D11Query* EventQuery;
        public D3D11Fence FenceWrapper;
        public D3D11Buffer TransientPrimary;
        public List<D3D11Buffer> TransientOverflow;
        public ulong CurrentFrameId;
    }

    private SlotState[] _slots;
    private readonly List<D3D11Buffer> _transientFreePool = [];
    private readonly object _transientFreePoolLock = new();

    private readonly object _mappedResourceLock = new();
    private readonly Dictionary<MappedResourceCacheKey, MappedResourceInfo> _mappedResources
        = [];

    private readonly object _stagingResourcesLock = new();
    private readonly List<D3D11Buffer> _availableStagingBuffers = [];

    private readonly Silk.NET.Direct3D11.D3D11 _d3d11Api;

    public override string DeviceName => _deviceName;

    public override string VendorName => _vendorName;

    public override GraphicsApiVersion ApiVersion => _apiVersion;

    public override GraphicsBackend BackendType => GraphicsBackend.Direct3D11;

    public override bool IsUvOriginTopLeft => true;

    public override bool IsDepthRangeZeroToOne => true;

    public override bool IsClipSpaceYInverted => false;

    public override ResourceFactory ResourceFactory => _d3d11ResourceFactory;

    public ID3D11Device* Device => _device;

    public IDXGIAdapter* Adapter => _dxgiAdapter;

    public bool IsDebugEnabled { get; }

    public bool SupportsConcurrentResources => _supportsConcurrentResources;

    public bool SupportsCommandBuffers => _supportsCommandBuffers;

    public int DeviceId => _deviceId;

    public override Swapchain MainSwapchain => _mainSwapchain;

    public override GraphicsDeviceFeatures Features { get; }

    public D3D11GraphicsDevice(GraphicsDeviceOptions options, D3D11DeviceOptions d3D11DeviceOptions, SwapchainDescription? swapchainDesc)
        : this(MergeOptions(d3D11DeviceOptions, options), swapchainDesc, options)
    {
    }

    public D3D11GraphicsDevice(D3D11DeviceOptions options, SwapchainDescription? swapchainDesc)
        : this(options, swapchainDesc, default)
    {
    }

    private D3D11GraphicsDevice(D3D11DeviceOptions options, SwapchainDescription? swapchainDesc, GraphicsDeviceOptions graphicsOptions)
    {
#pragma warning disable CS0618
        _d3d11Api = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Silk.NET.Direct3D11.D3D11.GetApi(DXSwapchainProvider.Win32)
            : Silk.NET.Direct3D11.D3D11.GetApi(DXSwapchainProvider.Sdl2);
#pragma warning restore CS0618

        var flags = (CreateDeviceFlag)options.DeviceCreationFlags;
#if DEBUG
        flags |= CreateDeviceFlag.Debug;
#endif
        if ((flags & CreateDeviceFlag.Debug) != 0 && !SdkLayersAvailable(_d3d11Api))
        {
            flags &= ~CreateDeviceFlag.Debug;
        }

        D3DFeatureLevel featureLevel;
        ID3D11Device* pDevice = null;
        ID3D11DeviceContext* pContext = null;

        try
        {
            D3DFeatureLevel* pFeatureLevels = stackalloc D3DFeatureLevel[]
            {
                D3DFeatureLevel.Level111,
                D3DFeatureLevel.Level110,
            };

            IDXGIAdapter* pAdapter = options.AdapterPtr != IntPtr.Zero
                ? (IDXGIAdapter*)options.AdapterPtr
                : null;

            SilkMarshal.ThrowHResult(_d3d11Api.CreateDevice(
                pAdapter,
                pAdapter != null ? D3DDriverType.Unknown : D3DDriverType.Hardware,
                IntPtr.Zero,
                (uint)flags,
                pFeatureLevels,
                2,
                Silk.NET.Direct3D11.D3D11.SdkVersion,
                &pDevice,
                &featureLevel,
                &pContext));
        }
        catch
        {
            // Fallback: let the driver pick the feature level
            SilkMarshal.ThrowHResult(_d3d11Api.CreateDevice(
                (IDXGIAdapter*)null,
                D3DDriverType.Hardware,
                IntPtr.Zero,
                (uint)flags,
                (D3DFeatureLevel*)null,
                0,
                Silk.NET.Direct3D11.D3D11.SdkVersion,
                &pDevice,
                &featureLevel,
                &pContext));
        }

        _device = default;
        _device.Handle = pDevice;
        _immediateContext = default;
        _immediateContext.Handle = pContext;

        {
            IDXGIDevice* pDxgiDevice;
            Guid dxgiDeviceGuid = IDXGIDevice.Guid;
            SilkMarshal.ThrowHResult(((IUnknown*)pDevice)->QueryInterface(&dxgiDeviceGuid, (void**)&pDxgiDevice));

            IDXGIAdapter* pAdapterOut;
            SilkMarshal.ThrowHResult(pDxgiDevice->GetAdapter(&pAdapterOut));
            _dxgiAdapter = default;
            _dxgiAdapter.Handle = pAdapterOut;

            AdapterDesc desc;
            SilkMarshal.ThrowHResult(pAdapterOut->GetDesc(&desc));
            _deviceName = new string(desc.Description);
            _vendorName = "id:" + desc.VendorId.ToString("x8");
            _deviceId = (int)desc.DeviceId;

            pDxgiDevice->Release();
        }

        switch (featureLevel)
        {
            case D3DFeatureLevel.Level100:
                _apiVersion = new GraphicsApiVersion(10, 0, 0, 0);
                break;

            case D3DFeatureLevel.Level101:
                _apiVersion = new GraphicsApiVersion(10, 1, 0, 0);
                break;

            case D3DFeatureLevel.Level110:
                _apiVersion = new GraphicsApiVersion(11, 0, 0, 0);
                break;

            case D3DFeatureLevel.Level111:
                _apiVersion = new GraphicsApiVersion(11, 1, 0, 0);
                break;

            case D3DFeatureLevel.Level120:
                _apiVersion = new GraphicsApiVersion(12, 0, 0, 0);
                break;

            case D3DFeatureLevel.Level121:
                _apiVersion = new GraphicsApiVersion(12, 1, 0, 0);
                break;

            case D3DFeatureLevel.Level122:
                _apiVersion = new GraphicsApiVersion(12, 2, 0, 0);
                break;
        }

        if (swapchainDesc != null)
        {
            SwapchainDescription desc = swapchainDesc.Value;
            _mainSwapchain = new D3D11Swapchain(this, ref desc);
        }

        FeatureDataThreading threadingData;
        pDevice->CheckFeatureSupport(Silk.NET.Direct3D11.Feature.Threading, &threadingData, (uint)sizeof(FeatureDataThreading));
        _supportsConcurrentResources = threadingData.DriverConcurrentCreates;
        _supportsCommandBuffers = threadingData.DriverCommandLists;

        IsDebugEnabled = (flags & CreateDeviceFlag.Debug) != 0;

        FeatureDataDoubles doublesData;
        pDevice->CheckFeatureSupport(Silk.NET.Direct3D11.Feature.Doubles, &doublesData, (uint)sizeof(FeatureDataDoubles));

        Features = new GraphicsDeviceFeatures(
            computeShader: true,
            geometryShader: true,
            tessellationShaders: true,
            multipleViewports: true,
            samplerLodBias: true,
            drawBaseVertex: true,
            drawBaseInstance: true,
            drawIndirect: true,
            drawIndirectBaseInstance: true,
            samplerAnisotropy: true,
            depthClipDisable: true,
            texture1D: true,
            independentBlend: true,
            structuredBuffer: featureLevel >= D3DFeatureLevel.Level110,
            subsetTextureView: true,
            CommandBufferDebugMarkers: featureLevel >= D3DFeatureLevel.Level111,
            bufferRangeBinding: featureLevel >= D3DFeatureLevel.Level111,
            shaderFloat64: doublesData.DoublePrecisionFloatShaderOps);

        _d3d11ResourceFactory = new D3D11ResourceFactory(this);
        _d3d11Info = new BackendInfoD3D11(this);

        InitializeFrameOptions(graphicsOptions);
        InitializeSlots();
        PostDeviceCreated();
    }

    private static bool SdkLayersAvailable(Silk.NET.Direct3D11.D3D11 d3d11)
    {
        // Try creating a null device with debug flag to check if SDK layers are installed
        int hr = d3d11.CreateDevice(
            (IDXGIAdapter*)null,
            D3DDriverType.Null,
            IntPtr.Zero,
            (uint)CreateDeviceFlag.Debug,
            (D3DFeatureLevel*)null,
            0,
            Silk.NET.Direct3D11.D3D11.SdkVersion,
            (ID3D11Device**)null,
            (D3DFeatureLevel*)null,
            (ID3D11DeviceContext**)null);
        return hr >= 0;
    }

    private static D3D11DeviceOptions MergeOptions(D3D11DeviceOptions d3D11DeviceOptions, GraphicsDeviceOptions options)
    {
        if (options.Debug)
        {
            d3D11DeviceOptions.DeviceCreationFlags |= (uint)CreateDeviceFlag.Debug;
        }

        return d3D11DeviceOptions;
    }

    private void InitializeSlots()
    {
        _slots = new SlotState[_maxFramesInFlight];
        ID3D11Device* pDevice = (ID3D11Device*)_device;

        for (int i = 0; i < _slots.Length; i++)
        {
            QueryDesc queryDesc = new() { Query = Query.Event, MiscFlags = 0 };
            ID3D11Query* pQuery;
            SilkMarshal.ThrowHResult(pDevice->CreateQuery(&queryDesc, &pQuery));

            _slots[i] = new SlotState
            {
                EventQuery = pQuery,
                FenceWrapper = new D3D11Fence(signaled: false),
                TransientPrimary = (D3D11Buffer)ResourceFactory.CreateBuffer(
                    new BufferDescription(_transientInitialSize, BufferUsage.UniformBuffer | BufferUsage.Dynamic)),
                TransientOverflow = [],
                CurrentFrameId = 0,
            };
        }
    }

    private protected override Frame BeginFrameCore(ulong frameId, uint ringSlot)
    {
        ref SlotState slot = ref _slots[ringSlot];

        if (slot.CurrentFrameId != 0)
        {
            PollUntilComplete(slot.EventQuery);
            ulong completed = slot.CurrentFrameId;
            Volatile.Write(ref _lastCompletedFrameId, Math.Max(Volatile.Read(ref _lastCompletedFrameId), completed));
            slot.FenceWrapper.Set();
        }

        slot.FenceWrapper.Reset();

        if (slot.TransientOverflow.Count > 0)
        {
            lock (_transientFreePoolLock)
            {
                _transientFreePool.AddRange(slot.TransientOverflow);
            }
            slot.TransientOverflow.Clear();
        }

        slot.CurrentFrameId = frameId;

        return new D3D11Frame(this, frameId, ringSlot, slot.FenceWrapper,
            slot.TransientPrimary, slot.TransientOverflow);
    }

    private protected override void EndFrameCore(Frame frame)
    {
        D3D11Frame d3d11Frame = Util.AssertSubtype<Frame, D3D11Frame>(frame);
        d3d11Frame.UnmapActiveBuffer();

        ref SlotState slot = ref _slots[frame.RingSlot];

        lock (_immediateContextLock)
        {
            ((ID3D11DeviceContext*)_immediateContext)->End((ID3D11Asynchronous*)slot.EventQuery);
        }
    }

    private protected override bool IsFrameCompleteCore(ulong frameId)
    {
        uint ringSlot = (uint)((frameId - 1) % _maxFramesInFlight);
        ref SlotState slot = ref _slots[ringSlot];

        if (slot.CurrentFrameId > frameId)
            return true;

        if (slot.CurrentFrameId == frameId)
        {
            int done;
            lock (_immediateContextLock)
            {
                int hr = ((ID3D11DeviceContext*)_immediateContext)->GetData(
                    (ID3D11Asynchronous*)slot.EventQuery, &done, sizeof(int),
                    (uint)AsyncGetdataFlag.Donotflush);
                return hr == 0 && done != 0;
            }
        }

        return true;
    }

    private protected override bool WaitForFrameCore(ulong frameId, ulong nanosecondTimeout)
    {
        uint ringSlot = (uint)((frameId - 1) % _maxFramesInFlight);
        ref SlotState slot = ref _slots[ringSlot];

        if (slot.CurrentFrameId > frameId)
            return true;

        if (slot.CurrentFrameId == frameId)
        {
            long deadline = nanosecondTimeout == ulong.MaxValue
                ? long.MaxValue
                : Environment.TickCount64 + (long)(nanosecondTimeout / 1_000_000);

            lock (_immediateContextLock)
            {
                while (true)
                {
                    int done;
                    int hr = ((ID3D11DeviceContext*)_immediateContext)->GetData(
                        (ID3D11Asynchronous*)slot.EventQuery, &done, sizeof(int), 0);
                    if (hr == 0 && done != 0)
                        return true;

                    if (nanosecondTimeout != ulong.MaxValue && Environment.TickCount64 >= deadline)
                        return false;

                    Thread.Yield();
                }
            }
        }

        return true;
    }

    private void PollUntilComplete(ID3D11Query* query)
    {
        lock (_immediateContextLock)
        {
            while (true)
            {
                int done;
                int hr = ((ID3D11DeviceContext*)_immediateContext)->GetData(
                    (ID3D11Asynchronous*)query, &done, sizeof(int), 0);
                if (hr == 0 && done != 0)
                    return;
                Thread.Yield();
            }
        }
    }

    internal void SubmitCommandBufferInternal(CommandBuffer cl)
    {
        D3D11CommandBuffer d3d11CL = Util.AssertSubtype<CommandBuffer, D3D11CommandBuffer>(cl);
        lock (_immediateContextLock)
        {
            if (d3d11CL.DeviceCommandList != null)
            {
                ((ID3D11DeviceContext*)_immediateContext)->ExecuteCommandList(d3d11CL.DeviceCommandList, false);
                d3d11CL.OnCompleted();
            }
        }
    }

    internal D3D11Buffer CreateTransientBuffer(uint sizeInBytes)
    {
        lock (_transientFreePoolLock)
        {
            for (int i = 0; i < _transientFreePool.Count; i++)
            {
                if (_transientFreePool[i].SizeInBytes >= sizeInBytes)
                {
                    D3D11Buffer buf = _transientFreePool[i];
                    _transientFreePool.RemoveAt(i);
                    return buf;
                }
            }
        }

        return Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(
            ResourceFactory.CreateBuffer(new BufferDescription(sizeInBytes, BufferUsage.UniformBuffer | BufferUsage.Dynamic)));
    }

    private protected override void SwapBuffersCore(Swapchain swapchain)
    {
        lock (_immediateContextLock)
        {
            D3D11Swapchain d3d11SC = Util.AssertSubtype<Swapchain, D3D11Swapchain>(swapchain);
            d3d11SC.DxgiSwapChain->Present((uint)d3d11SC.SyncInterval, 0);
        }
    }

    public override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
    {
        Format dxgiFormat = D3D11Formats.ToDxgiFormat(format, depthFormat);
        if (CheckFormatMultisample(dxgiFormat, 32))
        {
            return TextureSampleCount.Count32;
        }
        else if (CheckFormatMultisample(dxgiFormat, 16))
        {
            return TextureSampleCount.Count16;
        }
        else if (CheckFormatMultisample(dxgiFormat, 8))
        {
            return TextureSampleCount.Count8;
        }
        else if (CheckFormatMultisample(dxgiFormat, 4))
        {
            return TextureSampleCount.Count4;
        }
        else if (CheckFormatMultisample(dxgiFormat, 2))
        {
            return TextureSampleCount.Count2;
        }

        return TextureSampleCount.Count1;
    }

    private bool CheckFormatMultisample(Format format, int sampleCount)
    {
        uint numQualityLevels;
        int hr = ((ID3D11Device*)_device)->CheckMultisampleQualityLevels(format, (uint)sampleCount, &numQualityLevels);
        return hr >= 0 && numQualityLevels != 0;
    }

    private protected override bool GetPixelFormatSupportCore(
        PixelFormat format,
        TextureType type,
        TextureUsage usage,
        out PixelFormatProperties properties)
    {
        if (D3D11Formats.IsUnsupportedFormat(format))
        {
            properties = default(PixelFormatProperties);
            return false;
        }

        Format dxgiFormat = D3D11Formats.ToDxgiFormat(format, (usage & TextureUsage.DepthStencil) != 0);

        uint fsRaw;
        int fhr = ((ID3D11Device*)_device)->CheckFormatSupport(dxgiFormat, &fsRaw);
        if (fhr < 0)
        {
            properties = default(PixelFormatProperties);
            return false;
        }
        FormatSupport fs = (FormatSupport)fsRaw;

        if ((usage & TextureUsage.RenderTarget) != 0 && (fs & FormatSupport.RenderTarget) == 0
            || (usage & TextureUsage.DepthStencil) != 0 && (fs & FormatSupport.DepthStencil) == 0
            || (usage & TextureUsage.Sampled) != 0 && (fs & FormatSupport.ShaderSample) == 0
            || (usage & TextureUsage.Cubemap) != 0 && (fs & FormatSupport.Texturecube) == 0
            || (usage & TextureUsage.Storage) != 0 && (fs & FormatSupport.TypedUnorderedAccessView) == 0)
        {
            properties = default(PixelFormatProperties);
            return false;
        }

        const uint MaxTextureDimension = 16384;
        const uint MaxVolumeExtent = 2048;

        uint sampleCounts = 0;
        if (CheckFormatMultisample(dxgiFormat, 1)) { sampleCounts |= (1 << 0); }
        if (CheckFormatMultisample(dxgiFormat, 2)) { sampleCounts |= (1 << 1); }
        if (CheckFormatMultisample(dxgiFormat, 4)) { sampleCounts |= (1 << 2); }
        if (CheckFormatMultisample(dxgiFormat, 8)) { sampleCounts |= (1 << 3); }
        if (CheckFormatMultisample(dxgiFormat, 16)) { sampleCounts |= (1 << 4); }
        if (CheckFormatMultisample(dxgiFormat, 32)) { sampleCounts |= (1 << 5); }

        properties = new PixelFormatProperties(
            MaxTextureDimension,
            type == TextureType.Texture1D ? 1 : MaxTextureDimension,
            type != TextureType.Texture3D ? 1 : MaxVolumeExtent,
            uint.MaxValue,
            type == TextureType.Texture3D ? 1 : MaxVolumeExtent,
            sampleCounts);
        return true;
    }

    protected override MappedResource MapCore(MappableResource resource, MapMode mode, uint subresource)
    {
        MappedResourceCacheKey key = new(resource, subresource);
        lock (_mappedResourceLock)
        {
            if (_mappedResources.TryGetValue(key, out MappedResourceInfo info))
            {
                if (info.Mode != mode)
                {
                    throw new RenderException("The given resource was already mapped with a different MapMode.");
                }

                info.RefCount += 1;
                _mappedResources[key] = info;
            }
            else
            {
                // No current mapping exists -- create one.

                if (resource is D3D11Buffer buffer)
                {
                    lock (_immediateContextLock)
                    {
                        MappedSubresource msr;
                        SilkMarshal.ThrowHResult(((ID3D11DeviceContext*)_immediateContext)->Map(
                            (ID3D11Resource*)buffer.Buffer,
                            0,
                            D3D11Formats.VdToD3D11MapMode((buffer.Usage & BufferUsage.Dynamic) == BufferUsage.Dynamic, mode),
                            0,
                            &msr));

                        info.MappedResource = new MappedResource(resource, mode, (IntPtr)msr.PData, buffer.SizeInBytes);
                        info.RefCount = 1;
                        info.Mode = mode;
                        _mappedResources.Add(key, info);
                    }
                }
                else
                {
                    D3D11Texture texture = Util.AssertSubtype<MappableResource, D3D11Texture>(resource);
                    lock (_immediateContextLock)
                    {
                        MappedSubresource msr;
                        SilkMarshal.ThrowHResult(((ID3D11DeviceContext*)_immediateContext)->Map(
                            texture.DeviceTexture,
                            subresource,
                            D3D11Formats.VdToD3D11MapMode(false, mode),
                            0,
                            &msr));

                        info.MappedResource = new MappedResource(
                            resource,
                            mode,
                            (IntPtr)msr.PData,
                            texture.Height * msr.RowPitch,
                            subresource,
                            msr.RowPitch,
                            msr.DepthPitch);
                        info.RefCount = 1;
                        info.Mode = mode;
                        _mappedResources.Add(key, info);
                    }
                }
            }

            return info.MappedResource;
        }
    }

    protected override void UnmapCore(MappableResource resource, uint subresource)
    {
        MappedResourceCacheKey key = new(resource, subresource);
        bool commitUnmap;

        lock (_mappedResourceLock)
        {
            if (!_mappedResources.TryGetValue(key, out MappedResourceInfo info))
            {
                throw new RenderException($"The given resource ({resource}) is not mapped.");
            }

            info.RefCount -= 1;
            commitUnmap = info.RefCount == 0;
            if (commitUnmap)
            {
                lock (_immediateContextLock)
                {
                    if (resource is D3D11Buffer buffer)
                    {
                        ((ID3D11DeviceContext*)_immediateContext)->Unmap((ID3D11Resource*)buffer.Buffer, 0);
                    }
                    else
                    {
                        D3D11Texture texture = Util.AssertSubtype<MappableResource, D3D11Texture>(resource);
                        ((ID3D11DeviceContext*)_immediateContext)->Unmap(texture.DeviceTexture, subresource);
                    }

                    bool result = _mappedResources.Remove(key);
                    Debug.Assert(result);
                }
            }
            else
            {
                _mappedResources[key] = info;
            }
        }
    }

    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        D3D11Buffer d3dBuffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(buffer);
        if (sizeInBytes == 0)
        {
            return;
        }

        bool isDynamic = (buffer.Usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;
        bool isStaging = (buffer.Usage & BufferUsage.Staging) == BufferUsage.Staging;
        bool isUniformBuffer = (buffer.Usage & BufferUsage.UniformBuffer) == BufferUsage.UniformBuffer;
        bool updateFullBuffer = bufferOffsetInBytes == 0 && sizeInBytes == buffer.SizeInBytes;
        bool useUpdateSubresource = (!isDynamic && !isStaging) && (!isUniformBuffer || updateFullBuffer);
        bool useMap = (isDynamic && updateFullBuffer) || isStaging;

        if (useUpdateSubresource)
        {
            Box subregion = new()
            {
                Left = bufferOffsetInBytes,
                Top = 0,
                Front = 0,
                Right = sizeInBytes + bufferOffsetInBytes,
                Bottom = 1,
                Back = 1,
            };

            lock (_immediateContextLock)
            {
                if (isUniformBuffer)
                {
                    ((ID3D11DeviceContext*)_immediateContext)->UpdateSubresource(
                        (ID3D11Resource*)d3dBuffer.Buffer, 0, (Box*)null, (void*)source, 0, 0);
                }
                else
                {
                    ((ID3D11DeviceContext*)_immediateContext)->UpdateSubresource(
                        (ID3D11Resource*)d3dBuffer.Buffer, 0, &subregion, (void*)source, 0, 0);
                }
            }
        }
        else if (useMap)
        {
            MappedResource mr = MapCore(buffer, MapMode.Write, 0);
            if (sizeInBytes < 1024)
            {
                Unsafe.CopyBlock((byte*)mr.Data + bufferOffsetInBytes, source.ToPointer(), sizeInBytes);
            }
            else
            {
                Buffer.MemoryCopy(
                    source.ToPointer(),
                    (byte*)mr.Data + bufferOffsetInBytes,
                    buffer.SizeInBytes,
                    sizeInBytes);
            }
            UnmapCore(buffer, 0);
        }
        else
        {
            D3D11Buffer staging = GetFreeStagingBuffer(sizeInBytes);
            UpdateBuffer(staging, 0, source, sizeInBytes);
            Box sourceRegion = new()
            {
                Left = 0,
                Top = 0,
                Front = 0,
                Right = sizeInBytes,
                Bottom = 1,
                Back = 1,
            };
            lock (_immediateContextLock)
            {
                ((ID3D11DeviceContext*)_immediateContext)->CopySubresourceRegion(
                    (ID3D11Resource*)d3dBuffer.Buffer, 0, bufferOffsetInBytes, 0, 0,
                    (ID3D11Resource*)staging.Buffer, 0,
                    &sourceRegion);
            }

            lock (_stagingResourcesLock)
            {
                _availableStagingBuffers.Add(staging);
            }
        }
    }

    private D3D11Buffer GetFreeStagingBuffer(uint sizeInBytes)
    {
        lock (_stagingResourcesLock)
        {
            foreach (D3D11Buffer buffer in _availableStagingBuffers)
            {
                if (buffer.SizeInBytes >= sizeInBytes)
                {
                    _availableStagingBuffers.Remove(buffer);
                    return buffer;
                }
            }
        }

        DeviceBuffer staging = ResourceFactory.CreateBuffer(
            new BufferDescription(sizeInBytes, BufferUsage.Staging));

        return Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(staging);
    }

    private protected override void UpdateTextureCore(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x,
        uint y,
        uint z,
        uint width,
        uint height,
        uint depth,
        uint mipLevel,
        uint arrayLayer)
    {
        D3D11Texture d3dTex = Util.AssertSubtype<Texture, D3D11Texture>(texture);
        bool useMap = (texture.Usage & TextureUsage.Staging) == TextureUsage.Staging;
        if (useMap)
        {
            uint subresource = texture.CalculateSubresource(mipLevel, arrayLayer);
            MappedResourceCacheKey key = new(texture, subresource);
            MappedResource map = MapCore(texture, MapMode.Write, subresource);

            uint denseRowSize = FormatHelpers.GetRowPitch(width, texture.Format);
            uint denseSliceSize = FormatHelpers.GetDepthPitch(denseRowSize, height, texture.Format);

            Util.CopyTextureRegion(
                source.ToPointer(),
                0, 0, 0,
                denseRowSize, denseSliceSize,
                map.Data.ToPointer(),
                x, y, z,
                map.RowPitch, map.DepthPitch,
                width, height, depth,
                texture.Format);

            UnmapCore(texture, subresource);
        }
        else
        {
            int subresource = D3D11Util.ComputeSubresource(mipLevel, texture.MipLevels, arrayLayer);
            Box resourceRegion = new()
            {
                Left = x,
                Top = y,
                Front = z,
                Right = x + width,
                Bottom = y + height,
                Back = z + depth,
            };

            uint srcRowPitch = FormatHelpers.GetRowPitch(width, texture.Format);
            uint srcDepthPitch = FormatHelpers.GetDepthPitch(srcRowPitch, height, texture.Format);
            lock (_immediateContextLock)
            {
                ((ID3D11DeviceContext*)_immediateContext)->UpdateSubresource(
                    d3dTex.DeviceTexture,
                    (uint)subresource,
                    &resourceRegion,
                    (void*)source,
                    srcRowPitch,
                    srcDepthPitch);
            }
        }
    }

    public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
    {
        return Util.AssertSubtype<Fence, D3D11Fence>(fence).Wait(nanosecondTimeout);
    }

    private readonly object _resetEventsLock = new();
    private readonly List<ManualResetEvent[]> _resetEvents = [];

    private ManualResetEvent[] GetResetEventArray(int length)
    {
        lock (_resetEventsLock)
        {
            for (int i = _resetEvents.Count - 1; i > 0; i--)
            {
                ManualResetEvent[] array = _resetEvents[i];
                if (array.Length == length)
                {
                    _resetEvents.RemoveAt(i);
                    return array;
                }
            }
        }

        ManualResetEvent[] newArray = new ManualResetEvent[length];
        return newArray;
    }

    private void ReturnResetEventArray(ManualResetEvent[] array)
    {
        lock (_resetEventsLock)
        {
            _resetEvents.Add(array);
        }
    }

    public override void ResetFence(Fence fence)
    {
        Util.AssertSubtype<Fence, D3D11Fence>(fence).Reset();
    }

    internal override uint GetUniformBufferMinOffsetAlignmentCore() => 256u;

    internal override uint GetStructuredBufferMinOffsetAlignmentCore() => 16;

    protected override void PlatformDispose()
    {
        if (_slots != null)
        {
            foreach (ref SlotState slot in _slots.AsSpan())
            {
                if (slot.EventQuery != null)
                    ((IUnknown*)slot.EventQuery)->Release();
                slot.TransientPrimary?.Dispose();
                foreach (D3D11Buffer buf in slot.TransientOverflow)
                    buf.Dispose();
                slot.FenceWrapper?.Dispose();
            }
        }

        lock (_transientFreePoolLock)
        {
            foreach (D3D11Buffer buf in _transientFreePool)
                buf.Dispose();
            _transientFreePool.Clear();
        }

        foreach (DeviceBuffer buffer in _availableStagingBuffers)
        {
            buffer.Dispose();
        }
        _availableStagingBuffers.Clear();

        _d3d11ResourceFactory.Dispose();
        _mainSwapchain?.Dispose();
        _immediateContext.Dispose();

        if (IsDebugEnabled)
        {
            // Release our device reference. If refCount > 0, leaked objects are keeping it alive.
            ID3D11Device* pRawDevice = _device.Handle;
            uint refCount = pRawDevice->Release();
            _device = default;

            if (refCount > 0)
            {
                ID3D11Debug* pDebug;
                Guid debugGuid = ID3D11Debug.Guid;
                if (((IUnknown*)pRawDevice)->QueryInterface(&debugGuid, (void**)&pDebug) >= 0 && pDebug != null)
                {
                    pDebug->ReportLiveDeviceObjects(RldoFlags.Summary | RldoFlags.Detail | RldoFlags.IgnoreInternal);
                    pDebug->Release();
                }
            }

            _dxgiAdapter.Dispose();

            // Report live DXGI objects (only available on Windows)
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            try
            {
#pragma warning disable CS0618
                using var dxgi = DXGI.GetApi();
#pragma warning restore CS0618
                IDXGIDebug1* pDxgiDebug;
                Guid dxgiDebugGuid = IDXGIDebug1.Guid;
                if (dxgi.GetDebugInterface1(0, &dxgiDebugGuid, (void**)&pDxgiDebug) >= 0 && pDxgiDebug != null)
                {
                    Guid debugAll = DxgiDebugAll;
                    pDxgiDebug->ReportLiveObjects(debugAll, DebugRloFlags.Summary | DebugRloFlags.IgnoreInternal);
                    pDxgiDebug->Release();
                }
            }
            catch (Exception)
            {
                // DXGIGetDebugInterface1 may not be available on older Windows versions
            }
        }
        else
        {
            _device.Dispose();
            _dxgiAdapter.Dispose();
        }

        _d3d11Api.Dispose();
    }

    private protected override void WaitForIdleCore()
    {
        if (_slots == null) return;

        lock (_immediateContextLock)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ref SlotState slot = ref _slots[i];
                if (slot.CurrentFrameId == 0) continue;

                while (true)
                {
                    int done;
                    int hr = ((ID3D11DeviceContext*)_immediateContext)->GetData(
                        (ID3D11Asynchronous*)slot.EventQuery, &done, sizeof(int), 0);
                    if (hr == 0 && done != 0) break;
                    Thread.Yield();
                }

                slot.FenceWrapper.Set();
            }
        }
    }

    public override bool GetD3D11Info(out BackendInfoD3D11 info)
    {
        info = _d3d11Info;
        return true;
    }
}
