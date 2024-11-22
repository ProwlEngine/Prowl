// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

public interface IAssetRef
{
    Guid AssetID { get; set; }
    ushort FileID { get; set; }
    string Name { get; }
    bool IsAvailable { get; }
    bool IsRuntimeResource { get; }
    bool IsLoaded { get; }
    bool IsExplicitNull { get; }
    Type InstanceType { get; }

    object? GetInstance();
    void SetInstance(object? obj);
}
