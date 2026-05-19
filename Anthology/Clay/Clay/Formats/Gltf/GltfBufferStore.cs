using Prowl.Clay.Importer;
using Prowl.Clay.Internal.IO;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// Resolves and caches the raw bytes for every <see cref="GltfBuffer"/> declared in a glTF document.
/// </summary>
/// <remarks>
/// Buffer URIs come in three flavors:
/// <list type="bullet">
/// <item>Missing (only legal for the implicit GLB buffer): bytes are the BIN chunk.</item>
/// <item><c>data:</c> URI: bytes are inline base64.</item>
/// <item>Relative or absolute path: bytes are read via <see cref="IFileResolver"/>.</item>
/// </list>
/// </remarks>
internal sealed class GltfBufferStore
{
    private readonly byte[]?[] _bytes;
    private readonly GltfDom _dom;

    public GltfBufferStore(GltfDom dom, byte[]? glbBin, ImportContext ctx)
    {
        _dom = dom;
        int n = dom.Buffers?.Length ?? 0;
        _bytes = new byte[n][];

        for (int i = 0; i < n; i++)
        {
            var buf = dom.Buffers![i];
            if (buf.Uri is null)
            {
                if (i != 0)
                    throw new ImportException(
                        $"Buffer {i} has no URI; only buffer 0 may be the implicit GLB BIN chunk.",
                        ctx.SourcePath, ctx.Format);
                if (glbBin is null)
                    throw new ImportException(
                        "Buffer 0 has no URI but the GLB has no BIN chunk.",
                        ctx.SourcePath, ctx.Format);
                _bytes[i] = SizedSlice(glbBin, buf.ByteLength, $"GLB BIN chunk", ctx);
                continue;
            }

            if (DataUri.TryDecode(buf.Uri, out _, out byte[] data))
            {
                _bytes[i] = SizedSlice(data, buf.ByteLength, $"data: URI for buffer {i}", ctx);
                continue;
            }

            if (ctx.SourcePath is null)
                throw new ImportException(
                    $"Buffer {i} references external file '{buf.Uri}' but no source path is set.",
                    ctx.SourcePath, ctx.Format);

            string? resolved = ctx.Resolver.Resolve(ctx.SourcePath, Uri.UnescapeDataString(buf.Uri));
            if (resolved is null)
                throw new ImportException(
                    $"Could not resolve external buffer file '{buf.Uri}' referenced by buffer {i}.",
                    ctx.SourcePath, ctx.Format);

            byte[] read = ctx.Resolver.ReadAllBytes(resolved);
            _bytes[i] = SizedSlice(read, buf.ByteLength, $"external buffer '{buf.Uri}'", ctx);
        }
    }

    /// <summary>
    /// Returns a span over the bytes of a <see cref="GltfBufferView"/>.
    /// </summary>
    public ReadOnlySpan<byte> GetBufferView(GltfBufferView view)
    {
        byte[] buf = GetBuffer(view.Buffer);
        if (view.ByteOffset < 0 || view.ByteOffset + view.ByteLength > buf.Length)
            throw new ImportException(
                $"BufferView out of range: offset {view.ByteOffset} length {view.ByteLength} buffer length {buf.Length}.");
        return new ReadOnlySpan<byte>(buf, view.ByteOffset, view.ByteLength);
    }

    public byte[] GetBuffer(int index) =>
        (uint)index < (uint)_bytes.Length && _bytes[index] is { } b
            ? b
            : throw new ImportException($"Missing buffer index {index}.");

    public GltfBufferView GetBufferView(int index)
    {
        var views = _dom.BufferViews;
        if (views is null || (uint)index >= (uint)views.Length)
            throw new ImportException($"Missing bufferView index {index}.");
        return views[index];
    }

    private static byte[] SizedSlice(byte[] source, int expectedLength, string label, ImportContext ctx)
    {
        // The spec allows the actual bytes to be longer than declared (extra padding); shorter is an error.
        if (source.Length < expectedLength)
            throw new ImportException(
                $"{label}: declared byteLength {expectedLength} but only {source.Length} bytes available.",
                ctx.SourcePath, ctx.Format);
        if (source.Length == expectedLength)
            return source;
        byte[] trimmed = new byte[expectedLength];
        Buffer.BlockCopy(source, 0, trimmed, 0, expectedLength);
        return trimmed;
    }
}
