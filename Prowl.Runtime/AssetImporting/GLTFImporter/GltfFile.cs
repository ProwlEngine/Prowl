using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Prowl.Runtime.AssetImporting.Gltf
{
    public class GltfFile
    {
        public GltfRoot Root { get; }
        public byte[][] Buffers { get; }

        private GltfFile(GltfRoot root, byte[][] buffers)
        {
            Root = root;
            Buffers = buffers;
        }

        /// <summary>
        /// Load a .glb or .gltf file from disk.
        /// </summary>
        public static GltfFile Load(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            bool isGlb = ext == ".glb";
            string basePath = Path.GetDirectoryName(filePath) ?? ".";

            using var stream = File.OpenRead(filePath);
            return Load(stream, basePath, isGlb);
        }

        /// <summary>
        /// Load from a stream. For .gltf files, basePath is used to resolve relative buffer/image URIs.
        /// </summary>
        public static GltfFile Load(Stream stream, string basePath, bool isGlb)
        {
            if (isGlb)
                return LoadGlb(stream);
            else
                return LoadGltf(stream, basePath);
        }

        private static GltfFile LoadGlb(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            // --- Header (12 bytes) ---
            uint magic = reader.ReadUInt32();
            if (magic != 0x46546C67u)
                throw new InvalidDataException($"Invalid GLB magic: 0x{magic:X8}. Expected 0x46546C67 ('glTF').");

            uint version = reader.ReadUInt32();
            if (version != 2)
                throw new InvalidDataException($"Unsupported GLB version: {version}. Only version 2 is supported.");

            uint totalLength = reader.ReadUInt32();

            // --- JSON chunk ---
            uint jsonChunkLength = reader.ReadUInt32();
            uint jsonChunkType = reader.ReadUInt32();
            if (jsonChunkType != 0x4E4F534Au)
                throw new InvalidDataException($"Expected JSON chunk type (0x4E4F534A), got 0x{jsonChunkType:X8}.");

            byte[] jsonBytes = reader.ReadBytes((int)jsonChunkLength);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
            var root = JsonSerializer.Deserialize<GltfRoot>(jsonBytes, options)
                ?? throw new InvalidDataException("Failed to deserialize GLB JSON chunk.");

            // --- BIN chunk (optional) ---
            byte[]? binData = null;
            if (stream.Position < totalLength)
            {
                uint binChunkLength = reader.ReadUInt32();
                uint binChunkType = reader.ReadUInt32();
                if (binChunkType == 0x004E4942u) // "BIN\0"
                {
                    binData = reader.ReadBytes((int)binChunkLength);
                }
            }

            // Build buffer array: buffer 0 is the embedded BIN chunk, rest are external (rare in GLB)
            var buffers = new byte[root.Buffers.Count][];
            for (int i = 0; i < root.Buffers.Count; i++)
            {
                if (i == 0 && binData != null && root.Buffers[i].Uri == null)
                {
                    buffers[i] = binData;
                }
                else if (root.Buffers[i].Uri != null)
                {
                    buffers[i] = ResolveBufferUri(root.Buffers[i].Uri!, ".");
                }
                else
                {
                    buffers[i] = Array.Empty<byte>();
                }
            }

            return new GltfFile(root, buffers);
        }

        private static GltfFile LoadGltf(Stream stream, string basePath)
        {
            string json;
            using (var sr = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
            {
                json = sr.ReadToEnd();
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
            var root = JsonSerializer.Deserialize<GltfRoot>(json, options)
                ?? throw new InvalidDataException("Failed to deserialize GLTF JSON.");

            var buffers = new byte[root.Buffers.Count][];
            for (int i = 0; i < root.Buffers.Count; i++)
            {
                var uri = root.Buffers[i].Uri;
                if (uri != null)
                {
                    buffers[i] = ResolveBufferUri(uri, basePath);
                }
                else
                {
                    buffers[i] = Array.Empty<byte>();
                }
            }

            return new GltfFile(root, buffers);
        }

        private static byte[] ResolveBufferUri(string uri, string basePath)
        {
            // Data URI: "data:application/octet-stream;base64,..."
            if (uri.StartsWith("data:", StringComparison.Ordinal))
            {
                int commaIndex = uri.IndexOf(',');
                if (commaIndex < 0)
                    throw new InvalidDataException("Malformed data URI in buffer.");
                string base64 = uri.Substring(commaIndex + 1);
                return Convert.FromBase64String(base64);
            }

            // External file, relative to basePath
            string fullPath = Path.Combine(basePath, Uri.UnescapeDataString(uri));
            return File.ReadAllBytes(fullPath);
        }
    }
}
