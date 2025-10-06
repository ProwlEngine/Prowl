using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.Runtime.AssetImporting;

namespace Prowl.Runtime
{
    public class BasicAssetProvider : IAssetProvider
    {
        // Dictionary to store cached assets with their paths as keys
        private readonly Dictionary<string, EngineObject> _assetCache = new Dictionary<string, EngineObject>();

        private readonly Assembly? _embeddedResourceAssembly;

        public BasicAssetProvider()
        {
            _embeddedResourceAssembly = Assembly.GetExecutingAssembly();
        }

        public bool HasAsset(string relativeAssetPath)
        {
            if (_assetCache.ContainsKey(relativeAssetPath))
                return true;

            if (relativeAssetPath.StartsWith("$"))
                return TryGetEmbeddedResourceName(relativeAssetPath.Substring(1), out _);

            return File.Exists(relativeAssetPath);
        }

        public T? LoadAsset<T>(string assetPath) where T : EngineObject
        {
            // Check if the asset is already in the cache
            if (_assetCache.TryGetValue(assetPath, out EngineObject cachedAsset))
            {
                // Return the cached asset if it's the correct type
                if (cachedAsset is T typedAsset)
                    return typedAsset;

                // If the requested type is different, remove it from cache to reload with correct type
                _assetCache.Remove(assetPath);
            }

            if (!HasAsset(assetPath))
                throw new FileNotFoundException($"Asset '{assetPath}' not found.");

            // Check if this is an embedded resource
            bool isEmbeddedResource = assetPath.StartsWith("$");
            string actualPath = isEmbeddedResource ? assetPath.Substring(1) : assetPath;

            string extension = Path.GetExtension(actualPath).ToLower();

            // Validate supported asset types
            string[] supportedTypes = { ".png", ".bmp", ".tga", ".jpg", ".gif", ".psd", ".dds", ".hdr",
                                        ".obj", ".fbx", ".gltf", ".wav", ".mp3", ".ogg",
                                        ".shader", ".mat", ".mesh" };

            if (!supportedTypes.Contains(extension))
                throw new NotSupportedException($"Asset type '{extension}' is not supported.");

            // Load asset based on extension
            T? asset = extension switch
            {
                ".png" or ".bmp" or ".tga" or ".jpg" or ".gif" or ".psd" or ".dds" or ".hdr" => LoadTexture<T>(isEmbeddedResource, actualPath, assetPath),
                ".obj" or ".fbx" or ".gltf" => LoadModel<T>(isEmbeddedResource, actualPath, assetPath),
                ".shader" => LoadShader<T>(isEmbeddedResource, actualPath, assetPath),
                ".mat" or ".mesh" => LoadSerializedAsset<T>(isEmbeddedResource, actualPath, assetPath),
                ".wav" or ".mp3" or ".ogg" => HandleAudio<T>(assetPath),
                _ => throw new NotSupportedException($"Asset type '{extension}' is not supported.")
            };

            if (asset != null)
                _assetCache[assetPath] = asset;

            return asset;
        }

        private T? LoadTexture<T>(bool isEmbedded, string actualPath, string assetPath) where T : EngineObject
        {
            T? asset = isEmbedded
                ? LoadFromEmbedded(actualPath, stream => Resources.Texture2D.FromStream(stream, true) as T)
                : Resources.Texture2D.FromFile(assetPath, true) as T;

            if (asset != null)
                asset.AssetPath = assetPath;

            return asset;
        }

        private T? LoadModel<T>(bool isEmbedded, string actualPath, string assetPath) where T : EngineObject
        {
            T? asset = isEmbedded
                ? LoadFromEmbedded(actualPath, stream => new ModelImporter().Import(stream, actualPath) as T)
                : new ModelImporter().Import(new FileInfo(assetPath)) as T;

            if (asset != null)
                asset.AssetPath = assetPath;

            return asset;
        }

        private T? LoadShader<T>(bool isEmbedded, string actualPath, string assetPath) where T : EngineObject
        {
            T? asset = isEmbedded
                ? LoadFromEmbedded(actualPath, stream => new ShaderImporter().Import(stream, actualPath) as T)
                : new ShaderImporter().Import(new FileInfo(assetPath)) as T;

            if (asset != null)
                asset.AssetPath = assetPath;

            return asset;
        }

        private T? LoadSerializedAsset<T>(bool isEmbedded, string actualPath, string assetPath) where T : EngineObject
        {
            string content = isEmbedded
                ? LoadFromEmbedded(actualPath, stream =>
                {
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                })
                : File.ReadAllText(assetPath);

            var echo = EchoObject.ReadFromString(content);
            T? asset = Serializer.Deserialize<T>(echo);

            if (asset != null)
                asset.AssetPath = assetPath;

            return asset;
        }

        private T? HandleAudio<T>(string assetPath) where T : EngineObject
        {
            Debug.LogWarning($"Audio loading is not implemented yet. Asset: {assetPath}");
            return null;
        }

        private TResult LoadFromEmbedded<TResult>(string resourcePath, Func<Stream, TResult> loader)
        {
            using var stream = GetEmbeddedResourceStream(resourcePath);
            return loader(stream);
        }

        public void ClearCache() => _assetCache.Clear();

        public void RemoveFromCache(string assetPath) => _assetCache.Remove(assetPath);

        private bool TryGetEmbeddedResourceName(string resourcePath, out string? resourceName)
        {
            resourceName = null;

            if (_embeddedResourceAssembly == null)
                return false;

            string normalizedPath = resourcePath.Replace('/', '.').Replace('\\', '.');
            string[] resourceNames = _embeddedResourceAssembly.GetManifestResourceNames();

            // Try exact match first, then filename match
            resourceName = resourceNames.FirstOrDefault(r => r.EndsWith(normalizedPath, StringComparison.OrdinalIgnoreCase))
                        ?? resourceNames.FirstOrDefault(r => r.EndsWith(Path.GetFileName(resourcePath), StringComparison.OrdinalIgnoreCase));

            return resourceName != null;
        }

        public Stream GetEmbeddedResourceStream(string resourcePath)
        {
            if (_embeddedResourceAssembly == null)
                throw new InvalidOperationException("No embedded resource assembly configured.");

            if (!TryGetEmbeddedResourceName(resourcePath, out string? resourceName) || resourceName == null)
                throw new FileNotFoundException($"Embedded resource '{resourcePath}' not found.");

            return _embeddedResourceAssembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"Failed to load embedded resource '{resourceName}'.");
        }
    }
}
