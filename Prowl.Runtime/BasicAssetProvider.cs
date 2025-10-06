using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime.AssetImporting;

namespace Prowl.Runtime
{
    public class BasicAssetProvider : IAssetProvider
    {
        // Dictionary to store cached assets with their paths as keys
        private readonly Dictionary<string, EngineObject> _assetCache = new Dictionary<string, EngineObject>();

        public bool HasAsset(string relativeAssetPath)
        {
            // First check if the asset is in cache
            if (_assetCache.ContainsKey(relativeAssetPath))
                return true;

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

            // Get extension
            string extension = System.IO.Path.GetExtension(assetPath);
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
                asset = Prowl.Runtime.Resources.Texture2D.FromFile(assetPath, true) as T;
                if (asset != null)
                    asset.AssetPath = assetPath;
            }
            else if (extension == ".obj" || extension == ".fbx" || extension == ".gltf")
            {
                // Load model
                asset = new ModelImporter().Import(new FileInfo(assetPath)) as T;
                if (asset != null)
                    asset.AssetPath = assetPath;
            }
            else if (extension == ".wav" || extension == ".mp3" || extension == ".ogg")
            {
                Debug.LogWarning($"Audio loading is not implemented yet. Asset: {assetPath}");
            }
            else if (extension == ".shader")
            {
                asset = new ShaderImporter().Import(new FileInfo(assetPath)) as T;
                if (asset != null)
                    asset.AssetPath = assetPath;
            }
            else if (extension == ".mat" || extension == ".mesh")
            {
                string assetString = System.IO.File.ReadAllText(assetPath);
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
    }
}
