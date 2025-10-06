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
            // First check if the asset is in cache
            if (_assetCache.ContainsKey(relativeAssetPath))
                return true;

            // Check if this is an embedded resource (prefixed with '$')
            if (relativeAssetPath.StartsWith("$"))
            {
                return TryGetEmbeddedResourceName(relativeAssetPath.Substring(1), out _);
            }

            // If not in cache, check if the asset exists in the specified path
            return System.IO.File.Exists(relativeAssetPath);
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

            if (HasAsset(assetPath) == false)
            {
                // Asset not found
                throw new FileNotFoundException($"Asset '{assetPath}' not found.");
            }

            // Check if this is an embedded resource
            bool isEmbeddedResource = assetPath.StartsWith("$");
            string actualPath = isEmbeddedResource ? assetPath.Substring(1) : assetPath;

            // Get extension
            string extension = System.IO.Path.GetExtension(actualPath);
            // Check if the asset is a supported type
            string[] supportedTypes = {
                ".png", ".bmp", ".tga", ".jpg", ".gif", ".psd", ".dds", ".hdr", // Textures
                ".obj", ".fbx", ".gltf", // Models
                ".wav", ".mp3", ".ogg", // Audio
                ".shader", // Shaders
                ".mat", // Materials
                ".mesh", // Mesh
            };

            if (!supportedTypes.Contains(extension.ToLower()))
            {
                // Asset type not supported
                throw new NotSupportedException($"Asset type '{extension}' is not supported.");
            }

            T? asset = null;

            if (extension == ".png" || extension == ".bmp" || extension == ".tga" || extension == ".jpg" || extension == ".gif" || extension == ".psd" || extension == ".dds" || extension == ".hdr")
            {
                // Load texture
                if (isEmbeddedResource)
                {
                    using (Stream stream = GetEmbeddedResourceStream(actualPath))
                        asset = Prowl.Runtime.Resources.Texture2D.FromStream(stream, true) as T;
                }
                else
                {
                    asset = Prowl.Runtime.Resources.Texture2D.FromFile(assetPath, true) as T;
                }
                if (asset != null)
                    asset.AssetPath = assetPath;
            }
            else if (extension == ".obj" || extension == ".fbx" || extension == ".gltf")
            {
                // Load model
                if (isEmbeddedResource)
                {
                    using (Stream stream = GetEmbeddedResourceStream(actualPath))
                        asset = new ModelImporter().Import(stream, actualPath) as T;
                }
                else
                {
                    asset = new ModelImporter().Import(new FileInfo(assetPath)) as T;
                }
                if (asset != null)
                    asset.AssetPath = assetPath;
            }
            else if (extension == ".wav" || extension == ".mp3" || extension == ".ogg")
            {
                Debug.LogWarning($"Audio loading is not implemented yet. Asset: {assetPath}");
            }
            else if (extension == ".shader")
            {
                if (isEmbeddedResource)
                {
                    using (Stream stream = GetEmbeddedResourceStream(actualPath))
                        asset = new ShaderImporter().Import(stream, actualPath) as T;
                }
                else
                {
                    asset = new ShaderImporter().Import(new FileInfo(assetPath)) as T;
                }
                if (asset != null)
                    asset.AssetPath = assetPath;
            }
            else if (extension == ".mat" || extension == ".mesh")
            {
                string assetString;
                if (isEmbeddedResource)
                {
                    using (Stream stream = GetEmbeddedResourceStream(actualPath))
                    using (StreamReader reader = new StreamReader(stream))
                        assetString = reader.ReadToEnd();
                }
                else
                {
                    assetString = System.IO.File.ReadAllText(assetPath);
                }
                var echo = EchoObject.ReadFromString(assetString);
                asset = Serializer.Deserialize<T>(echo);
                if (asset != null)
                    asset.AssetPath = assetPath;
            }
            else
            {
                throw new NotSupportedException($"Asset type '{extension}' is not supported, (But Valid Extension?).");
            }

            // Cache the asset if it was loaded successfully
            if (asset != null)
                _assetCache[assetPath] = asset;

            return asset;
        }

        // Method to clear the cache
        public void ClearCache()
        {
            _assetCache.Clear();
        }

        // Method to remove a specific asset from the cache
        public void RemoveFromCache(string assetPath)
        {
            if (_assetCache.ContainsKey(assetPath))
                _assetCache.Remove(assetPath);
        }

        /// <summary>
        /// Tries to find an embedded resource by path, converting path separators to dots.
        /// Example: "Assets/Textures/icon.png" -> "YourAssembly.Assets.Textures.icon.png"
        /// </summary>
        private bool TryGetEmbeddedResourceName(string resourcePath, out string? resourceName)
        {
            resourceName = null;

            if (_embeddedResourceAssembly == null)
                return false;

            // Convert path separators to dots for embedded resource naming
            string normalizedPath = resourcePath.Replace('/', '.').Replace('\\', '.');

            // Get all embedded resource names from the assembly
            string[] resourceNames = _embeddedResourceAssembly.GetManifestResourceNames();

            // Try to find exact match (with assembly prefix)
            resourceName = resourceNames.FirstOrDefault(r => r.EndsWith(normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (resourceName != null)
                return true;

            // Try to find match by just the filename
            string fileName = System.IO.Path.GetFileName(resourcePath);
            resourceName = resourceNames.FirstOrDefault(r => r.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

            return resourceName != null;
        }

        /// <summary>
        /// Gets a stream for an embedded resource.
        /// </summary>
        private Stream GetEmbeddedResourceStream(string resourcePath)
        {
            if (_embeddedResourceAssembly == null)
                throw new InvalidOperationException("No embedded resource assembly was provided to BasicAssetProvider.");

            if (!TryGetEmbeddedResourceName(resourcePath, out string? resourceName) || resourceName == null)
                throw new FileNotFoundException($"Embedded resource '{resourcePath}' not found in assembly '{_embeddedResourceAssembly.FullName}'.");

            Stream? stream = _embeddedResourceAssembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new FileNotFoundException($"Failed to load embedded resource '{resourceName}' from assembly.");

            return stream;
        }
    }
}
