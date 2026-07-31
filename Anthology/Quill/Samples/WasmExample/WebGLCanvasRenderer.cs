using System.Runtime.InteropServices;

using Prowl.Quill;
using Prowl.Vector;

namespace WasmExample;

/// <summary>
/// ICanvasRenderer implementation that renders via WebGL2 through JS interop.
/// </summary>
public class WebGLCanvasRenderer : ICanvasRenderer
{
    private int _nextTextureId = 1;
    private readonly Dictionary<int, (int w, int h)> _textureSizes = new();

    // Reusable buffers to avoid per-frame allocations
    private int[] _drawCallInfoBuffer = Array.Empty<int>();
    private double[] _scissorBuffer = Array.Empty<double>();
    private double[] _brushBuffer = Array.Empty<double>();

    private const int VERTEX_SIZE = 20; // 8 position + 8 uv + 4 color
    private const int DC_INFO_STRIDE = 3;   // texId, fontTexId, elemCount
    private const int SCISSOR_STRIDE = 8;  // affine(4) + translation(2) + extent(2)
    private const int BRUSH_STRIDE = 28;   // see the packing below

    public (int w, int h) GetCanvasSize()
    {
        return (WebGLInterop.GetCanvasWidth(), WebGLInterop.GetCanvasHeight());
    }

    public object CreateTexture(uint width, uint height)
    {
        int texId = _nextTextureId++;
        _textureSizes[texId] = ((int)width, (int)height);
        WebGLInterop.CreateTexture(texId, (int)width, (int)height);
        return texId;
    }

    public Int2 GetTextureSize(object texture)
    {
        int texId = (int)texture;
        if (_textureSizes.TryGetValue(texId, out var size))
            return new Int2(size.w, size.h);
        return new Int2(0, 0);
    }

    public void SetTextureData(object texture, IntRect bounds, byte[] data)
    {
        int texId = (int)texture;
        WebGLInterop.SetTextureData(texId, bounds.Min.X, bounds.Min.Y, bounds.Size.X, bounds.Size.Y, data);
    }

    private static void EnsureSize<T>(ref T[] arr, int needed)
    {
        if (arr.Length < needed)
            arr = new T[needed];
    }

    public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
    {
        if (drawCalls.Count == 0) return;

        ReadOnlySpan<Vertex> vertices = canvas.Vertices;
        ReadOnlySpan<uint> indices = canvas.Indices;
        int vertexCount = vertices.Length;
        int indexCount = indices.Length;

        if (vertexCount == 0 || indexCount == 0) return;

        // JS interop marshals a copy of whatever it is handed, so build the exact-sized arrays
        // directly from the canvas backing store rather than staging them through a scratch buffer.
        int vertexBytes = vertexCount * VERTEX_SIZE;
        var vertexSlice = new byte[vertexBytes];
        MemoryMarshal.AsBytes(vertices).CopyTo(vertexSlice);

        var indexSlice = new int[indexCount];
        MemoryMarshal.Cast<uint, int>(indices).CopyTo(indexSlice);

        float fbScale = canvas.FramebufferScale;
        int dcCount = drawCalls.Count;
        EnsureSize(ref _drawCallInfoBuffer, dcCount * DC_INFO_STRIDE);
        EnsureSize(ref _scissorBuffer, dcCount * SCISSOR_STRIDE);
        EnsureSize(ref _brushBuffer, dcCount * BRUSH_STRIDE);

        for (int i = 0; i < dcCount; i++)
        {
            var dc = drawCalls[i];
            int di = i * DC_INFO_STRIDE;

            // Draw call info
            int texId = dc.Texture != null ? (int)dc.Texture : 0;
            int fontTexId = dc.FontAtlas != null ? (int)dc.FontAtlas : 0;
            _drawCallInfoBuffer[di] = texId;
            _drawCallInfoBuffer[di + 1] = fontTexId;
            _drawCallInfoBuffer[di + 2] = dc.ElementCount;

            // Scissor: a 2D affine plus the extent, with the framebuffer scale already folded in.
            dc.GetScissor(fbScale, out var scissorXf, out var scissorT, out var scissorExt);
            int s = i * SCISSOR_STRIDE;
            _scissorBuffer[s] = scissorXf.X;
            _scissorBuffer[s + 1] = scissorXf.Y;
            _scissorBuffer[s + 2] = scissorXf.Z;
            _scissorBuffer[s + 3] = scissorXf.W;
            _scissorBuffer[s + 4] = scissorT.X;
            _scissorBuffer[s + 5] = scissorT.Y;
            _scissorBuffer[s + 6] = scissorExt.X;
            _scissorBuffer[s + 7] = scissorExt.Y;

            // Brush
            int b = i * BRUSH_STRIDE;
            _brushBuffer[b] = (int)dc.Brush.Type;

            dc.GetBrushTransform(fbScale, out var brushXf, out var brushT);
            _brushBuffer[b + 1] = brushXf.X;
            _brushBuffer[b + 2] = brushXf.Y;
            _brushBuffer[b + 3] = brushXf.Z;
            _brushBuffer[b + 4] = brushXf.W;
            _brushBuffer[b + 5] = brushT.X;
            _brushBuffer[b + 6] = brushT.Y;

            _brushBuffer[b + 7] = dc.Brush.Color1.R / 255.0;
            _brushBuffer[b + 8] = dc.Brush.Color1.G / 255.0;
            _brushBuffer[b + 9] = dc.Brush.Color1.B / 255.0;
            _brushBuffer[b + 10] = dc.Brush.Color1.A / 255.0;
            _brushBuffer[b + 11] = dc.Brush.Color2.R / 255.0;
            _brushBuffer[b + 12] = dc.Brush.Color2.G / 255.0;
            _brushBuffer[b + 13] = dc.Brush.Color2.B / 255.0;
            _brushBuffer[b + 14] = dc.Brush.Color2.A / 255.0;

            _brushBuffer[b + 15] = dc.Brush.Point1.X;
            _brushBuffer[b + 16] = dc.Brush.Point1.Y;
            _brushBuffer[b + 17] = dc.Brush.Point2.X;
            _brushBuffer[b + 18] = dc.Brush.Point2.Y;

            _brushBuffer[b + 19] = dc.Brush.CornerRadii;
            _brushBuffer[b + 20] = dc.Brush.Feather;

            dc.GetTextureTransform(fbScale, out var texXf, out var texT);
            _brushBuffer[b + 21] = texXf.X;
            _brushBuffer[b + 22] = texXf.Y;
            _brushBuffer[b + 23] = texXf.Z;
            _brushBuffer[b + 24] = texXf.W;
            _brushBuffer[b + 25] = texT.X;
            _brushBuffer[b + 26] = texT.Y;

            _brushBuffer[b + 27] = canvas.Text.FontEngine.DistanceRange;
        }

        // Pass exact-sized arrays to JS
        var dcInfoSlice = new int[dcCount * DC_INFO_STRIDE];
        Array.Copy(_drawCallInfoBuffer, dcInfoSlice, dcCount * DC_INFO_STRIDE);
        var scissorSlice = new double[dcCount * SCISSOR_STRIDE];
        Array.Copy(_scissorBuffer, scissorSlice, dcCount * SCISSOR_STRIDE);
        var brushSlice = new double[dcCount * BRUSH_STRIDE];
        Array.Copy(_brushBuffer, brushSlice, dcCount * BRUSH_STRIDE);

        double scale = canvas.FramebufferScale;
        WebGLInterop.Render(vertexSlice, indexSlice, dcInfoSlice,
            scissorSlice, brushSlice, scale);
    }

    public void Dispose() { }
}
