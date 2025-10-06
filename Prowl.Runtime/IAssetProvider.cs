// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime;

public interface IAssetProvider
{
    public bool HasAsset(string relativeAssetPath);
    public T? LoadAsset<T>(string relativeAssetPath) where T : EngineObject;
}
