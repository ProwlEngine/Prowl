// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime;

public interface IAssetRef
{
    Guid AssetID { get; set; }
    string Name { get; }
    bool IsRuntimeResource { get; }
    bool IsExplicitNull { get; }
    Type InstanceType { get; }

    object? GetInstance();
    void SetInstance(object? obj);
}
