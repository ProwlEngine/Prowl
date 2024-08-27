using System;
using System.Collections.Generic;
using Veldrid;
using System.Diagnostics.CodeAnalysis;


namespace Prowl.Runtime
{
    public struct ShaderPipelineDescription : IEquatable<ShaderPipelineDescription>
    {
        public ShaderPass pass;
        public ShaderVariant variant;
        
        public PolygonFillMode fillMode; // Defined by mesh.
        public PrimitiveTopology topology; // Defined by mesh.

        public static readonly FrontFace FrontFace = FrontFace.Clockwise;

        public bool scissorTest;
        public OutputDescription? output;

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(pass, variant, fillMode, topology, scissorTest, output);
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is not ShaderPipelineDescription other)
                return false;

            return Equals(other);
        }

        public bool Equals(ShaderPipelineDescription other)
        {
            return 
                pass == other.pass &&
                variant == other.variant && 
                fillMode == other.fillMode &&
                topology == other.topology &&
                scissorTest == other.scissorTest &&
                output.Equals(other.output);
        }
    }

    internal sealed partial class ShaderPipeline : IDisposable
    {
        public readonly ShaderVariant shader;

        public readonly ShaderSetDescription shaderSet;
        public readonly ResourceLayout resourceLayout;

        private Dictionary<string, uint> semanticLookup;
        private Dictionary<string, ulong> uniformLookup;

        private int bufferCount;

        public Uniform[] Uniforms => shader.Uniforms;


        public ShaderPipeline(ShaderPipelineDescription description)
        {
            this.shader = description.variant;

            ShaderDescription[] shaderDescriptions = shader.GetProgramsForBackend();
            
            // Create shader set description
            Veldrid.Shader[] shaders = new Veldrid.Shader[shaderDescriptions.Length];

            this.semanticLookup = new();

            for (int shaderIndex = 0; shaderIndex < shaders.Length; shaderIndex++)
                shaders[shaderIndex] = Graphics.Factory.CreateShader(shaderDescriptions[shaderIndex]);

            VertexLayoutDescription[] vertexLayouts = new VertexLayoutDescription[shader.VertexInputs.Length];

            for (int inputIndex = 0; inputIndex < vertexLayouts.Length; inputIndex++)
            {
                StageInput input = shader.VertexInputs[inputIndex];

                // Add in_var_ to match reflected name in SPIRV-Cross generated GLSL.
                vertexLayouts[inputIndex] = new VertexLayoutDescription(
                    new VertexElementDescription("in_var_" + input.semantic, input.format, VertexElementSemantic.TextureCoordinate));

                semanticLookup[input.semantic] = (uint)inputIndex;

                // If the last char of the semantic is a single '0', add a non-indexed version of the semantic to the lookup.
                if (input.semantic.Length >= 2 && 
                    input.semantic[input.semantic.Length - 1] == '0' && 
                    !char.IsNumber(input.semantic[input.semantic.Length - 2]))
                {
                    semanticLookup[input.semantic.Substring(0, input.semantic.Length - 1)] = (uint)inputIndex;
                }
            }

            foreach (var k in semanticLookup.Keys)
                Console.WriteLine(k);

            this.shaderSet = new ShaderSetDescription(vertexLayouts, shaders);

            // Create resource layout and uniform lookups
            this.uniformLookup = new();

            ResourceLayoutDescription layoutDescription = new ResourceLayoutDescription(
                new ResourceLayoutElementDescription[Uniforms.Length]);

            for (ushort uniformIndex = 0; uniformIndex < Uniforms.Length; uniformIndex++)
            {
                Uniform uniform = Uniforms[uniformIndex];
                ShaderStages stages = shader.UniformStages[uniformIndex];

                layoutDescription.Elements[uniformIndex] = 
                    new ResourceLayoutElementDescription(GetGLSLName(uniform.name), uniform.kind, stages);

                uniformLookup[uniform.name] = Pack(uniformIndex, -1, -1);

                if (uniform.kind != ResourceKind.UniformBuffer)
                    continue;
                
                uniformLookup[uniform.name] = Pack(uniformIndex, (short)bufferCount, -1);

                for (short member = 0; member < uniform.members.Length; member++)
                    uniformLookup[uniform.members[member].name] = Pack(uniformIndex, (short)bufferCount, member);

                bufferCount++;
            }

            this.resourceLayout = Graphics.Factory.CreateResourceLayout(layoutDescription);

            /*
            RasterizerStateDescription rasterizerState = new(
                description.pass.CullMode, 
                description.fillMode, 
                ShaderPipelineDescription.FrontFace, 
                description.pass.DepthClipEnabled, 
                description.scissorTest);

            GraphicsPipelineDescription pipelineDescription = new(
                description.pass.Blend, 
                description.pass.DepthStencilState, 
                rasterizerState, 
                description.topology,
                shaderSet, 
                [ resourceLayout ],
                description.output ?? Graphics.ScreenTarget.Framebuffer.OutputDescription);

            this.pipelineObject = Graphics.Factory.CreateGraphicsPipeline(pipelineDescription);
            */
        }


        private BindableResource GetBindableResource(GraphicsDevice device, Uniform uniform, out DeviceBuffer? buffer)
        {
            buffer = null;

            if (uniform.kind == ResourceKind.TextureReadOnly)
                return Texture2D.Empty.TextureView;

            if (uniform.kind == ResourceKind.TextureReadWrite)
                return Texture2D.EmptyRW.TextureView;
            
            if (uniform.kind == ResourceKind.Sampler)
                return Graphics.Device.PointSampler;

            if (uniform.kind == ResourceKind.StructuredBufferReadOnly)
                return GraphicsBuffer.Empty.Buffer;
            
            if (uniform.kind == ResourceKind.StructuredBufferReadWrite)
                return GraphicsBuffer.EmptyRW.Buffer;

            uint bufferSize = (uint)Math.Ceiling(uniform.size / (double)16) * 16;
            buffer = device.ResourceFactory.CreateBuffer(new BufferDescription(bufferSize, BufferUsage.UniformBuffer | BufferUsage.DynamicWrite));

            return buffer;
        }


        public BindableResourceSet CreateResources(GraphicsDevice device)
        {
            DeviceBuffer[] boundBuffers = new DeviceBuffer[bufferCount];
            BindableResource[] boundResources = new BindableResource[Uniforms.Length];
            byte[][] intermediateBuffers = new byte[bufferCount][];

            for (int i = 0, b = 0; i < Uniforms.Length; i++)
            {
                boundResources[i] = GetBindableResource(device, Uniforms[i], out DeviceBuffer? buffer);

                if (buffer != null)
                {
                    boundBuffers[b] = buffer;
                    intermediateBuffers[b] = new byte[buffer.SizeInBytes];

                    b++; 
                }
            }

            ResourceSetDescription setDescription = new ResourceSetDescription(resourceLayout, boundResources);
            BindableResourceSet resources = new BindableResourceSet(this, setDescription, boundBuffers, intermediateBuffers);

            return resources;
        }


        public bool GetUniform(string name, out int uniform, out int buffer, out int member)
        {
            uniform = -1;
            buffer = -1;
            member = -1;

            if (uniformLookup.TryGetValue(name, out ulong packed))
            {
                Unpack(packed, out ushort u, out short b, out member);
                uniform = u;
                buffer = b;
                return true;
            }

            return false;
        }


        public void BindVertexBuffer(CommandList list, string semantic, DeviceBuffer buffer, uint offset = 0)
        {
            if (semanticLookup.TryGetValue(semantic, out uint location))
                list.SetVertexBuffer(location, buffer, offset);
        }


        // This is so fucking stupid. 
        // The following GLSL:
        // "uniform type_SomeBuf { mat4 a; } _SomeBuf;"
        // must be bound using 'type_SomeBuf' instead of '_SomeBuf'.
        // Absolutely NO IDEA if this works for other devices, assuming it will... even though _SomeBuf seems more correct?
        private static string GetGLSLName(string name)
        {
            if (name[0] == '_')
                return "type" + name.Replace("$", "");
            
            return "type_" + name.Replace("$", "");
        }

        public static ulong Pack(ushort a, short b, int c)
            => ((ulong)(ushort)a << 48) | ((ulong)(ushort)b << 32) | (uint)c;

        public static void Unpack(ulong packed, out ushort a, out short b, out int c)    
            => (a, b, c) = ((ushort)(packed >> 48), (short)(packed >> 32), (int)(packed & uint.MaxValue));
        
        public static void Dispose()
        {

        }
    }
}