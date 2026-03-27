using System;
using System.Collections.Generic;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Veldrid.D3D11
{
    internal unsafe class D3D11Buffer : DeviceBuffer
    {
        private readonly ID3D11Device* _device;
        private ComPtr<ID3D11Buffer> _buffer;
        private readonly object _accessViewLock = new object();
        private readonly Dictionary<OffsetSizePair, ComPtr<ID3D11ShaderResourceView>> _srvs
            = new Dictionary<OffsetSizePair, ComPtr<ID3D11ShaderResourceView>>();
        private readonly Dictionary<OffsetSizePair, ComPtr<ID3D11UnorderedAccessView>> _uavs
            = new Dictionary<OffsetSizePair, ComPtr<ID3D11UnorderedAccessView>>();
        private readonly uint _structureByteStride;
        private readonly bool _rawBuffer;
        private string _name;
        private bool _disposed;

        public override uint SizeInBytes { get; }

        public override BufferUsage Usage { get; }

        public override bool IsDisposed => _disposed;

        public ID3D11Buffer* Buffer => _buffer;

        public D3D11Buffer(ID3D11Device* device, uint sizeInBytes, BufferUsage usage, uint structureByteStride, bool rawBuffer)
        {
            _device = device;
            SizeInBytes = sizeInBytes;
            Usage = usage;
            _structureByteStride = structureByteStride;
            _rawBuffer = rawBuffer;

            BufferDesc bd = new BufferDesc
            {
                ByteWidth = sizeInBytes,
                BindFlags = (uint)D3D11Formats.VdToD3D11BindFlags(usage),
                Usage = Silk.NET.Direct3D11.Usage.Default,
            };

            if ((usage & BufferUsage.StructuredBufferReadOnly) == BufferUsage.StructuredBufferReadOnly
                || (usage & BufferUsage.StructuredBufferReadWrite) == BufferUsage.StructuredBufferReadWrite)
            {
                if (rawBuffer)
                {
                    bd.MiscFlags = (uint)ResourceMiscFlag.BufferAllowRawViews;
                }
                else
                {
                    bd.MiscFlags = (uint)ResourceMiscFlag.BufferStructured;
                    bd.StructureByteStride = structureByteStride;
                }
            }

            if ((usage & BufferUsage.IndirectBuffer) == BufferUsage.IndirectBuffer)
            {
                bd.MiscFlags = (uint)ResourceMiscFlag.DrawindirectArgs;
            }

            if ((usage & BufferUsage.Dynamic) == BufferUsage.Dynamic)
            {
                bd.Usage = Silk.NET.Direct3D11.Usage.Dynamic;
                bd.CPUAccessFlags = (uint)CpuAccessFlag.Write;
            }
            else if ((usage & BufferUsage.Staging) == BufferUsage.Staging)
            {
                bd.Usage = Silk.NET.Direct3D11.Usage.Staging;
                bd.CPUAccessFlags = (uint)(CpuAccessFlag.Read | CpuAccessFlag.Write);
            }

            ID3D11Buffer* pBuffer;
            SilkMarshal.ThrowHResult(device->CreateBuffer(in bd, null, &pBuffer));
            _buffer = default;
            _buffer.Handle = pBuffer;
        }

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                D3D11Util.SetDebugName((ID3D11DeviceChild*)_buffer.Handle, value);
                foreach (var kvp in _srvs)
                    D3D11Util.SetDebugName((ID3D11DeviceChild*)kvp.Value.Handle, value + "_SRV");
                foreach (var kvp in _uavs)
                    D3D11Util.SetDebugName((ID3D11DeviceChild*)kvp.Value.Handle, value + "_UAV");
            }
        }

        public override void Dispose()
        {
            if (!_disposed)
            {
                foreach (KeyValuePair<OffsetSizePair, ComPtr<ID3D11ShaderResourceView>> kvp in _srvs)
                {
                    kvp.Value.Dispose();
                }
                foreach (KeyValuePair<OffsetSizePair, ComPtr<ID3D11UnorderedAccessView>> kvp in _uavs)
                {
                    kvp.Value.Dispose();
                }
                _buffer.Dispose();
                _disposed = true;
            }
        }

        internal ID3D11ShaderResourceView* GetShaderResourceView(uint offset, uint size)
        {
            lock (_accessViewLock)
            {
                OffsetSizePair pair = new OffsetSizePair(offset, size);
                if (!_srvs.TryGetValue(pair, out ComPtr<ID3D11ShaderResourceView> srv))
                {
                    srv = CreateShaderResourceView(offset, size);
                    _srvs.Add(pair, srv);
                }

                return srv;
            }
        }

        internal ID3D11UnorderedAccessView* GetUnorderedAccessView(uint offset, uint size)
        {
            lock (_accessViewLock)
            {
                OffsetSizePair pair = new OffsetSizePair(offset, size);
                if (!_uavs.TryGetValue(pair, out ComPtr<ID3D11UnorderedAccessView> uav))
                {
                    uav = CreateUnorderedAccessView(offset, size);
                    _uavs.Add(pair, uav);
                }

                return uav;
            }
        }

        private ComPtr<ID3D11ShaderResourceView> CreateShaderResourceView(uint offset, uint size)
        {
            ShaderResourceViewDesc srvDesc = new ShaderResourceViewDesc();

            if (_rawBuffer)
            {
                srvDesc.Format = Format.FormatR32Typeless;
                srvDesc.ViewDimension = D3DSrvDimension.D3DSrvDimensionBufferex;
                srvDesc.BufferEx.FirstElement = offset / 4;
                srvDesc.BufferEx.NumElements = size / 4;
                srvDesc.BufferEx.Flags = (uint)BufferexSrvFlag.Raw;
            }
            else
            {
                srvDesc.ViewDimension = D3DSrvDimension.D3DSrvDimensionBuffer;
                srvDesc.Buffer.FirstElement = offset / _structureByteStride;
                srvDesc.Buffer.NumElements = size / _structureByteStride;
            }

            ID3D11ShaderResourceView* pSrv;
            SilkMarshal.ThrowHResult(_device->CreateShaderResourceView((ID3D11Resource*)_buffer.Handle, in srvDesc, &pSrv));
            ComPtr<ID3D11ShaderResourceView> result = default;
            result.Handle = pSrv;
            return result;
        }

        private ComPtr<ID3D11UnorderedAccessView> CreateUnorderedAccessView(uint offset, uint size)
        {
            UnorderedAccessViewDesc uavDesc = new UnorderedAccessViewDesc
            {
                ViewDimension = UavDimension.Buffer,
            };

            if (_rawBuffer)
            {
                uavDesc.Format = Format.FormatR32Typeless;
                uavDesc.Buffer.FirstElement = offset / 4;
                uavDesc.Buffer.NumElements = size / 4;
                uavDesc.Buffer.Flags = (uint)BufferUavFlag.Raw;
            }
            else
            {
                uavDesc.Format = Format.FormatUnknown;
                uavDesc.Buffer.FirstElement = offset / _structureByteStride;
                uavDesc.Buffer.NumElements = size / _structureByteStride;
            }

            ID3D11UnorderedAccessView* pUav;
            SilkMarshal.ThrowHResult(_device->CreateUnorderedAccessView((ID3D11Resource*)_buffer.Handle, in uavDesc, &pUav));
            ComPtr<ID3D11UnorderedAccessView> result = default;
            result.Handle = pUav;
            return result;
        }

        private struct OffsetSizePair : IEquatable<OffsetSizePair>
        {
            public readonly uint Offset;
            public readonly uint Size;

            public OffsetSizePair(uint offset, uint size)
            {
                Offset = offset;
                Size = size;
            }

            public bool Equals(OffsetSizePair other) => Offset.Equals(other.Offset) && Size.Equals(other.Size);
            public override int GetHashCode() => HashHelper.Combine(Offset.GetHashCode(), Size.GetHashCode());
        }
    }
}
