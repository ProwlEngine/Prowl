// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
using System;

namespace Prowl.Runtime.UI;

[Flags]
public enum UIDirtyFlags
{
    None = 0,
    Vertices = 1 << 0,
    Layout = 1 << 1,
    Material = 1 << 2,
    Hierarchy = 1 << 3,
    All = ~0
}
