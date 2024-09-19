// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

using Veldrid;

namespace Prowl.Runtime.GUI.Graphics;

public enum ColorSpaceHandling
{
    LinearizeSRGB,
    Direct
}

public static class UIDrawListRenderer
{
    private static readonly Assembly s_assembly = Assembly.GetAssembly(typeof(UIDrawListRenderer)) ?? throw new NullReferenceException();
    private static ColorSpaceHandling s_handling;

    // Device objects
    private static DeviceBuffer s_vertexBuffer;
    private static DeviceBuffer s_indexBuffer;
    private static DeviceBuffer s_projMatrixBuffer;

    private static Veldrid.Shader s_vertexShader;
    private static Veldrid.Shader s_fragmentShader;

    private static ResourceLayout s_layout;
    private static ResourceLayout s_textureLayout;

    private static Pipeline s_pipeline;

    private static ResourceSet s_mainResourceSet;


    private static bool s_initialized = false;

    // Max active texture sets before the cache is cleared.
    // If for some reason we're rendering 500+ textures caching isn't solving anything anyways.
    private static readonly int s_texturesBeforeClear = 500;
    private static readonly Dictionary<TextureView, ResourceSet> s_textureSets = [];



    public static void Initialize(OutputDescription outputDescription, ColorSpaceHandling handling)
    {
        s_initialized = true;
        s_handling = handling;

        CreateDeviceResources(outputDescription, handling);

        GetResourceSet(Font.DefaultFont.Texture.TextureView);
    }


    private static void EnsureBuffer(ref DeviceBuffer current, uint targetSize, BufferUsage usage, string name)
    {
        if (current != null && current.SizeInBytes > targetSize)
            return;

        current?.Dispose();
        current = Runtime.Graphics.Factory.CreateBuffer(new BufferDescription(targetSize, usage));
        current.Name = name;
    }


    private static void CreateDeviceResources(OutputDescription outputDescription, ColorSpaceHandling handling)
    {
        s_handling = handling;

        ResourceFactory factory = Runtime.Graphics.Factory;

        EnsureBuffer(ref s_vertexBuffer, 10000, BufferUsage.VertexBuffer | BufferUsage.DynamicWrite, "UI Vertex Buffer");
        EnsureBuffer(ref s_indexBuffer, 2000, BufferUsage.IndexBuffer | BufferUsage.DynamicWrite, "UI Index Buffer");
        EnsureBuffer(ref s_projMatrixBuffer, 64, BufferUsage.UniformBuffer | BufferUsage.DynamicWrite, "UI Projection Buffer");

        byte[] vertexShaderBytes = LoadEmbeddedShaderCode("gui-vertex", ShaderStages.Vertex, s_handling);
        byte[] fragmentShaderBytes = LoadEmbeddedShaderCode("gui-frag", ShaderStages.Fragment, s_handling);

        s_vertexShader = factory.CreateShader(new ShaderDescription(ShaderStages.Vertex, vertexShaderBytes, "VS"));
        s_vertexShader.Name = "UI Vertex Shader";

        s_fragmentShader = factory.CreateShader(new ShaderDescription(ShaderStages.Fragment, fragmentShaderBytes, "FS"));
        s_fragmentShader.Name = "UI Fragment Shader";

        VertexLayoutDescription[] vertexLayouts =
        [
            new VertexLayoutDescription(
                new VertexElementDescription("in_position", VertexElementSemantic.Position, VertexElementFormat.Float3),
                new VertexElementDescription("in_texCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("in_color", VertexElementSemantic.Color, VertexElementFormat.Byte4_Norm))
        ];

        s_layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ProjectionMatrixBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
            new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        s_layout.Name = "UI Resource Layout";

        s_textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment)));
        s_textureLayout.Name = "UI Texture Layout";

        GraphicsPipelineDescription pd = new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,

            new DepthStencilStateDescription(false, false, ComparisonKind.Always),

            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, true),

            PrimitiveTopology.TriangleList,

            new ShaderSetDescription(
                vertexLayouts,
                [s_vertexShader, s_fragmentShader]
            ),

            [s_layout, s_textureLayout],

            outputDescription,

            ResourceBindingModel.Default
        );

        s_pipeline = factory.CreateGraphicsPipeline(pd);
        s_pipeline.Name = "UI Pipeline";

        s_mainResourceSet = factory.CreateResourceSet(new ResourceSetDescription(s_layout,
            s_projMatrixBuffer,
            Runtime.Graphics.Device.LinearSampler));

        s_mainResourceSet.Name = "UI Main Resource Set";
    }


    public static ResourceSet GetResourceSet(TextureView textureView)
    {
        if (!s_textureSets.TryGetValue(textureView, out ResourceSet resourceSet))
        {
            resourceSet = Runtime.Graphics.Factory.CreateResourceSet(new ResourceSetDescription(s_textureLayout, textureView));
            resourceSet.Name = $"UI {textureView.Name} Resource Set";

            s_textureSets.Add(textureView, resourceSet);
        }

        return resourceSet;
    }


    public static void ClearCachedImageResources()
    {
        foreach (IDisposable resource in s_textureSets.Values)
            resource.Dispose();

        s_textureSets.Clear();

        GetResourceSet(Font.DefaultFont.Texture.TextureView);
    }


    private static byte[] LoadEmbeddedShaderCode(string name, ShaderStages stage, ColorSpaceHandling handling)
    {
        // No spec. constants since it creates a divide between platforms that do/don't support them
        if (stage == ShaderStages.Vertex && handling == ColorSpaceHandling.LinearizeSRGB)
            name += "-linear";

        name += Runtime.Graphics.Device.BackendType switch
        {
            GraphicsBackend.Direct3D11 => ".hlsl",
            GraphicsBackend.Vulkan     => ".spv",
            GraphicsBackend.OpenGL     => ".glsl",
            GraphicsBackend.OpenGLES   => ".glsles",
            GraphicsBackend.Metal      => ".metal",
            _                          => throw new NotImplementedException()
        };

        using Stream s = s_assembly.GetManifestResourceStream(name);

        byte[] ret = new byte[s.Length];
        s.Read(ret, 0, (int)s.Length);
        return ret;
    }


    public static unsafe void Draw(CommandList cl, UIDrawList[] lists, Vector2 displaySize, double clipscale)
    {
        if (!s_initialized)
        {
            Debug.LogWarning("UI Draw List Renderer not initialized. Try to ensure that Initialize() is called to avoid implicit initialization.");
            Initialize(Runtime.Graphics.ScreenTarget.OutputDescription, ColorSpaceHandling.Direct);
        }

        if (lists.Length == 0)
        {
            return;
        }

        if (s_textureSets.Count > s_texturesBeforeClear)
            ClearCachedImageResources();

        uint totalVtxCount = 0;
        uint totalIdxCount = 0;

        for (int i = 0; i < lists.Length; i++)
        {
            totalVtxCount = Math.Max(totalVtxCount, (uint)lists[i]._vertices.Count);
            totalIdxCount = Math.Max(totalIdxCount, (uint)lists[i]._indices.Count);
        }

        uint totalVBSize = totalVtxCount * (uint)sizeof(UIVertex);
        EnsureBuffer(ref s_vertexBuffer, totalVBSize, BufferUsage.VertexBuffer | BufferUsage.DynamicWrite, "UI Vertex Buffer");

        uint totalIBSize = totalIdxCount * sizeof(uint);
        EnsureBuffer(ref s_indexBuffer, totalIBSize, BufferUsage.IndexBuffer | BufferUsage.DynamicWrite, "UI Index Buffer");

        // Update projection matrix
        System.Numerics.Matrix4x4 mvp = System.Numerics.Matrix4x4.CreateOrthographicOffCenter(
            0f,
            (float)displaySize.x,
            (float)displaySize.y,
            0.0f,
            -100000.0f,
            100000.0f);

        cl.UpdateBuffer(s_projMatrixBuffer, 0, ref mvp);


        cl.SetVertexBuffer(0, s_vertexBuffer);
        cl.SetIndexBuffer(s_indexBuffer, IndexFormat.UInt32);
        cl.SetPipeline(s_pipeline);
        cl.SetGraphicsResourceSet(0, s_mainResourceSet);

        // Render command lists
        for (int n = 0; n < lists.Length; n++)
        {
            UIDrawList cmdListPtr = lists[n];

            if (cmdListPtr._vertices.Count == 0)
                continue;

            cl.UpdateBuffer(s_vertexBuffer, 0, CollectionsMarshal.AsSpan(cmdListPtr._vertices));
            cl.UpdateBuffer(s_indexBuffer, 0, CollectionsMarshal.AsSpan(cmdListPtr._indices));

            uint idxOffset = 0;
            for (int cmd_i = 0; cmd_i < cmdListPtr._commandList.Count; cmd_i++)
            {
                UIDrawCmd pcmd = cmdListPtr._commandList[cmd_i];

                Vector4 clipRect = pcmd.ClipRect * clipscale;

                if (clipRect.x >= displaySize.x && clipRect.y >= displaySize.y && clipRect.z < 0.0f && clipRect.w < 0.0f)
                    continue;

                if (pcmd.Texture != null)
                    cl.SetGraphicsResourceSet(1, GetResourceSet(pcmd.Texture.TextureView));

                cl.SetScissorRect(
                    0,
                    (uint)pcmd.ClipRect.x,
                    (uint)pcmd.ClipRect.y,
                    (uint)(pcmd.ClipRect.z - pcmd.ClipRect.x),
                    (uint)(pcmd.ClipRect.w - pcmd.ClipRect.y));

                cl.DrawIndexed(pcmd.ElemCount, 1, idxOffset, 0, 0);

                idxOffset += pcmd.ElemCount;
            }

            // Clear Depth Buffer
            cl.ClearDepthStencil(1.0f);
        }
    }


    public static void Dispose()
    {
        s_vertexBuffer.Dispose();
        s_indexBuffer.Dispose();
        s_projMatrixBuffer.Dispose();
        s_vertexShader.Dispose();
        s_fragmentShader.Dispose();
        s_layout.Dispose();
        s_textureLayout.Dispose();
        s_pipeline.Dispose();
        s_mainResourceSet.Dispose();

        foreach (IDisposable resource in s_textureSets.Values)
        {
            resource.Dispose();
        }
    }
}
