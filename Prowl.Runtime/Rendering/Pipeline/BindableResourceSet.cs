using System;
using System.Numerics;
using Veldrid;

#pragma warning disable

namespace Prowl.Runtime
{
    internal class BindableResourceSet
    {
        public ShaderPipeline Pipeline { get; private set; }

        private bool modifiedResources; 

        public ResourceSetDescription description; 
        private ResourceSet resources;

        private DeviceBuffer[] uniformBuffers;

        private byte[][] intermediateBuffers;
        private bool[] bufferWasModified;


        public BindableResourceSet(ShaderPipeline pipeline, ResourceSetDescription description, DeviceBuffer[] buffers, byte[][] intermediate)
        {
            this.Pipeline = pipeline;
            this.description = description;
            this.uniformBuffers = buffers;
            this.intermediateBuffers = intermediate;
            this.bufferWasModified = new bool[uniformBuffers.Length];
        }


        public void Bind(ResourceFactory factory, CommandList list)
        {
            if (modifiedResources)
            {
                resources?.Dispose();
                resources = factory.CreateResourceSet(description);

                modifiedResources = false;
            }

            for (int i = 0; i < uniformBuffers.Length; i++)
            {
                if (bufferWasModified[i])
                {
                    list.UpdateBuffer(uniformBuffers[i], 0, intermediateBuffers[i]);
                    bufferWasModified[i] = false;
                }
            }

            list.SetGraphicsResourceSet(0, resources);
        }

        
        public bool UpdateBuffer(CommandList list, string ID)
        {
            if (!GetUniform(ID, out Uniform? uniform, out int buffer, out _))
                return false;
            
            if (buffer < 0)
                return false;
            
            list.UpdateBuffer(uniformBuffers[buffer], 0, intermediateBuffers[buffer]);

            return true;
        }


        public bool GetUniform(string ID, out Uniform? uniform, out int buffer, out UniformMember member)
        {
            uniform = null;
            buffer = -1;
            member = default;

            if (!Pipeline.GetUniform(ID, out int uniformIndex, out buffer, out int memberIndex))
                return false;

            uniform = Pipeline.Uniforms[uniformIndex];

            if (memberIndex >= 0)
                member = uniform.members[memberIndex];

            return true;
        }


        public void SetResource(BindableResource newResource, uint index)
        {
            BindableResource res = description.BoundResources[index];

            modifiedResources |= res.Resource != newResource.Resource;
            
            description.BoundResources[index] = newResource;
        }


        public bool SetTexture(string ID, Texture value)
        {
            if (value == null || 
                (!value.Usage.HasFlag(TextureUsage.Sampled) &&
                !value.Usage.HasFlag(TextureUsage.Storage)))
                return false;

            if (!GetUniform(ID, out Uniform? uniform, out _, out _))
                return false;

            if (uniform.kind != ResourceKind.TextureReadOnly && uniform.kind != ResourceKind.TextureReadWrite)
                return false;

            if (!value.Usage.HasFlag(TextureUsage.Storage) && uniform.kind == ResourceKind.TextureReadWrite)
                return false;

            SetResource(value.TextureView, uniform.binding);

            return true;
        }


        public bool SetSampler(string ID, TextureSampler value)
        {
            if (value == null)
                return false;

            if (!GetUniform(ID, out Uniform? uniform, out _, out _))
                return false;

            if (uniform.kind != ResourceKind.Sampler)
                return false;
            
            SetResource(value.InternalSampler, uniform.binding);

            return true;
        }


        public unsafe bool SetFloat(string ID, float value)
            => UploadData(ID, &value, ValueType.Float, sizeof(float));


        public unsafe bool SetInt(string ID, int value)
            => UploadData(ID, &value, ValueType.Int, sizeof(int));


        public unsafe bool SetInt(string ID, uint value)
            => UploadData(ID, &value, ValueType.UInt, sizeof(uint));


        public unsafe bool SetVector(string ID, Vector4 value)
            => UploadData(ID, &value, ValueType.Float, sizeof(float) * 4);
        

        public unsafe bool SetMatrix(string ID, Matrix4x4 value)
            => UploadData(ID, &value, ValueType.Float, sizeof(float) * 4 * 4);


        public unsafe bool SetFloatArray(string ID, float[] values)
        {
            if (values != null)
                fixed (float* valuesPtr = values)
                    return UploadData(ID, valuesPtr, ValueType.Float, sizeof(float) * values.Length);
        
            return false;
        }


        public unsafe bool SetIntArray(string ID, int[] values)
        {
            if (values != null)
                fixed (int* valuesPtr = values)
                    return UploadData(ID, valuesPtr, ValueType.Int, sizeof(int) * values.Length);

            return false;
        }


        public unsafe bool SetVectorArray(string ID, Vector4[] values)
        {
            if (values != null)
                fixed (Vector4* valuesPtr = values)
                    return UploadData(ID, valuesPtr, ValueType.Float, sizeof(float) * 4 * values.Length);

            return false;
        }

        
        public unsafe bool SetMatrixArray(string ID, Matrix4x4[] values)
        {
            if (values != null)
                fixed (Matrix4x4* valuesPtr = values)
                    return UploadData(ID, valuesPtr, ValueType.Float, sizeof(float) * 4 * 4 * values.Length);

            return false;
        }


        private unsafe bool UploadData<T>(string ID, T* dataPtr, ValueType type, int maxSize) where T : unmanaged
        {
            if (!GetUniform(ID, out Uniform? uniform, out int bufferIndex, out UniformMember member))
                return false;

            if (bufferIndex < 0)
                return false;

            if (uniform.kind != ResourceKind.UniformBuffer || member.type != type)
                return false;

            long size = Math.Min(member.size, maxSize);
            byte[] bytes = intermediateBuffers[bufferIndex]; 

            fixed (byte* bytesPtr = bytes)
                Buffer.MemoryCopy(dataPtr, (bytesPtr + member.bufferOffsetInBytes), member.size, size);
            
            bufferWasModified[bufferIndex] |= true;

            return true;
        }

        public bool SetBuffer(CommandList list, string ID, GraphicsBuffer value)
        {
            if (!GetUniform(ID, out Uniform? uniform, out _, out _))
                return false;
                
            if (!value.Buffer.Usage.HasFlag(BufferUsage.StructuredBufferReadWrite) && uniform.kind == ResourceKind.StructuredBufferReadWrite)
                return false;
            
            SetResource(value.Buffer, uniform.binding);

            return true;
        }
    }
}