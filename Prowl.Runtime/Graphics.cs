// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Runtime.Rendering;
using System.Linq;
using Prowl.Runtime.GraphicsBackend.OpenGL;
using Prowl.Runtime.Resources;
using Prowl.Runtime.GraphicsBackend;
using Prowl.Runtime.GraphicsBackend.Primitives;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime;

public class MeshRenderable : IRenderable
{
    private Mesh _mesh;
    private Material _material;
    private Double4x4 _transform;
    private int _layerIndex;
    private PropertyState _properties;

    public MeshRenderable(Mesh mesh, Material material, Double4x4 matrix, int layerIndex, PropertyState? propertyBlock = null)
    {
        _mesh = mesh;
        _material = material;
        _transform = matrix;
        _layerIndex = layerIndex;
        _properties = propertyBlock ?? new();
    }

    public Material GetMaterial() => _material;
    public int GetLayer() => _layerIndex;

    public void GetRenderingData(out PropertyState properties, out Mesh drawData, out Double4x4 model)
    {
        drawData = _mesh;
        properties = _properties;
        model = _transform;
    }

    public void GetCullingData(out bool isRenderable, out AABBD bounds)
    {
        isRenderable = true;
        //bounds = Bounds.CreateFromMinMax(new Vector3(999999), new Vector3(999999));
        bounds = _mesh.bounds.Transform(_transform);
    }
}

public static class Graphics
{
    public static GraphicsDevice Device { get; internal set; }

#warning TODO: Move these to a separate class "GraphicsCapabilities" and add more, Their Assigned by GLDevice which is very ugly
    public static int MaxTextureSize { get; internal set; }
    public static int MaxCubeMapTextureSize { get; internal set; }
    public static int MaxArrayTextureLayers { get; internal set; }
    public static int MaxFramebufferColorAttachments { get; internal set; }

    public static Double2 ScreenSize => new Double2(Window.InternalWindow.FramebufferSize.X, Window.InternalWindow.FramebufferSize.Y);
    public static RectInt ScreenRect => new RectInt(0, 0, Window.InternalWindow.FramebufferSize.X, Window.InternalWindow.FramebufferSize.Y);

    private static Shader? s_blitShader;
    private static Material? s_blitMaterial;
    public static Material BlitMaterial
    {
        get
        {
            if (s_blitShader == null)
                s_blitShader = Shader.LoadDefault(DefaultShader.Blit);

            if (s_blitMaterial == null)
                s_blitMaterial = new Material(s_blitShader);

            return s_blitMaterial;
        }
    }

    public static void Blit(Texture2D source, Material? mat = null, int pass = 0)
    {
        mat ??= BlitMaterial;
        mat.SetTexture("_MainTex", source);
        Blit(mat, pass);
    }
    public static void Blit(RenderTexture source, RenderTexture target, Material? mat = null, int pass = 0, bool clearDepth = false, bool clearColor = false, Float4 color = default)
    {
        mat ??= BlitMaterial;
        mat.SetTexture("_MainTex", source.MainTexture);
        Blit(target, mat, pass, clearDepth, clearColor, color);
    }
    public static void Blit(RenderTexture target, Material? mat = null, int pass = 0, bool clearDepth = false, bool clearColor = false, Float4 color = default)
    {
        mat ??= BlitMaterial;
        if (target != null)
        {
            Graphics.Device.BindFramebuffer(target.frameBuffer);
        }
        else
        {
            Graphics.Device.UnbindFramebuffer();
            Graphics.Device.Viewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
        }
        if (clearDepth || clearColor)
        {
            ClearFlags clear = 0;
            if (clearDepth) clear |= ClearFlags.Depth;
            if (clearColor) clear |= ClearFlags.Color;
            Device.Clear(color.R, color.G, color.B, color.A, clear | ClearFlags.Stencil);
        }
        Blit(mat, pass);
    }
    public static void Blit(Material? mat = null, int pass = 0)
    {
        mat ??= BlitMaterial;
        DrawMeshNow(Mesh.GetFullscreenQuad(), mat, pass);
    }


    public static void DrawMeshNow(Mesh mesh, Material mat, int passIndex = 0)
    {
        if (mesh.VertexCount <= 0) return;

        // Mesh data can vary between meshes, so we need to let the shader know which attributes are in use
        mat.SetKeyword("HAS_NORMALS", mesh.HasNormals);
        mat.SetKeyword("HAS_TANGENTS", mesh.HasTangents);
        mat.SetKeyword("HAS_UV", mesh.HasUV);
        mat.SetKeyword("HAS_UV2", mesh.HasUV2);
        mat.SetKeyword("HAS_COLORS", mesh.HasColors || mesh.HasColors32);
        mat.SetKeyword("HAS_BONEINDICES", mesh.HasBoneIndices);
        mat.SetKeyword("HAS_BONEWEIGHTS", mesh.HasBoneWeights);
        mat.SetKeyword("SKINNED", mesh.HasBoneIndices && mesh.HasBoneWeights);

        var pass = mat.Shader.GetPass(passIndex);

        if (!pass.TryGetVariantProgram(mat._localKeywords, out var variant))
            throw new Exception($"Failed to set shader pass {pass.Name}. No variant found for the current keyword state.");

        Device.SetState(pass.State);

        PropertyState.Apply(mat._properties, variant);

        mesh.Upload();

        unsafe
        {
            Device.BindVertexArray(mesh.VertexArrayObject);
            Device.DrawIndexed(mesh.MeshTopology, (uint)mesh.IndexCount, mesh.IndexFormat == IndexFormat.UInt32, null);
            Device.BindVertexArray(null);
        }
    }

    public static void Initialize()
    {
        Device = new GLDevice();
        Device.Initialize(true);
    }

    public static void StartFrame()
    {
        Device.UnbindFramebuffer();
        Device.Viewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
        Device.SetState(new(), true);

        Device.BindVertexArray(null);
        Device.Clear(0, 0, 0, 1, ClearFlags.Color | ClearFlags.Depth | ClearFlags.Stencil);
    }

    public static void EndFrame()
    {
        RenderTexture.UpdatePool();
    }

    public static void Dispose()
    {
        Device.Dispose();
    }
}
