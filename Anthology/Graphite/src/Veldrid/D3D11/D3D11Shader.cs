using System;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

namespace Veldrid.D3D11
{
    internal unsafe class D3D11Shader : Shader
    {
        private string _name;

        private ComPtr<ID3D11DeviceChild> _deviceShader;
        private bool _disposed;
        public ComPtr<ID3D11DeviceChild> DeviceShader => _deviceShader;
        public byte[] Bytecode { get; internal set; }

        public D3D11Shader(ID3D11Device* device, ShaderDescription description)
            : base(description.Stage, description.EntryPoint)
        {
            if (description.ShaderBytes.Length > 4
                && description.ShaderBytes[0] == 0x44
                && description.ShaderBytes[1] == 0x58
                && description.ShaderBytes[2] == 0x42
                && description.ShaderBytes[3] == 0x43)
            {
                Bytecode = Util.ShallowClone(description.ShaderBytes);
            }
            else
            {
                Bytecode = CompileCode(description);
            }

            fixed (byte* pBytecode = Bytecode)
            {
                switch (description.Stage)
                {
                    case ShaderStages.Vertex:
                    {
                        ID3D11VertexShader* pShader;
                        SilkMarshal.ThrowHResult(device->CreateVertexShader(pBytecode, (nuint)Bytecode.Length, null, &pShader));
                        _deviceShader = default;
                        _deviceShader.Handle = (ID3D11DeviceChild*)pShader;
                        break;
                    }
                    case ShaderStages.Geometry:
                    {
                        ID3D11GeometryShader* pShader;
                        SilkMarshal.ThrowHResult(device->CreateGeometryShader(pBytecode, (nuint)Bytecode.Length, null, &pShader));
                        _deviceShader = default;
                        _deviceShader.Handle = (ID3D11DeviceChild*)pShader;
                        break;
                    }
                    case ShaderStages.TessellationControl:
                    {
                        ID3D11HullShader* pShader;
                        SilkMarshal.ThrowHResult(device->CreateHullShader(pBytecode, (nuint)Bytecode.Length, null, &pShader));
                        _deviceShader = default;
                        _deviceShader.Handle = (ID3D11DeviceChild*)pShader;
                        break;
                    }
                    case ShaderStages.TessellationEvaluation:
                    {
                        ID3D11DomainShader* pShader;
                        SilkMarshal.ThrowHResult(device->CreateDomainShader(pBytecode, (nuint)Bytecode.Length, null, &pShader));
                        _deviceShader = default;
                        _deviceShader.Handle = (ID3D11DeviceChild*)pShader;
                        break;
                    }
                    case ShaderStages.Fragment:
                    {
                        ID3D11PixelShader* pShader;
                        SilkMarshal.ThrowHResult(device->CreatePixelShader(pBytecode, (nuint)Bytecode.Length, null, &pShader));
                        _deviceShader = default;
                        _deviceShader.Handle = (ID3D11DeviceChild*)pShader;
                        break;
                    }
                    case ShaderStages.Compute:
                    {
                        ID3D11ComputeShader* pShader;
                        SilkMarshal.ThrowHResult(device->CreateComputeShader(pBytecode, (nuint)Bytecode.Length, null, &pShader));
                        _deviceShader = default;
                        _deviceShader.Handle = (ID3D11DeviceChild*)pShader;
                        break;
                    }
                    default:
                        throw Illegal.Value<ShaderStages>();
                }
            }
        }

        private static byte[] CompileCode(ShaderDescription description)
        {
            string profile;
            switch (description.Stage)
            {
                case ShaderStages.Vertex:
                    profile = "vs_5_0";
                    break;
                case ShaderStages.Geometry:
                    profile = "gs_5_0";
                    break;
                case ShaderStages.TessellationControl:
                    profile = "hs_5_0";
                    break;
                case ShaderStages.TessellationEvaluation:
                    profile = "ds_5_0";
                    break;
                case ShaderStages.Fragment:
                    profile = "ps_5_0";
                    break;
                case ShaderStages.Compute:
                    profile = "cs_5_0";
                    break;
                default:
                    throw Illegal.Value<ShaderStages>();
            }

            // D3DCOMPILE_DEBUG = 0x1, D3DCOMPILE_OPTIMIZATION_LEVEL3 = 0x8000
            uint flags = description.Debug ? 0x1u : 0x8000u;

            D3DCompiler compiler = D3DCompiler.GetApi();
            ID3D10Blob* pResult = null;
            ID3D10Blob* pError = null;

            fixed (byte* pSource = description.ShaderBytes)
            {
                nint pEntryPoint = SilkMarshal.StringToPtr(description.EntryPoint);
                nint pProfile = SilkMarshal.StringToPtr(profile);

                int hr = compiler.Compile(
                    pSource, (nuint)description.ShaderBytes.Length,
                    (byte*)null,
                    null,
                    null,
                    (byte*)pEntryPoint,
                    (byte*)pProfile,
                    flags, 0,
                    &pResult, &pError);

                SilkMarshal.Free(pEntryPoint);
                SilkMarshal.Free(pProfile);

                if (pResult == null)
                {
                    string errorMsg = string.Empty;
                    if (pError != null)
                    {
                        byte* errorPtr = (byte*)pError->GetBufferPointer();
                        nuint errorSize = pError->GetBufferSize();
                        errorMsg = Encoding.ASCII.GetString(errorPtr, (int)errorSize);
                        pError->Release();
                    }
                    throw new VeldridException($"Failed to compile HLSL code: {errorMsg}");
                }

                byte* resultPtr = (byte*)pResult->GetBufferPointer();
                nuint resultSize = pResult->GetBufferSize();
                byte[] bytecode = new byte[resultSize];
                new Span<byte>(resultPtr, (int)resultSize).CopyTo(bytecode);

                if (pError != null) pError->Release();
                pResult->Release();

                return bytecode;
            }
        }

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                D3D11Util.SetDebugName(_deviceShader.Handle, value);
            }
        }

        public override bool IsDisposed => _disposed;

        public override void Dispose()
        {
            if (!_disposed)
            {
                _deviceShader.Dispose();
                _disposed = true;
            }
        }
    }
}
