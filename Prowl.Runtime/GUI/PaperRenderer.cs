// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Quill;
using Prowl.Runtime.GraphicsBackend;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.GUI;

public class PaperRenderer : ICanvasRenderer
{
    // Graphics objects
    private GraphicsProgram _shaderProgram;
    private GraphicsVertexArray _vertexArrayObject;
    private GraphicsBuffer _vertexBuffer;
    private GraphicsBuffer _elementBuffer;
    private Texture2D _defaultTexture;

    // View properties
    private Float4x4 _projection;

    public void Initialize(int width, int height)
    {
        InitializeShaders();

        // Create vertex buffer
        _vertexBuffer = Graphics.Device.CreateBuffer<float>(BufferType.VertexBuffer, Array.Empty<float>(), true);

        // Create element buffer
        _elementBuffer = Graphics.Device.CreateBuffer<uint>(BufferType.ElementsBuffer, Array.Empty<uint>(), true);

        // Create a VertexArray
        var vertexFormat = new VertexFormat(
        [
                new((VertexFormat.VertexSemantic)0, VertexFormat.VertexType.Float, 2, 0),
                new((VertexFormat.VertexSemantic)1, VertexFormat.VertexType.Float, 2, 0),
                new((VertexFormat.VertexSemantic)2, VertexFormat.VertexType.Float, 4, 0)
        ]);

        _vertexArrayObject = Graphics.Device.CreateVertexArray(vertexFormat, _vertexBuffer, _elementBuffer);

        // Set the default texture
        _defaultTexture = new Texture2D(1, 1);
        byte[] pixelData = [255, 255, 255, 255];
        _defaultTexture.SetData(new Memory<byte>(pixelData), 0, 0, 1, 1);

        UpdateProjection(width, height);
    }

    public void UpdateProjection(int width, int height)
    {
        _projection = Float4x4.CreateOrthoOffCenter(0, width, height, 0, -1, 1);
    }

    public void Cleanup()
    {
        _vertexBuffer?.Dispose();
        _elementBuffer?.Dispose();
        _vertexArrayObject?.Dispose();
        _shaderProgram?.Dispose();
        _defaultTexture?.Dispose();
    }

    private void InitializeShaders()
    {
        // Load Paper UI shader
        var shader = Shader.LoadDefault(DefaultShader.UI);
        if (shader.IsNotValid())
        {
            Debug.LogError("Failed to load UI shader. Make sure 'UI.shader' exists in Assets/Defaults folder.");
            return;
        }

        Rendering.Shaders.ShaderPass pass = shader.GetPass(0);
        if (!pass.TryGetVariantProgram(null, out _shaderProgram))
        {
            Debug.LogError("Failed to compile UI shader.");
            return;
        }
    }

    private Float4 ToVector4(Color32 color)
    {
        return new Float4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }

    public object CreateTexture(uint width, uint height)
    {
        return new Texture2D(width, height);
    }

    public Int2 GetTextureSize(object texture)
    {
        if (texture is not Texture2D tkTexture)
            throw new ArgumentException("Invalid texture type");

        return new Int2((int)tkTexture.Width, (int)tkTexture.Height);
    }

    public void SetTextureData(object texture, IntRect bounds, byte[] data)
    {
        if (texture is not Texture2D tkTexture)
            throw new ArgumentException("Invalid texture type");

        tkTexture.SetData(new Memory<byte>(data), bounds.Min.X, bounds.Min.Y, (uint)bounds.Size.X, (uint)bounds.Size.Y);
    }

    public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
    {
        // Skip if canvas is empty
        if (drawCalls.Count == 0)
            return;

        // Configure state for UI rendering
        var state = new RasterizerState
        {
            DepthTest = false,
            DoBlend = true,
            BlendSrc = RasterizerState.Blending.One,
            BlendDst = RasterizerState.Blending.OneMinusSrcAlpha,
            Blend = RasterizerState.BlendMode.Add,

            CullFace = RasterizerState.PolyFace.None,
        };

        Graphics.Device.SetState(state);
        Graphics.Device.BindProgram(_shaderProgram);
        Graphics.Device.SetUniformMatrix(_shaderProgram, "projection", false, (Float4x4)_projection);

        // Bind vertex array
        Graphics.Device.BindVertexArray(_vertexArrayObject);

        // Update buffer data
        if (canvas.Vertices.Count > 0)
        {
            float[] packedVertexData = new float[canvas.Vertices.Count * 8];
            for (int i = 0; i < canvas.Vertices.Count; i++)
            {
                Vertex vertex = canvas.Vertices[i];
                packedVertexData[i * 8 + 0] = vertex.x;
                packedVertexData[i * 8 + 1] = vertex.y;
                packedVertexData[i * 8 + 2] = vertex.u;
                packedVertexData[i * 8 + 3] = vertex.v;
                packedVertexData[i * 8 + 4] = vertex.r / 255f;
                packedVertexData[i * 8 + 5] = vertex.g / 255f;
                packedVertexData[i * 8 + 6] = vertex.b / 255f;
                packedVertexData[i * 8 + 7] = vertex.a / 255f;
            }
            Graphics.Device.SetBuffer(_vertexBuffer, packedVertexData, true);
        }

        if (canvas.Indices.Count > 0)
        {
            Graphics.Device.SetBuffer(_elementBuffer, canvas.Indices.ToArray(), true);
        }

        // Process draw calls
        int indexOffset = 0;
        foreach (DrawCall drawCall in drawCalls)
        {
            // Handle texture binding
            Texture2D texture = (drawCall.Texture as Texture2D) ?? _defaultTexture;
            Graphics.Device.SetUniformTexture(_shaderProgram, "texture0", 0, texture.Handle);

            // Set scissor rectangle
            drawCall.GetScissor(out Float4x4 scissor, out Float2 extent);
            Graphics.Device.SetUniformMatrix(_shaderProgram, "scissorMat", false, scissor);
            Graphics.Device.SetUniformV2(_shaderProgram, "scissorExt", extent);

            // Set brush parameters
            Graphics.Device.SetUniformMatrix(_shaderProgram, "brushMat", false, drawCall.Brush.BrushMatrix);
            Graphics.Device.SetUniformI(_shaderProgram, "brushType", (int)drawCall.Brush.Type);
            Graphics.Device.SetUniformV4(_shaderProgram, "brushColor1", ToVector4(drawCall.Brush.Color1));
            Graphics.Device.SetUniformV4(_shaderProgram, "brushColor2", ToVector4(drawCall.Brush.Color2));
            Graphics.Device.SetUniformV4(_shaderProgram, "brushParams", new Float4(
                drawCall.Brush.Point1.X,
                drawCall.Brush.Point1.Y,
                drawCall.Brush.Point2.X,
                drawCall.Brush.Point2.Y));
            Graphics.Device.SetUniformV2(_shaderProgram, "brushParams2", new Float2(
                drawCall.Brush.CornerRadii,
                drawCall.Brush.Feather));

            // Draw the elements
            Graphics.Device.DrawIndexed(
                Topology.Triangles,
                (uint)drawCall.ElementCount,
                indexOffset,
                0,
                true);

            indexOffset += drawCall.ElementCount;
        }

        // Unbind vertex array
        Graphics.Device.BindVertexArray(null);
    }

    public void Dispose()
    {
        Cleanup();
    }
}
