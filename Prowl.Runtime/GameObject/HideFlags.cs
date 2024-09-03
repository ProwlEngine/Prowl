// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime
{
    [Flags]
    public enum HideFlags
    {
        None = 0,
        Hide = 1 << 0,
        NotEditable = 1 << 1,
        DontSave = 1 << 2,
        HideAndDontSave = 1 << 3,
        NoGizmos = 1 << 4
    }
}
