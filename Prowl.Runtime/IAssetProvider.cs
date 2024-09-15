// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime;

public interface IAssetProvider
{
    public bool HasAsset(Guid assetID);
    public AssetRef<T> LoadAsset<T>(string relativeAssetPath, ushort fileID = 0) where T : EngineObject;
    public AssetRef<T> LoadAsset<T>(Guid guid, ushort fileID = 0) where T : EngineObject;
    public AssetRef<T> LoadAsset<T>(IAssetRef assetID) where T : EngineObject;
    public SerializedAsset? LoadAsset(Guid guid);
}
