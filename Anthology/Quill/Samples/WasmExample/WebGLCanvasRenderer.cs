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
    private byte[] _vertexBuffer = Array.Empty<byte>();
    private int[] _indexBuffer = Array.Empty<int>();
    private int[] _drawCallInfoBuffer = Array.Empty<int>();
    private double[] _scissorBuffer = Array.Empty<double>();
    private double[] _brushBuffer = Array.Empty<double>();

    private const int VERTEX_SIZE = 44; // 20 core + 24 slug
    private const int DC_INFO_STRIDE = 8; // texId, elemCount, slugCurveTex, slugBandTex, cW, cH, bW, bH

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

    public object? CreateFloatTexture(int width, int height, int components, float[] data)
    {
        int texId = _nextTextureId++;
        _textureSizes[texId] = (width, height);
        // JS interop needs double[] for numeric arrays
        double[] doubleData = new double[data.Length];
        for (int i = 0; i < data.Length; i++)
            doubleData[i] = data[i];
        WebGLInterop.CreateFloatTexture(texId, width, height, components, doubleData);
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

        var vertices = canvas.Vertices;
        var indices = canvas.Indices;
        int vertexCount = vertices.Count;
        int indexCount = indices.Count;

        if (vertexCount == 0 || indexCount == 0) return;

        int vertexBytes = vertexCount * VERTEX_SIZE;
        EnsureSize(ref _vertexBuffer, vertexBytes);
        EnsureSize(ref _indexBuffer, indexCount);

        int dcCount = drawCalls.Count;
        EnsureSize(ref _drawCallInfoBuffer, dcCount * DC_INFO_STRIDE);
        EnsureSize(ref _scissorBuffer, dcCount * 18);
        EnsureSize(ref _brushBuffer, dcCount * 47);

        // Convert vertices to raw bytes (44 bytes each)
        for (int i = 0; i < vertexCount; i++)
        {
            var v = vertices[i];
            int offset = i * VERTEX_SIZE;
            BitConverter.TryWriteBytes(_vertexBuffer.AsSpan(offset), v.x);
            BitConverter.TryWriteBytes(_vertexBuffer.AsSpan(offset + 4), v.y);
            BitConverter.TryWriteBytes(_vertexBuffer.AsSpan(offset + 8), v.u);
            BitConverter.TryWriteBytes(_vertexBuffer.AsSpan(offset + 12), v.v);
            _vertexBuffer[offset + 16] = v.r;
            _vertexBuffer[offset + 17] = v.g;
            _vertexBuffer[offset + 18] = v.b;
            _vertexBuffer[offset + 19] = v.a;
            BitConverter.TryWriteBytes(_vertexBuffer.AsSpan(offset + 20), v.slugBandScaleX);
            BitConverter.TryWriteBytes(_vertexBuffer.AsSpan(offset + 24), v.slugBandScaleY);
            BitConverter.TryWriteBytes(_vertexBuffer.AsSpan(offset + 28), v.slugBandOffsetX);
            BitConverter.TryWriteBytes(_vertexBuffer.AsSpan(offset + 32), v.slugBandOffsetY);
            BitConverter.TryWriteBytes(_vertexBuffer.AsSpan(offset + 36), v.slugPackedBandLoc);
            BitConverter.TryWriteBytes(_vertexBuffer.AsSpan(offset + 40), v.slugBandCount);
        }

        // Convert indices
        for (int i = 0; i < indexCount; i++)
            _indexBuffer[i] = (int)indices[i];

        for (int i = 0; i < dcCount; i++)
        {
            var dc = drawCalls[i];
            int di = i * DC_INFO_STRIDE;

            // Draw call info
            int texId = dc.Texture != null ? (int)dc.Texture : 0;
            _drawCallInfoBuffer[di] = texId;
            _drawCallInfoBuffer[di + 1] = dc.ElementCount;
            _drawCallInfoBuffer[di + 2] = dc.SlugCurveTexture != null ? (int)dc.SlugCurveTexture : 0;
            _drawCallInfoBuffer[di + 3] = dc.SlugBandTexture != null ? (int)dc.SlugBandTexture : 0;
            _drawCallInfoBuffer[di + 4] = dc.SlugCurveTexWidth;
            _drawCallInfoBuffer[di + 5] = dc.SlugCurveTexHeight;
            _drawCallInfoBuffer[di + 6] = dc.SlugBandTexWidth;
            _drawCallInfoBuffer[di + 7] = dc.SlugBandTexHeight;

            // Scissor
            dc.GetScissor(out var scissorMat, out var scissorExt);
            int s = i * 18;
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                    _scissorBuffer[s + col * 4 + row] = scissorMat[row, col];
            _scissorBuffer[s + 16] = scissorExt.X;
            _scissorBuffer[s + 17] = scissorExt.Y;

            // Brush
            int b = i * 47;
            _brushBuffer[b] = (int)dc.Brush.Type;

            var bm = dc.Brush.BrushMatrix;
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                    _brushBuffer[b + 1 + col * 4 + row] = bm[row, col];

            _brushBuffer[b + 17] = dc.Brush.Color1.R / 255.0;
            _brushBuffer[b + 18] = dc.Brush.Color1.G / 255.0;
            _brushBuffer[b + 19] = dc.Brush.Color1.B / 255.0;
            _brushBuffer[b + 20] = dc.Brush.Color1.A / 255.0;
            _brushBuffer[b + 21] = dc.Brush.Color2.R / 255.0;
            _brushBuffer[b + 22] = dc.Brush.Color2.G / 255.0;
            _brushBuffer[b + 23] = dc.Brush.Color2.B / 255.0;
            _brushBuffer[b + 24] = dc.Brush.Color2.A / 255.0;

            _brushBuffer[b + 25] = dc.Brush.Point1.X;
            _brushBuffer[b + 26] = dc.Brush.Point1.Y;
            _brushBuffer[b + 27] = dc.Brush.Point2.X;
            _brushBuffer[b + 28] = dc.Brush.Point2.Y;

            _brushBuffer[b + 29] = dc.Brush.CornerRadii;
            _brushBuffer[b + 30] = dc.Brush.Feather;

            var tm = dc.Brush.TextureMatrix;
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                    _brushBuffer[b + 31 + col * 4 + row] = tm[row, col];
        }

        // Pass exact-sized arrays to JS
        var vertexSlice = new byte[vertexBytes];
        Array.Copy(_vertexBuffer, vertexSlice, vertexBytes);
        var indexSlice = new int[indexCount];
        Array.Copy(_indexBuffer, indexSlice, indexCount);
        var dcInfoSlice = new int[dcCount * DC_INFO_STRIDE];
        Array.Copy(_drawCallInfoBuffer, dcInfoSlice, dcCount * DC_INFO_STRIDE);
        var scissorSlice = new double[dcCount * 18];
        Array.Copy(_scissorBuffer, scissorSlice, dcCount * 18);
        var brushSlice = new double[dcCount * 47];
        Array.Copy(_brushBuffer, brushSlice, dcCount * 47);

        double scale = canvas.Scale;
        WebGLInterop.Render(vertexSlice, indexSlice, dcInfoSlice,
            scissorSlice, brushSlice, scale);
    }

    public void Dispose() { }
}
