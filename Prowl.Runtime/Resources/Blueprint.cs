// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.NodeSystem;
using Prowl.Runtime.Utils;

namespace Prowl.Runtime;

[CreateAssetMenu("Visual Scripting/Blueprint")]
public sealed class Blueprint : NodeGraph
{
    public override string[] NodeCategories => new[]
    {
        "Event",
        "Flow Control",
        "Math",
        "General",
        "GameObject",
        "String",
    };
}
