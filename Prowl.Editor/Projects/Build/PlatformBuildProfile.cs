// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Editor.Build;

/// <summary>
/// Per-platform scripting define symbols and any future platform-specific build knobs.
/// Serialised as part of the project's build settings.
/// </summary>
/// <remarks>
/// Deliberately has no drawing code. A profile is data, so a CI build can construct one without a UI
/// toolkit present. The editor renders it through a registered drawer keyed on the profile type.
/// </remarks>
public class PlatformBuildProfile
{
    /// <summary>
    /// Scripting define symbols passed to the compiler when building for this platform.
    /// </summary>
    public List<string> ScriptingDefineSymbols { get; set; } = [];

    public virtual Type? GetPipelineType() => null;

    public virtual void ToDefault() { }

    public virtual void ModifyDefines(List<string> defines) { }
}
