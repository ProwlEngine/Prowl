using System;

using Veldrid;

using Prowl.Runtime.Rendering;

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


    public Mesh Mesh;

    public int IndexCount => Mesh.IndexCount;

    public IndexFormat IndexFormat => Mesh.IndexFormat;

    public PrimitiveTopology Topology => Mesh.Topology;

    public DeviceBuffer VertexBufferCopy;
    public DeviceBuffer VertexOutput;
    public DeviceBuffer SkinningBuffer;

    public int NormalsStart { get; private set; }
    public int TangentsStart { get; private set; }
    public int VertexBufferLength { get; private set; }

    public int BoneWeightsStart { get; private set; }
    public int BindPoseStart { get; private set; }
    public int BoneTransformStart { get; private set; }
    public int SkinningBufferLength { get; private set; }


    public ComputeDescriptor ComputeDescriptor;



    public SkinnedMesh(Mesh sourceMesh)
    {
        Mesh = sourceMesh;
        ComputeDescriptor = new(SkinningShader);

        RecreateVertexBuffers();
        RecreateSkinningBuffer();
    }


    private void RecalculateBufferOffsets()
    {
        const int floatSize = sizeof(float);
        const int intSize = sizeof(int);

        const int int4Size = intSize * 4;
        const int vec3Size = floatSize * 3;
        const int vec4Size = floatSize * 4;
        const int mat4Size = vec4Size * 4;

        int vertLen = Mesh.Vertices.Length;

        NormalsStart = vertLen * vec3Size;
        TangentsStart = NormalsStart + (Mesh.HasTangents ? vertLen * vec3Size : 0);
        VertexBufferLength = TangentsStart + (Mesh.HasTangents ? vertLen * vec4Size : 0);

        BoneWeightsStart = vertLen * int4Size;                                                // Where bone indices end
        BindPoseStart = BoneWeightsStart + (vertLen * vec4Size);                              // Where bone weights end
        BoneTransformStart = BindPoseStart + (Mesh.bindPoses.Length * mat4Size);              // Where bind poses end
        SkinningBufferLength = BoneTransformStart + (Mesh.bindPoses.Length * mat4Size);       // Where bone transforms end
    }


    private void RecreateVertexBuffers()
    {
        VertexBufferCopy?.Dispose();
        VertexBufferCopy = Graphics.Factory.CreateBuffer(new BufferDescription((uint)VertexBufferLength, Mesh.VertexBuffer.Usage | BufferUsage.StructuredBufferReadOnly));

        VertexOutput?.Dispose();
        VertexOutput = Graphics.Factory.CreateBuffer(new BufferDescription((uint)VertexBufferLength, Mesh.VertexBuffer.Usage | BufferUsage.StructuredBufferReadWrite));
    }


    private void RecreateSkinningBuffer()
    {
        SkinningBuffer?.Dispose();
        SkinningBuffer = Graphics.Factory.CreateBuffer(new BufferDescription((uint)SkinningBufferLength, BufferUsage.StructuredBufferReadOnly));
    }


    public void RecomputeSkinning(System.Numerics.Matrix4x4[] boneTransforms)
    {
        RecalculateBufferOffsets();

        if (VertexBufferCopy.SizeInBytes != (uint)VertexBufferLength)
            RecreateVertexBuffers();

        if (SkinningBuffer.SizeInBytes != (uint)SkinningBufferLength)
            RecreateSkinningBuffer();

        int kernel;

        if (Mesh.HasNormals && Mesh.HasTangents)
            kernel = SkinningShader.Res.GetKernelIndex("SkinFull");
        else if (Mesh.HasNormals)
            kernel = SkinningShader.Res.GetKernelIndex("SkinVertexNormal");
        else if (Mesh.HasTangents)
            kernel = SkinningShader.Res.GetKernelIndex("SkinVertexTangent");
        else
            kernel = SkinningShader.Res.GetKernelIndex("SkinVertex");


        ComputeDescriptor.SetInt("BufferLength", Mesh.VertexCount);

        ComputeDispatcher.Dispatch(ComputeDescriptor, kernel, (uint)Math.Ceiling(Mesh.VertexCount / 64.0), 1, 1);
    }


    public void SetDrawData(CommandList commandList, ShaderPipeline pipeline)
    {
        commandList.SetIndexBuffer(Mesh.IndexBuffer, IndexFormat);

        pipeline.BindVertexBuffer(commandList, "POSITION0", VertexBufferCopy, 0);
        pipeline.BindVertexBuffer(commandList, "TEXCOORD0", Mesh.VertexBuffer, (uint)Mesh.UVStart);
        pipeline.BindVertexBuffer(commandList, "TEXCOORD1", Mesh.VertexBuffer, (uint)Mesh.UV2Start);
        pipeline.BindVertexBuffer(commandList, "NORMAL0", VertexBufferCopy, (uint)NormalsStart);
        pipeline.BindVertexBuffer(commandList, "TANGENT0", VertexBufferCopy, (uint)TangentsStart);
        pipeline.BindVertexBuffer(commandList, "COLOR0", Mesh.VertexBuffer, (uint)Mesh.ColorsStart);
    }


    public void Dispose()
    {
        VertexBufferCopy?.Dispose();
        VertexOutput?.Dispose();
        SkinningBuffer?.Dispose();

        GC.SuppressFinalize(this);
    }


    ~SkinnedMesh()
    {
        Dispose();
    }
}

