namespace Prowl.Clay.Importer;

/// <summary>
/// Identifies the format of a model from a path or a peek at its first bytes.
/// </summary>
internal static class FormatDetector
{
    /// <summary>Returns the canonical token (<c>"gltf"</c>, <c>"glb"</c>, <c>"obj"</c>, <c>"fbx"</c>, <c>"vrm"</c>)
    /// for a path, or <c>null</c> when the extension is unrecognized.</summary>
    public static string? FromPath(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".gltf" => "gltf",
            ".glb"  => "glb",
            ".vrm"  => "vrm",
            ".obj"  => "obj",
            ".fbx"  => "fbx",
            _       => null,
        };
    }

    /// <summary>
    /// Peeks the first few bytes of a seekable stream to identify the format. Restores the
    /// stream's position before returning.
    /// </summary>
    public static bool TryDetectFromStream(Stream stream, out string format)
    {
        format = string.Empty;
        if (!stream.CanSeek)
            return false;

        long origin = stream.Position;
        try
        {
            Span<byte> head = stackalloc byte[32];
            int read = stream.Read(head);
            if (read >= 4 && head[0] == 0x67 && head[1] == 0x6C && head[2] == 0x54 && head[3] == 0x46)
            {
                // "glTF" magic from the GLB header.
                format = "glb";
                return true;
            }

            if (read >= 7 &&
                head[0] == 0x4B && head[1] == 0x61 && head[2] == 0x79 && head[3] == 0x64 &&
                head[4] == 0x61 && head[5] == 0x72 && head[6] == 0x61)
            {
                // "Kaydara" prefix of the "Kaydara FBX Binary  " magic.
                format = "fbx";
                return true;
            }

            if (read >= 1 && (head[0] == (byte)'{' || head[0] == (byte)' ' || head[0] == (byte)'\r' || head[0] == (byte)'\n' || head[0] == (byte)'\t'))
            {
                // Likely a JSON document; could be .gltf.
                format = "gltf";
                return true;
            }

            return false;
        }
        finally
        {
            stream.Position = origin;
        }
    }
}
