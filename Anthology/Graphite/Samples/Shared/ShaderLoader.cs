using System;

using Prowl.Slang;

using System.Linq;
using System.Collections.Generic;


namespace Prowl.Veldrid.Samples;


public static class ShaderLoader
{
    private class FileProvider : IFileProvider
    {
        public Memory<byte>? LoadFile(string path)
        {
            return FileLoader.Load(path);
        }
    }


    private static Dictionary<GraphicsBackend, TargetDescription> s_descriptions = new()
    {
        [GraphicsBackend.OpenGL] =
            new TargetDescription()
            {
                Format = CompileTarget.Glsl,
                Profile = GlobalSession.FindProfile("glsl_410")
            },
        [GraphicsBackend.OpenGLES] =
            new TargetDescription()
            {
                Format = CompileTarget.Glsl,
                Profile = GlobalSession.FindProfile("glsl_es_310")
            },
        [GraphicsBackend.Vulkan] =
            new TargetDescription()
            {
                Format = CompileTarget.Spirv,
                Profile = GlobalSession.FindProfile("spirv_1_4")
            },
        [GraphicsBackend.Direct3D11] =
            new TargetDescription()
            {
                Format = CompileTarget.Hlsl,
                Profile = GlobalSession.FindProfile("sm_5_0")
            },
    };


    public static ShaderProgram CreateShader(GraphicsDevice device)
    {
        SessionDescription sessionDesc = new()
        {
            DefaultMatrixLayoutMode = MatrixLayoutMode.ColumnMajor,
            FileProvider = new FileProvider(),
            SearchPaths = [.. FileLoader.SearchDirectories.Select(x => x.FullName)],
            Targets = [s_descriptions[device.BackendType]]
        };

        Session sesh = GlobalSession.CreateSession(sessionDesc);

        Module mod = sesh.LoadModule("Shader.slang", out _);

        EntryPoint vert = mod.FindEntryPointByName("vertex");
        EntryPoint frag = mod.FindEntryPointByName("fragment");

        ComponentType composite = sesh.CreateCompositeComponentType([mod, vert, frag], out _);

        ShaderDescription shaderDesc = new()
        {
            BlendState = BlendStateDescription.SingleDisabled,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new(FaceCullMode.Back, FrontFace.Clockwise, true, false),
            ResourceLayouts =
            [
                new ResourceLayoutDescription()
                {
                    Set = 0,
                    Elements =
                    [
                        new ResourceLayoutElementDescription()
                        {
                            Stages = ShaderStages.Vertex | ShaderStages.Fragment,
                            Name = "Model",
                            GLUniformName = "block_ModelData_0",
                            Kind = ResourceKind.UniformBuffer,
                            BindingIndex = 0,
                            UniformFields =
                            [
                                new UniformBlockField("MatrixMVP", 0, sizeof(float) * 4 * 4, UniformScalarType.Float4x4),
                                new UniformBlockField("Color", sizeof(float) * 4 * 4, sizeof(float) * 4, UniformScalarType.Float4)
                            ]
                        },
                        new ResourceLayoutElementDescription()
                        {
                            Stages = ShaderStages.Fragment,
                            Name = "MainTexture",
                            GLUniformName = "Model_MainTexture_0",
                            Kind = ResourceKind.TextureReadOnly,
                            BindingIndex = 1,
                        }
                    ]
                }
            ],
            VertexLayouts =
            [
                new VertexLayoutDescription()
                {
                    Location = 0,
                    Stride = sizeof(float) * 3,
                    Elements = [ new VertexElementDescription("POSITION", VertexElementFormat.Float3) ]
                },

                new VertexLayoutDescription()
                {
                    Location = 1,
                    Stride = sizeof(float) * 2,
                    Elements = [ new VertexElementDescription("UV", VertexElementFormat.Float2) ]
                },

                new VertexLayoutDescription()
                {
                    Location = 2,
                    Stride = sizeof(float) * 4,
                    Elements = [ new VertexElementDescription("COLOR", VertexElementFormat.Float4) ]
                }
            ],
            Stages =
            [
                new ShaderStageDescription()
                {
                    ShaderBytes = composite.GetEntryPointCode(0, 0, out _).ToArray(),
                    Stage = ShaderStages.Vertex,
                    EntryPoint = "vertex"
                },
                new ShaderStageDescription()
                {
                    ShaderBytes = composite.GetEntryPointCode(1, 0, out _).ToArray(),
                    Stage = ShaderStages.Fragment,
                    EntryPoint = "fragment"
                }
            ]
        };


        Console.WriteLine(System.Text.Encoding.UTF8.GetString(shaderDesc.Stages[0].ShaderBytes));
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(shaderDesc.Stages[1].ShaderBytes));


        return device.ResourceFactory.CreateShaderProgram(shaderDesc);
    }
}
