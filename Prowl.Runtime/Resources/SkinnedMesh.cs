using System;

using Veldrid;

using Prowl.Runtime.Rendering;

using Vector3F = System.Numerics.Vector3;
using Vector4F = System.Numerics.Vector4;
using Matrix4x4F = System.Numerics.Matrix4x4;

namespace Prowl.Runtime;


public class SkinnedMesh : IGeometryDrawData, IDisposable
{
    private static AssetRef<ComputeShader>? _skinningShader;
    public static AssetRef<ComputeShader> SkinningShader
    {
        get
        {
            if (_skinningShader == null)
            {
                _skinningShader = Application.AssetProvider.LoadAsset<ComputeShader>("Defaults/MeshSkinner.compute");
            }

            return _skinningShader.Value;
        }
    }


    public AssetRef<Mesh> MeshRes;
    public Mesh Mesh => MeshRes.Res;

    public int IndexCount => Mesh.IndexCount;

    public IndexFormat IndexFormat => Mesh.IndexFormat;

    public PrimitiveTopology Topology => Mesh.Topology;

    public DeviceBuffer VertexOutput;
    public DeviceBuffer NormalOutput;
    public DeviceBuffer TangentOutput;
    public DeviceBuffer BoneBuffer;

    public int BoneBufferLength { get; private set; }


    public ComputeDescriptor ComputeDescriptor;



    public SkinnedMesh(AssetRef<Mesh> sourceMesh)
    {
        MeshRes = sourceMesh;
        ComputeDescriptor = new(SkinningShader);
    }


    private static unsafe void ValidateBuffer(ref DeviceBuffer buffer, uint sizeBytes, int stride, BufferUsage usage)
    {
        const int maxDiff = 2048; // If data is more than 2 kilobytes larger, downsize the buffer

        if (buffer == null || buffer.SizeInBytes < sizeBytes || buffer.SizeInBytes - sizeBytes > maxDiff)
        {
            buffer?.Dispose();
            buffer = Graphics.Factory.CreateBuffer(new BufferDescription(sizeBytes, usage, (uint)stride));
        }
    }


    public unsafe void RecomputeSkinning(Matrix4x4F[] boneTransforms)
    {
        Mesh.Upload();

        if (!Mesh.HasBoneIndices || !Mesh.HasBoneWeights || !Mesh.HasBindPoses)
            return;

        const BufferUsage usage = BufferUsage.VertexBuffer | BufferUsage.StructuredBufferReadWrite;

        ValidateBuffer(ref VertexOutput, Mesh.VertexBuffer.SizeInBytes, sizeof(Vector3F), usage);
        ValidateBuffer(ref BoneBuffer, Mesh.BindPoseBuffer.SizeInBytes, sizeof(Matrix4x4F), BufferUsage.StructuredBufferReadOnly);

        if (Mesh.HasNormals)
            ValidateBuffer(ref NormalOutput, Mesh.NormalBuffer.SizeInBytes, sizeof(Vector3F), usage);

        if (Mesh.HasTangents)
            ValidateBuffer(ref TangentOutput, Mesh.TangentBuffer.SizeInBytes, sizeof(Vector3F), usage);

        int kernel;

        if (Mesh.HasNormals && Mesh.HasTangents)
            kernel = SkinningShader.Res.GetKernelIndex("SkinFull");
        else if (Mesh.HasNormals)
            kernel = SkinningShader.Res.GetKernelIndex("SkinVertexNormal");
        else if (Mesh.HasTangents)
            kernel = SkinningShader.Res.GetKernelIndex("SkinVertexTangent");
        else
            kernel = SkinningShader.Res.GetKernelIndex("SkinVertex");

        Graphics.Device.UpdateBuffer(BoneBuffer, 0, boneTransforms);

        ComputeDescriptor.SetInt("BufferLength", Mesh.VertexCount);

        ComputeDescriptor.SetRawBuffer("InPositions", Mesh.VertexBuffer);

        if (Mesh.HasNormals)
            ComputeDescriptor.SetRawBuffer("InNormals", Mesh.NormalBuffer);

        if (Mesh.HasTangents)
            ComputeDescriptor.SetRawBuffer("InTangents", Mesh.TangentBuffer);

        ComputeDescriptor.SetRawBuffer("BoneIndices", Mesh.BoneIndexBuffer);
        ComputeDescriptor.SetRawBuffer("BoneWeights", Mesh.BoneWeightBuffer);
        ComputeDescriptor.SetRawBuffer("BindPoses", Mesh.BindPoseBuffer);

        ComputeDescriptor.SetRawBuffer("BoneTransforms", BoneBuffer);

        ComputeDescriptor.SetRawBuffer("OutPositions", VertexOutput);

        if (Mesh.HasNormals)
            ComputeDescriptor.SetRawBuffer("OutNormals", NormalOutput);

        if (Mesh.HasTangents)
            ComputeDescriptor.SetRawBuffer("OutTangents", TangentOutput);

        ComputeDispatcher.Dispatch(ComputeDescriptor, kernel, (uint)Math.Ceiling(Mesh.VertexCount / 64.0), 1, 1);
    }


    public void SetDrawData(CommandList commandList, ShaderPipeline pipeline)
    {
        if (!Mesh.HasBoneIndices || !Mesh.HasBoneWeights || !Mesh.HasBindPoses || VertexOutput == null)
        {
            Mesh.SetDrawData(commandList, pipeline);
            return;
        }

        commandList.SetIndexBuffer(Mesh.IndexBuffer, IndexFormat);

        pipeline.BindVertexBuffer(commandList, "POSITION0", VertexOutput);
        pipeline.BindVertexBuffer(commandList, "TEXCOORD0", Mesh.HasUV ? Mesh.UVBuffer : Mesh.VertexBuffer);
        pipeline.BindVertexBuffer(commandList, "TEXCOORD1", Mesh.HasUV2 ? Mesh.UV2Buffer : Mesh.VertexBuffer);
        pipeline.BindVertexBuffer(commandList, "NORMAL0", Mesh.HasNormals ? NormalOutput : Mesh.VertexBuffer);
        pipeline.BindVertexBuffer(commandList, "TANGENT0", Mesh.HasTangents ? TangentOutput : Mesh.VertexBuffer);
        pipeline.BindVertexBuffer(commandList, "COLOR0", Mesh.HasColors ? Mesh.ColorBuffer : Mesh.VertexBuffer);
    }


    public void Dispose()
    {
        VertexOutput?.Dispose();
        VertexOutput = null;
        NormalOutput?.Dispose();
        NormalOutput = null;
        TangentOutput?.Dispose();
        TangentOutput = null;
        BoneBuffer?.Dispose();
        BoneBuffer = null;

        GC.SuppressFinalize(this);
    }


    ~SkinnedMesh()
    {
        Dispose();
    }
}

